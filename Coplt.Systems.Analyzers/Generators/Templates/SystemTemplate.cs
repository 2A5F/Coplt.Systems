using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Coplt.Analyzers.Generators.Templates;
using Coplt.Analyzers.Utilities;
using Microsoft.CodeAnalysis;

namespace Coplt.Systems.Analyzers.Generators.Templates;

public record struct UpdateMethod(string Name, ImmutableArray<Arg> Args);

public record struct Injection(
    string Name,
    string Type,
    Accessibility Accessibility,
    bool IsStruct,
    bool Ref,
    bool ReadOnly,
    bool Get,
    bool Set
);

public record struct Arg(
    string Type,
    RefKind Ref
);

public class SystemTemplate(
    GenBase GenBase,
    string name,
    bool IsGroup,
    bool IsSystem,
    bool IsStruct,
    bool ReadOnly,
    bool HasDispose,
    ImmutableArray<Injection> Injections,
    ImmutableArray<UpdateMethod> Updates,
    ImmutableArray<Arg> CtorArgs
) : ATemplate(GenBase)
{
    private string InjectionDataName = $"__InjectionData";
    private string InjectionFieldName = $"__injection_data";

    protected override void DoGen()
    {
        Dictionary<string, int> InjectionTypes = new();
        var injection_inc = 0;
        foreach (var injection in Injections)
        {
            if (InjectionTypes.ContainsKey(injection.Type)) continue;
            else InjectionTypes.Add(injection.Type, injection_inc++);
        }
        foreach (var update in Updates)
        {
            foreach (var arg in update.Args)
            {
                if (InjectionTypes.ContainsKey(arg.Type)) continue;
                else InjectionTypes.Add(arg.Type, injection_inc++);
            }
        }

        sb.AppendLine(
            "[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Auto)]");
        sb.Append(GenBase.Target.Code);
        sb.AppendLine(IsGroup || IsSystem
            ? $" : global::Coplt.Systems.ISystemBase"
            : $" : global::Coplt.Systems.ISystem");
        sb.AppendLine("{");

        #region Initialization FIeld

        {
            var r = ReadOnly ? "readonly " : "";
            sb.AppendLine();
            sb.AppendLine($"    private {r}{InjectionDataName} {InjectionFieldName};");
        }

        #endregion

        #region Props

        {
            sb.AppendLine();
            foreach (var injection in Injections)
            {
                var i = InjectionTypes[injection.Type];
                var ro = injection.ReadOnly ? "readonly " : "";
                var r = injection.Ref ? $"ref " : "";
                sb.AppendLine(
                    $"    {injection.Accessibility.GetAccessStr()} partial {r}{ro}{injection.Type} {injection.Name}");
                sb.AppendLine($"    {{");
                if (injection.Get)
                {
                    sb.AppendLine($"        get => {r}{InjectionFieldName}._{i}.Value;");
                }
                if (injection.Set)
                {
                    sb.AppendLine($"        set => {InjectionFieldName}._{i}.Value = value;");
                }
                sb.AppendLine($"    }}");
            }
        }

        #endregion

        #region Setup and Create

        {
            Dictionary<string, int> CtorInjectionTypes = new();
            var ctor_injection_inc = 0;
            foreach (var arg in CtorArgs)
            {
                if (CtorInjectionTypes.ContainsKey(arg.Type)) continue;
                else CtorInjectionTypes.Add(arg.Type, ctor_injection_inc++);
            }
            sb.AppendLine();
            sb.AppendLine(
                $"    static void global::Coplt.Systems.ISystemBase.Create(global::Coplt.Systems.SetupContext ctx, ref object slot)");
            sb.AppendLine($"    {{");
            sb.AppendLine(
                $"        ref var self = ref global::System.Runtime.CompilerServices.Unsafe.As<object, global::{GenBase.RawFullName}>(ref slot);");
            foreach (var kv in CtorInjectionTypes)
            {
                sb.AppendLine($"        var c{kv.Value} = ctx.GetRef<{kv.Key}>();");
            }
            var box = IsStruct ? "(object)" : "";
            sb.Append($"        self = new global::{GenBase.RawFullName}(");
            var first = true;
            foreach (var arg in CtorArgs)
            {
                var i = CtorInjectionTypes[arg.Type];
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
                sb.Append($"{r}c{i}.Value");
            }
            sb.AppendLine($");");
            sb.AppendLine(
                $"        ref var data = ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in self.{InjectionFieldName});");
            foreach (var kv in InjectionTypes)
            {
                var get = CtorInjectionTypes.TryGetValue(kv.Key, out var ci) ? $"c{ci}" : $"ctx.GetRef<{kv.Key}>()";
                sb.AppendLine($"        data._{kv.Value} = {get};");
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
                    var i = InjectionTypes[arg.Type];
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
                    sb.Append($"{r}{InjectionFieldName}._{i}.Value");
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

        {
            sb.AppendLine();
            sb.AppendLine($"    private struct {InjectionDataName}");
            sb.AppendLine($"    {{");
            foreach (var kv in InjectionTypes)
            {
                sb.AppendLine($"        public global::Coplt.Systems.InjectRef<{kv.Key}> _{kv.Value};");
            }
            sb.AppendLine($"    }}");
        }

        #endregion

        sb.AppendLine();
        sb.AppendLine("}");
    }
}
