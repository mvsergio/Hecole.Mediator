using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Hecole.Mediator.Analyzers;

/// <summary>
/// Restrictive rules, all off by default: HM1001 (assembly scanning), HM1002 (Singleton lifetime), HM1003 (pipeline
/// behaviors outside the composition project) and HM1004 (TaskWhenAllPublisher). Each reports a legitimate library
/// feature that a project may choose to forbid. The rules report references to symbols, wherever they appear, rather than
/// the shape of a call; actions are registered only for the rules that are on somewhere in the compilation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RestrictedUsageAnalyzer : DiagnosticAnalyzer
{
    private const string TaskWhenAllPublisherName = "TaskWhenAllPublisher";

    private static readonly ImmutableHashSet<string> BehaviorMethods =
        ImmutableHashSet.Create(System.StringComparer.Ordinal, "AddBehavior", "AddOpenBehavior", "AddOpenBehaviors");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Descriptors.AssemblyScanning,
        Descriptors.SingletonLifetime,
        Descriptors.BehaviorRegistration,
        Descriptors.ParallelPublisher);

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
        if (symbols is null)
        {
            return;
        }

        var compilation = context.Compilation;
        var token = context.CancellationToken;
        var scanning = AnalyzerHelpers.MayBeOn(compilation, Descriptors.AssemblyScanning, token);
        var singleton = AnalyzerHelpers.MayBeOn(compilation, Descriptors.SingletonLifetime, token);
        var behaviors = AnalyzerHelpers.MayBeOn(compilation, Descriptors.BehaviorRegistration, token) && symbols.PipelineBehavior is not null;
        var publisher = AnalyzerHelpers.MayBeOn(compilation, Descriptors.ParallelPublisher, token) && symbols.TaskWhenAllPublisher is not null;

        if (symbols.Configuration is { } configuration)
        {
            // HM1001 and HM1003: configuration calls and method groups.
            if (scanning || behaviors)
            {
                context.RegisterOperationAction(
                    operationContext => AnalyzeConfigurationMember(operationContext, configuration),
                    OperationKind.Invocation,
                    OperationKind.MethodReference);
            }

            // HM1001, HM1002, HM1003: the configuration used through dynamic hides every later call from them.
            var dynamicDescriptors = ImmutableArray.CreateBuilder<(DiagnosticDescriptor Descriptor, string Message)>();
            if (scanning)
            {
                dynamicDescriptors.Add((Descriptors.AssemblyScanning, "HecoleMediatorConfiguration is used through dynamic, so assembly scanning calls cannot be seen"));
            }

            if (singleton)
            {
                dynamicDescriptors.Add((Descriptors.SingletonLifetime, "HecoleMediatorConfiguration is used through dynamic, so the Lifetime it receives cannot be seen"));
            }

            if (behaviors)
            {
                dynamicDescriptors.Add((Descriptors.BehaviorRegistration, "HecoleMediatorConfiguration is used through dynamic, so behavior registrations cannot be seen"));
            }

            if (dynamicDescriptors.Count > 0)
            {
                var reported = dynamicDescriptors.ToImmutable();
                context.RegisterOperationAction(
                    operationContext => AnalyzeDynamicConversion(operationContext, configuration, reported),
                    OperationKind.Conversion);
            }

            // HM1002: assignments to Lifetime.
            if (singleton
                && configuration.GetMembers("Lifetime").FirstOrDefault() is IPropertySymbol lifetime
                && lifetime.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } lifetimeType
                && Constant(lifetimeType, "Singleton") is { } singletonValue)
            {
                var allowed = new[] { Constant(lifetimeType, "Transient"), Constant(lifetimeType, "Scoped") }
                    .Where(value => value is not null)
                    .ToImmutableArray();
                var lifetimeRule = new LifetimeAssignment(lifetime, singletonValue, allowed);
                context.RegisterOperationAction(
                    lifetimeRule.Analyze,
                    OperationKind.SimpleAssignment,
                    OperationKind.CompoundAssignment,
                    OperationKind.Increment,
                    OperationKind.Decrement,
                    OperationKind.DeconstructionAssignment);
            }
        }

        // HM1003: behavior declarations and every typeof / generic type argument that mentions a behavior.
        if (behaviors)
        {
            var references = new BehaviorReferences(symbols.PipelineBehavior!, symbols.Configuration);
            context.RegisterSymbolAction(references.AnalyzeDeclaration, SymbolKind.NamedType);
            context.RegisterSyntaxNodeAction(references.AnalyzeTypeReference, SyntaxKind.TypeArgumentList, SyntaxKind.TypeOfExpression);
        }

        // HM1004: every reference to TaskWhenAllPublisher, by its name or through a using alias declared anywhere.
        if (publisher)
        {
            var target = symbols.TaskWhenAllPublisher!;
            var names = AnalyzerHelpers.CollectAliasNames(compilation, token);
            names.Add(TaskWhenAllPublisherName);
            context.RegisterSyntaxNodeAction(
                syntaxContext => AnalyzePublisherReference(syntaxContext, target, names),
                SyntaxKind.IdentifierName);
        }
    }

    private static object? Constant(INamedTypeSymbol enumType, string name)
        => enumType.GetMembers(name).FirstOrDefault() is IFieldSymbol { HasConstantValue: true } field ? field.ConstantValue : null;

    // HM1001 and the cfg.AddBehavior / AddOpenBehavior / AddOpenBehaviors part of HM1003 (calls and method groups).
    private static void AnalyzeConfigurationMember(OperationAnalysisContext context, INamedTypeSymbol configuration)
    {
        var method = context.Operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IMethodReferenceOperation reference => reference.Method,
            _ => null,
        };

        if (method is null || !SymbolEqualityComparer.Default.Equals(method.ContainingType, configuration))
        {
            return;
        }

        var (descriptor, message) = method.Name switch
        {
            "RegisterServicesFromAssembly" or "RegisterServicesFromAssemblies" or "RegisterServicesFromAssemblyContaining"
                => (Descriptors.AssemblyScanning, $"'{method.Name}' scans an assembly"),
            _ when BehaviorMethods.Contains(method.Name) => (Descriptors.BehaviorRegistration, $"'{method.Name}' registers a pipeline behavior"),
            _ => (null, ""),
        };

        if (descriptor is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor, context.Operation.Syntax.GetLocation(), message));
        }
    }

    // HM1001, HM1002, HM1003: an expression of type HecoleMediatorConfiguration converted to dynamic (assignment,
    // argument, cast). Every member used afterwards is bound at run time, out of reach of the analyzer.
    private static void AnalyzeDynamicConversion(
        OperationAnalysisContext context,
        INamedTypeSymbol configuration,
        ImmutableArray<(DiagnosticDescriptor Descriptor, string Message)> reported)
    {
        var conversion = (IConversionOperation)context.Operation;
        if (conversion.Type?.TypeKind != TypeKind.Dynamic
            || !SymbolEqualityComparer.Default.Equals(conversion.Operand.Type, configuration))
        {
            return;
        }

        var location = conversion.Syntax.GetLocation();
        foreach (var (descriptor, message) in reported)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, message));
        }
    }

    // HM1004: every reference to TaskWhenAllPublisher (constructor, typeof, generic argument, base type, using alias and
    // uses of the alias), except XML documentation and nameof.
    private static void AnalyzePublisherReference(SyntaxNodeAnalysisContext context, INamedTypeSymbol publisher, HashSet<string> names)
    {
        var name = (IdentifierNameSyntax)context.Node;
        if (!names.Contains(name.Identifier.ValueText) || name.IsPartOfStructuredTrivia() || AnalyzerHelpers.IsAliasDeclarationName(name))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
        {
            symbol = constructor.ContainingType;
        }

        if (!SymbolEqualityComparer.Default.Equals(symbol, publisher)
            || AnalyzerHelpers.IsInsideNameOf(name, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Descriptors.ParallelPublisher, name.GetLocation()));
    }

    // HM1002: any assignment to HecoleMediatorConfiguration.Lifetime, unless the value is a compile-time constant equal to
    // Transient or Scoped. Simple assignment (statement, lambda, object initializer), compound assignment, ++/--, and
    // deconstruction target.
    private sealed class LifetimeAssignment(IPropertySymbol lifetime, object singleton, ImmutableArray<object?> allowed)
    {
        private const string SingletonMessage = "HecoleMediatorConfiguration.Lifetime is set to ServiceLifetime.Singleton";

        private const string UnprovenMessage = "HecoleMediatorConfiguration.Lifetime is set to a value that cannot be proven not to be "
            + "ServiceLifetime.Singleton; assign the constant ServiceLifetime.Transient or ServiceLifetime.Scoped";

        public void Analyze(OperationAnalysisContext context)
        {
            switch (context.Operation)
            {
                case ISimpleAssignmentOperation assignment when IsLifetime(assignment.Target):
                    Check(context, assignment.Value, assignment.Syntax);
                    break;
                case ICompoundAssignmentOperation compound when IsLifetime(compound.Target):
                    Report(context, compound.Syntax, UnprovenMessage);
                    break;
                case IIncrementOrDecrementOperation increment when IsLifetime(increment.Target):
                    Report(context, increment.Syntax, UnprovenMessage);
                    break;
                case IDeconstructionAssignmentOperation deconstruction:
                    CheckTuple(context, deconstruction.Target, deconstruction.Value);
                    break;
            }
        }

        // Pairs each target element with the matching element of a tuple literal value; any other value is unknown.
        private void CheckTuple(OperationAnalysisContext context, IOperation target, IOperation? value)
        {
            if (IsLifetime(target))
            {
                Check(context, value, target.Syntax);
                return;
            }

            if (target is not ITupleOperation targets)
            {
                return;
            }

            var values = AnalyzerHelpers.WithoutConversions(value) as ITupleOperation;
            for (var i = 0; i < targets.Elements.Length; i++)
            {
                var element = values is not null && values.Elements.Length == targets.Elements.Length ? values.Elements[i] : null;
                CheckTuple(context, targets.Elements[i], element);
            }
        }

        private void Check(OperationAnalysisContext context, IOperation? value, SyntaxNode syntax)
        {
            var constant = value?.ConstantValue ?? default;
            if (constant.HasValue && allowed.Any(item => Equals(item, constant.Value)))
            {
                return;
            }

            Report(context, syntax, constant.HasValue && Equals(constant.Value, singleton) ? SingletonMessage : UnprovenMessage);
        }

        private bool IsLifetime(IOperation target)
            => target is IPropertyReferenceOperation reference
               && SymbolEqualityComparer.Default.Equals(reference.Property.OriginalDefinition, lifetime);

        private static void Report(OperationAnalysisContext context, SyntaxNode syntax, string message)
            => context.ReportDiagnostic(Diagnostic.Create(Descriptors.SingletonLifetime, syntax.GetLocation(), message));
    }

    // HM1003 by symbol: (a) a type declaration that implements IPipelineBehavior<,>; (b) every typeof and every generic type
    // argument (the outermost one, at any depth of the syntax) whose type is IPipelineBehavior<,> (open or closed), a type
    // implementing it, or a constructed type containing one of those. Sites inside a reported configuration call
    // (cfg.AddBehavior...) or inside the body of a reported behavior declaration are not reported again.
    private sealed class BehaviorReferences(INamedTypeSymbol behavior, INamedTypeSymbol? configuration)
    {
        private readonly ConcurrentDictionary<ITypeSymbol, bool> _cache = new(SymbolEqualityComparer.Default);

        public void AnalyzeDeclaration(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;
            if (!Implements(type) || type.Locations.FirstOrDefault() is not { IsInSource: true } location)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.BehaviorRegistration,
                location,
                type.Locations.Skip(1).Where(other => other.IsInSource),
                $"'{type.Name}' declares a pipeline behavior"));
        }

        public void AnalyzeTypeReference(SyntaxNodeAnalysisContext context)
        {
            if (context.Node.IsPartOfStructuredTrivia())
            {
                return;
            }

            if (context.Node is TypeOfExpressionSyntax typeOf)
            {
                if (TypeOf(context, typeOf.Type) is { } type && Contains(type))
                {
                    Report(context, typeOf);
                }

                return;
            }

            var list = (TypeArgumentListSyntax)context.Node;
            if (IsNestedInTypeReference(list))
            {
                return;
            }

            foreach (var argument in list.Arguments)
            {
                if (argument is not OmittedTypeArgumentSyntax && TypeOf(context, argument) is { } type && Contains(type))
                {
                    Report(context, argument);
                }
            }
        }

        private void Report(SyntaxNodeAnalysisContext context, SyntaxNode site)
        {
            if (IsInsideBehaviorDeclaration(context.ContainingSymbol) || IsInsideConfigurationCall(site, context))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.BehaviorRegistration,
                site.GetLocation(),
                $"'{site}' refers to a pipeline behavior type"));
        }

        private static ITypeSymbol? TypeOf(SyntaxNodeAnalysisContext context, TypeSyntax syntax)
            => context.SemanticModel.GetTypeInfo(syntax, context.CancellationToken).Type
               ?? context.SemanticModel.GetSymbolInfo(syntax, context.CancellationToken).Symbol as ITypeSymbol;

        // A type argument list inside another type argument or a typeof: the enclosing site already covers it.
        private static bool IsNestedInTypeReference(SyntaxNode list)
        {
            for (var node = list.Parent?.Parent; node is TypeSyntax or TypeArgumentListSyntax or TupleElementSyntax or TypeOfExpressionSyntax; node = node.Parent)
            {
                if (node is TypeArgumentListSyntax or TypeOfExpressionSyntax)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsInsideBehaviorDeclaration(ISymbol? symbol)
        {
            for (var type = symbol as INamedTypeSymbol ?? symbol?.ContainingType; type is not null; type = type.ContainingType)
            {
                if (Implements(type))
                {
                    return true;
                }
            }

            return false;
        }

        // cfg.AddBehavior<X>(), cfg.AddBehavior(typeof(...)), cfg.AddOpenBehaviors([...]), and their method groups: the
        // call itself is reported.
        private bool IsInsideConfigurationCall(SyntaxNode site, SyntaxNodeAnalysisContext context)
        {
            if (configuration is null)
            {
                return false;
            }

            foreach (var ancestor in site.Ancestors())
            {
                if (ancestor is StatementSyntax or MemberDeclarationSyntax)
                {
                    return false;
                }

                // The method group (type arguments of cfg.AddBehavior<X>) or the call (its arguments).
                var name = (ancestor is InvocationExpressionSyntax invocation ? invocation.Expression : ancestor) switch
                {
                    MemberAccessExpressionSyntax access => access.Name,
                    MemberBindingExpressionSyntax binding => binding.Name,
                    _ => null,
                };

                if (name is not null
                    && BehaviorMethods.Contains(name.Identifier.ValueText)
                    && context.SemanticModel.GetSymbolInfo(ancestor, context.CancellationToken) is var info
                    && (info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) is IMethodSymbol method
                    && SymbolEqualityComparer.Default.Equals(method.ContainingType, configuration))
                {
                    return true;
                }
            }

            return false;
        }

        private bool Implements(INamedTypeSymbol type)
        {
            if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, behavior))
            {
                return true;
            }

            foreach (var candidate in type.OriginalDefinition.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, behavior))
                {
                    return true;
                }
            }

            return false;
        }

        private bool Contains(ITypeSymbol type)
        {
            if (_cache.TryGetValue(type, out var known))
            {
                return known;
            }

            var result = type switch
            {
                INamedTypeSymbol named => Implements(named)
                                          || (!named.IsUnboundGenericType && named.TypeArguments.Any(Contains))
                                          || (named.ContainingType is { } containing && Contains(containing)),
                IArrayTypeSymbol array => Contains(array.ElementType),
                IPointerTypeSymbol pointer => Contains(pointer.PointedAtType),
                _ => false,
            };

            _cache.TryAdd(type, result);
            return result;
        }
    }
}
