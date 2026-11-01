using System.Globalization;
using System.Text.Json.Serialization;

namespace Naqsh.Core.Timing;

/// <summary>
/// An exact rational frame rate (e.g. 60000/1001 for 59.94). All timing math is done with
/// integers so long runs never accumulate floating-point drift.
/// </summary>
public readonly record struct FrameRate
{
    /// <summary>Ticks per second used by <see cref="TimeSpan"/> (100 ns units).</summary>
    public const long TicksPerSecond = TimeSpan.TicksPerSecond;

    [JsonConstructor]
    public FrameRate(int numerator, int denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);

        Numerator = numerator;
        Denominator = denominator;
    }

    public int Numerator { get; }

    public int Denominator { get; }

    public static FrameRate Fps25 { get; } = new(25, 1);

    public static FrameRate Fps2997 { get; } = new(30000, 1001);

    public static FrameRate Fps30 { get; } = new(30, 1);

    public static FrameRate Fps50 { get; } = new(50, 1);

    public static FrameRate Fps5994 { get; } = new(60000, 1001);

    public static FrameRate Fps60 { get; } = new(60, 1);

    public static IReadOnlyList<FrameRate> Standard { get; } =
        [Fps25, Fps2997, Fps30, Fps50, Fps5994, Fps60];

    /// <summary>Frames per second as a double (display only, never for timing).</summary>
    [JsonIgnore]
    public double Value => (double)Numerator / Denominator;

    /// <summary>True for NTSC-family fractional rates (x/1001).</summary>
    [JsonIgnore]
    public bool IsFractional => Denominator == 1001;

    /// <summary>Nominal integer rate used for timecode (30 for 29.97, 60 for 59.94).</summary>
    [JsonIgnore]
    public int NominalFps => (int)((Numerator + Denominator - 1) / (long)Denominator);

    /// <summary>
    /// Start time of frame <paramref name="frameIndex"/> in 100 ns ticks: the first tick at or after
    /// the exact rational start (ceiling), so <see cref="TicksToFrame"/> maps it back to the same frame.
    /// </summary>
    public long FrameToTicks(long frameIndex)
    {
        Int128 t = (Int128)frameIndex * Denominator * TicksPerSecond;
        Int128 q = t / Numerator;
        if (t > 0 && q * Numerator != t)
        {
            q += 1;
        }

        return (long)q;
    }

    /// <summary>Index of the frame that is showing at <paramref name="ticks"/> (floor).</summary>
    public long TicksToFrame(long ticks)
    {
        Int128 f = (Int128)ticks * Numerator;
        Int128 d = (Int128)Denominator * TicksPerSecond;
        Int128 q = f / d;
        if (f < 0 && q * d != f)
        {
            q -= 1;
        }

        return (long)q;
    }

    /// <summary>Number of whole frames for a duration, rounded to the nearest frame (minimum 1).</summary>
    public int FramesForDuration(TimeSpan duration)
    {
        Int128 f = (Int128)duration.Ticks * Numerator;
        Int128 d = (Int128)Denominator * TicksPerSecond;
        var frames = (long)((f + (d / 2)) / d);
        return (int)Math.Clamp(frames, 1, int.MaxValue);
    }

    public override string ToString()
    {
        return Denominator == 1
            ? Numerator.ToString(CultureInfo.InvariantCulture)
            : Value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static bool TryParse(string? text, out FrameRate rate)
    {
        rate = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var r in Standard)
        {
            if (string.Equals(r.ToString(), text.Trim(), StringComparison.Ordinal))
            {
                rate = r;
                return true;
            }
        }

        return false;
    }
}
