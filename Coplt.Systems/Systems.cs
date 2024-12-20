using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Coplt.Systems.Utilities;

namespace Coplt.Systems;

public class Systems : IDisposable
{
    #region Debug

    public static bool DebugEnabled { get; set; } = IsDebug();

    private static bool IsDebug()
    {
#if DEBUG
        return true;
#else
        var asm = Assembly.GetExecutingAssembly();
        if (asm.GetCustomAttribute<DebuggableAttribute>() is not { } attr) return false;
        return (attr.DebuggingFlags & DebuggableAttribute.DebuggingModes.Default) != 0;
#endif
    }

    #endregion

    #region UnhandledException And Logger

    public event Action<ExceptionDispatchInfo>? UnhandledException;
    public void EmitUnhandledException(ExceptionDispatchInfo e) => UnhandledException?.Invoke(e);

    public ILogger? Logger { get; set; }

    public interface ILogger
    {
        public bool IsEnabled(LogLevel level);
        public void Log<T>(LogLevel level, T data, Func<T, string> formatter);
        /// If need to call the formatter asynchronously, should first use move to create a copy of the data.
        public void Log<T>(LogLevel level, T data, Func<T, T> move, Func<T, string> formatter);
        public void Log(LogLevel level, string message);
    }

    public enum LogLevel : byte
    {
        Debug,
        Info,
        Warn,
        Error
    }

    public class ConsoleLogger : ILogger
    {
        public static ConsoleLogger Instance { get; } = new();

        public bool IsEnabled(LogLevel level) => true;
        public void Log<T>(LogLevel level, T data, Func<T, string> formatter) =>
            Log(level, formatter(data));
        public void Log<T>(LogLevel level, T data, Func<T, T> move, Func<T, string> formatter) =>
            Log(level, formatter(data));
        public void Log(LogLevel level, string message)
        {
            var old_color = Console.ForegroundColor;
            var color = old_color;
            if (level is LogLevel.Error) Console.ForegroundColor = color = ConsoleColor.Red;
            else if (level is LogLevel.Warn) Console.ForegroundColor = color = ConsoleColor.Yellow;
            else if (level is LogLevel.Info) Console.ForegroundColor = color = ConsoleColor.Green;
            Console.WriteLine($"[{DateTime.Now:hh:mm:ss}] [{level}] {message}");
            if (color != old_color) Console.ForegroundColor = old_color;
        }
    }

    #endregion

    #region Graph

    internal class Node(Systems Systems, Type type) : IDisposable
    {
        #region System

        public Type Type => type;
        public SystemMeta Meta { get; } = SystemMeta.FromType(type)!;
        public bool IsGroup { get; } = type.IsAssignableTo(typeof(ISystemGroup));

        #endregion

        #region Graph

        private HashSet<Node>? m_sub_nodes;
        public HashSet<Node>? m_to_links;
        public HashSet<Node>? m_from_links;
        public HashSet<Node> SubNodes => m_sub_nodes ??= new();
        public HashSet<Node> ToLinks => m_to_links ??= new();
        public HashSet<Node> FromLinks => m_from_links ??= new();

        public ulong Order { get; set; }
        public ulong TraversalId { get; set; }
        public int SortInc { get; set; }

        public void GraphReset()
        {
            Order = 0;
            SortInc = 0;
            m_to_links?.Clear();
            m_from_links?.Clear();
        }

        #endregion

        #region Instance

        public List<Node>? SortedSubNodes { get; set; }
        private UnTypedResRef m_system_instance;
        private volatile byte m_instance_create_lock;
        private volatile bool m_instance_created;
        private volatile bool m_disposed;

        public void Update()
        {
            #region Setup

            re:
            if (m_disposed) return;
            if (!m_instance_created)
            {
                if (Interlocked.CompareExchange(ref m_instance_create_lock, 255, 0) == 0)
                {
                    try
                    {
                        if (m_disposed) return;
                        if (!m_instance_created)
                        {
                            try
                            {
                                var slot = m_system_instance = Systems.m_system_instances.GetOrAdd(type);
                                Systems.GetOrAddSystemCreator(type)
                                    .Create(new(Systems), new(slot));
                                if (Meta.Setup) ((ISystemBase)slot.GetObject()!).Setup();
                            }
                            catch (Exception e)
                            {
                                m_disposed = true;
                                Systems.Logger?.Log(LogLevel.Error, type,
                                    static type => $"System {type} setup failed, this system will not be update");
                                Systems.EmitUnhandledException(ExceptionDispatchInfo.Capture(e));
                                return;
                            }
                            m_instance_created = true;
                        }
                    }
                    finally
                    {
                        m_instance_create_lock = 0;
                    }
                }
                else goto re;
            }

            #endregion

            try
            {
                var instance = (ISystemBase)m_system_instance.GetObject()!;
                if (Meta.Update) instance.Update();

                if (IsGroup)
                {
                    var group = (ISystemGroup)instance;
                    group.UpdateSybSystems(new GroupUpdateContext(this));
                }
            }
            catch (Exception e)
            {
                Systems.EmitUnhandledException(ExceptionDispatchInfo.Capture(e));
            }
        }

        public void Dispose()
        {
            m_disposed = true;
            if (m_instance_created && m_system_instance.GetObject() is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception e)
                {
                    Systems.EmitUnhandledException(ExceptionDispatchInfo.Capture(e));
                }
            }
            if (SortedSubNodes is { } sub_nodes)
            {
                foreach (var node in sub_nodes)
                {
                    node.Dispose();
                }
            }
        }

        #endregion

        #region ToString

        public override string ToString() => $"Node({Type})";

        #endregion
    }

    #endregion

    #region Fields

    internal ResourceProvider<Unit> m_default_resource_provider = new DefaultResourceProvider();
    internal readonly ConcurrentDictionary<Type, ResourceProvider> m_resource_providers = new();
    internal readonly ResourceContainer m_system_instances = new();
    internal readonly ConcurrentDictionary<Type, ASystemCreator> m_system_creators = new();
    // read and clear only when enter write
    internal RwLock<ConcurrentDictionary<Type, Node>> m_new_systems = new(new());
    // read only when m_new_systems enter read
    internal readonly Dictionary<Type, Node> m_system_nodes = new();
    internal readonly Node m_root_group;
    internal Type m_default_group_type = typeof(RootGroup);
    internal readonly Lock m_exec_locker = new();
    // do not allow parallel traversal of the graph
    internal ulong m_graph_traversal_id = 0;
    internal bool m_first_update = true;

    #endregion

    #region Ctor

    public Systems()
    {
        m_root_group = new(this, typeof(RootGroup));
        m_system_nodes.Add(typeof(RootGroup), m_root_group);
        AddSystemCreator<RootGroup>();
        SetResourceProvider(new SystemRefResourceProvider(this));
    }

    #endregion

    #region SystemCreator

    internal void AddSystemCreator<T>() where T : ISystemBase =>
        m_system_creators.AddOrUpdate(
            typeof(T),
            static _ => new SystemCreator<T>(),
            static (_, v) => v is SystemCreator<T> ? v : new SystemCreator<T>()
        );

    internal ASystemCreator GetOrAddSystemCreator(Type type) =>
        m_system_creators.GetOrAdd(
            type,
            static type => new SystemCreator(type)
        );

    internal abstract class ASystemCreator
    {
        public abstract void Create(InjectContext ctx, SystemHandle handle);
    }

    internal class SystemCreator : ASystemCreator
    {
        private readonly MethodInfo m_method;
        public SystemCreator(Type type)
        {
            var method = typeof(ISystemBase).GetMethod(nameof(ISystemBase.Create));
            var im = type.GetInterfaceMap(typeof(ISystemBase));
            for (var i = 0; i < im.InterfaceMethods.Length; i++)
            {
                if (im.InterfaceMethods[i] != method) continue;
                m_method = im.TargetMethods[i];
                return;
            }
            throw new UnreachableException();
        }
        public override void Create(InjectContext ctx, SystemHandle handle) => m_method.Invoke(null, [ctx, handle]);
    }

    internal class SystemCreator<T> : ASystemCreator where T : ISystemBase
    {
        public override void Create(InjectContext ctx, SystemHandle handle) => T.Create(ctx, handle);
    }

    #endregion

    #region Resource

    public ResourceProvider<Unit> DefaultResourceProvider
    {
        get => m_default_resource_provider;
        set => m_default_resource_provider = value;
    }

    public void SetResourceProvider<T>(T provider) where T : ResourceProvider =>
        m_resource_providers[typeof(T)] = provider;
    public T? TryGetResourceProvider<T>() where T : ResourceProvider =>
        m_resource_providers.TryGetValue(typeof(T), out var provider) ? (T)provider : null;
    public T GetResourceProvider<T>() where T : ResourceProvider =>
        TryGetResourceProvider<T>() ?? throw new NotSupportedException($"Resource provider [{typeof(T)}] is not set");

    public void SetResource<T>(T resource) => m_default_resource_provider.GetRef<T>(default).Set(resource);

    public T GetResource<T>() => GetResourceRef<T>().Get();

    public ResRef<T> GetResourceRef<T>() => m_default_resource_provider.GetRef<T>(default);

    #endregion

    #region AddSystem

    public void SetDefaultSystemGroup<T>(bool no_warn = false) where T : ISystemGroup
    {
        if (!m_first_update && !no_warn)
            Logger?.Log(LogLevel.Warn,
                "It is not recommended to dynamically modify the default group. Modifying the default group will not modify the loaded system.");
        m_default_group_type = typeof(T);
        AddSystemCreator<T>();
    }

    public void SetDefaultSystemGroup(Type type, bool no_warn = false)
    {
        if (!type.IsAssignableTo(typeof(ISystemGroup)))
            throw new ArgumentException("Type must be an ISystemGroup", nameof(type));
        if (!m_first_update && !no_warn)
            Logger?.Log(LogLevel.Warn,
                "It is not recommended to dynamically modify the default group. Modifying the default group will not modify the loaded system.");
        m_default_group_type = type;
        GetOrAddSystemCreator(type);
    }

    public void Add<T>() where T : ISystemBase
    {
        if (m_system_nodes.ContainsKey(typeof(T))) return;
        using var new_systems = m_new_systems.EnterRead();
        new_systems.Value.TryAdd(typeof(T), new Node(this, typeof(T)));
        AddSystemCreator<T>();
    }

    public void Add(Type type)
    {
        if (!type.IsAssignableTo(typeof(ISystemBase)))
            throw new ArgumentException("Type must be an ISystemBase", nameof(type));
        using var new_systems = m_new_systems.EnterRead();
        if (m_system_nodes.ContainsKey(type)) return;
        new_systems.Value.TryAdd(type, new Node(this, type));
        GetOrAddSystemCreator(type);
    }

    #endregion

    #region Load New Systems

    private struct RecCtx(ulong TraversalId)
    {
        public Stack<Node> Stack = new();
        public ulong TraversalId = TraversalId;

        public void Clear(ulong TraversalId)
        {
            Stack.Clear();
            this.TraversalId = TraversalId;
        }

        /// <summary>
        /// Returns whether the node is repeated
        /// </summary>
        public readonly bool Record(Node node, bool push = true)
        {
            if (push) Push(node);
            if (node.TraversalId >= TraversalId) return true;
            if (push) node.TraversalId = TraversalId;
            return false;
        }

        public readonly void Push(Node node) => Stack.Push(node);

        public readonly void Pop(Node node)
        {
            if (Stack.TryPop(out var r))
            {
                if (r != node) throw new UnreachableException("Logical Error");
                r.TraversalId--;
            }
            else throw new UnreachableException("Logical Error");
        }

        public RecCtx Clone() => new()
        {
            Stack = new(Stack),
            TraversalId = TraversalId,
        };
    }

    private void LoadNewSystems()
    {
        using var new_systems = m_new_systems.EnterWrite();
        if (new_systems.Value.Count == 0) return;
        var changed_groups = new Dictionary<Type, Node>();
        var ctx = new RecCtx(++m_graph_traversal_id);

        #region Check default group

        {
            var default_group = GetOrAddGroup(m_default_group_type);
            if (!(default_group.Meta.Group == null || default_group.Meta.Group == typeof(RootGroup)))
                Logger?.Log(LogLevel.Error, default_group.Type,
                    static type => $"A default group cannot have a parent group; group: {type}");
        }

        #endregion

        #region Ensure groups

        {
            foreach (var (type, node) in new_systems.Value)
            {
                if (!node.IsGroup) continue;
                DirectAddSystem(type, node);
            }
            foreach (var (_, node) in new_systems.Value)
            {
                EnsureGroups(ctx, node, changed_groups);
                ctx.Clear(++m_graph_traversal_id);
            }
        }

        #endregion

        #region Add non group systems

        foreach (var (type, node) in new_systems.Value)
        {
            if (node.IsGroup) continue;
            m_system_nodes.Add(type, node);
        }

        #endregion

        #region Sort systems

        foreach (var group in changed_groups.Values)
        {
            SortSystemsInGroup(group);
        }

        #endregion

        new_systems.Value.Clear();
    }

    private void SortSystemsInGroup(Node group)
    {
        if (group.SubNodes.Count == 0)
        {
            group.SortedSubNodes = null;
            return;
        }

        #region Reset nodes

        foreach (var node in group.SubNodes)
        {
            node.GraphReset();
        }

        #endregion

        #region Build graph

        foreach (var node in group.SubNodes)
        {
            foreach (var after in node.Meta.After)
            {
                if (!m_system_nodes.TryGetValue(after, out var after_source))
                {
                    Logger?.Log(LogLevel.Warn, (node, after),
                        static a =>
                            $"System [{a.after}] was not added, but was found in the after of system [{a.node.Type}]");
                    continue;
                }
                if (!group.SubNodes.Contains(after_source))
                {
                    Logger?.Log(LogLevel.Warn, (node, after, group),
                        static a =>
                            $"System [{a.after}] not in the group [{a.group.Type}], but was found in the after of system [{a.node.Type}], the system can only sort within the same group");
                    continue;
                }
                after_source.ToLinks.Add(node);
                node.FromLinks.Add(after_source);
            }
            foreach (var before in node.Meta.Before)
            {
                if (!m_system_nodes.TryGetValue(before, out var before_target))
                {
                    Logger?.Log(LogLevel.Warn, (node, before),
                        static a =>
                            $"System [{a.before}] was not added, but was found in the before of system [{a.node.Type}]");
                    continue;
                }
                if (!group.SubNodes.Contains(before_target))
                {
                    Logger?.Log(LogLevel.Warn, (node, before, group),
                        static a =>
                            $"System [{a.before}] not in the group [{a.group.Type}], but was found in the before of system [{a.node.Type}], the system can only sort within the same group");
                    continue;
                }
                node.ToLinks.Add(before_target);
                before_target.FromLinks.Add(node);
            }
        }

        #endregion

        #region Sort

        var ctx = new RecCtx(++m_graph_traversal_id);
        foreach (var node in group.SubNodes)
        {
            if (node is { m_from_links.Count: not 0 }
                or { m_to_links: null or { Count: 0 } }
                or { SortInc: > 0 }) continue;
            SortGraph(ref ctx, node, 0);
            ctx.Clear(++m_graph_traversal_id);
        }

        #endregion

        #region Finish sort

        group.SortedSubNodes = group.SubNodes
            .OrderBy(static a => a.Order)
            .ThenBy(static a => a.Meta.Partition)
            .ToList();

        #endregion
    }

    private void SortGraph(ref RecCtx ctx, Node node, ulong base_order)
    {
        if (ctx.Record(node))
        {
            LogCircularDependencies(ctx, false);
            return;
        }
        node.Order = Math.Max(base_order, node.Order);
        if (node is { m_to_links: null or { Count: 0 } }) goto end;
        var order = node.Order + 1;
        foreach (var cur in node.m_to_links)
        {
            SortGraph(ref ctx, cur, order);
        }
        end:
        ctx.Pop(node);
        node.SortInc++;
    }

    private void DirectAddSystem(Type type, Node node) => m_system_nodes.Add(type, node);

    private void EnsureGroups(in RecCtx ctx, Node node, Dictionary<Type, Node> changed_groups)
    {
        var exists = false;
        for (;;)
        {
            if (node.Type == typeof(RootGroup)) return;
            if (ctx.Record(node))
            {
                LogCircularDependencies(ctx, true);
                return;
            }
            if (exists) return;
            var parent = node.Meta.Group ?? m_default_group_type;
            var group_node = GetOrAddGroup(parent, out exists);
            if (group_node.SubNodes.Add(node)) changed_groups.TryAdd(parent, group_node);
            node = group_node;
        }
    }

    private Node GetOrAddGroup(Type type) => GetOrAddGroup(type, out _);

    private Node GetOrAddGroup(Type type, out bool exists)
    {
        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(m_system_nodes, type, out exists)!;
        if (exists) return slot;
        Logger?.Log(LogLevel.Debug, type, static type => $"Implicitly added group {type}");
        var node = new Node(this, type);
        slot = node;
        return node;
    }

    private void LogCircularDependencies(RecCtx ctx, bool group)
    {
        Logger?.Log(LogLevel.Error, (ctx, group), static a => (a.ctx.Clone(), a.group), static a =>
        {
            var sb = new StringBuilder();
            sb.Append(a.group ? "System group circular dependencies: " : "System circular dependencies: ");
            var first = true;
            foreach (var node in a.ctx.Stack)
            {
                if (first) first = false;
                else sb.Append(a.group ? " <- " : " -> ");
                sb.Append($"[{node.Type}]");
            }
            return sb.ToString();
        });
    }

    #endregion

    #region Update

    public void Update()
    {
        lock (m_exec_locker)
        {
            if (m_first_update) m_first_update = false;
            LoadNewSystems();
            try
            {
                m_root_group.Update();
            }
            catch (Exception e)
            {
                EmitUnhandledException(ExceptionDispatchInfo.Capture(e));
            }
        }
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        lock (m_exec_locker)
        {
            try
            {
                m_root_group.Dispose();
            }
            catch (Exception e)
            {
                EmitUnhandledException(ExceptionDispatchInfo.Capture(e));
            }
        }
    }

    #endregion
}
