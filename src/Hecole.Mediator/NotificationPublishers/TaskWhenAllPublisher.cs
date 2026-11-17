namespace Hecole.Mediator.NotificationPublishers;

/// <summary>
/// Starts every handler, in registration order, without waiting for the previous one, and completes when all of them
/// have completed (<see cref="Task.WhenAll(IEnumerable{Task})"/>; no <see cref="Task.Run(Func{Task})"/>, so each handler
/// runs synchronously until its first incomplete await). Every handler starts even if another one throws, synchronously
/// or not; the returned task carries every exception (awaiting it throws the first one).
/// <para>
/// Handlers run concurrently: they must not share a non thread-safe scoped service such as a <c>DbContext</c>.
/// </para>
/// </summary>
public class TaskWhenAllPublisher : INotificationPublisher
{
    /// <inheritdoc />
    public Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlerExecutors);

        var tasks = handlerExecutors is ICollection<NotificationHandlerExecutor> collection
            ? new List<Task>(collection.Count)
            : [];

        foreach (var handler in handlerExecutors)
            tasks.Add(Start(handler, notification, cancellationToken));

        return Task.WhenAll(tasks);
    }

    // A handler that throws before returning its task must not stop the next ones from starting: the exception becomes
    // the handler's task, as if the handler were an async method.
    private static Task Start(NotificationHandlerExecutor handler, INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            return handler.HandlerCallback(notification, cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            var canceled = new TaskCompletionSource();
            canceled.SetCanceled(ex.CancellationToken);
            return canceled.Task;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }
}
