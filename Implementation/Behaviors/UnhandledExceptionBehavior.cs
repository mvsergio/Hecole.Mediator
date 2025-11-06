using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;
using Microsoft.Extensions.Logging;

namespace Hecole.Mediator.Implementation.Behaviors
{
    /// <summary>
    /// Captura exceções não tratadas durante a execução de um request no pipeline.
    /// Similar ao UnhandledExceptionBehavior do MediatR.
    /// </summary>
    /// <typeparam name="TRequest">Tipo da requisição.</typeparam>
    /// <typeparam name="TResponse">Tipo da resposta esperada.</typeparam>
    public sealed class UnhandledExceptionBehavior<TRequest, TResponse>
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> _logger;

        public UnhandledExceptionBehavior(ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            try
            {
                // Executa o próximo comportamento ou handler
                return await next().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancela silenciosamente (não é uma falha real)
                _logger.LogWarning("A operação foi cancelada para o request {RequestName}.", typeof(TRequest).Name);
                throw;
            }
            catch (Exception ex)
            {
                // Log estruturado e detalhado
                _logger.LogError(ex,
                    "Erro não tratado ao processar request {RequestName} com payload: {@Request}",
                    typeof(TRequest).Name,
                    request);

                // Repropaga a exceção para o middleware de exceção da API
                throw;
            }
        }
    }
}
