namespace Hecole.Mediator;

/// <summary>
/// The requests the mediator can send, each with the handler and the pipeline behaviors that actually run for it.
/// Resolve it from the container. An application can use it to check its own policy at start-up (for example, that every
/// command runs a given behavior before another one).
/// <para>
/// Lists first the requests registered by an assembly scan or <c>AddRequestHandler</c>, in registration order; then the
/// requests whose handler was registered directly in the container as a closed
/// <see cref="IRequestHandler{TRequest, TResponse}"/> or <see cref="IRequestHandler{TRequest}"/>
/// (<c>services.AddTransient&lt;IRequestHandler&lt;A, B&gt;, H&gt;()</c>), in registration order. A direct registration
/// whose request can never be dispatched to it (an interface or abstract request type, a request without exactly one
/// request interface, or a response type other than the one the request declares) is not listed.
/// </para>
/// <para>
/// Limits: the catalog is computed from a copy of the registrations (service descriptors) of the
/// <c>IServiceCollection</c>, taken when the mediator or the catalog is first resolved (or by <c>VerifyHecoleMediator</c>,
/// whichever comes first), with the rules of the default
/// container; registrations added after that are not reflected, and neither are services a third-party container
/// registers by its own means. Keyed registrations are ignored. A registration of
/// <c>IEnumerable&lt;IPipelineBehavior&lt;TRequest, TResponse&gt;&gt;</c> as a service replaces the list of behaviors in
/// the container but not in the catalog (<c>VerifyHecoleMediator</c> reports it). The behaviors of each request are
/// computed on the first read of <see cref="Requests"/> or <see cref="Find"/>, never by <c>Send</c>.
/// </para>
/// </summary>
public interface IRequestCatalog
{
    /// <summary>
    /// The known requests: those registered by a scan or <c>AddRequestHandler</c>, in registration order, then those
    /// whose handler was registered directly in the container, in registration order.
    /// </summary>
    IReadOnlyList<RequestDescriptor> Requests { get; }

    /// <summary>
    /// The descriptor of <paramref name="requestType"/> (exact type), or <see langword="null"/> when it is not listed.
    /// The interface provides this member with a default implementation that searches <see cref="Requests"/>, so a type
    /// that implements only <see cref="Requests"/> keeps compiling; the catalog of the library answers with a lookup.
    /// </summary>
    /// <param name="requestType">The request type.</param>
    /// <returns>The descriptor, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestType"/> is <see langword="null"/>.</exception>
    RequestDescriptor? Find(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);

        foreach (var request in Requests)
        {
            if (request.RequestType == requestType)
                return request;
        }

        return null;
    }
}
