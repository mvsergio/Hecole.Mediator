using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Hecole.Mediator.Analyzers;

/// <summary>
/// HM1005 (off by default): every concrete request handler declared in the compilation needs a matching
/// <c>AddRequestHandler</c> call in the same compilation. Kept apart from the other analyzers so it costs nothing while
/// the rule is off.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExplicitRegistrationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.HandlerNotRegistered);

    public override void Initialize(AnalysisContext context)
    {
        // Generated code is analyzed (a generated handler or AddRequestHandler call counts) but never reported on.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var symbols = MediatorSymbols.TryCreate(context.Compilation);
        if (symbols?.Extensions is null)
        {
            return;
        }

        var state = new State(symbols, symbols.Extensions);
        context.RegisterSymbolAction(state.CollectHandler, SymbolKind.NamedType);
        context.RegisterOperationAction(state.CollectRegistration, OperationKind.Invocation);
        context.RegisterCompilationEndAction(state.Report);
    }

    private sealed class State(MediatorSymbols symbols, INamedTypeSymbol extensions)
    {
        private readonly ConcurrentDictionary<(INamedTypeSymbol Handler, ITypeSymbol Request), byte> _handlers =
            new(PairComparer.Instance);

        private readonly ConcurrentDictionary<(INamedTypeSymbol Handler, ITypeSymbol Request), byte> _registered =
            new(PairComparer.Instance);

        public void CollectHandler(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;
            if (type.TypeKind != TypeKind.Class || !MediatorSymbols.IsConcreteClosed(type))
            {
                return;
            }

            foreach (var handlerInterface in symbols.GetHandlerInterfaces(type))
            {
                if (!MediatorSymbols.IsDecoratorOf(type, handlerInterface))
                {
                    _handlers.TryAdd((type, handlerInterface.TypeArguments[0]), 0);
                }
            }
        }

        public void CollectRegistration(OperationAnalysisContext context)
        {
            var method = ((IInvocationOperation)context.Operation).TargetMethod;
            if (method.Name != "AddRequestHandler"
                || !SymbolEqualityComparer.Default.Equals(method.ContainingType, extensions)
                || method.TypeArguments.Length < 2)
            {
                return;
            }

            // AddRequestHandler<TRequest, TResponse, THandler>() or AddRequestHandler<TRequest, THandler>().
            if (method.TypeArguments[method.TypeArguments.Length - 1] is INamedTypeSymbol handler)
            {
                _registered.TryAdd((handler, method.TypeArguments[0]), 0);
            }
        }

        public void Report(CompilationAnalysisContext context)
        {
            foreach (var pair in _handlers.Keys)
            {
                if (_registered.ContainsKey(pair))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.HandlerNotRegistered,
                    pair.Handler.Locations.FirstOrDefault(),
                    pair.Handler.ToDisplayString(),
                    pair.Request.ToDisplayString()));
            }
        }
    }

    private sealed class PairComparer : IEqualityComparer<(INamedTypeSymbol Handler, ITypeSymbol Request)>
    {
        public static readonly PairComparer Instance = new();

        public bool Equals((INamedTypeSymbol Handler, ITypeSymbol Request) x, (INamedTypeSymbol Handler, ITypeSymbol Request) y)
            => SymbolEqualityComparer.Default.Equals(x.Handler, y.Handler)
               && SymbolEqualityComparer.Default.Equals(x.Request, y.Request);

        public int GetHashCode((INamedTypeSymbol Handler, ITypeSymbol Request) obj)
            => (SymbolEqualityComparer.Default.GetHashCode(obj.Handler) * 397)
               ^ SymbolEqualityComparer.Default.GetHashCode(obj.Request);
    }
}
