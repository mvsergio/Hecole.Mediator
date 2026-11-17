namespace Hecole.Mediator;

/// <summary>
/// Sends a request to its single handler, through the request pipeline.
/// </summary>
public interface ISender
{
    /// <summary>
    /// Sends a request that produces a response.
    /// </summary>
    /// <typeparam name="TResponse">The type of the response.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The response produced by the handler.</returns>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request that produces no response value.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest;

    /// <summary>
    /// Sends a request whose type is known only at run time.
    /// </summary>
    /// <param name="request">The request; it must implement <see cref="IRequest{TResponse}"/> or <see cref="IRequest"/>.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The response, or <see cref="Unit.Value"/> for a request without a response value.</returns>
    /// <exception cref="ArgumentException">The object is not a request.</exception>
    Task<object?> Send(object request, CancellationToken cancellationToken = default);
}
