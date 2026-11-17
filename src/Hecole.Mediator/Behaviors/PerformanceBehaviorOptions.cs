namespace Hecole.Mediator.Behaviors;

/// <summary>
/// Options of <see cref="PerformanceBehavior{TRequest, TResponse}"/>. Register an instance in the container to change
/// the threshold (<c>services.AddSingleton(new PerformanceBehaviorOptions { Threshold = TimeSpan.FromSeconds(1) })</c>);
/// without a registration, the defaults apply.
/// </summary>
public sealed class PerformanceBehaviorOptions
{
    private TimeSpan _threshold = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// A request that takes longer than this is logged as a warning. Default: 500 ms. Must not be negative.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan Threshold
    {
        get => _threshold;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            _threshold = value;
        }
    }
}
