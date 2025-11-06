using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;
using System.Collections.Concurrent;
using System.Reflection;

namespace Hecole.Mediator.Implementation;

public sealed class CoreMediator(IServiceProvider serviceProvider) : ICoreMediator
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private static readonly ConcurrentDictionary<Type, MethodInfo> _handlerMethodCache = new();

    public async Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));

        var handler = _serviceProvider.GetService(handlerType);
        if (handler is null)
            throw new InvalidOperationException($"Handler not found for the request type {requestType.Name}");

        var method = _handlerMethodCache.GetOrAdd(handlerType, static type =>
        {
            var m = type.GetMethod("Handle", BindingFlags.Public | BindingFlags.Instance);
            if (m is null)
                throw new InvalidOperationException($"The handler '{type.FullName}' does not contain the Handle method.");
            return m;
        });

        // Loads registered behaviors
        var behaviorType = typeof(IEnumerable<>).MakeGenericType(typeof(IPipelineBehavior<,>)
            .MakeGenericType(requestType, typeof(TResponse)));

        var behaviors = _serviceProvider.GetService(behaviorType) as IEnumerable<object>;
        var castedBehaviors = behaviors?.CastDynamic<IPipelineBehavior<IRequest<TResponse>, TResponse>>()
            ?? Enumerable.Empty<IPipelineBehavior<IRequest<TResponse>, TResponse>>();

        // Executes pipeline + final handler
        return await CorePipelineExecutor.ExecutePipeline(
            request,
            castedBehaviors,
            async () =>
            {
                var task = (Task<TResponse>?)method.Invoke(handler, new object[] { request, cancellationToken });
                if (task is null)
                    throw new InvalidOperationException($"Failure invoking the Handle method in '{handlerType.FullName}'.");
                return await task.ConfigureAwait(false);
            },
            cancellationToken);
    }

    public async Task Publish<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        var notificationType = notification.GetType();
        var handlerType = typeof(INotificationHandler<>).MakeGenericType(notificationType);

        var handlers = (_serviceProvider.GetService(typeof(IEnumerable<>).MakeGenericType(handlerType))
            as IEnumerable<object>)?.ToList() ?? new List<object>();

        if (handlers.Count == 0)
            return;

        var tasks = new List<Task>(handlers.Count);

        foreach (var handler in handlers)
        {
            if (handler is null) continue;

            var method = _handlerMethodCache.GetOrAdd(handler.GetType(), static type =>
            {
                var m = type.GetMethod("Handle", BindingFlags.Public | BindingFlags.Instance);
                if (m is null)
                    throw new InvalidOperationException($"The handler '{type.FullName}' does not contain the Handle method.");
                return m;
            });

            var result = method.Invoke(handler, new object[] { notification, cancellationToken });
            if (result is Task task)
                tasks.Add(task);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}

internal static class EnumerableExtensions
{
    public static IEnumerable<TCast> CastDynamic<TCast>(this IEnumerable<object> source)
    {
        foreach (var item in source)
        {
            if (item is TCast casted)
                yield return casted;
        }
    }
}