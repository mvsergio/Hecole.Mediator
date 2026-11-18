using Microsoft.Extensions.Logging;

namespace Hecole.Mediator.Behaviors;

/// <summary>
/// Optional pipeline behavior that logs any exception thrown by the rest of the pipeline, with the request type
/// name, and rethrows the same exception with its original stack trace. Cancellation is not an error: an
/// <see cref="OperationCanceledException"/> thrown while the token this behavior received is canceled is logged at
/// Debug and rethrown; one thrown with that token not canceled (a timeout started inside, for example) is logged as an
/// error. The token this behavior received is the caller's token when it is the outermost behavior, and otherwise the
/// token the behavior around it continued with, so register it outermost to classify by the caller's token. A
/// <see cref="RequestValidationException"/> (an invalid request, not an unhandled error) is logged at Warning with the
/// request type name and the number of errors only, and rethrown. Never writes the request, the response or the texts
/// of validation errors. Enters the pipeline only when registered:
/// <c>cfg.AddOpenBehavior(typeof(UnhandledExceptionBehavior&lt;,&gt;))</c>.
/// </summary>
/// <remarks>
/// Events (category <c>Hecole.Mediator.Behaviors.UnhandledExceptionBehavior</c>):
/// <list type="table">
/// <item><term>3001 UnhandledException</term><description>Error, with the exception object and the request type name.</description></item>
/// <item><term>3002 RequestCanceledByCaller</term><description>Debug, with the request type name.</description></item>
/// <item><term>3003 RequestValidationFailed</term><description>Warning, when a <see cref="RequestValidationException"/> is thrown, with the request type name and the error count; no exception object, no message, no error text.</description></item>
/// </list>
/// </remarks>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response type.</typeparam>
public sealed class UnhandledExceptionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> _logger;

    /// <summary>Creates the behavior.</summary>
    /// <param name="logger">Logger of this behavior.</param>
    public UnhandledExceptionBehavior(ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            return await next().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            BehaviorLog.RequestCanceledByCaller(_logger, RequestName<TRequest>.Value);
            throw;
        }
        catch (RequestValidationException exception)
        {
            BehaviorLog.RequestValidationFailed(_logger, RequestName<TRequest>.Value, exception.Errors.Count);
            throw;
        }
        catch (Exception exception)
        {
            BehaviorLog.UnhandledException(_logger, exception, RequestName<TRequest>.Value);
            throw;
        }
    }
}
