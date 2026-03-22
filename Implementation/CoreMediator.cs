using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;
using System.Collections.Concurrent;
using System.Reflection;

namespace Hecole.Mediator.Implementation;

public sealed class CoreMediator(IServiceProvider serviceProvider) : ICoreMediator
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private static readonly ConcurrentDictionary<Type, MethodInfo> _handlerMethodCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> _executePipelineCache = new();

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

        // Resolve behaviors for the CONCRETE request type
        var behaviorInterfaceType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(behaviorInterfaceType);
        var behaviors = _serviceProvider.GetService(enumerableType) as IEnumerable<object>;
        var behaviorList = behaviors?.ToList() ?? [];

        // Build the handler delegate
        RequestHandlerDelegate<TResponse> handlerDelegate = async () =>
        {
            var task = (Task<TResponse>?)method.Invoke(handler, new object[] { request, cancellationToken });
            if (task is null)
                throw new InvalidOperationException($"Failure invoking the Handle method in '{handlerType.FullName}'.");
            return await task.ConfigureAwait(false);
        };

        // If no behaviors, execute handler directly
        if (behaviorList.Count == 0)
            return await handlerDelegate().ConfigureAwait(false);

        // Use reflection to call the generic ExecutePipeline with the concrete request type
        var executePipelineMethod = _executePipelineCache.GetOrAdd(requestType, rt =>
        {
            return typeof(CorePipelineExecutor)
                .GetMethod(nameof(CorePipelineExecutor.ExecutePipeline), BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(rt, typeof(TResponse));
        });

        var resultTask = (Task<TResponse>?)executePipelineMethod.Invoke(null,
            [request, behaviorList, handlerDelegate, cancellationToken]);

        return await resultTask!.ConfigureAwait(false);
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
