using System.Threading;
using System.Threading.Tasks;

namespace Hecole.Mediator.Interfaces
{
    /// <summary>
    /// Defines a handler for a notification. Multiple handlers can be registered for the same notification type.
    /// </summary>
    /// <typeparam name="TNotification">The type of the notification.</typeparam>
    public interface INotificationHandler<in TNotification> where TNotification : INotification
    {
        /// <summary>
        /// Handles the notification.
        /// </summary>
        Task Handle(TNotification notification, CancellationToken cancellationToken);
    }
}
