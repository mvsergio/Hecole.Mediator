using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

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

        var state = new State(symbols);
        context.RegisterSymbolAction(state.Collect, SymbolKind.NamedType);
        if (symbols.Extensions is not null)
        {
            context.RegisterOperationAction(state.CollectExplicitRegistration, OperationKind.Invocation);
        }

        context.RegisterCompilationEndAction(state.Report);
    }

    private sealed class State(MediatorSymbols symbols)
    {
        // Concrete request types declared here, with their shape (true: IRequest<T>).
        private readonly ConcurrentDictionary<INamedTypeSymbol, bool> _requests = new(SymbolEqualityComparer.Default);

        // Request types some handler declared here handles (abstract and generic handlers included).
        private readonly ConcurrentDictionary<ITypeSymbol, byte> _handled = new(SymbolEqualityComparer.Default);

        // Concrete, closed, non-decorator handlers per request type (HM0002).
        private readonly ConcurrentDictionary<ITypeSymbol, ConcurrentDictionary<INamedTypeSymbol, byte>> _handlers =
            new(SymbolEqualityComparer.Default);

        // Request types registered here with AddRequestHandler (HM0002: registration is explicit, the duplicate is a choice).
        private readonly ConcurrentDictionary<ITypeSymbol, byte> _explicitlyRegistered = new(SymbolEqualityComparer.Default);

        private int _handlerTypeCount;
        private int _openVoidHandler;
        private int _openResponseHandler;

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

            var countsForDuplicates = type.TypeKind == TypeKind.Class && MediatorSymbols.IsConcreteClosed(type);
            var isHandler = false;

            foreach (var handlerInterface in symbols.GetHandlerInterfaces(type))
            {
                isHandler = true;
                var requestType = handlerInterface.TypeArguments[0];

                if (requestType is ITypeParameterSymbol)
                {
                    // Open generic handler (registered as IRequestHandler<,> in the container): may handle any request.
                    if (symbols.HasResponse(handlerInterface))
                    {
                        Interlocked.Exchange(ref _openResponseHandler, 1);
                    }
                    else
                    {
                        Interlocked.Exchange(ref _openVoidHandler, 1);
                    }

                    continue;
                }

                _handled.TryAdd(requestType, 0);

                if (countsForDuplicates && !MediatorSymbols.IsDecoratorOf(type, handlerInterface))
                {
                    _handlers.GetOrAdd(requestType, _ => new(SymbolEqualityComparer.Default)).TryAdd(type, 0);
                }
            }

            if (isHandler)
            {
                Interlocked.Increment(ref _handlerTypeCount);
            }
        }

        public void CollectExplicitRegistration(OperationAnalysisContext context)
        {
            var method = ((IInvocationOperation)context.Operation).TargetMethod;
            if (method.Name == "AddRequestHandler"
                && method.TypeArguments.Length >= 2
                && SymbolEqualityComparer.Default.Equals(method.ContainingType, symbols.Extensions))
            {
                _explicitlyRegistered.TryAdd(method.TypeArguments[0], 0);
            }
        }

        public void Report(CompilationAnalysisContext context)
        {
            // HM0001: a compilation without any request handler is a contracts assembly; its requests are handled elsewhere.
            if (Volatile.Read(ref _handlerTypeCount) > 0)
            {
                var openVoid = Volatile.Read(ref _openVoidHandler) == 1;
                var openResponse = Volatile.Read(ref _openResponseHandler) == 1;

                foreach (var pair in _requests)
                {
                    var request = pair.Key;
                    var hasResponse = pair.Value;

                    if (_handled.ContainsKey(request) || (hasResponse ? openResponse : openVoid))
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
                if (pair.Value.Count < 2 || _explicitlyRegistered.ContainsKey(pair.Key))
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
