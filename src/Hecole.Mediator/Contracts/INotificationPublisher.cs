namespace Hecole.Mediator;

/// <summary>
/// Strategy that runs the handlers of one published notification: in sequence, in parallel, or any other way.
/// The default is <see cref="NotificationPublishers.ForeachAwaitPublisher"/>; set another with
/// <see cref="HecoleMediatorConfiguration.NotificationPublisher"/> or
/// <see cref="HecoleMediatorConfiguration.NotificationPublisherType"/>.
/// </summary>
public interface INotificationPublisher
{
    /// <summary>
    /// Runs the handlers of <paramref name="notification"/>.
    /// </summary>
    /// <param name="handlerExecutors">One entry per handler, in registration order. The publisher may run them in any
    /// order, skip some, or run them concurrently.</param>
    /// <param name="notification">The notification being published.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the publication is over; the task returned by <c>Publish</c>.</returns>
    Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken);
}
