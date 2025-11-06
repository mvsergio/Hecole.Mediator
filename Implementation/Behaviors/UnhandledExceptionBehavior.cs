using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;
using Microsoft.Extensions.Logging;

namespace Hecole.Mediator.Implementation.Behaviors;

/// <summary>
/// Captures unhandled exceptions during the execution of a request in the pipeline.
/// Similar to the UnhandledExceptionBehavior of MediatR.
/// </summary>
/// <typeparam name="TRequest">Type of the request.</typeparam>
/// <typeparam name="TResponse">Type of the expected response.</typeparam>
public sealed class UnhandledExceptionBehavior<TRequest, TResponse>(ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            // Executes the next behavior or handler
            return await next().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancels silently (not a real failure)
            _logger.LogWarning("The operation was canceled for the request {RequestName}.", typeof(TRequest).Name);
            throw;
        }
        catch (Exception ex)
        {
            // Structured and detailed log
            _logger.LogError(ex,
                "Unhandled error while processing request {RequestName} with payload: {@Request}",
                typeof(TRequest).Name,
                request);
            // Repropagates the exception to the API exception middleware
            throw;
        }
    }
}