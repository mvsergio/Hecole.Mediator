namespace Hecole.Mediator;

/// <summary>
/// Sends a request to its single handler, through the request pipeline.
/// </summary>
/// <remarks>
/// Only argument errors are thrown synchronously: a null request, an object that is not a request, or a request type
/// that does not implement exactly one request interface. Every other failure arrives in the returned task, including
/// a mediator resolved from the root provider while <see cref="HecoleMediatorConfiguration.RequireScope"/> is on, a
/// request without handler, a behavior or handler that cannot be resolved (an exception in its constructor, for
/// example) and an exception thrown by a behavior or handler before it returns its task.
/// </remarks>
public interface ISender
{
    /// <summary>
    /// Sends a request that produces a response.
    /// </summary>
    /// <typeparam name="TResponse">The type of the response.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Token that cancels the operation; the outermost pipeline behavior (or the
    /// handler, without behaviors) receives it.</param>
    /// <returns>The response produced by the handler. Any failure other than an argument error arrives in this task.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The request type implements more than one request interface.</exception>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request that produces no response value.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Token that cancels the operation; the outermost pipeline behavior (or the
    /// handler, without behaviors) receives it.</param>
    /// <returns>A task that completes when the request has been handled. Any failure other than an argument error
    /// arrives in this task.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The request type implements more than one request interface.</exception>
    Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest;

    /// <summary>
    /// Sends a request whose type is known only at run time.
    /// </summary>
    /// <param name="request">The request; it must implement <see cref="IRequest{TResponse}"/> or <see cref="IRequest"/>.</param>
    /// <param name="cancellationToken">Token that cancels the operation; the outermost pipeline behavior (or the
    /// handler, without behaviors) receives it.</param>
    /// <returns>The response, or <see cref="Unit.Value"/> for a request without a response value. Any failure other
    /// than an argument error arrives in this task.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The object is not a request, or implements more than one request
    /// interface.</exception>
    Task<object?> Send(object request, CancellationToken cancellationToken = default);
}
