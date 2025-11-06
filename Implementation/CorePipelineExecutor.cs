using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;

namespace Hecole.Mediator.Implementation
{
    internal static class CorePipelineExecutor
    {
        /// <summary>
        /// Executa o pipeline de behaviors em sequência e chama o handler final.
        /// </summary>
        public static async Task<TResponse> ExecutePipeline<TRequest, TResponse>(
            TRequest request,
            IEnumerable<IPipelineBehavior<IRequest<TResponse>, TResponse>> behaviors,
            RequestHandlerDelegate<TResponse> handler,
            CancellationToken cancellationToken)
            where TRequest : IRequest<TResponse>
        {
            if (request is null)
                throw new ArgumentNullException(nameof(request));

            if (handler is null)
                throw new ArgumentNullException(nameof(handler));

            // Se não houver behaviors, executa diretamente o handler
            if (behaviors is null || !behaviors.Any())
                return await handler().ConfigureAwait(false);

            // Monta o pipeline reverso (último behavior mais externo)
            RequestHandlerDelegate<TResponse> next = handler;

            foreach (var behavior in behaviors.Reverse())
            {
                var current = next;
                next = () => behavior.Handle(request, current, cancellationToken);
            }

            return await next().ConfigureAwait(false);
        }
    }
}
