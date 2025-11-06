using System.Diagnostics;
using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;
using Microsoft.Extensions.Logging;

namespace Hecole.Mediator.Implementation.Behaviors;

public sealed class PerformanceBehavior<TRequest, TResponse>(ILogger<PerformanceBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();
        if (stopwatch.ElapsedMilliseconds > 500)
        {
            _logger.LogWarning(
                "Request {RequestName} took {ElapsedMilliseconds} ms to process",
                typeof(TRequest).Name,
                stopwatch.ElapsedMilliseconds);
        }
        return response;
    }
}