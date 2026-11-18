using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Hecole.Mediator.Analyzers;

/// <summary>
/// Collects the <c>AddRequestHandler</c> registrations of a compilation as (request, handler) pairs:
/// <c>AddRequestHandler&lt;TRequest, TResponse, THandler&gt;()</c> and <c>AddRequestHandler&lt;TRequest, THandler&gt;()</c>
/// with concrete type arguments, and one level of forwarding through a generic method declared in the compilation whose
/// body calls <c>AddRequestHandler</c> with its own type parameters
/// (<c>static IServiceCollection Feature&lt;TReq, TRes, THandler&gt;(this IServiceCollection s) =&gt; s.AddRequestHandler&lt;TReq, TRes, THandler&gt;()</c>,
/// then <c>services.Feature&lt;Req, Res, Handler&gt;()</c> registers <c>(Req, Handler)</c>). A forwarder that forwards to
/// another forwarder is not followed. Reachability is not considered: a call in dead code counts.
/// </summary>
internal sealed class RegistrationCollector(INamedTypeSymbol extensions)
{
    private const string AddRequestHandler = "AddRequestHandler";

    private readonly ConcurrentQueue<(ITypeSymbol Request, ITypeSymbol Handler)> _direct = new();

    // Generic method definition -> AddRequestHandler (request, handler) templates written with its type parameters.
    private readonly ConcurrentDictionary<IMethodSymbol, ConcurrentQueue<(ITypeSymbol Request, ITypeSymbol Handler)>> _templates =
        new(SymbolEqualityComparer.Default);

    // Calls to generic methods declared in this compilation (candidate forwarders): definition and type arguments.
    private readonly ConcurrentQueue<(IMethodSymbol Definition, ImmutableArray<ITypeSymbol> TypeArguments)> _genericCalls = new();

    /// <summary>Operation action for <see cref="OperationKind.Invocation"/>.</summary>
    public void Collect(OperationAnalysisContext context)
    {
        var method = ((IInvocationOperation)context.Operation).TargetMethod;

        if (method.Name == AddRequestHandler
            && method.TypeArguments.Length >= 2
            && SymbolEqualityComparer.Default.Equals(method.ContainingType, extensions))
        {
            var request = method.TypeArguments[0];
            var handler = method.TypeArguments[method.TypeArguments.Length - 1];
            var forwarder = FindForwarder(request, handler, out var hasTypeParameters);

            if (!hasTypeParameters)
            {
                _direct.Enqueue((request, handler));
            }
            else if (forwarder is not null)
            {
                _templates.GetOrAdd(forwarder, _ => new()).Enqueue((request, handler));
            }

            return;
        }

        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        if (method.IsGenericMethod && definition.Locations.Any(location => location.IsInSource))
        {
            _genericCalls.Enqueue((definition, method.TypeArguments));
        }
    }

    /// <summary>Every registered (request, handler) pair; call from the compilation end action.</summary>
    public IEnumerable<(ITypeSymbol Request, ITypeSymbol Handler)> Resolve()
    {
        foreach (var pair in _direct)
        {
            yield return pair;
        }

        foreach (var call in _genericCalls)
        {
            if (!_templates.TryGetValue(call.Definition, out var templates)
                || call.TypeArguments.Length != call.Definition.TypeParameters.Length)
            {
                continue;
            }

            foreach (var template in templates)
            {
                if (Substitute(template.Request, call.Definition, call.TypeArguments) is { } request
                    && Substitute(template.Handler, call.Definition, call.TypeArguments) is { } handler)
                {
                    yield return (request, handler);
                }
            }
        }
    }

    // The single generic method whose type parameters appear in the arguments; null when type parameters of a type, or
    // of more than one method, appear.
    private static IMethodSymbol? FindForwarder(ITypeSymbol request, ITypeSymbol handler, out bool hasTypeParameters)
    {
        IMethodSymbol? forwarder = null;
        var valid = true;
        hasTypeParameters = false;

        Visit(request, ref forwarder, ref valid, ref hasTypeParameters);
        Visit(handler, ref forwarder, ref valid, ref hasTypeParameters);
        return valid ? forwarder : null;

        static void Visit(ITypeSymbol type, ref IMethodSymbol? forwarder, ref bool valid, ref bool found)
        {
            switch (type)
            {
                case ITypeParameterSymbol parameter:
                    found = true;
                    if (parameter.DeclaringMethod is not { } method)
                    {
                        valid = false;
                    }
                    else if (forwarder is null)
                    {
                        forwarder = method.OriginalDefinition;
                    }
                    else if (!SymbolEqualityComparer.Default.Equals(forwarder, method.OriginalDefinition))
                    {
                        valid = false;
                    }

                    break;
                case INamedTypeSymbol named:
                    foreach (var argument in named.TypeArguments)
                    {
                        Visit(argument, ref forwarder, ref valid, ref found);
                    }

                    if (named.ContainingType is { } containing)
                    {
                        Visit(containing, ref forwarder, ref valid, ref found);
                    }

                    break;
                case IArrayTypeSymbol array:
                    Visit(array.ElementType, ref forwarder, ref valid, ref found);
                    break;
            }
        }
    }

    // Replaces the type parameters of `definition` with `arguments`; null when the result would still be open.
    private static ITypeSymbol? Substitute(ITypeSymbol type, IMethodSymbol definition, ImmutableArray<ITypeSymbol> arguments)
    {
        switch (type)
        {
            case ITypeParameterSymbol parameter:
                if (!SymbolEqualityComparer.Default.Equals(parameter.DeclaringMethod?.OriginalDefinition, definition))
                {
                    return null;
                }

                var argument = arguments[parameter.Ordinal];
                return argument is ITypeParameterSymbol ? null : argument;
            case INamedTypeSymbol { IsGenericType: true } named:
                if (named.ContainingType is { IsGenericType: true })
                {
                    return null;
                }

                var substituted = new ITypeSymbol[named.TypeArguments.Length];
                for (var i = 0; i < substituted.Length; i++)
                {
                    if (Substitute(named.TypeArguments[i], definition, arguments) is not { } item)
                    {
                        return null;
                    }

                    substituted[i] = item;
                }

                return named.ConstructedFrom.Construct(substituted);
            default:
                return type;
        }
    }
}
