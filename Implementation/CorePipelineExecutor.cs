using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;

namespace Hecole.Mediator.Implementation;

internal static class CorePipelineExecutor
{
    /// <summary>
    /// Executes the pipeline of behaviors in sequence and calls the final handler.
    /// TRequest is the CONCRETE request type (e.g. UpdateUsuarioCommand).
    /// </summary>
    public static async Task<TResponse> ExecutePipeline<TRequest, TResponse>(
        TRequest request,
        IEnumerable<object> behaviors,
        RequestHandlerDelegate<TResponse> handler,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);

        // Cast behaviors to the correct concrete generic type
        var typedBehaviors = behaviors
            .OfType<IPipelineBehavior<TRequest, TResponse>>()
            .ToList();

        // If there are no behaviors, executes the handler directly
        if (typedBehaviors.Count == 0)
            return await handler().ConfigureAwait(false);

        // Builds the reverse pipeline (last behavior most external)
        RequestHandlerDelegate<TResponse> next = handler;

        foreach (var behavior in typedBehaviors.AsEnumerable().Reverse())
        {
            var current = next;
            next = () => behavior.Handle(request, current, cancellationToken);
        }

        return await next().ConfigureAwait(false);
    }
}
