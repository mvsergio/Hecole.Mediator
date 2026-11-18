using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Internal;

/// <summary>
/// Sends one request type to its handler. One instance per registered request type, created at registration
/// (generic code for <c>AddRequestHandler</c>, reflection once for a scanned handler) and shared by every call; stateless. The request arrives as <see cref="object"/> because the caller only knows
/// the runtime type; the single cast to the request type is the only conversion on the hot path.
/// </summary>
internal abstract class RequestExecutor
{
    internal static THandler ResolveHandler<THandler>(IServiceProvider services, Type requestType)
        where THandler : class
        => services.GetService<THandler>() ?? throw new InvalidOperationException(
            $"No handler is registered for the request '{requestType.FullName}'. Register it with " +
            "services.AddRequestHandler<TRequest, TResponse, THandler>(), services.AddRequestHandler<TRequest, THandler>(), " +
            "an assembly scan (cfg.RegisterServicesFromAssembly) " +
            $"or as {typeof(THandler).Name.Split('`')[0]}<...> in the container.");

    public abstract Type RequestType { get; }

    public abstract Type ResponseType { get; }

    /// <summary>Path of <c>Send&lt;TRequest&gt;(TRequest)</c>: the response, if any, is not observed.</summary>
    public abstract Task SendAsTask(object request, IServiceProvider services, CancellationToken cancellationToken);

    /// <summary>Path of <c>Send(object)</c>: the response is boxed.</summary>
    public abstract Task<object?> SendBoxed(object request, IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>Executor whose response type is known; <c>Send&lt;TResponse&gt;</c> uses it without boxing.</summary>
internal abstract class RequestExecutor<TResponse> : RequestExecutor
{
    public sealed override Type ResponseType => typeof(TResponse);

    public abstract Task<TResponse> Send(object request, IServiceProvider services, CancellationToken cancellationToken);

    public override Task SendAsTask(object request, IServiceProvider services, CancellationToken cancellationToken)
        => Send(request, services, cancellationToken);

    public sealed override async Task<object?> SendBoxed(object request, IServiceProvider services, CancellationToken cancellationToken)
        => await Send(request, services, cancellationToken).ConfigureAwait(false);
}

/// <summary>Executor of a request that produces <typeparamref name="TResponse"/>.</summary>
internal sealed class Executor<TRequest, TResponse> : RequestExecutor<TResponse>, IHandlerInvoker<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public override Type RequestType => typeof(TRequest);

    public override Task<TResponse> Send(object request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var typed = (TRequest)request;
        var behaviors = RequestPipeline.ResolveBehaviors<TRequest, TResponse>(services);

        return behaviors.Length == 0
            ? InvokeHandler(typed, services, cancellationToken)
            : RequestPipeline.Run<TRequest, TResponse, Executor<TRequest, TResponse>>(typed, services, behaviors, cancellationToken);
    }

    public static Task<TResponse> InvokeHandler(TRequest request, IServiceProvider services, CancellationToken cancellationToken)
        => ResolveHandler<IRequestHandler<TRequest, TResponse>>(services, typeof(TRequest)).Handle(request, cancellationToken);
}

/// <summary>Executor of a request without response value; the pipeline sees <see cref="Unit"/>.</summary>
internal sealed class VoidExecutor<TRequest> : RequestExecutor<Unit>, IHandlerInvoker<TRequest, Unit>
    where TRequest : IRequest
{
    public override Type RequestType => typeof(TRequest);

    public override Task<Unit> Send(object request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var typed = (TRequest)request;
        var behaviors = RequestPipeline.ResolveBehaviors<TRequest, Unit>(services);

        return behaviors.Length == 0
            ? InvokeHandler(typed, services, cancellationToken)
            : RequestPipeline.Run<TRequest, Unit, VoidExecutor<TRequest>>(typed, services, behaviors, cancellationToken);
    }

    public override Task SendAsTask(object request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var typed = (TRequest)request;
        var behaviors = RequestPipeline.ResolveBehaviors<TRequest, Unit>(services);

        // Without behaviors the handler's task is the answer; no Unit adaptation needed.
        return behaviors.Length == 0
            ? ResolveHandler<IRequestHandler<TRequest>>(services, typeof(TRequest)).Handle(typed, cancellationToken)
            : RequestPipeline.Run<TRequest, Unit, VoidExecutor<TRequest>>(typed, services, behaviors, cancellationToken);
    }

    public static Task<Unit> InvokeHandler(TRequest request, IServiceProvider services, CancellationToken cancellationToken)
        => ToUnit(ResolveHandler<IRequestHandler<TRequest>>(services, typeof(TRequest)).Handle(request, cancellationToken));

    private static Task<Unit> ToUnit(Task task)
    {
        return task.IsCompletedSuccessfully ? Unit.Task : Await(task);

        static async Task<Unit> Await(Task task)
        {
            await task.ConfigureAwait(false);
            return Unit.Value;
        }
    }
}
