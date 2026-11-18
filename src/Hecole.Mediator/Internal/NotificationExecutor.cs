using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Internal;

/// <summary>
/// Publishes one notification type: resolves its handlers from the container
/// (<c>IEnumerable&lt;INotificationHandler&lt;TNotification&gt;&gt;</c>, exact type, on every call so lifetimes are kept),
/// and hands every handler the container returned, in its order, to the <see cref="INotificationPublisher"/>. Nothing is
/// de-duplicated here: the library itself never registers the same handler twice for a notification, and a handler
/// registered twice directly in the container runs twice (<c>VerifyHecoleMediator</c> reports it).
/// One stateless instance per notification type, shared by every call; no reflection on the hot path.
/// </summary>
internal abstract class NotificationExecutor
{
    public abstract Type NotificationType { get; }

    public abstract Task Publish(INotification notification, IServiceProvider services, INotificationPublisher publisher, CancellationToken cancellationToken);

    /// <summary>The executor of <paramref name="notificationType"/>, created with reflection (registration or first use only).</summary>
    public static NotificationExecutor For(Type notificationType)
        => (NotificationExecutor)typeof(NotificationExecutor<>).MakeGenericType(notificationType)
            .GetField(nameof(NotificationExecutor<INotification>.Instance), BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
}

/// <summary>Executor of <typeparamref name="TNotification"/>.</summary>
internal sealed class NotificationExecutor<TNotification> : NotificationExecutor
    where TNotification : INotification
{
    public static readonly NotificationExecutor<TNotification> Instance = new();

    private NotificationExecutor()
    {
    }

    public override Type NotificationType => typeof(TNotification);

    public override Task Publish(INotification notification, IServiceProvider services, INotificationPublisher publisher, CancellationToken cancellationToken)
        => publisher.Publish(Executors(services), notification, cancellationToken);

    private static NotificationHandlerExecutor[] Executors(IServiceProvider services)
    {
        var handlers = services.GetService<IEnumerable<INotificationHandler<TNotification>>>() switch
        {
            null => [],
            INotificationHandler<TNotification>[] array => array,
            var other => other.ToArray(),
        };

        if (handlers.Length == 0)
            return [];

        var count = 0;
        foreach (var handler in handlers)
        {
            if (handler is not null)
                count++;
        }

        var executors = new NotificationHandlerExecutor[count];
        for (int i = 0, j = 0; i < handlers.Length; i++)
        {
            if (handlers[i] is { } handler)
                executors[j++] = new NotificationHandlerExecutor(handler, handler.Invoke);
        }

        return executors;
    }
}

/// <summary>Callback of a <see cref="NotificationHandlerExecutor"/>: one delegate bound to the handler, no closure.</summary>
internal static class NotificationHandlerCallback
{
    public static Task Invoke<TNotification>(this INotificationHandler<TNotification> handler, INotification notification, CancellationToken cancellationToken)
        where TNotification : INotification
        => handler.Handle((TNotification)notification, cancellationToken);
}

/// <summary>
/// Map of notification type to executor, by exact runtime type: notification types known at registration
/// (<c>AddNotificationHandler</c>, assembly scan) in a <see cref="FrozenDictionary{TKey, TValue}"/>; any other type
/// (handlers registered directly in the container, or no handler at all) gets its executor on first use, kept for the next calls.
/// </summary>
internal sealed class NotificationCatalog(IEnumerable<NotificationExecutor> registered)
{
    private readonly FrozenDictionary<Type, NotificationExecutor> _registered =
        registered.ToFrozenDictionary(static e => e.NotificationType);

    private readonly ConcurrentDictionary<Type, NotificationExecutor> _onDemand = new();

    /// <summary>Notification types whose executor was created on first use and kept (diagnostics and tests).</summary>
    public int OnDemandCount => _onDemand.Count;

    public NotificationExecutor GetExecutor(Type notificationType)
    {
        if (_registered.TryGetValue(notificationType, out var executor) || _onDemand.TryGetValue(notificationType, out executor))
            return executor;

        return _onDemand.GetOrAdd(notificationType, static type => NotificationExecutor.For(type));
    }
}
