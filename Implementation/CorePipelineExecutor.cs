using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;

namespace Hecole.Mediator.Implementation;

internal static class CorePipelineExecutor
{
    /// <summary>
    /// Executes the pipeline of behaviors in sequence and calls the final handler.
    /// </summary>
    public static async Task<TResponse> ExecutePipeline<TRequest, TResponse>(
        TRequest request,
        IEnumerable<IPipelineBehavior<IRequest<TResponse>, TResponse>> behaviors,
        RequestHandlerDelegate<TResponse> handler,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);

        // If there are no behaviors, executes the handler directly
        if (behaviors is null || !behaviors.Any())
            return await handler().ConfigureAwait(false);

        // Builds the reverse pipeline (last behavior most external)
        RequestHandlerDelegate<TResponse> next = handler;

        foreach (var behavior in behaviors.Reverse())
        {
            var current = next;
            next = () => behavior.Handle(request, current, cancellationToken);
        }

        return await next().ConfigureAwait(false);
    }
}