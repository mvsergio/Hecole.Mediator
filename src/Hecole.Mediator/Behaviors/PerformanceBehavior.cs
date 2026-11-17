using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Hecole.Mediator.Behaviors;

/// <summary>
/// Optional pipeline behavior that logs a warning when a request takes longer than
/// <see cref="PerformanceBehaviorOptions.Threshold"/> (default 500 ms). The time is measured once, with
/// <see cref="Stopwatch.GetTimestamp()"/>, and counts whether the request completes, throws or is canceled. Never
/// writes the request or the response. Enters the pipeline only when registered:
/// <c>cfg.AddOpenBehavior(typeof(PerformanceBehavior&lt;,&gt;))</c>.
/// </summary>
/// <remarks>
/// Event (category <c>Hecole.Mediator.Behaviors.PerformanceBehavior</c>): 2001 SlowRequest, Warning, with the
/// request type name, the elapsed milliseconds and the threshold in milliseconds.
/// </remarks>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response type.</typeparam>
public sealed class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    private readonly TimeSpan _threshold;

    /// <summary>Creates the behavior.</summary>
    /// <param name="logger">Logger of this behavior.</param>
    /// <param name="options">
    /// Threshold options; resolved from the container when registered there, otherwise
    /// <see langword="null"/> and the defaults of <see cref="PerformanceBehaviorOptions"/> apply.
    /// </param>
    public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger, PerformanceBehaviorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _threshold = (options ?? new PerformanceBehaviorOptions()).Threshold;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var start = Stopwatch.GetTimestamp();
        try
        {
            return await next().ConfigureAwait(false);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(start);
            if (elapsed > _threshold)
            {
                BehaviorLog.SlowRequest(_logger, RequestName<TRequest>.Value, elapsed.TotalMilliseconds, _threshold.TotalMilliseconds);
            }
        }
    }
}
