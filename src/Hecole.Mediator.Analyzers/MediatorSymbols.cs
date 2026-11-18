using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Hecole.Mediator.Analyzers;

/// <summary>
/// Hecole.Mediator symbols resolved by metadata name, once per compilation, from the assembly named
/// <c>Hecole.Mediator</c> only: another referenced assembly declaring a type with the same full name neither hides nor
/// replaces them. <see cref="TryCreate"/> returns <see langword="null"/> when the compilation does not reference
/// Hecole.Mediator; analyzers then register nothing.
/// </summary>
internal sealed class MediatorSymbols
{
    private const string AssemblyName = "Hecole.Mediator";

    private MediatorSymbols(Compilation compilation, INamedTypeSymbol request, INamedTypeSymbol requestOfT,
        INamedTypeSymbol handlerOfRequest, INamedTypeSymbol handlerOfRequestAndResponse)
    {
        Request = request;
        RequestOfT = requestOfT;
        VoidHandler = handlerOfRequest;
        Handler = handlerOfRequestAndResponse;
        PipelineBehavior = Resolve(compilation, "Hecole.Mediator.IPipelineBehavior`2");
        Configuration = Resolve(compilation, "Hecole.Mediator.HecoleMediatorConfiguration");
        Extensions = Resolve(compilation, "Microsoft.Extensions.DependencyInjection.HecoleMediatorServiceCollectionExtensions");
        TaskWhenAllPublisher = Resolve(compilation, "Hecole.Mediator.NotificationPublishers.TaskWhenAllPublisher");
    }

    /// <summary><c>IRequest</c> (no response).</summary>
    public INamedTypeSymbol Request { get; }

    /// <summary><c>IRequest&lt;TResponse&gt;</c> definition.</summary>
    public INamedTypeSymbol RequestOfT { get; }

    /// <summary><c>IRequestHandler&lt;TRequest&gt;</c> definition.</summary>
    public INamedTypeSymbol VoidHandler { get; }

    /// <summary><c>IRequestHandler&lt;TRequest, TResponse&gt;</c> definition.</summary>
    public INamedTypeSymbol Handler { get; }

    public INamedTypeSymbol? PipelineBehavior { get; }

    public INamedTypeSymbol? Configuration { get; }

    public INamedTypeSymbol? Extensions { get; }

    public INamedTypeSymbol? TaskWhenAllPublisher { get; }

    public static MediatorSymbols? TryCreate(Compilation compilation)
    {
        var request = Resolve(compilation, "Hecole.Mediator.IRequest");
        var requestOfT = Resolve(compilation, "Hecole.Mediator.IRequest`1");
        var voidHandler = Resolve(compilation, "Hecole.Mediator.IRequestHandler`1");
        var handler = Resolve(compilation, "Hecole.Mediator.IRequestHandler`2");

        if (request is null || requestOfT is null || voidHandler is null || handler is null)
        {
            return null;
        }

        return new MediatorSymbols(compilation, request, requestOfT, voidHandler, handler);
    }

    /// <summary>True when <paramref name="type"/> is the handler interface definition, with or without response.</summary>
    public bool IsHandlerDefinition(INamedTypeSymbol type)
        => SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, Handler)
           || SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, VoidHandler);

    // GetTypeByMetadataName returns null when two referenced assemblies declare the same full name, which would disable
    // every analyzer; the type is taken from the Hecole.Mediator assembly instead (the compilation itself when it is the
    // library being built).
    private static INamedTypeSymbol? Resolve(Compilation compilation, string metadataName)
    {
        foreach (var candidate in compilation.GetTypesByMetadataName(metadataName))
        {
            if (candidate.ContainingAssembly?.Name == AssemblyName)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// True for a class or struct that can be instantiated as is: not abstract, not static, not an open generic
    /// (itself or through a containing type).
    /// </summary>
    public static bool IsConcreteClosed(INamedTypeSymbol type)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsAbstract || type.IsStatic)
        {
            return false;
        }

        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.TypeParameters.Length > 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Request shape of <paramref name="type"/>: <see langword="true"/> for IRequest&lt;T&gt;, <see langword="false"/> for
    /// IRequest; <see langword="null"/> when it is not a request or implements more than one request interface
    /// (the library rejects those).
    /// </summary>
    public bool? GetRequestShape(INamedTypeSymbol type)
    {
        var count = 0;
        var hasResponse = false;

        foreach (var candidate in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, Request))
            {
                count++;
            }
            else if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, RequestOfT))
            {
                count++;
                hasResponse = true;
            }
        }

        return count == 1 ? hasResponse : null;
    }

    /// <summary>Closed or open request handler interfaces implemented by <paramref name="type"/>.</summary>
    public IEnumerable<INamedTypeSymbol> GetHandlerInterfaces(INamedTypeSymbol type)
    {
        foreach (var candidate in type.AllInterfaces)
        {
            var definition = candidate.OriginalDefinition;
            if (SymbolEqualityComparer.Default.Equals(definition, Handler)
                || SymbolEqualityComparer.Default.Equals(definition, VoidHandler))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>True when the handler interface has a response (IRequestHandler&lt;TRequest, TResponse&gt;).</summary>
    public bool HasResponse(INamedTypeSymbol handlerInterface)
        => SymbolEqualityComparer.Default.Equals(handlerInterface.OriginalDefinition, Handler);

    /// <summary>
    /// Decorator: some public constructor of <paramref name="type"/> takes the same handler interface it implements.
    /// Only public constructors count, as the container builds the class with one of them; a non-public constructor
    /// taking the interface does not make a plain handler a decorator.
    /// </summary>
    public static bool IsDecoratorOf(INamedTypeSymbol type, INamedTypeSymbol handlerInterface)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            foreach (var parameter in constructor.Parameters)
            {
                if (SymbolEqualityComparer.Default.Equals(parameter.Type, handlerInterface))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
