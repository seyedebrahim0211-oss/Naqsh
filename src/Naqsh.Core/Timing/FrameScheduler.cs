namespace Naqsh.Core.Timing;

/// <summary>
/// Pure, allocation-free frame pacing logic for the render loop. Due times are computed from the
/// start time and the exact rational rate (never by adding a rounded period), so there is no drift.
/// When the loop falls behind, late frames are counted as dropped and the schedule jumps forward.
/// </summary>
public struct FrameScheduler
{
    private long _startTicks;

    public FrameScheduler(FrameRate rate, long startTicks)
    {
        Rate = rate;
        _startTicks = startTicks;
        FrameIndex = 0;
        DroppedFrames = 0;
    }

    public FrameRate Rate { get; private set; }

    /// <summary>Index of the next frame to render.</summary>
    public long FrameIndex { get; private set; }

    public long DroppedFrames { get; private set; }

    /// <summary>Clock time (ticks) at which <see cref="FrameIndex"/> is due.</summary>
    public readonly long NextDueTicks => _startTicks + Rate.FrameToTicks(FrameIndex);

    /// <summary>
    /// Call after waking. Returns the frame index to render now. If more than one whole frame
    /// period has passed since the due time, the skipped frames are counted as dropped.
    /// </summary>
    public long BeginFrame(long nowTicks)
    {
        var behind = Rate.TicksToFrame(nowTicks - _startTicks) - FrameIndex;
        if (behind >= 1)
        {
            DroppedFrames += behind;
            FrameIndex += behind;
        }

        return FrameIndex;
    }

    /// <summary>Marks the current frame as rendered and moves to the next one.</summary>
    public void EndFrame() => FrameIndex++;

    /// <summary>
    /// Changes the rate. The frame index keeps counting (no jump backwards) and the current frame
    /// becomes due at <paramref name="nowTicks"/>.
    /// </summary>
    public void Restart(FrameRate rate, long nowTicks)
    {
        Rate = rate;
        _startTicks = nowTicks - rate.FrameToTicks(FrameIndex);
    }

    public void ResetDropped() => DroppedFrames = 0;
}
