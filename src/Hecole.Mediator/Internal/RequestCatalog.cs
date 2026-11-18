using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Internal;

/// <summary>
/// Map of request type to executor, by exact runtime type. Requests registered with <c>AddRequestHandler</c> or found by
/// an assembly scan are in an immutable <see cref="FrozenDictionary{TKey, TValue}"/> built once. Any other request type (handler registered
/// directly in the container) gets its executor created on first use, with reflection only in that creation, and
/// kept for the next calls. Also the <see cref="IRequestCatalog"/> of the container: its listing is computed once, on the
/// first read of <see cref="Requests"/> or <see cref="Find"/>, from the copies of the registrations and of the non-keyed
/// service descriptors that <see cref="MediatorRuntime"/> took when it was created; <see cref="GetExecutor"/> never
/// touches it.
/// </summary>
internal sealed class RequestCatalog : IRequestCatalog
{
    private readonly FrozenDictionary<Type, RequestExecutor> _registered;
    private readonly ConcurrentDictionary<Type, RequestExecutor> _onDemand = new();
    private readonly Lazy<Listing> _listing;

    /// <param name="registrations">Copy of the registrations; not changed afterwards.</param>
    /// <param name="services">Copy of the non-keyed service descriptors; not changed afterwards.</param>
    public RequestCatalog(HandlerRegistration[] registrations, ServiceDescriptor[] services)
    {
        _registered = registrations.ToFrozenDictionary(static r => r.RequestType, static r => r.Executor);
        _listing = new Lazy<Listing>(
            () => Listing.Build(registrations, services), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public int Count => _registered.Count;

    public IReadOnlyList<RequestDescriptor> Requests => _listing.Value.Requests;

    public RequestDescriptor? Find(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        return _listing.Value.ByRequest.GetValueOrDefault(requestType);
    }

    public RequestExecutor GetExecutor(Type requestType)
    {
        if (_registered.TryGetValue(requestType, out var executor) || _onDemand.TryGetValue(requestType, out executor))
            return executor;

        return _onDemand.GetOrAdd(requestType, static type => CreateExecutor(type));
    }

    private static RequestExecutor CreateExecutor(Type requestType)
    {
        var responseType = RequestTypes.GetResponseType(requestType);
        var executorType = responseType is null
            ? typeof(VoidExecutor<>).MakeGenericType(requestType)
            : typeof(Executor<,>).MakeGenericType(requestType, responseType);

        return (RequestExecutor)Activator.CreateInstance(executorType)!;
    }

    /// <summary>The descriptors of the catalog, in listing order and by request type.</summary>
    private sealed class Listing(ReadOnlyCollection<RequestDescriptor> requests, FrozenDictionary<Type, RequestDescriptor> byRequest)
    {
        public ReadOnlyCollection<RequestDescriptor> Requests { get; } = requests;

        public FrozenDictionary<Type, RequestDescriptor> ByRequest { get; } = byRequest;

        /// <summary>
        /// Lists the registered requests, then the requests whose handler is only a closed handler registration in
        /// <paramref name="services"/> (non-keyed descriptors), and reads the effective handler and the pipeline of each.
        /// </summary>
        public static Listing Build(HandlerRegistration[] registrations, ServiceDescriptor[] services)
        {
            // The last registration of a service type is the one the container returns.
            var lastByService = new Dictionary<Type, ServiceDescriptor>();
            foreach (var descriptor in services)
                lastByService[descriptor.ServiceType] = descriptor;

            var requests = new List<RequestDescriptor>();
            var byRequest = new Dictionary<Type, RequestDescriptor>();

            foreach (var registration in registrations)
            {
                // A handler removed from the container keeps the type it was registered with (VerifyHecoleMediator reports it).
                var handlerType = lastByService.TryGetValue(registration.ServiceType, out var handler)
                    ? ServiceDescriptors.ImplementationOf(handler)
                    : registration.HandlerType;
                Add(registration.RequestType, registration.ResponseType, handlerType);
            }

            foreach (var descriptor in services)
            {
                if (TryReadDirectHandler(descriptor.ServiceType, out var requestType, out var responseType)
                    && !byRequest.ContainsKey(requestType))
                {
                    Add(requestType, responseType, ServiceDescriptors.ImplementationOf(lastByService[descriptor.ServiceType]));
                }
            }

            return new Listing(requests.AsReadOnly(), byRequest.ToFrozenDictionary());

            void Add(Type requestType, Type responseType, Type handlerType)
            {
                var (behaviors, skipped) = PipelineTypes.Read(services, requestType, responseType);
                var request = new RequestDescriptor(requestType, responseType, handlerType)
                {
                    Behaviors = Array.AsReadOnly(behaviors),
                    SkippedBehaviors = Array.AsReadOnly(skipped),
                };
                requests.Add(request);
                byRequest.Add(requestType, request);
            }
        }

        /// <summary>
        /// Reads a closed <see cref="IRequestHandler{TRequest, TResponse}"/> or <see cref="IRequestHandler{TRequest}"/>
        /// service type whose request can be dispatched to it: a concrete request type with exactly one request interface,
        /// declaring the handler's response (none for <see cref="IRequestHandler{TRequest}"/>, whose pipeline response is
        /// <see cref="Unit"/>).
        /// </summary>
        private static bool TryReadDirectHandler(Type serviceType, out Type requestType, out Type responseType)
        {
            requestType = responseType = null!;
            if (!serviceType.IsGenericType || serviceType.ContainsGenericParameters)
                return false;

            var definition = serviceType.GetGenericTypeDefinition();
            if (definition != typeof(IRequestHandler<,>) && definition != typeof(IRequestHandler<>))
                return false;

            var arguments = serviceType.GetGenericArguments();
            if (arguments[0].IsInterface || arguments[0].IsAbstract)
                return false;

            Type? declared;
            try
            {
                declared = RequestTypes.GetResponseType(arguments[0]);
            }
            catch (ArgumentException)
            {
                return false;
            }

            var handled = definition == typeof(IRequestHandler<>) ? null : arguments[1];
            if (declared != handled)
                return false;

            requestType = arguments[0];
            responseType = handled ?? typeof(Unit);
            return true;
        }
    }
}

/// <summary>Reads service descriptors with the default container's rules.</summary>
internal static class ServiceDescriptors
{
    /// <summary>
    /// The implementation type of a non-keyed descriptor: its implementation type, the type of its instance or, for a
    /// factory registration (implementation type unknown before resolution), the service type itself.
    /// </summary>
    public static Type ImplementationOf(ServiceDescriptor descriptor)
        => descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType() ?? descriptor.ServiceType;
}

/// <summary>The pipeline the default container builds for a request, read from the service descriptors.</summary>
internal static class PipelineTypes
{
    /// <summary>
    /// The behaviors of <c>(requestType, responseType)</c> in the order the default container resolves
    /// <c>IEnumerable&lt;IPipelineBehavior&lt;TRequest, TResponse&gt;&gt;</c> (one pass in descriptor order, closed and open
    /// registrations mixed), and the open behaviors it skips because their constraints are not satisfied.
    /// <paramref name="services"/> holds non-keyed descriptors only.
    /// </summary>
    public static (Type[] Behaviors, Type[] Skipped) Read(IReadOnlyList<ServiceDescriptor> services, Type requestType, Type responseType)
    {
        var closed = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, responseType);
        List<Type>? behaviors = null;
        List<Type>? skipped = null;

        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == closed)
            {
                (behaviors ??= []).Add(ServiceDescriptors.ImplementationOf(descriptor));
            }
            else if (descriptor.ServiceType == typeof(IPipelineBehavior<,>)
                     && descriptor.ImplementationType is { IsGenericTypeDefinition: true } open)
            {
                if (TryClose(open, requestType, responseType, out var closedBehavior))
                    (behaviors ??= []).Add(closedBehavior);
                else
                    (skipped ??= []).Add(open);
            }
        }

        return (behaviors?.ToArray() ?? [], skipped?.ToArray() ?? []);
    }

    /// <summary>
    /// Closes <paramref name="openBehavior"/> over <c>(requestType, responseType)</c> as the default container does
    /// (positional type arguments); <see langword="false"/> when its generic constraints are not satisfied, in which case
    /// the container skips it. The single place that decides whether an open behavior runs for a request.
    /// </summary>
    public static bool TryClose(Type openBehavior, Type requestType, Type responseType, out Type closedBehavior)
    {
        try
        {
            closedBehavior = openBehavior.MakeGenericType(requestType, responseType);
            return true;
        }
        catch (ArgumentException)
        {
            closedBehavior = null!;
            return false;
        }
    }
}

/// <summary>Reads the request contract of a type. Used at registration and when creating an executor on demand.</summary>
internal static class RequestTypes
{
    /// <summary>
    /// The <c>TResponse</c> of <see cref="IRequest{TResponse}"/>, or <see langword="null"/> for <see cref="IRequest"/>.
    /// A type with no request interface, or with more than one (<see cref="IRequest"/> and <see cref="IRequest{TResponse}"/>,
    /// or two <see cref="IRequest{TResponse}"/>), has no single response type and is refused.
    /// </summary>
    public static Type? GetResponseType(Type requestType)
    {
        var count = 0;
        Type? responseType = null;
        foreach (var contract in requestType.GetInterfaces())
        {
            if (contract == typeof(IRequest))
            {
                count++;
            }
            else if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IRequest<>))
            {
                count++;
                responseType = contract.GetGenericArguments()[0];
            }
        }

        return count switch
        {
            1 => responseType,
            0 => throw new ArgumentException(
                $"The type '{requestType.FullName}' does not implement {nameof(IRequest)} or {nameof(IRequest)}<TResponse>."),
            _ => throw new ArgumentException(
                $"The request '{requestType.FullName}' implements more than one request interface " +
                $"({nameof(IRequest)} and/or {nameof(IRequest)}<TResponse>); it must implement exactly one."),
        };
    }
}
