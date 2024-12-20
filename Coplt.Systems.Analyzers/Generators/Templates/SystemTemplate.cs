using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Coplt.Analyzers.Generators.Templates;
using Coplt.Analyzers.Utilities;
using Microsoft.CodeAnalysis;

namespace Coplt.Systems.Analyzers.Generators.Templates;

public record struct InjectedMethod(string Name, ImmutableArray<Arg> Args);
public record struct ResourceProvider(string Type, string AttrType, string AttrCtor);

public record struct Injection(
    string Name,
    string Type,
    string TypeWithNullable,
    ResourceProvider? ResourceProvider,
    Accessibility Accessibility,
    bool IsStruct,
    bool Ref,
    bool ReadOnly,
    bool Get,
    bool Set
);

public record struct Arg(
    string Type,
    RefKind Ref,
    ResourceProvider? ResourceProvider
);

public record struct SystemMeta()
{
    public long Partition { get; set; }
    public string? Group { get; set; }
    public ImmutableArray<string> Before { get; set; } = [];
    public ImmutableArray<string> After { get; set; } = [];
}

public class SystemTemplate(
    GenBase GenBase,
    string name,
    SystemMeta Meta,
    bool IsGroup,
    bool IsSystem,
    bool IsStruct,
    bool ReadOnly,
    bool HasDispose,
    ImmutableArray<Injection> Injections,
    ImmutableArray<InjectedMethod> Setups,
    ImmutableArray<InjectedMethod> Updates,
    ImmutableArray<Arg> CtorArgs
) : ATemplate(GenBase)
{
    private const string InjectionDataName = $"__InjectionData";
    private const string InjectionFieldName = $"__injection_data";
    private const string AttributeInstanceName = $"__AttributeInstance";

    private readonly Dictionary<string, int> ResourceProviderTypes = new();
    private readonly Dictionary<(string t, ResourceProvider? rp), (int t, int rp)> InjectionTypes = new();
    private readonly Dictionary<ResourceProvider, int> ResourceProviders = new();

    protected override void DoGen()
    {
        var has_drp = false;
        var injection_inc = 0;
        var resource_provider_type_inc = 0;
        var resource_provider_inc = 0;
        foreach (var injection in Injections)
        {
            var rp = -1;
            if (injection.ResourceProvider is { } ResourceProvider)
            {
                if (!ResourceProviderTypes.TryGetValue(ResourceProvider.Type, out rp))
                    ResourceProviderTypes.Add(ResourceProvider.Type,
                        rp = resource_provider_type_inc++);
                if (!ResourceProviders.ContainsKey(ResourceProvider))
                    ResourceProviders.Add(ResourceProvider, resource_provider_inc++);
            }
            else has_drp = true;
            if (!InjectionTypes.ContainsKey((injection.Type, injection.ResourceProvider)))
                InjectionTypes.Add((injection.Type, injection.ResourceProvider),
                    (injection_inc++, rp));
        }
        foreach (var setup in Setups)
        {
            foreach (var arg in setup.Args)
            {
                var rp = -1;
                if (arg.ResourceProvider is { } ResourceProvider)
                {
                    if (!ResourceProviderTypes.TryGetValue(ResourceProvider.Type, out rp))
                        ResourceProviderTypes.Add(ResourceProvider.Type, rp = resource_provider_type_inc++);
                    if (!ResourceProviders.ContainsKey(ResourceProvider))
                        ResourceProviders.Add(ResourceProvider, resource_provider_inc++);
                }
                else has_drp = true;
                if (!InjectionTypes.ContainsKey((arg.Type, arg.ResourceProvider)))
                    InjectionTypes.Add((arg.Type, arg.ResourceProvider), (injection_inc++, rp));
            }
        }
        foreach (var update in Updates)
        {
            foreach (var arg in update.Args)
            {
                var rp = -1;
                if (arg.ResourceProvider is { } ResourceProvider)
                {
                    if (!ResourceProviderTypes.TryGetValue(ResourceProvider.Type, out rp))
                        ResourceProviderTypes.Add(ResourceProvider.Type, rp = resource_provider_type_inc++);
                    if (!ResourceProviders.ContainsKey(ResourceProvider))
                        ResourceProviders.Add(ResourceProvider, resource_provider_inc++);
                }
                else has_drp = true;
                if (!InjectionTypes.ContainsKey((arg.Type, arg.ResourceProvider)))
                    InjectionTypes.Add((arg.Type, arg.ResourceProvider), (injection_inc++, rp));
            }
        }

        if (Setups.Length == 0) sb.AppendLine("[global::Coplt.Systems.SkipSetup]");
        if (Updates.Length == 0) sb.AppendLine("[global::Coplt.Systems.SkipUpdate]");
        sb.AppendLine(
            "[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Auto)]");
        sb.Append(GenBase.Target.Code);
        sb.AppendLine(IsGroup || IsSystem
            ? $" : global::Coplt.Systems.ISystemBase"
            : $" : global::Coplt.Systems.ISystem");
        sb.AppendLine("{");

        #region Initialization Field

        if (InjectionTypes.Count > 0)
        {
            var r = ReadOnly ? "readonly " : "";
            sb.AppendLine();
            sb.AppendLine($"    private {r}{InjectionDataName} {InjectionFieldName};");
        }

        #endregion

        #region Props

        {
            foreach (var injection in Injections)
            {
                sb.AppendLine();
                var i = InjectionTypes[(injection.Type, injection.ResourceProvider)];
                var ro = injection.ReadOnly ? "readonly " : "";
                var r = injection.Ref ? $"ref " : "";
                if (IsStruct)
                {
                    sb.AppendLine("    [global::System.Diagnostics.CodeAnalysis.UnscopedRefAttribute]");
                }
                sb.AppendLine(
                    $"    {injection.Accessibility.GetAccessStr()} partial {r}{ro}{injection.TypeWithNullable} {injection.Name}");
                sb.AppendLine($"    {{");
                if (injection.Get)
                {
                    var get = injection.Ref ? injection.ReadOnly ? "GetImmRef" : "GetMutRef" : "Get";
                    sb.AppendLine($"        get => {r}{InjectionFieldName}._{i.t}.{get}();");
                }
                if (injection.Set)
                {
                    sb.AppendLine($"        set => {InjectionFieldName}._{i.t}.Set(value);");
                }
                sb.AppendLine($"    }}");
            }
        }

        #endregion

        #region AddToSystems

        {
            sb.AppendLine();
            sb.AppendLine(
                $"    static void global::Coplt.Systems.ISystemBase.AddToSystems(global::Coplt.Systems.Systems systems)");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        systems.Add<global::{GenBase.RawFullName}>();");
            sb.AppendLine($"    }}");
        }

        #endregion

        #region Create

        {
            Dictionary<(string t, ResourceProvider? rp), (int t, int rp)> CtorInjectionTypes = new();
            var ctor_injection_inc = 0;
            foreach (var arg in CtorArgs)
            {
                var rp = -1;
                if (arg.ResourceProvider is { } ResourceProvider)
                {
                    if (!ResourceProviderTypes.TryGetValue(ResourceProvider.Type, out rp))
                        ResourceProviderTypes.Add(ResourceProvider.Type, rp = resource_provider_type_inc++);
                    if (!ResourceProviders.ContainsKey(ResourceProvider))
                        ResourceProviders.Add(ResourceProvider, resource_provider_inc++);
                }
                else has_drp = true;
                if (!CtorInjectionTypes.ContainsKey((arg.Type, arg.ResourceProvider)))
                    CtorInjectionTypes.Add((arg.Type, arg.ResourceProvider),
                        (ctor_injection_inc++, rp));
            }
            sb.AppendLine();
            sb.AppendLine(
                $"    static void global::Coplt.Systems.ISystemBase.Create(global::Coplt.Systems.InjectContext ctx, global::Coplt.Systems.SystemHandle handle)");
            sb.AppendLine($"    {{");
            sb.AppendLine(
                $"        ref var self = ref handle.UnsafeAs<global::{GenBase.RawFullName}>().GetMutRef();");
            if (CtorInjectionTypes.Count > 0 || InjectionTypes.Count > 0)
            {
                if (has_drp) sb.AppendLine($"        var drp = ctx.DefaultResourceProvider;");
                foreach (var kv in ResourceProviderTypes)
                {
                    sb.AppendLine($"        var rp{kv.Value} = ctx.GetResourceProvider<{kv.Key}>();");
                }
            }
            foreach (var kv in CtorInjectionTypes)
            {
                var req = $"new() {{ SrcSystem = handle }}";
                var rp = kv.Value.rp < 0 ? "drp" : $"rp{kv.Value.rp}";
                var rpi = kv.Value.rp < 0
                    ? "default"
                    : $"{AttributeInstanceName}._{ResourceProviders[kv.Key.rp!.Value]}.GetData()";
                sb.AppendLine($"        var c{kv.Value.t} = {rp}.GetRef<{kv.Key.t}>({rpi}, {req});");
            }
            var box = IsStruct ? "(object)" : "";
            sb.Append($"        self = new global::{GenBase.RawFullName}(");
            var first = true;
            foreach (var arg in CtorArgs)
            {
                var i = CtorInjectionTypes[(arg.Type, arg.ResourceProvider)];
                var r = arg.Ref switch
                {
                    RefKind.Ref => "ref ",
                    RefKind.Out => "out ",
                    RefKind.In => "in ",
                    RefKind.RefReadOnlyParameter => "ref ",
                    _ => ""
                };
                if (first) first = false;
                else sb.Append(", ");
                var get = arg.Ref switch
                {
                    RefKind.Ref or RefKind.Out or RefKind.RefReadOnlyParameter => "GetMutRef",
                    RefKind.In => "GetImmRef",
                    _ => "Get"
                };
                sb.Append($"{r}c{i.t}.{get}()");
            }
            sb.AppendLine($");");
            if (InjectionTypes.Count > 0)
            {
                sb.AppendLine(
                    $"        ref var data = ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in self.{InjectionFieldName});");
            }
            foreach (var kv in InjectionTypes)
            {
                var req = $"new() {{ SrcSystem = handle }}";
                var rp = kv.Value.rp < 0 ? "drp" : $"rp{kv.Value.rp}";
                var rpi = kv.Value.rp < 0
                    ? "default"
                    : $"{AttributeInstanceName}._{ResourceProviders[kv.Key.rp!.Value]}.GetData()";
                var get = CtorInjectionTypes.TryGetValue(kv.Key, out var ci)
                    ? $"c{ci.t}"
                    : $"{rp}.GetRef<{kv.Key.t}>({rpi}, {req})";
                sb.AppendLine($"        data._{kv.Value.t} = {get};");
            }
            sb.AppendLine($"    }}");
        }

        #endregion

        #region Setup

        {
            sb.AppendLine();
            sb.AppendLine($"    void global::Coplt.Systems.ISystemBase.Setup()");
            sb.AppendLine($"    {{");
            foreach (var setup in Setups)
            {
                sb.Append($"        this.{setup.Name}(");
                var first = true;
                foreach (var arg in setup.Args)
                {
                    var i = InjectionTypes[(arg.Type, arg.ResourceProvider)];
                    var r = arg.Ref switch
                    {
                        RefKind.Ref => "ref ",
                        RefKind.Out => "out ",
                        RefKind.In => "in ",
                        RefKind.RefReadOnlyParameter => "ref ",
                        _ => ""
                    };
                    if (first) first = false;
                    else sb.Append(", ");
                    var get = arg.Ref switch
                    {
                        RefKind.Ref or RefKind.Out or RefKind.RefReadOnlyParameter => "GetMutRef",
                        RefKind.In => "GetImmRef",
                        _ => "Get"
                    };
                    sb.Append($"{r}{InjectionFieldName}._{i.t}.{get}()");
                }
                sb.AppendLine($");");
            }
            sb.AppendLine($"    }}");
        }

        #endregion

        #region Update

        {
            sb.AppendLine();
            sb.AppendLine($"    void global::Coplt.Systems.ISystemBase.Update()");
            sb.AppendLine($"    {{");
            foreach (var update in Updates)
            {
                sb.Append($"        this.{update.Name}(");
                var first = true;
                foreach (var arg in update.Args)
                {
                    var i = InjectionTypes[(arg.Type, arg.ResourceProvider)];
                    var r = arg.Ref switch
                    {
                        RefKind.Ref => "ref ",
                        RefKind.Out => "out ",
                        RefKind.In => "in ",
                        RefKind.RefReadOnlyParameter => "ref ",
                        _ => ""
                    };
                    if (first) first = false;
                    else sb.Append(", ");
                    var get = arg.Ref switch
                    {
                        RefKind.Ref or RefKind.Out or RefKind.RefReadOnlyParameter => "GetMutRef",
                        RefKind.In => "GetImmRef",
                        _ => "Get"
                    };
                    sb.Append($"{r}{InjectionFieldName}._{i.t}.{get}()");
                }
                sb.AppendLine($");");
            }
            sb.AppendLine($"    }}");
        }

        #endregion

        #region Dispose

        if (!HasDispose)
        {
            sb.AppendLine();
            sb.AppendLine($"    public void Dispose()");
            sb.AppendLine($"    {{");
            sb.AppendLine($"    }}");
        }

        #endregion

        #region InjectionData

        if (InjectionTypes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"    private struct {InjectionDataName}");
            sb.AppendLine($"    {{");
            foreach (var kv in InjectionTypes)
            {
                sb.AppendLine($"        public global::Coplt.Systems.ResRef<{kv.Key.t}> _{kv.Value.t};");
            }
            sb.AppendLine($"    }}");
        }

        #endregion

        sb.AppendLine();
        sb.AppendLine("}");
    }

    protected override void DoGenFileScope()
    {
        if (ResourceProviders.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"file static class {AttributeInstanceName}");
            sb.AppendLine($"{{");
            foreach (var kv in ResourceProviders)
            {
                sb.AppendLine($"    public static readonly {kv.Key.AttrType} _{kv.Value} = {kv.Key.AttrCtor};");
            }
            sb.AppendLine($"}}");
        }
    }
}
