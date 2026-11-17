using System.Diagnostics;

namespace Hecole.Mediator.Benchmarks;

/// <summary>
/// Stopwatch and <see cref="GC.GetAllocatedBytesForCurrentThread"/> harness, the same methodology used to measure
/// 1.3.0: warm-up, then <see cref="Rounds"/> rounds of <see cref="Operations"/> calls each; the median round is reported.
/// </summary>
internal static class Harness
{
    public const int Operations = 300_000;
    public const int Rounds = 7;
    public const int WarmUp = 30_000;

    public static async Task<(double Nanoseconds, long Bytes)> Measure(Func<Task> operation)
    {
        for (var i = 0; i < WarmUp; i++)
            await operation().ConfigureAwait(false);

        var times = new double[Rounds];
        var allocations = new long[Rounds];
        for (var round = 0; round < Rounds; round++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < Operations; i++)
                await operation().ConfigureAwait(false);
            stopwatch.Stop();
            allocations[round] = (GC.GetAllocatedBytesForCurrentThread() - before) / Operations;
            times[round] = stopwatch.Elapsed.TotalNanoseconds / Operations;
        }

        Array.Sort(times);
        Array.Sort(allocations);
        return (times[Rounds / 2], allocations[Rounds / 2]);
    }

    public static void RaisePriority()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.PriorityClass = ProcessPriorityClass.High;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            // Priority is best effort; the run is still valid, only noisier.
        }
    }
}
