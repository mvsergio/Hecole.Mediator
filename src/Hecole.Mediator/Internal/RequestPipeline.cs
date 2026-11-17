using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Internal;

/// <summary>
/// Behaviors of a request, read from the container on every call
/// (<c>IEnumerable&lt;IPipelineBehavior&lt;TRequest, TResponse&gt;&gt;</c>), so transient and scoped behaviors keep their
/// lifetime. Registration order is execution order: the first registered is the outermost.
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
}

/// <summary>
/// One <c>Send</c> through at least one behavior. A single object per call holds the request, the provider and the
/// caller's token; the chain of <c>next</c> delegates is built from the innermost behavior outward, one small step
/// object per inner behavior, and the outermost behavior is called directly. Each <c>next</c> is bound to its
/// position, so a behavior may call it more than once (retry). The handler is resolved only when the pipeline
/// reaches it.
/// <para>
/// Token rule: <c>next()</c> without a token continues with the caller's token;
/// <c>next(token)</c> continues with the token given.
/// </para>
/// </summary>
internal abstract class PipelineInvocation<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly CancellationToken _callerToken;

    protected PipelineInvocation(TRequest request, IServiceProvider services, CancellationToken callerToken)
    {
        Request = request;
        Services = services;
        _callerToken = callerToken;
    }

    protected TRequest Request { get; }

    protected IServiceProvider Services { get; }

    public Task<TResponse> Run(IPipelineBehavior<TRequest, TResponse>[] behaviors)
    {
        RequestHandlerDelegate<TResponse> next = InvokeHandlerStep;
        for (var i = behaviors.Length - 1; i > 0; i--)
            next = new Step(this, behaviors[i], next).Invoke;

        return behaviors[0].Handle(Request, next, _callerToken);
    }

    /// <summary>Resolves the handler and calls it with <paramref name="cancellationToken"/>.</summary>
    protected abstract Task<TResponse> InvokeHandler(CancellationToken cancellationToken);

    private Task<TResponse> InvokeHandlerStep(CancellationToken t) => InvokeHandler(t.CanBeCanceled ? t : _callerToken);

    private sealed class Step(PipelineInvocation<TRequest, TResponse> owner, IPipelineBehavior<TRequest, TResponse> behavior, RequestHandlerDelegate<TResponse> next)
    {
        public Task<TResponse> Invoke(CancellationToken t)
            => behavior.Handle(owner.Request, next, t.CanBeCanceled ? t : owner._callerToken);
    }
}
