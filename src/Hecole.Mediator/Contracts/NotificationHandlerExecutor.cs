namespace Hecole.Mediator;

/// <summary>
/// One notification handler, as given to an <see cref="INotificationPublisher"/>.
/// </summary>
/// <param name="HandlerInstance">The handler instance, an <see cref="INotificationHandler{TNotification}"/>.</param>
/// <param name="HandlerCallback">Calls the handler's <c>Handle</c> with the notification and token given.</param>
public record NotificationHandlerExecutor(object HandlerInstance, Func<INotification, CancellationToken, Task> HandlerCallback);
