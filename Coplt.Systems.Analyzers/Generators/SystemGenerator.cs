using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Coplt.Analyzers.Generators.Templates;
using Coplt.Analyzers.Utilities;
using Coplt.Systems.Analyzers.Generators.Templates;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Coplt.Systems.Analyzers.Generators;

[Generator]
public class SystemGenerator : IIncrementalGenerator
{
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
                    if (attr_args.TryGetValue("Parallel", out var Parallel))
                        meta.Parallel = Partition.Value is true;
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
                            .FirstOrDefault(a =>
                                a.AttributeClass?.ToDisplayString() == "Coplt.Systems.InjectAttribute");
                        return (s, attr);
                    })
                    .Where(static a => a.attr != null || a.s is { IsPartialDefinition: true })
                    .Select(static a =>
                    {
                        var args = a.attr?.NamedArguments.ToDictionary(static a => a.Key, static a => a.Value);
                        var exclude =
                            args != null && args.TryGetValue("Exclude", out var exclude_c) &&
                            exclude_c.Value is true;
                        return (a.s, exclude);
                    })
                    .Where(static a => !a.exclude)
                    .Select(static a => new Injection(a.s.Name,
                        a.s.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
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
                            .FirstOrDefault(a =>
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
                        var args = a.s.Parameters.Select(p =>
                                new Arg(p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), p.RefKind))
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
                        var args = a.s.Parameters.Select(p =>
                                new Arg(p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), p.RefKind))
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
                            new Arg(a.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), a.RefKind))
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
