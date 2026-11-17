using Microsoft.Extensions.Logging;

namespace Hecole.Mediator.Behaviors;

/// <summary>
/// Log messages of the optional behaviors, generated at compile time. Each method checks <c>IsEnabled</c> before
/// formatting, so nothing is allocated when the level is off. Only the request type name, times and the exception
/// object are written; the request and the response are never written.
/// </summary>
/// <remarks>
/// Stable event ids: 1001 to 1004 <see cref="LoggingBehavior{TRequest, TResponse}"/>;
/// 2001 <see cref="PerformanceBehavior{TRequest, TResponse}"/>;
/// 3001 and 3002 <see cref="UnhandledExceptionBehavior{TRequest, TResponse}"/>.
/// </remarks>
internal static partial class BehaviorLog
{
    [LoggerMessage(EventId = 1001, EventName = "RequestStarting", Level = LogLevel.Information,
        Message = "Handling {RequestName}")]
    public static partial void RequestStarting(ILogger logger, string requestName);

    [LoggerMessage(EventId = 1002, EventName = "RequestFinished", Level = LogLevel.Information,
        Message = "Handled {RequestName} in {ElapsedMilliseconds} ms")]
    public static partial void RequestFinished(ILogger logger, string requestName, double elapsedMilliseconds);

    [LoggerMessage(EventId = 1003, EventName = "RequestFailed", Level = LogLevel.Information,
        Message = "{RequestName} failed with {ExceptionType} after {ElapsedMilliseconds} ms")]
    public static partial void RequestFailed(ILogger logger, string requestName, string exceptionType, double elapsedMilliseconds);

    [LoggerMessage(EventId = 1004, EventName = "RequestCanceled", Level = LogLevel.Information,
        Message = "{RequestName} was canceled after {ElapsedMilliseconds} ms")]
    public static partial void RequestCanceled(ILogger logger, string requestName, double elapsedMilliseconds);

    [LoggerMessage(EventId = 2001, EventName = "SlowRequest", Level = LogLevel.Warning,
        Message = "{RequestName} took {ElapsedMilliseconds} ms, over the threshold of {ThresholdMilliseconds} ms")]
    public static partial void SlowRequest(ILogger logger, string requestName, double elapsedMilliseconds, double thresholdMilliseconds);

    [LoggerMessage(EventId = 3001, EventName = "UnhandledException", Level = LogLevel.Error,
        Message = "Unhandled exception while handling {RequestName}")]
    public static partial void UnhandledException(ILogger logger, Exception exception, string requestName);

    [LoggerMessage(EventId = 3002, EventName = "RequestCanceledByCaller", Level = LogLevel.Debug,
        Message = "{RequestName} was canceled by the caller")]
    public static partial void RequestCanceledByCaller(ILogger logger, string requestName);
}

/// <summary>Request type name, computed once per type.</summary>
internal static class RequestName<TRequest>
{
    public static readonly string Value = typeof(TRequest).Name;
}
