using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Hecole.Mediator.Behaviors;

/// <summary>
/// Optional pipeline behavior that logs the start and the end of each request with the request type name and the
/// elapsed time. Never writes the request or the response. Enters the pipeline only when registered:
/// <c>cfg.AddOpenBehavior(typeof(LoggingBehavior&lt;,&gt;))</c>.
/// </summary>
/// <remarks>
/// Events (category <c>Hecole.Mediator.Behaviors.LoggingBehavior</c>):
/// <list type="table">
/// <item><term>1001 RequestStarting</term><description>Information, before the next step.</description></item>
/// <item><term>1002 RequestFinished</term><description>Information, on success, with the elapsed milliseconds.</description></item>
/// <item><term>1003 RequestFailed</term><description>Information, when the next step throws, with the exception type name (not the exception) and the elapsed milliseconds. The exception goes on to the caller unchanged.</description></item>
/// <item><term>1004 RequestCanceled</term><description>Information, when an <see cref="OperationCanceledException"/> is thrown and the caller's token was canceled.</description></item>
/// </list>
/// </remarks>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response type.</typeparam>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    /// <summary>Creates the behavior.</summary>
    /// <param name="logger">Logger of this behavior.</param>
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var requestName = RequestName<TRequest>.Value;
        BehaviorLog.RequestStarting(_logger, requestName);
        var start = Stopwatch.GetTimestamp();
        try
        {
            var response = await next().ConfigureAwait(false);
            BehaviorLog.RequestFinished(_logger, requestName, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            BehaviorLog.RequestCanceled(_logger, requestName, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            BehaviorLog.RequestFailed(_logger, requestName, exception.GetType().Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            throw;
        }
    }
}
