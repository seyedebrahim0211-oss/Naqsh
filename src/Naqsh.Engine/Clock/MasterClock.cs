using System.Diagnostics;

namespace Naqsh.Engine.Clock;

/// <summary>
/// The engine's single master clock in 100 ns ticks, based on QueryPerformanceCounter.
/// (A later phase slaves it to a hardware output clock such as DeckLink.)
/// </summary>
public sealed class MasterClock
{
    private readonly long _originTimestamp = Stopwatch.GetTimestamp();

    public static MasterClock Shared { get; } = new();

    /// <summary>Ticks (100 ns) since the clock was created. Allocation-free.</summary>
    public long NowTicks => ToTicks(Stopwatch.GetTimestamp() - _originTimestamp);

    public static long ToTicks(long stopwatchDelta)
    {
        var f = Stopwatch.Frequency;
        if (f == TimeSpan.TicksPerSecond)
        {
            return stopwatchDelta;
        }

        // Split to avoid overflow on long runs.
        var seconds = stopwatchDelta / f;
        var rem = stopwatchDelta % f;
        return (seconds * TimeSpan.TicksPerSecond) + (rem * TimeSpan.TicksPerSecond / f);
    }
}
