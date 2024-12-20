using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Coplt.Analyzers.Generators.Templates;
using Coplt.Analyzers.Utilities;
using Coplt.Systems.Analyzers.Generators.Templates;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Coplt.Systems.Analyzers.Generators;

[Generator]
public class SystemGenerator : IIncrementalGenerator
{
    private static readonly SymbolDisplayFormat NullableFullyQualifiedFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
        SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
        SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
        SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
    );

    private static bool IsResourceProviderAttribute(
        INamedTypeSymbol symbol, out ITypeSymbol? target
    )
    {
        for (;;)
        {
            if (symbol.IsGenericType)
            {
                var gt = symbol.ConstructUnboundGenericType();
                var gtn = gt.ToDisplayString();
                if (gtn == "Coplt.Systems.ResourceProviderAttribute<,>")
                {
                    target = symbol.TypeArguments[0];
                    return true;
                }
            }
            if (symbol.BaseType is not null)
            {
                symbol = symbol.BaseType;
                continue;
            }
            target = null;
            return false;
        }
    }

    private static void BuildAttrArg(StringBuilder sb, TypedConstant arg)
    {
        switch (arg.Kind)
        {
            case TypedConstantKind.Primitive:
            {
                var value = arg.Value;
                sb.Append(value switch
                {
                    null => $"null",
                    true => "true",
                    false => "false",
                    char c => SyntaxFactory
                        .LiteralExpression(SyntaxKind.CharacterLiteralExpression, SyntaxFactory.Literal(c))
                        .ToFullString(),
                    string s => SyntaxFactory
                        .LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(s)).ToFullString(),
                    int => $"{value}",
                    uint => $"{value}u",
                    long => $"{value}L",
                    ulong => $"{value}UL",
                    float => $"{value}f",
                    double => $"{value}d",
                    decimal => $"{value}m",
                    _ => $"({arg.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){value}"
                });
                break;
            }
            case TypedConstantKind.Enum:
                sb.Append($"({arg.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){arg.Value}");
                break;
            case TypedConstantKind.Type:
                sb.Append(arg.Value is null
                    ? "null"
                    : $"typeof({((ISymbol)arg.Value).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})");
                break;
            case TypedConstantKind.Array:
            {
                var first = true;
                sb.Append("[");
                foreach (var a in arg.Values)
                {
                    if (first) first = false;
                    else sb.Append(", ");
                    BuildAttrArg(sb, a);
                }
                sb.Append("]");
                break;
            }
            default:
                sb.Append(" ");
                break;
        }
    }

    private static string BuildAttrCtor(AttributeData attr)
    {
        var sb = new StringBuilder();
        sb.Append("new(");
        var first = true;
        foreach (var arg in attr.ConstructorArguments)
        {
            if (first) first = false;
            else sb.Append(", ");
            BuildAttrArg(sb, arg);
        }
        if (attr.NamedArguments.Length > 0)
        {
            first = true;
            sb.Append(" {");
            foreach (var kv in attr.NamedArguments)
            {
                if (first) first = false;
                else sb.Append(", ");
                var name = kv.Key;
                var arg = kv.Value;
                sb.Append($"{name} = ");
                BuildAttrArg(sb, arg);
            }
            sb.Append(" }");
        }
        sb.Append(")");
        return sb.ToString();
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var sources = context.SyntaxProvider.ForAttributeWithMetadataName("Coplt.Systems.SystemAttribute",
            static (syntax, _) =>
                syntax is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax,
            static (ctx, _) =>
            {
                var diagnostics = new List<Diagnostic>();
                var attr = ctx.Attributes.First();
                var syntax = (TypeDeclarationSyntax)ctx.TargetNode;
                var symbol = (INamedTypeSymbol)ctx.TargetSymbol;
                var nullable = ctx.SemanticModel.Compilation.Options.NullableContextOptions;
                var rawFullName = symbol.ToDisplayString();
                var nameWraps = symbol.WrapNames();
                var nameWrap = symbol.WrapName();

                var usings = new HashSet<string>();
                Utils.GetUsings(syntax, usings);
                var genBase = new GenBase(rawFullName, nullable, usings, nameWraps, nameWrap);

                #region Meta

                var meta = new SystemMeta();
                {
                    var attr_args = attr.NamedArguments.ToDictionary(static a => a.Key, static a => a.Value);
                    if (attr_args.TryGetValue("Partition", out var Partition))
                        meta.Partition = Partition.Value is long p ? p : 0;
                    if (attr_args.TryGetValue("Group", out var Group))
                        meta.Group = ((ITypeSymbol)Group.Value!)
                            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (attr_args.TryGetValue("Before", out var Before))
                        meta.Before =
                        [
                            ..Before.Values.Select(static a =>
                                ((ITypeSymbol)a.Value!).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        ];
                    if (attr_args.TryGetValue("After", out var After))
                        meta.After =
                        [
                            ..After.Values.Select(static a =>
                                ((ITypeSymbol)a.Value!).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        ];
                }

                #endregion

                #region IsGroup

                var is_group =
                    symbol.AllInterfaces.Any(static s => s.ToDisplayString() == "Coplt.Systems.ISystemGroup");
                var is_system = symbol.AllInterfaces.Any(static s => s.ToDisplayString() == "Coplt.Systems.ISystem");

                #endregion

                #region props

                var props = symbol.GetMembers()
                    .Where(static s => s is IPropertySymbol)
                    .Cast<IPropertySymbol>()
                    .Where(static s => s is { CanBeReferencedByName : true, IsStatic : false })
                    .Select(static s =>
                    {
                        var attr = s.GetAttributes()
                            .FirstOrDefault(static a =>
                                a.AttributeClass?.ToDisplayString() == "Coplt.Systems.InjectAttribute");
                        var rp = s.GetAttributes()
                            .Where(static a => a.AttributeClass != null)
                            .Select(static a =>
                                IsResourceProviderAttribute(a.AttributeClass!, out var target) ? (target, a) : default)
                            .FirstOrDefault(static a => a.target != null);
                        return (
                            s, attr,
                            rp: rp.target?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            rpa: rp.a
                        );
                    })
                    .Where(static a => a.attr != null || a.s is { IsPartialDefinition: true })
                    .Select(static a =>
                    {
                        var args = a.attr?.NamedArguments.ToDictionary(static a => a.Key, static a => a.Value);
                        var exclude =
                            args != null && args.TryGetValue("Exclude", out var exclude_c) &&
                            exclude_c.Value is true;
                        return (a.s, a.rp, a.rpa, exclude);
                    })
                    .Where(static a => !a.exclude)
                    .Select(static a => new Injection(a.s.Name,
                        a.s.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        a.s.Type.ToDisplayString(NullableFullyQualifiedFormat),
                        a.rp is null
                            ? default
                            : new(
                                a.rp,
                                a.rpa.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ??
                                "void",
                                BuildAttrCtor(a.rpa)
                            ),
                        a.s.DeclaredAccessibility, a.s.Type.IsValueType,
                        a.s.ReturnsByRef || a.s.ReturnsByRefReadonly, a.s.ReturnsByRefReadonly,
                        a.s.GetMethod is not null,
                        a.s.SetMethod is not null))
                    .ToImmutableArray();

                #endregion

                #region setups

                var setups = symbol.GetMembers()
                    .Where(static s => s is IMethodSymbol)
                    .Cast<IMethodSymbol>()
                    .Where(static s => s is { CanBeReferencedByName : true, IsStatic : false, IsGenericMethod: false })
                    .Select(static s =>
                    {
                        var attr = s.GetAttributes()
                            .FirstOrDefault(static a =>
                                a.AttributeClass?.ToDisplayString() == "Coplt.Systems.SetupAttribute");
                        return (s, attr);
                    })
                    .Where(static a => a.s.Name == "Setup" || a.attr != null)
                    .Select(static a =>
                    {
                        var args = a.attr?.NamedArguments.ToDictionary(static a => a.Key, static a => a.Value);
                        var order = args != null && args
                            .TryGetValue("Order", out var order_c) && order_c.Value is int order_i
                            ? order_i
                            : 0;
                        var exclude =
                            args != null && args.TryGetValue("Exclude", out var exclude_c) &&
                            exclude_c.Value is true;
                        return (a.s, order, exclude);
                    })
                    .Where(static a => !a.exclude)
                    .OrderBy(static a => a.order)
                    .Select(static a =>
                    {
                        var args = a.s.Parameters.Select(static p =>
                            {
                                var rp = p.GetAttributes()
                                    .Where(static a => a.AttributeClass != null)
                                    .Select(static a =>
                                        IsResourceProviderAttribute(a.AttributeClass!, out var target)
                                            ? (target, a)
                                            : default)
                                    .FirstOrDefault(static a => a.target != null);
                                return new Arg(
                                    p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    p.RefKind,
                                    rp.target is null
                                        ? default
                                        : new(
                                            rp.target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                            rp.a.AttributeClass?.ToDisplayString(SymbolDisplayFormat
                                                .FullyQualifiedFormat) ?? "void",
                                            BuildAttrCtor(rp.a)
                                        )
                                );
                            })
                            .ToImmutableArray();
                        return new InjectedMethod(a.s.Name, args);
                    })
                    .ToImmutableArray();

                #endregion

                #region updates

                var updates = symbol.GetMembers()
                    .Where(static s => s is IMethodSymbol)
                    .Cast<IMethodSymbol>()
                    .Where(static s => s is { CanBeReferencedByName : true, IsStatic : false, IsGenericMethod: false })
                    .Select(static s =>
                    {
                        var attr = s.GetAttributes()
                            .FirstOrDefault(a =>
                                a.AttributeClass?.ToDisplayString() == "Coplt.Systems.UpdateAttribute");
                        return (s, attr);
                    })
                    .Where(static a => a.s.Name == "Update" || a.attr != null)
                    .Select(static a =>
                    {
                        var args = a.attr?.NamedArguments.ToDictionary(static a => a.Key, static a => a.Value);
                        var order = args != null && args
                            .TryGetValue("Order", out var order_c) && order_c.Value is int order_i
                            ? order_i
                            : 0;
                        var exclude =
                            args != null && args.TryGetValue("Exclude", out var exclude_c) &&
                            exclude_c.Value is true;
                        return (a.s, order, exclude);
                    })
                    .Where(static a => !a.exclude)
                    .OrderBy(static a => a.order)
                    .Select(static a =>
                    {
                        var args = a.s.Parameters.Select(static p =>
                            {
                                var rp = p.GetAttributes()
                                    .Where(static a => a.AttributeClass != null)
                                    .Select(static a =>
                                        IsResourceProviderAttribute(a.AttributeClass!, out var target)
                                            ? (target, a)
                                            : default)
                                    .FirstOrDefault(static a => a.target != null);
                                return new Arg(
                                    p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    p.RefKind,
                                    rp.target is null
                                        ? default
                                        : new(
                                            rp.target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                            rp.a.AttributeClass?.ToDisplayString(SymbolDisplayFormat
                                                .FullyQualifiedFormat) ?? "void",
                                            BuildAttrCtor(rp.a)
                                        )
                                );
                            })
                            .ToImmutableArray();
                        return new InjectedMethod(a.s.Name, args);
                    })
                    .ToImmutableArray();

                #endregion

                #region dispose

                var has_dispose =
                    symbol
                        .GetAttributes()
                        .Any(static a =>
                            a.AttributeClass?.ToDisplayString() == "Coplt.Dropping.DroppingAttribute")
                    || symbol.GetMembers()
                        .Where(static s => s is IMethodSymbol)
                        .Cast<IMethodSymbol>()
                        .Any(static s => s is
                        {
                            Name: "Dispose",
                            TypeParameters: [],
                            Parameters: [],
                            ReturnType.SpecialType: SpecialType.System_Void,
                        });

                #endregion

                #region Ctor Args

                var ctor = symbol.Constructors.FirstOrDefault();
                var ctor_args = ctor == null
                    ? []
                    : ctor.Parameters.Select(static a =>
                        {
                            var rp = a.GetAttributes()
                                .Where(static a => a.AttributeClass != null)
                                .Select(static a =>
                                    IsResourceProviderAttribute(a.AttributeClass!, out var target)
                                        ? (target, a)
                                        : default)
                                .FirstOrDefault(static a => a.target != null);
                            return new Arg(
                                a.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                a.RefKind,
                                rp.target is null
                                    ? default
                                    : new(
                                        rp.target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                        rp.a.AttributeClass?.ToDisplayString(SymbolDisplayFormat
                                            .FullyQualifiedFormat) ?? "void",
                                        BuildAttrCtor(rp.a)
                                    )
                            );
                        })
                        .ToImmutableArray();

                #endregion

                return (genBase, symbol.Name, meta, is_group, is_system, symbol.IsValueType, symbol.IsReadOnly,
                    has_dispose, props, setups, updates, ctor_args, AlwaysEq.Create(diagnostics));
            }
        );
        context.RegisterSourceOutput(sources, static (ctx, input) =>
        {
            var (genBase, name, meta, is_group, is_system, isStruct, readOnly, has_dispose,
                    props, setups, updates, ctor_args, diagnostics) =
                input;
            if (diagnostics.Value.Count > 0)
            {
                foreach (var diagnostic in diagnostics.Value)
                {
                    ctx.ReportDiagnostic(diagnostic);
                }
            }
            var code =
                new SystemTemplate(genBase, name, meta, is_group, is_system, isStruct, readOnly,
                    has_dispose, props, setups, updates, ctor_args
                ).Gen();
            var sourceText = SourceText.From(code, Encoding.UTF8);
            var rawSourceFileName = genBase.FileFullName;
            var sourceFileName = $"{rawSourceFileName}.system.g.cs";
            ctx.AddSource(sourceFileName, sourceText);
        });
    }
}
