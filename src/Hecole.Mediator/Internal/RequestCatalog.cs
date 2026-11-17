using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace Hecole.Mediator.Internal;

/// <summary>
/// Map of request type to executor, by exact runtime type. Requests registered with <c>AddRequestHandler</c> or found by
/// an assembly scan are in an immutable <see cref="FrozenDictionary{TKey, TValue}"/> built once. Any other request type (handler registered
/// directly in the container) gets its executor created on first use, with reflection only in that creation, and
/// kept for the next calls. Also the <see cref="IRequestCatalog"/> of the container.
/// </summary>
internal sealed class RequestCatalog : IRequestCatalog
{
    private readonly FrozenDictionary<Type, RequestExecutor> _registered;
    private readonly ConcurrentDictionary<Type, RequestExecutor> _onDemand = new();

    public RequestCatalog(IReadOnlyList<HandlerRegistration> registrations)
    {
        _registered = registrations.ToFrozenDictionary(static r => r.RequestType, static r => r.Executor);
        Requests = Array.AsReadOnly(registrations.Select(static r => new RequestDescriptor(r.RequestType, r.ResponseType, r.HandlerType)).ToArray());
    }

    public int Count => _registered.Count;

    public IReadOnlyList<RequestDescriptor> Requests { get; }

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
