using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Coplt.Systems;

public class Systems : IDisposable
{
    #region UnhandledException

    public static event Action<Exception>? UnhandledException;

    #endregion

    #region Stages

    internal interface IGroupStage
    {
        public List<Stage> Stages { get; set; }
    }

    internal abstract class Stage(Systems Systems) : IDisposable
    {
        public int Order { get; set; }
        public int Mark { get; set; }
        public Systems Systems { get; } = Systems;
        public abstract Type? Type { get; }
        public abstract SystemMeta? Meta { get; }
        public virtual bool AnyUpdate { get; set; } = true;
        public abstract void Create();
        public abstract void Setup();
        public abstract void Update();
        protected abstract void Dispose(bool disposing);
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    internal class ParallelStage(Systems Systems, List<Stage> Stages) : Stage(Systems)
    {
        public List<Stage> Stages { get; set; } = Stages;
        public override Type? Type => null;
        public override SystemMeta? Meta => null;
        
        private List<Stage> UpdateStages { get; set; } = null!;
        
        public override void Create()
        {
            Parallel.ForEach(Stages, static stage => stage.Create());
            AnyUpdate = Stages.Any(static stage => stage.AnyUpdate);
        }

        public override void Setup()
        {
            Parallel.ForEach(Stages, static stage => stage.Setup());
            UpdateStages = Stages.Where(static s => s.AnyUpdate).ToList();
        }

        public override void Update()
        {
            Parallel.ForEach(UpdateStages, static stage => stage.Update());
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            foreach (var stage in Stages) stage.Dispose();
        }
    }

    internal class SystemStage<T>(Systems Systems) : Stage(Systems)
        where T : ISystemBase
    {
        public T? m_data;

        public override Type Type => typeof(T);
        public override SystemMeta Meta { get; } = T.Meta;
        public override void Create()
        {
            T.Create(new(Systems), ref Unsafe.As<T, object>(ref m_data!));
            AnyUpdate = Meta.Update;
        }

        public override void Setup()
        {
            if (Meta.Setup) m_data!.Setup();
        }

        public override void Update()
        {
            if (Meta.Update) m_data!.Update();
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            if (m_data is null) return;
            try
            {
                m_data.Dispose();
            }
            catch (Exception e)
            {
                UnhandledException?.Invoke(e);
            }
        }
    }

    internal class GroupStage<T>(Systems Systems, List<Stage> Stages) : SystemStage<T>(Systems), IGroupStage
        where T : ISystemGroup
    {
        public List<Stage> Stages { get; set; } = Stages;
        private List<Stage> UpdateStages { get; set; } = null!;

        public override void Create()
        {
            base.Create();
            foreach (var stage in Stages)
            {
                stage.Create();
            }
            AnyUpdate = Stages.Any(static stage => stage.AnyUpdate);
        }

        public override void Setup()
        {
            base.Setup();
            foreach (var stage in Stages)
            {
                stage.Setup();
            }
            UpdateStages = Stages.Where(static s => s.AnyUpdate).ToList();
        }

        public override void Update()
        {
            base.Update();
            m_data!.UpdateSybSystems(new(UpdateStages));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            foreach (var stage in Stages) stage.Dispose();
        }
    }

    #endregion

    #region Fields

    internal readonly ConcurrentDictionary<Type, InjectRef> m_resources = new();
    /// <summary>
    /// Temporary use before loading
    /// </summary>
    internal Dictionary<Type, Stage>? m_stage_map = new();
    internal Type? m_default_group_type;
    internal List<Stage> m_stages = new();
    internal readonly Lock m_exec_locker = new();
    internal volatile bool m_loaded;

    #endregion

    #region SetResource

    public void SetResource<T>(T resource)
    {
        m_resources.AddOrUpdate(
            typeof(T),
            static (_, resource) => new InjectRef<T>(resource),
            static (_, value, resource) =>
            {
                ((InjectRef<T>)value).Value = resource;
                return value;
            }, resource
        );
    }

    public T GetResource<T>() => GetResourceRef<T>().Value;

    public InjectRef<T> GetResourceRef<T>() =>
        (InjectRef<T>)m_resources.GetOrAdd(typeof(T), static _ => new InjectRef<T>());

    #endregion

    #region AddSystem

    public void AddSystem<T>() where T : ISystem
    {
        // ReSharper disable once InconsistentlySynchronizedField
        if (m_loaded) throw new ArgumentException("Cannot add systems while already loaded");
        lock (m_exec_locker)
        {
            if (m_loaded) throw new ArgumentException("Cannot add systems while already loaded");
            m_stage_map!.TryAdd(typeof(T), new SystemStage<T>(this));
        }
    }

    public void AddSystemGroup<T>(bool default_group = false) where T : ISystemGroup
    {
        // ReSharper disable once InconsistentlySynchronizedField
        if (m_loaded) throw new ArgumentException("Cannot add systems while already loaded");
        lock (m_exec_locker)
        {
            if (m_loaded) throw new ArgumentException("Cannot add systems while already loaded");
            var stage = new GroupStage<T>(this, new());
            m_stage_map!.TryAdd(typeof(T), stage);
            if (default_group)
            {
                m_default_group_type = typeof(T);
                stage.Meta.Group = typeof(RootGroup);
            }
        }
    }

    #endregion

    #region Setup

    public void Setup()
    {
        lock (m_exec_locker)
        {
            if (m_loaded) throw new InvalidOperationException("Cannot duplicate load");
            m_loaded = true;
            LoadStages();
            m_stage_map = null;
            foreach (var stage in m_stages)
            {
                stage.Create();
            }
            foreach (var stage in m_stages)
            {
                stage.Setup();
            }
        }
    }

    private void LoadStages()
    {
        foreach (var (type, stage) in m_stage_map!)
        {
            if (m_default_group_type is not null && stage.Meta is { Group: null })
            {
                stage.Meta.Group = m_default_group_type;
            }
            if (stage.Meta != null && stage.Meta.Group == typeof(RootGroup))
            {
                stage.Meta.Group = null;
            }
            if (stage.Meta is { Group: { } group_type })
            {
                if (m_stage_map.TryGetValue(group_type, out var group))
                {
                    if (group is IGroupStage group_stage)
                        group_stage.Stages.Add(stage);
                    else
                        throw new ArgumentException($"{group_type} is not a system group, required by {type}");
                }
                else
                {
                    throw new ArgumentException(
                        $"System group {group_type} is required but not added, required by {type}");
                }
                continue;
            }
            m_stages.Add(stage);
        }
        m_stages = LoadStages(m_stages);
    }

    private List<Stage> LoadStages(IEnumerable<Stage> stages)
    {
        var dep_graph = new Dictionary<Type, (Dictionary<Type, Stage> to, Stage stage)>();
        foreach (var stage in stages)
        {
            var type = stage.Type;
            if (type is null) throw new UnreachableException("The current step should not have parallel stages");
            dep_graph.Add(type, (new(), stage));
        }
        foreach (var (_, stage) in dep_graph.Values)
        {
            var type = stage.Type!;
            var meta = stage.Meta;
            if (meta is null) continue;
            foreach (var target in meta.Before)
            {
                if (!dep_graph.TryGetValue(type, out var slot)) continue;
                slot.to.Add(target, dep_graph[target].stage);
            }
            foreach (var target in meta.After)
            {
                if (!dep_graph.TryGetValue(target, out var slot)) continue;
                slot.to.Add(type, stage);
            }
        }
        var pass = 1;
        foreach (var (to, stage) in dep_graph.Values.OrderByDescending(static a => a.to.Count))
        {
            CalcStageOrder(dep_graph, pass++, stage.Order, to, stage);
        }
        var list = new LinkedList<Stage>(dep_graph.Values
            .Select(static a => a.stage)
            .OrderBy(static s => s.Order)
            .ThenBy(static s => s.Meta?.Partition ?? 0)
            .ThenBy(static s => s.Meta is { Parallel: true }));

        #region load group

        foreach (var stage in list)
        {
            if (stage is not IGroupStage group_stage) continue;
            group_stage.Stages = LoadStages(group_stage.Stages);
        }

        #endregion

        #region merge parallel

        if (list.Count > 0)
        {
            var cur = list.First!;
            ParallelStage? parallel = null;
            re:
            if (cur.Value.Meta is { Parallel: true })
            {
                if (parallel is null) parallel = new ParallelStage(this, [cur.Value]);
                else parallel.Stages.Add(cur.Value);
                var next = cur.Next;
                if (next is null)
                {
                    list.Remove(cur);
                    list.AddLast(parallel);
                }
                else
                {
                    list.Remove(cur);
                    cur = next;
                    goto re;
                }
            }
            else
            {
                if (parallel is not null)
                {
                    list.AddBefore(cur, parallel);
                    parallel = null;
                }
                var next = cur.Next;
                if (next is not null)
                {
                    cur = next;
                    goto re;
                }
            }
        }

        #endregion

        return list.ToList();
    }

    private void CalcStageOrder(
        Dictionary<Type, (Dictionary<Type, Stage> to, Stage stage)> graph, int pass,
        int order, Dictionary<Type, Stage> to, Stage stage
    )
    {
        try
        {
            if (stage.Mark >= pass)
                throw new ArgumentException($"There are circular dependencies between systems");
            stage.Mark = pass;
            stage.Order = Math.Max(order, stage.Order);
            foreach (var (type, target) in to)
            {
                CalcStageOrder(graph, pass, order + 1, graph[type].to, target);
            }
        }
        catch (Exception e)
        {
            throw new Exception($"Failed to sort system {stage.Type}", e);
        }
    }

    #endregion

    #region Update

    public void Update()
    {
        lock (m_exec_locker)
        {
            if (!m_loaded) throw new InvalidOperationException("Cannot update when systems not loaded");
            foreach (var stage in m_stages)
            {
                stage.Update();
            }
        }
    }

    #endregion

    #region Reset

    public void Reset()
    {
        lock (m_exec_locker)
        {
            foreach (var stage in m_stages)
            {
                stage.Dispose();
            }
            m_stage_map = new();
            m_resources.Clear();
            m_loaded = false;
        }
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        lock (m_exec_locker)
        {
            foreach (var stage in m_stages)
            {
                stage.Dispose();
            }
            m_stage_map = null;
            m_resources.Clear();
            m_loaded = false;
        }
    }

    #endregion
}
