using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hecole.Mediator.Analyzers;

/// <summary>
/// HM1005 (off by default): every concrete request handler declared in the compilation needs a matching
/// <c>AddRequestHandler</c> call in the same compilation (directly, or through one generic helper that forwards its type
/// parameters, see <see cref="RegistrationCollector"/>). Kept apart from the other analyzers so it costs nothing while
/// the rule is off.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExplicitRegistrationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.HandlerNotRegistered);

    public override void Initialize(AnalysisContext context)
    {
        // Generated code is analyzed (a generated handler or AddRequestHandler call counts) and reported on: a restrictive
        // rule must not be silenced by a generated-code header.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
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

        var registrations = new RegistrationCollector(symbols.Extensions);
        var state = new State(symbols, registrations);
        context.RegisterSymbolAction(state.CollectHandler, SymbolKind.NamedType);
        context.RegisterOperationAction(registrations.Collect, OperationKind.Invocation);
        context.RegisterCompilationEndAction(state.Report);
    }

    private sealed class State(MediatorSymbols symbols, RegistrationCollector registrations)
    {
        private readonly ConcurrentDictionary<(INamedTypeSymbol Handler, ITypeSymbol Request), byte> _handlers =
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

        public void Report(CompilationAnalysisContext context)
        {
            var registered = new HashSet<(INamedTypeSymbol Handler, ITypeSymbol Request)>(PairComparer.Instance);
            foreach (var registration in registrations.Resolve())
            {
                if (registration.Handler is INamedTypeSymbol handler)
                {
                    registered.Add((handler, registration.Request));
                }
            }

            foreach (var pair in _handlers.Keys)
            {
                if (registered.Contains(pair))
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
