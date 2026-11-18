using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hecole.Mediator.Analyzers;

/// <summary>
/// HM0001 (request without a handler) and HM0002 (more than one handler for the same request). Only types declared in
/// the compilation being analyzed are considered; handlers and requests of referenced assemblies never count.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequestHandlerAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.RequestWithoutHandler, Descriptors.MultipleHandlers);

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
        if (symbols is null)
        {
            return;
        }

        var registrations = symbols.Extensions is null ? null : new RegistrationCollector(symbols.Extensions);
        var state = new State(symbols, registrations);
        context.RegisterSymbolAction(state.Collect, SymbolKind.NamedType);
        if (registrations is not null)
        {
            context.RegisterOperationAction(registrations.Collect, OperationKind.Invocation);
        }

        context.RegisterCompilationEndAction(state.Report);
    }

    private sealed class State(MediatorSymbols symbols, RegistrationCollector? registrations)
    {
        // Concrete request types declared here, with their shape (true: IRequest<T>).
        private readonly ConcurrentDictionary<INamedTypeSymbol, bool> _requests = new(SymbolEqualityComparer.Default);

        // Request types handled by a concrete, closed handler class declared here. Abstract and open generic handler classes
        // do not count: neither scanning nor the container can register them as such.
        private readonly ConcurrentDictionary<ITypeSymbol, byte> _handled = new(SymbolEqualityComparer.Default);

        // Concrete, closed, non-decorator handlers per request type (HM0002).
        private readonly ConcurrentDictionary<ITypeSymbol, ConcurrentDictionary<INamedTypeSymbol, byte>> _handlers =
            new(SymbolEqualityComparer.Default);

        private int _handlerTypeCount;

        public void Collect(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;

            if (MediatorSymbols.IsConcreteClosed(type) && symbols.GetRequestShape(type) is bool hasResponse)
            {
                _requests.TryAdd(type, hasResponse);
            }

            if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            {
                return;
            }

            // Only a concrete, closed class can be registered (scanning skips the others; AddRequestHandler needs a class).
            var registrable = type.TypeKind == TypeKind.Class && MediatorSymbols.IsConcreteClosed(type);
            var isHandler = false;

            foreach (var handlerInterface in symbols.GetHandlerInterfaces(type))
            {
                // Any handler class, even abstract or open generic, means this is not a contracts assembly.
                isHandler = true;
                if (!registrable)
                {
                    continue;
                }

                var requestType = handlerInterface.TypeArguments[0];
                _handled.TryAdd(requestType, 0);

                if (!MediatorSymbols.IsDecoratorOf(type, handlerInterface))
                {
                    _handlers.GetOrAdd(requestType, _ => new(SymbolEqualityComparer.Default)).TryAdd(type, 0);
                }
            }

            if (isHandler)
            {
                Interlocked.Increment(ref _handlerTypeCount);
            }
        }

        public void Report(CompilationAnalysisContext context)
        {
            // Request types registered here with AddRequestHandler. HM0001: the registration names a handler (for example
            // a closed construction of a generic handler class). HM0002: the handler is chosen explicitly.
            var explicitlyRegistered = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            if (registrations is not null)
            {
                foreach (var registration in registrations.Resolve())
                {
                    explicitlyRegistered.Add(registration.Request);
                }
            }

            // HM0001: a compilation without any request handler is a contracts assembly; its requests are handled elsewhere.
            if (Volatile.Read(ref _handlerTypeCount) > 0)
            {
                foreach (var request in _requests.Keys)
                {
                    if (_handled.ContainsKey(request) || explicitlyRegistered.Contains(request))
                    {
                        continue;
                    }

                    context.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.RequestWithoutHandler,
                        request.Locations.FirstOrDefault(),
                        request.ToDisplayString()));
                }
            }

            // HM0002: report on every handler involved, with the full sorted list (deterministic). Not reported when this
            // compilation registers the request with AddRequestHandler: the handler is chosen explicitly (alternative
            // implementations, test doubles) and registering two different ones still throws at run time.
            foreach (var pair in _handlers)
            {
                if (pair.Value.Count < 2 || explicitlyRegistered.Contains(pair.Key))
                {
                    continue;
                }

                var handlers = pair.Value.Keys
                    .OrderBy(handler => handler.ToDisplayString(), System.StringComparer.Ordinal)
                    .ToList();
                var names = string.Join(", ", handlers.Select(handler => handler.ToDisplayString()));

                foreach (var handler in handlers)
                {
                    var others = handlers
                        .Where(other => !SymbolEqualityComparer.Default.Equals(other, handler))
                        .SelectMany(other => other.Locations)
                        .Where(location => location.IsInSource);

                    context.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.MultipleHandlers,
                        handler.Locations.FirstOrDefault(),
                        others,
                        pair.Key.ToDisplayString(),
                        handlers.Count,
                        names));
                }
            }
        }
    }
}
