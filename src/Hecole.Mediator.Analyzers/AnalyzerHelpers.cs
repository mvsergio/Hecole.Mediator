using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Hecole.Mediator.Analyzers;

/// <summary>Helpers shared by the analyzers.</summary>
internal static class AnalyzerHelpers
{
    /// <summary>
    /// True when <paramref name="descriptor"/> may be reported in this compilation: its effective severity is not
    /// <c>none</c> in the compilation options, in a global configuration or in the configuration of at least one syntax
    /// tree. Restrictive rules are off by default, so a rule nobody turned on costs nothing: its actions are not
    /// registered. The compiler still applies the per-file severity to whatever is reported.
    /// </summary>
    public static bool MayBeOn(Compilation compilation, DiagnosticDescriptor descriptor, CancellationToken cancellationToken)
    {
        if (IsOn(descriptor.GetEffectiveSeverity(compilation.Options)))
        {
            return true;
        }

        var provider = compilation.Options.SyntaxTreeOptionsProvider;
        if (provider is null)
        {
            return false;
        }

        if (provider.TryGetGlobalDiagnosticValue(descriptor.Id, cancellationToken, out var global) && IsOn(global))
        {
            return true;
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            if (provider.TryGetDiagnosticValue(tree, descriptor.Id, cancellationToken, out var value) && IsOn(value))
            {
                return true;
            }
        }

        return false;

        static bool IsOn(ReportDiagnostic value) => value is not (ReportDiagnostic.Suppress or ReportDiagnostic.Default);
    }

    /// <summary>
    /// Names of every <c>using</c> alias (global or not, at any namespace level) declared in the compilation, collected
    /// once from the using directives only. A syntax action binds an identifier only when its text is one of these names
    /// (or the name it looks for), so an alias declared in another file is still resolved without binding every identifier.
    /// </summary>
    public static HashSet<string> CollectAliasNames(Compilation compilation, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (tree.GetRoot(cancellationToken) is CompilationUnitSyntax root)
            {
                Collect(root.Usings, root.Members, names);
            }
        }

        return names;

        static void Collect(SyntaxList<UsingDirectiveSyntax> usings, SyntaxList<MemberDeclarationSyntax> members, HashSet<string> names)
        {
            foreach (var directive in usings)
            {
                if (directive.Alias is { } alias)
                {
                    names.Add(alias.Name.Identifier.ValueText);
                }
            }

            foreach (var member in members)
            {
                if (member is BaseNamespaceDeclarationSyntax ns)
                {
                    Collect(ns.Usings, ns.Members, names);
                }
            }
        }
    }

    /// <summary>True when the identifier is the alias being declared (<c>using Name = ...;</c>), not a use of it.</summary>
    public static bool IsAliasDeclarationName(IdentifierNameSyntax name)
        => name.Parent is NameEqualsSyntax { Parent: UsingDirectiveSyntax };

    /// <summary>True when <paramref name="node"/> is inside a <c>nameof(...)</c> of the same statement or member.</summary>
    public static bool IsInsideNameOf(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } invocation
                && model.GetOperation(invocation, cancellationToken) is INameOfOperation)
            {
                return true;
            }

            if (ancestor is StatementSyntax or MemberDeclarationSyntax)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Climbs from a type name to the outermost syntax that still denotes the same type: through a qualified or
    /// alias-qualified name of which it is the right part, and a nullable annotation.
    /// </summary>
    public static SyntaxNode TypePosition(SyntaxNode name)
    {
        var node = name;
        while (true)
        {
            switch (node.Parent)
            {
                case QualifiedNameSyntax qualified when qualified.Right == node:
                case AliasQualifiedNameSyntax aliased when aliased.Name == node:
                case NullableTypeSyntax:
                    node = node.Parent;
                    continue;
                default:
                    return node;
            }
        }
    }

    /// <summary>True when the type position is a base type in the base list of a type declaration (class, struct, interface, record).</summary>
    public static bool IsBaseType(SyntaxNode typePosition)
        => typePosition.Parent is BaseTypeSyntax { Parent: BaseListSyntax { Parent: TypeDeclarationSyntax } };

    /// <summary>Strips the conversions wrapped around <paramref name="operation"/>.</summary>
    public static IOperation? WithoutConversions(IOperation? operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }
}
