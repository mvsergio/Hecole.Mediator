using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Internal;

/// <summary>
/// Default <see cref="IMediator"/>. <c>Send</c> and <c>Publish</c> find the executor of the message's runtime type in
/// their catalog and run it with the provider this mediator was resolved from.
/// </summary>
internal sealed class Mediator(IServiceProvider serviceProvider, MediatorRuntime runtime) : IMediator
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly RequestCatalog _catalog = runtime.Catalog;
    private readonly NotificationCatalog _notifications = runtime.Notifications;
    private readonly bool _resolvedFromRoot = runtime.RequireScope && ReferenceEquals(serviceProvider, runtime.RootProvider);

    // Resolved on the first Publish from this mediator's provider, so a Send-only mediator never resolves it;
    // one publisher instance per mediator.
    private INotificationPublisher? _publisher;

    /// <inheritdoc />
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executor = GetExecutor(request.GetType());

        return executor is RequestExecutor<TResponse> typed
            ? typed.Send(request, _serviceProvider, cancellationToken)
            : SendCovariant<TResponse>(executor, request, cancellationToken);
    }

    /// <inheritdoc />
    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        ArgumentNullException.ThrowIfNull(request);

        return GetExecutor(request.GetType()).SendAsTask(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc />
    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request is not IBaseRequest)
        {
            throw new ArgumentException(
                $"The type '{request.GetType().FullName}' does not implement {nameof(IRequest)} or {nameof(IRequest)}<TResponse>.",
                nameof(request));
        }

        return GetExecutor(request.GetType()).SendBoxed(request, _serviceProvider, cancellationToken);
    }

    private RequestExecutor GetExecutor(Type requestType)
    {
        if (_resolvedFromRoot)
            ThrowResolvedFromRoot();

        return _catalog.GetExecutor(requestType);
    }

    private static void ThrowResolvedFromRoot()
        => throw new InvalidOperationException(
            "The mediator was resolved from the root IServiceProvider. Handlers resolved there capture scoped services " +
            "(such as a DbContext) for the lifetime of the application and are never disposed. Resolve IMediator, ISender " +
            "or IPublisher from a scope (IServiceScopeFactory.CreateScope(), HttpContext.RequestServices) or, if this is " +
            $"intended, set {nameof(HecoleMediatorConfiguration)}.{nameof(HecoleMediatorConfiguration.RequireScope)} = false.");

    // IRequest<out TResponse> is covariant: an IRequest<object> may hold a request whose handler returns a string.
    private async Task<TResponse> SendCovariant<TResponse>(RequestExecutor executor, object request, CancellationToken cancellationToken)
    {
        if (!typeof(TResponse).IsAssignableFrom(executor.ResponseType))
        {
            throw new InvalidOperationException(
                $"The handler of '{executor.RequestType.FullName}' returns '{executor.ResponseType.FullName}', " +
                $"which is not assignable to '{typeof(TResponse).FullName}'.");
        }

        return (TResponse)(await executor.SendBoxed(request, _serviceProvider, cancellationToken).ConfigureAwait(false))!;
    }

    /// <inheritdoc />
    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification is not INotification typed)
        {
            throw new ArgumentException(
                $"The type '{notification.GetType().FullName}' does not implement {nameof(INotification)}.", nameof(notification));
        }

        return PublishCore(_notifications.GetExecutor(notification.GetType()), typed, cancellationToken);
    }

    /// <inheritdoc />
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        if (notification is null)
            throw new ArgumentNullException(nameof(notification));

        // Dispatch is by runtime type. When the static type is the runtime type (always for a struct),
        // the executor is the static instance of that type: no lookup.
        var executor = typeof(TNotification).IsValueType || notification.GetType() == typeof(TNotification)
            ? NotificationExecutor<TNotification>.Instance
            : _notifications.GetExecutor(notification.GetType());

        return PublishCore(executor, notification, cancellationToken);
    }

    private Task PublishCore(NotificationExecutor executor, INotification notification, CancellationToken cancellationToken)
    {
        if (_resolvedFromRoot)
            ThrowResolvedFromRoot();

        var publisher = _publisher ??= _serviceProvider.GetRequiredService<INotificationPublisher>();
        return executor.Publish(notification, _serviceProvider, publisher, cancellationToken);
    }
}
