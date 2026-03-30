namespace Hecole.Mediator.Interfaces
{
    /// <summary>
    /// Central mediator that dispatches requests to their handlers and publishes notifications.
    /// </summary>
    public interface ICoreMediator
    {
        /// <summary>
        /// Sends a request through the pipeline (behaviors + handler) and returns the response.
        /// </summary>
        /// <typeparam name="TResponse">The type of the response.</typeparam>
        /// <param name="request">The request to send.</param>
        /// <param name="cancellationToken">Optional cancellation token propagated to behaviors and handler.</param>
        /// <returns>The response from the handler.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the request type.</exception>
        Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes a notification to all registered handlers in parallel.
        /// </summary>
        /// <typeparam name="TNotification">The type of the notification.</typeparam>
        /// <param name="notification">The notification to publish.</param>
        /// <param name="cancellationToken">Optional cancellation token propagated to all handlers.</param>
        Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification;
    }
}
