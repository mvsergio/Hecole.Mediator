namespace Hecole.Mediator;

/// <summary>
/// Publishes a notification to all of its handlers.
/// </summary>
public interface IPublisher
{
    /// <summary>
    /// Publishes a notification whose type is known only at run time.
    /// </summary>
    /// <param name="notification">The notification; it must implement <see cref="INotification"/>.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when every handler has run.</returns>
    /// <exception cref="ArgumentException">The object is not a notification.</exception>
    Task Publish(object notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a notification.
    /// </summary>
    /// <typeparam name="TNotification">The type of the notification.</typeparam>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when every handler has run.</returns>
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
