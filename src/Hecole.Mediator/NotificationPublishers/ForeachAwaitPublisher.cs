namespace Hecole.Mediator.NotificationPublishers;

/// <summary>
/// Default publisher: runs the handlers one at a time, in registration order, each one only after the previous one
/// has completed. The first exception stops the publication (the remaining handlers do not run) and reaches the caller.
/// Safe for handlers that share scoped services such as a <c>DbContext</c>.
/// </summary>
public class ForeachAwaitPublisher : INotificationPublisher
{
    /// <inheritdoc />
    public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlerExecutors);

        if (handlerExecutors is NotificationHandlerExecutor[] array)
        {
            for (var i = 0; i < array.Length; i++)
                await array[i].HandlerCallback(notification, cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var handler in handlerExecutors)
            await handler.HandlerCallback(notification, cancellationToken).ConfigureAwait(false);
    }
}
