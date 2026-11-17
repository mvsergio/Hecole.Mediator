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
/// behavior registration) and HM1004 (TaskWhenAllPublisher). Each reports a legitimate library feature that a project
/// may choose to forbid.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RestrictedUsageAnalyzer : DiagnosticAnalyzer
{
    private const string TaskWhenAllPublisherName = "TaskWhenAllPublisher";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Descriptors.AssemblyScanning,
        Descriptors.SingletonLifetime,
        Descriptors.BehaviorRegistration,
        Descriptors.ParallelPublisher);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
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

        var configuration = symbols.Configuration;
        if (configuration is not null)
        {
            context.RegisterOperationAction(
                operationContext => AnalyzeConfigurationMember(operationContext, configuration),
                OperationKind.Invocation,
                OperationKind.MethodReference);

            if (configuration.GetMembers("Lifetime").FirstOrDefault() is IPropertySymbol lifetime
                && lifetime.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } lifetimeType
                && lifetimeType.GetMembers("Singleton").FirstOrDefault() is IFieldSymbol { HasConstantValue: true } singleton)
            {
                var singletonValue = singleton.ConstantValue;
                context.RegisterOperationAction(
                    operationContext => AnalyzeLifetimeAssignment(operationContext, lifetime, singletonValue),
                    OperationKind.SimpleAssignment);
            }
        }

        if (symbols.PipelineBehavior is { } behavior)
        {
            var registration = new ContainerRegistration(behavior, symbols.ServiceCollection, symbols.ServiceDescriptor);
            context.RegisterOperationAction(registration.Analyze, OperationKind.Invocation, OperationKind.ObjectCreation);
        }

        if (symbols.TaskWhenAllPublisher is { } publisher)
        {
            context.RegisterSyntaxNodeAction(
                syntaxContext => AnalyzePublisherReference(syntaxContext, publisher),
                SyntaxKind.IdentifierName);
        }
    }

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

        var descriptor = method.Name switch
        {
            "RegisterServicesFromAssembly" or "RegisterServicesFromAssemblies" or "RegisterServicesFromAssemblyContaining"
                => Descriptors.AssemblyScanning,
            "AddBehavior" or "AddOpenBehavior" or "AddOpenBehaviors" => Descriptors.BehaviorRegistration,
            _ => null,
        };

        if (descriptor is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor, context.Operation.Syntax.GetLocation(), method.Name));
        }
    }

    // HM1002: a constant equal to ServiceLifetime.Singleton assigned to HecoleMediatorConfiguration.Lifetime.
    private static void AnalyzeLifetimeAssignment(OperationAnalysisContext context, IPropertySymbol lifetime, object? singletonValue)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        if (assignment.Target is not IPropertyReferenceOperation target
            || !SymbolEqualityComparer.Default.Equals(target.Property.OriginalDefinition, lifetime))
        {
            return;
        }

        var value = assignment.Value.ConstantValue;
        if (value.HasValue && Equals(value.Value, singletonValue))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.SingletonLifetime, assignment.Syntax.GetLocation()));
        }
    }

    // HM1004: every reference to TaskWhenAllPublisher (constructor, typeof, generic argument, base type, alias), except
    // XML documentation and nameof.
    private static void AnalyzePublisherReference(SyntaxNodeAnalysisContext context, INamedTypeSymbol publisher)
    {
        var name = (IdentifierNameSyntax)context.Node;
        if (name.Identifier.ValueText != TaskWhenAllPublisherName || name.IsPartOfStructuredTrivia())
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
        {
            symbol = constructor.ContainingType;
        }

        if (!SymbolEqualityComparer.Default.Equals(symbol, publisher) || IsInsideNameOf(name, context))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Descriptors.ParallelPublisher, name.GetLocation()));
    }

    private static bool IsInsideNameOf(SyntaxNode node, SyntaxNodeAnalysisContext context)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } invocation
                && context.SemanticModel.GetOperation(invocation, context.CancellationToken) is INameOfOperation)
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

    // HM1003, container part: IPipelineBehavior<,> registered directly in an IServiceCollection, as a generic type
    // argument (services.AddTransient<IPipelineBehavior<A, B>, X>()) or a typeof argument
    // (services.AddTransient(typeof(IPipelineBehavior<,>), typeof(X<,>)), ServiceDescriptor.Transient(...),
    // new ServiceDescriptor(...)).
    private sealed class ContainerRegistration(
        INamedTypeSymbol behavior,
        INamedTypeSymbol? serviceCollection,
        INamedTypeSymbol? serviceDescriptor)
    {
        public void Analyze(OperationAnalysisContext context)
        {
            IMethodSymbol? method;
            ImmutableArray<IArgumentOperation> arguments;

            switch (context.Operation)
            {
                case IInvocationOperation invocation:
                    method = invocation.TargetMethod;
                    arguments = invocation.Arguments;
                    break;
                case IObjectCreationOperation creation:
                    method = creation.Constructor;
                    arguments = creation.Arguments;
                    break;
                default:
                    return;
            }

            if (method is null || !IsRegistration(method))
            {
                return;
            }

            if (HasBehaviorTypeArgument(method) || HasBehaviorTypeOfArgument(arguments))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.BehaviorRegistration,
                    context.Operation.Syntax.GetLocation(),
                    method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name : method.Name));
            }
        }

        private bool IsRegistration(IMethodSymbol method)
        {
            if (serviceDescriptor is not null && SymbolEqualityComparer.Default.Equals(method.ContainingType, serviceDescriptor))
            {
                return true;
            }

            if (serviceCollection is null || method.Name.StartsWith("Remove", System.StringComparison.Ordinal))
            {
                return false;
            }

            // Extension on IServiceCollection (AddTransient, TryAddEnumerable, Replace, ...), reduced or not.
            if (!method.IsExtensionMethod)
            {
                return false;
            }

            var receiverType = method.ReducedFrom is null
                ? (method.Parameters.Length > 0 ? method.Parameters[0].Type : null)
                : method.ReceiverType;
            return SymbolEqualityComparer.Default.Equals(receiverType, serviceCollection);
        }

        private bool HasBehaviorTypeArgument(IMethodSymbol method)
        {
            foreach (var typeArgument in method.TypeArguments)
            {
                if (IsBehavior(typeArgument))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasBehaviorTypeOfArgument(ImmutableArray<IArgumentOperation> arguments)
        {
            foreach (var argument in arguments)
            {
                var value = argument.Value;
                while (value is IConversionOperation conversion)
                {
                    value = conversion.Operand;
                }

                if (value is ITypeOfOperation typeOf && IsBehavior(typeOf.TypeOperand))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsBehavior(ITypeSymbol type)
            => type is INamedTypeSymbol named
               && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, behavior);
    }
}
