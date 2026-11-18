using System.Text;
using Microsoft.Extensions.Logging;

namespace Hecole.Mediator.Behaviors;

/// <summary>
/// Log messages of the optional behaviors, generated at compile time. Each method checks <c>IsEnabled</c> before
/// formatting, so nothing is allocated when the level is off. Only the request type name, times, counts and the
/// exception object are written; the request, the response and the texts of validation errors are never written.
/// </summary>
/// <remarks>
/// Stable event ids: 1001 to 1004 <see cref="LoggingBehavior{TRequest, TResponse}"/>;
/// 2001 <see cref="PerformanceBehavior{TRequest, TResponse}"/>;
/// 3001 to 3003 <see cref="UnhandledExceptionBehavior{TRequest, TResponse}"/>.
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

    [LoggerMessage(EventId = 3003, EventName = "RequestValidationFailed", Level = LogLevel.Warning,
        Message = "{RequestName} failed validation with {ErrorCount} error(s)")]
    public static partial void RequestValidationFailed(ILogger logger, string requestName, int errorCount);
}

/// <summary>Request type name (see <see cref="TypeNames.Format(Type)"/>), computed once per type.</summary>
internal static class RequestName<TRequest>
{
    public static readonly string Value = TypeNames.Format(typeof(TRequest));
}

/// <summary>
/// Readable type names for logs and messages: namespace-qualified, without assembly, nested types joined with
/// <c>.</c>, generic arguments in <c>&lt;...&gt;</c> formatted with the same rule (for example
/// <c>My.App.Classrooms.Create.Command</c> and <c>My.App.Paged&lt;My.App.Student&gt;</c>).
/// </summary>
internal static class TypeNames
{
    public static string Format(Type type)
    {
        var builder = new StringBuilder();
        Append(builder, type);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, Type type)
    {
        if (type.IsArray)
        {
            Append(builder, type.GetElementType()!);
            builder.Append('[').Append(',', type.GetArrayRank() - 1).Append(']');
            return;
        }

        if (type.IsGenericParameter)
        {
            builder.Append(type.Name);
            return;
        }

        // A nested type carries the generic arguments of its declaring types first; each level takes its own share.
        var arguments = type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes;
        var chain = new Stack<Type>();
        for (var current = type; current is not null; current = current.DeclaringType)
            chain.Push(current);

        if (!string.IsNullOrEmpty(type.Namespace))
            builder.Append(type.Namespace).Append('.');

        var used = 0;
        var first = true;
        foreach (var level in chain)
        {
            if (!first)
                builder.Append('.');
            first = false;

            var name = level.Name;
            var tick = name.IndexOf('`', StringComparison.Ordinal);
            builder.Append(tick < 0 ? name : name[..tick]);

            var total = level.IsGenericType ? level.GetGenericArguments().Length : 0;
            if (total > used && used < arguments.Length)
            {
                builder.Append('<');
                for (var i = used; i < total && i < arguments.Length; i++)
                {
                    if (i > used)
                        builder.Append(", ");
                    Append(builder, arguments[i]);
                }

                builder.Append('>');
                used = total;
            }
        }
    }
}
