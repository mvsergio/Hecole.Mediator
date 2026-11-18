namespace Hecole.Mediator;

/// <summary>
/// One request of the <see cref="IRequestCatalog"/>: its handler and the pipeline behaviors that run for it.
/// <para>
/// Computed from the registrations (service descriptors) of the container with the default container's rules; see
/// <see cref="IRequestCatalog"/> for the limits. Two descriptors are equal when their three types are equal and their
/// <see cref="Behaviors"/> and <see cref="SkippedBehaviors"/> hold the same types in the same order.
/// </para>
/// </summary>
/// <param name="RequestType">The request type.</param>
/// <param name="ResponseType">The response type; <see cref="Unit"/> for a request without response value.</param>
/// <param name="HandlerType">The implementation type of the handler that runs: the one of the last registration of
/// <see cref="IRequestHandler{TRequest, TResponse}"/> (or <see cref="IRequestHandler{TRequest}"/>) for this request, which is
/// the one the container returns. For a registration made with an instance, the instance's type; for a registration made
/// with a factory, whose implementation type is not known before resolution, the handler service type itself
/// (<c>IRequestHandler&lt;TRequest, TResponse&gt;</c> or <c>IRequestHandler&lt;TRequest&gt;</c>).</param>
public sealed record RequestDescriptor(Type RequestType, Type ResponseType, Type HandlerType)
{
    /// <summary>
    /// The pipeline behaviors that run for this request, outermost first, in the order the default container resolves
    /// <c>IEnumerable&lt;IPipelineBehavior&lt;TRequest, TResponse&gt;&gt;</c>: the registrations of the closed
    /// <c>IPipelineBehavior&lt;TRequest, TResponse&gt;</c> and of the open <c>IPipelineBehavior&lt;,&gt;</c> whose
    /// implementation closes over this request, in registration order. Each entry is the closed implementation type (for
    /// an open behavior, the type closed over <see cref="RequestType"/> and <see cref="ResponseType"/>); for a behavior
    /// registered with an instance, the instance's type; for a behavior registered with a factory, whose implementation
    /// type is not known before resolution, the closed service type <c>IPipelineBehavior&lt;TRequest, TResponse&gt;</c>
    /// itself. Empty by default.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value set is <see langword="null"/>.</exception>
    public IReadOnlyList<Type> Behaviors
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = [];

    /// <summary>
    /// The open generic behaviors registered as <c>IPipelineBehavior&lt;,&gt;</c> that do not run for this request because
    /// their generic constraints are not satisfied by it (the container skips them), as open generic type definitions,
    /// one entry per registration, in registration order. Empty by default.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value set is <see langword="null"/>.</exception>
    public IReadOnlyList<Type> SkippedBehaviors
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = [];

    /// <summary>
    /// Whether <paramref name="other"/> has the same request, response and handler types and the same
    /// <see cref="Behaviors"/> and <see cref="SkippedBehaviors"/>, compared item by item in order.
    /// </summary>
    /// <param name="other">The descriptor to compare with.</param>
    /// <returns><see langword="true"/> when both describe the same request, handler and pipeline.</returns>
    public bool Equals(RequestDescriptor? other)
        => other is not null
           && (ReferenceEquals(this, other)
               || (RequestType == other.RequestType
                   && ResponseType == other.ResponseType
                   && HandlerType == other.HandlerType
                   && Behaviors.SequenceEqual(other.Behaviors)
                   && SkippedBehaviors.SequenceEqual(other.SkippedBehaviors)));

    /// <summary>A hash code consistent with <see cref="Equals(RequestDescriptor?)"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RequestType);
        hash.Add(ResponseType);
        hash.Add(HandlerType);
        hash.Add(Behaviors.Count);
        foreach (var behavior in Behaviors)
            hash.Add(behavior);
        hash.Add(SkippedBehaviors.Count);
        foreach (var behavior in SkippedBehaviors)
            hash.Add(behavior);
        return hash.ToHashCode();
    }
}
