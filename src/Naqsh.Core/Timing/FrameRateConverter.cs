namespace Naqsh.Core.Timing;

/// <summary>
/// Maps output (project) frames to source frames with exact integer arithmetic, so 29.97/30 and
/// 59.94/60 mismatches never produce periodic stutter or long-term drift.
/// </summary>
public readonly struct FrameRateConverter
{
    private readonly long _num; // source.Num * output.Den
    private readonly long _den; // source.Den * output.Num

    public FrameRateConverter(FrameRate source, FrameRate output)
    {
        Source = source;
        Output = output;
        _num = (long)source.Numerator * output.Denominator;
        _den = (long)source.Denominator * output.Numerator;
    }

    public FrameRate Source { get; }

    public FrameRate Output { get; }

    /// <summary>
    /// Source frame to show for output frame <paramref name="outputFrame"/>: the source frame whose
    /// start time is nearest (ties go to the earlier frame) to the output frame's start time.
    /// </summary>
    public long NearestSourceFrame(long outputFrame)
    {
        Int128 p = (Int128)outputFrame * _num;
        Int128 q = p / _den;
        Int128 rem = p - (q * _den);
        if (rem * 2 > _den)
        {
            q += 1;
        }

        return (long)q;
    }

    /// <summary>
    /// For blending: the source frame at or before the output time and the weight (0..1) of the
    /// following frame.
    /// </summary>
    public long BlendSourceFrames(long outputFrame, out float nextWeight)
    {
        Int128 p = (Int128)outputFrame * _num;
        Int128 q = p / _den;
        Int128 rem = p - (q * _den);
        nextWeight = (float)((double)rem / _den);
        return (long)q;
    }
}

/// <summary>
/// Streaming form of <see cref="FrameRateConverter"/>: an integer fractional accumulator that says
/// how many source frames to advance on each output tick. No allocations, no floating point.
/// </summary>
public struct FrameAccumulator
{
    private readonly long _step;   // source.Num * output.Den
    private readonly long _period; // source.Den * output.Num
    private long _acc;

    public FrameAccumulator(FrameRate source, FrameRate output)
    {
        _step = (long)source.Numerator * output.Denominator;
        _period = (long)source.Denominator * output.Numerator;
        _acc = 0;
    }

    /// <summary>Advances one output frame and returns the number of source frames to consume (0, 1, 2…).</summary>
    public int Advance()
    {
        _acc += _step;
        var n = _acc / _period;
        _acc -= n * _period;
        return (int)n;
    }

    public void Reset() => _acc = 0;
}
