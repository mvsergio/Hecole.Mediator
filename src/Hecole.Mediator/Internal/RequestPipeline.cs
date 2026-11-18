using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Internal;

/// <summary>
/// Behaviors of a request, read from the container on every call
/// (<c>IEnumerable&lt;IPipelineBehavior&lt;TRequest, TResponse&gt;&gt;</c>), so transient and scoped behaviors keep their
/// lifetime. Registration order is execution order: the first registered is the outermost.
/// <para>
/// Token rule: the outermost behavior receives the caller's token. A behavior's <c>next(token)</c> continues with
/// <c>token</c> when it can be canceled; <c>next()</c>, or any token that cannot be canceled (such as
/// <see cref="CancellationToken.None"/>; the delegate cannot tell an omitted argument from an explicit
/// <see langword="default"/>), continues with the token that behavior received. The handler receives the token the
/// innermost behavior continued with. A behavior may call <c>next</c> more than once (retry); each call runs the rest
/// of the pipeline again with the token given to that call.
/// </para>
/// <para>
/// The chain is built lazily: the <c>next</c> of a behavior is created when the behavior before it runs, holding the
/// position and the token that behavior received. With each behavior calling <c>next</c> once, a call through
/// <c>n</c> behaviors allocates one invocation object, <c>n</c> delegates and <c>n - 1</c> step objects (the last
/// one smaller, as it only calls the handler). The handler is resolved only when the pipeline reaches it.
/// </para>
/// </summary>
internal static class RequestPipeline
{
    public static IPipelineBehavior<TRequest, TResponse>[] ResolveBehaviors<TRequest, TResponse>(IServiceProvider services)
        where TRequest : notnull
        => services.GetService<IEnumerable<IPipelineBehavior<TRequest, TResponse>>>() switch
        {
            null => [],
            IPipelineBehavior<TRequest, TResponse>[] array => array,
            var other => other.ToArray(),
        };

    /// <summary>Runs <paramref name="request"/> through at least one behavior, then the handler.</summary>
    public static Task<TResponse> Run<TRequest, TResponse, THandler>(
        TRequest request, IServiceProvider services, IPipelineBehavior<TRequest, TResponse>[] behaviors, CancellationToken callerToken)
        where TRequest : notnull
        where THandler : IHandlerInvoker<TRequest, TResponse>
        => behaviors.Length == 1
            ? behaviors[0].Handle(request, new SingleBehaviorInvocation<TRequest, TResponse, THandler>(request, services, callerToken).Next, callerToken)
            : behaviors[0].Handle(request, new PipelineInvocation<TRequest, TResponse, THandler>(request, services, behaviors, callerToken).NextOfOutermost, callerToken);
}

/// <summary>Resolves the handler of a request and calls it; the end of the pipeline.</summary>
internal interface IHandlerInvoker<TRequest, TResponse>
    where TRequest : notnull
{
    static abstract Task<TResponse> InvokeHandler(TRequest request, IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>A <c>Send</c> through exactly one behavior: its <c>next</c> calls the handler.</summary>
internal sealed class SingleBehaviorInvocation<TRequest, TResponse, THandler>(TRequest request, IServiceProvider services, CancellationToken callerToken)
    where TRequest : notnull
    where THandler : IHandlerInvoker<TRequest, TResponse>
{
    public Task<TResponse> Next(CancellationToken t) => THandler.InvokeHandler(request, services, t.CanBeCanceled ? t : callerToken);
}

/// <summary>
/// A <c>Send</c> through two behaviors or more. This object is the <c>next</c> of the outermost behavior; the
/// <c>next</c> of each inner behavior is created when that behavior is called (see <see cref="RequestPipeline"/>).
/// </summary>
internal sealed class PipelineInvocation<TRequest, TResponse, THandler>
    where TRequest : notnull
    where THandler : IHandlerInvoker<TRequest, TResponse>
{
    private readonly TRequest _request;
    private readonly IServiceProvider _services;
    private readonly IPipelineBehavior<TRequest, TResponse>[] _behaviors;
    private readonly CancellationToken _callerToken;

    public PipelineInvocation(TRequest request, IServiceProvider services, IPipelineBehavior<TRequest, TResponse>[] behaviors, CancellationToken callerToken)
    {
        _request = request;
        _services = services;
        _behaviors = behaviors;
        _callerToken = callerToken;
    }

    public Task<TResponse> NextOfOutermost(CancellationToken t) => Continue(1, t.CanBeCanceled ? t : _callerToken);

    // Calls the behavior at index (1 or more) with token, the token that behavior receives.
    private Task<TResponse> Continue(int index, CancellationToken token)
    {
        RequestHandlerDelegate<TResponse> next = index == _behaviors.Length - 1
            ? new LastStep(this, token).Invoke
            : new Step(this, index + 1, token).Invoke;

        return _behaviors[index].Handle(_request, next, token);
    }

    /// <summary>The <c>next</c> of an inner behavior that is not the last: the position after it and the token it received.</summary>
    private sealed class Step(PipelineInvocation<TRequest, TResponse, THandler> owner, int index, CancellationToken received)
    {
        public Task<TResponse> Invoke(CancellationToken t) => owner.Continue(index, t.CanBeCanceled ? t : received);
    }

    /// <summary>The <c>next</c> of the last behavior: calls the handler.</summary>
    private sealed class LastStep(PipelineInvocation<TRequest, TResponse, THandler> owner, CancellationToken received)
    {
        public Task<TResponse> Invoke(CancellationToken t)
            => THandler.InvokeHandler(owner._request, owner._services, t.CanBeCanceled ? t : received);
    }
}
