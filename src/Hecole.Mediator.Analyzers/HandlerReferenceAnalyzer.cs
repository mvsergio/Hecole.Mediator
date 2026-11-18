using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hecole.Mediator.Analyzers;

/// <summary>
/// HM1006 (off by default): every reference to <c>IRequestHandler&lt;,&gt;</c> or <c>IRequestHandler&lt;&gt;</c> outside
/// a handler declaration. Kept apart from the other analyzers so it costs nothing while the rule is off.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HandlerReferenceAnalyzer : DiagnosticAnalyzer
{
    private const string HandlerName = "IRequestHandler";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.HandlerInterfaceReference);

    public override void Initialize(AnalysisContext context)
    {
        // Generated code is analyzed and reported on: a restrictive rule must not be silenced by a generated-code header.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var symbols = MediatorSymbols.TryCreate(context.Compilation);
        if (symbols is null || !AnalyzerHelpers.MayBeOn(context.Compilation, Descriptors.HandlerInterfaceReference, context.CancellationToken))
        {
            return;
        }

        // The interface written by name (GenericNameSyntax, any position), and the uses of a using alias declared anywhere.
        context.RegisterSyntaxNodeAction(syntaxContext => Analyze(syntaxContext, symbols), SyntaxKind.GenericName);

        var aliases = AnalyzerHelpers.CollectAliasNames(context.Compilation, context.CancellationToken);
        if (aliases.Count > 0)
        {
            context.RegisterSyntaxNodeAction(syntaxContext => AnalyzeAlias(syntaxContext, symbols, aliases), SyntaxKind.IdentifierName);
        }
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, MediatorSymbols symbols)
    {
        var name = (GenericNameSyntax)context.Node;
        if (name.Identifier.ValueText == HandlerName)
        {
            Check(context, symbols, name);
        }
    }

    private static void AnalyzeAlias(SyntaxNodeAnalysisContext context, MediatorSymbols symbols, HashSet<string> aliases)
    {
        var name = (IdentifierNameSyntax)context.Node;
        if (aliases.Contains(name.Identifier.ValueText) && !AnalyzerHelpers.IsAliasDeclarationName(name))
        {
            Check(context, symbols, name);
        }
    }

    private static void Check(SyntaxNodeAnalysisContext context, MediatorSymbols symbols, SimpleNameSyntax name)
    {
        if (name.IsPartOfStructuredTrivia()
            || context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol is not INamedTypeSymbol type
            || !symbols.IsHandlerDefinition(type))
        {
            return;
        }

        var position = AnalyzerHelpers.TypePosition(name);
        if (IsHandlerDeclaration(position) || IsDecoratorMember(position, type, context))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Descriptors.HandlerInterfaceReference, name.GetLocation(), name.ToString()));
    }

    // Base list of a type declaration (class QueryHandler : IRequestHandler<Query, int>), explicit interface implementation
    // (Task<int> IRequestHandler<Query, int>.Handle(...)) and generic constraint (where THandler : IRequestHandler<...>):
    // they declare or constrain a handler, they do not call or register one.
    private static bool IsHandlerDeclaration(SyntaxNode position)
        => AnalyzerHelpers.IsBaseType(position)
           || position.Parent is ExplicitInterfaceSpecifierSyntax
           || position.Parent is TypeConstraintSyntax;

    // Decorator: a public constructor parameter of the handler interface the class implements, and a field or property of
    // that interface declared in such a class (the one storing the inner handler).
    private static bool IsDecoratorMember(SyntaxNode position, INamedTypeSymbol type, SyntaxNodeAnalysisContext context)
    {
        INamedTypeSymbol? container;
        switch (position.Parent)
        {
            case ParameterSyntax parameter when parameter.Type == position:
                if (context.SemanticModel.GetDeclaredSymbol(parameter, context.CancellationToken) is not IParameterSymbol
                    {
                        ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor, DeclaredAccessibility: Accessibility.Public } constructor,
                    })
                {
                    return false;
                }

                container = constructor.ContainingType;
                break;
            case VariableDeclarationSyntax { Parent: FieldDeclarationSyntax } variable when variable.Type == position:
            case PropertyDeclarationSyntax property when property.Type == position:
                container = context.ContainingSymbol as INamedTypeSymbol ?? context.ContainingSymbol?.ContainingType;
                break;
            default:
                return false;
        }

        return container is not null
               && container.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, type))
               && MediatorSymbols.IsDecoratorOf(container, type);
    }
}
