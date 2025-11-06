using System.Diagnostics;
using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;
using Microsoft.Extensions.Logging;

namespace Hecole.Mediator.Implementation.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        _logger.LogInformation("Starting processing of {RequestName}", requestName);
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();
        _logger.LogInformation("Finished {RequestName} in {ElapsedMilliseconds} ms", requestName, sw.ElapsedMilliseconds);
        return response;
    }
}