namespace Hecole.Mediator;

/// <summary>
/// Handles a notification of type <typeparamref name="TNotification"/>.
/// More than one handler may exist for the same notification type.
/// </summary>
/// <typeparam name="TNotification">The type of the notification.</typeparam>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>
    /// Handles the notification.
    /// </summary>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the notification has been handled.</returns>
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}
