namespace Hecole.Mediator;

/// <summary>
/// Publishes a notification to all of its handlers.
/// </summary>
/// <remarks>
/// Only argument errors are thrown synchronously: a null notification or an object that is not a notification. Every
/// other failure arrives in the returned task, including a mediator resolved from the root provider while
/// <see cref="HecoleMediatorConfiguration.RequireScope"/> is on, a handler or publisher that cannot be resolved, and an
/// exception thrown by a handler or by the publisher before it returns its task.
/// </remarks>
public interface IPublisher
{
    /// <summary>
    /// Publishes a notification whose type is known only at run time.
    /// </summary>
    /// <param name="notification">The notification; it must implement <see cref="INotification"/>.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when every handler has run. Any failure other than an argument error arrives in
    /// this task.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="notification"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The object is not a notification.</exception>
    Task Publish(object notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a notification.
    /// </summary>
    /// <typeparam name="TNotification">The type of the notification.</typeparam>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when every handler has run. Any failure other than an argument error arrives in
    /// this task.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="notification"/> is <see langword="null"/>.</exception>
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
