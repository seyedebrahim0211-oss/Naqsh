using Naqsh.Core.Timing;

namespace Naqsh.Core.Tests;

public class FrameRateTests
{
    [Fact]
    public void FrameToTicks_IsExactForFractionalRates()
    {
        var r = FrameRate.Fps5994;

        // 60000 frames at 59.94 = exactly 1001 seconds.
        Assert.Equal(1001 * TimeSpan.TicksPerSecond, r.FrameToTicks(60000));
        Assert.Equal(0, r.FrameToTicks(0));
    }

    [Theory]
    [InlineData(25, 1)]
    [InlineData(30000, 1001)]
    [InlineData(50, 1)]
    [InlineData(60000, 1001)]
    [InlineData(60, 1)]
    public void TicksToFrame_RoundTripsFrameStarts(int num, int den)
    {
        var r = new FrameRate(num, den);
        for (long f = 0; f < 5000; f += 7)
        {
            Assert.Equal(f, r.TicksToFrame(r.FrameToTicks(f)));
        }
    }

    [Fact]
    public void FramesForDuration_RoundsToNearestFrame()
    {
        Assert.Equal(50, FrameRate.Fps50.FramesForDuration(TimeSpan.FromSeconds(1)));
        Assert.Equal(30, FrameRate.Fps2997.FramesForDuration(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, FrameRate.Fps50.FramesForDuration(TimeSpan.Zero));
    }

    [Fact]
    public void Invalid_Rate_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRate(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRate(25, 0));
    }

    [Fact]
    public void ToString_IsTechnical()
    {
        Assert.Equal("50", FrameRate.Fps50.ToString());
        Assert.Equal("59.94", FrameRate.Fps5994.ToString());
        Assert.Equal("29.97", FrameRate.Fps2997.ToString());
    }
}

public class FrameRateConverterTests
{
    [Fact]
    public void Camera25p_In50p_RepeatsEachFrameTwice()
    {
        var c = new FrameRateConverter(FrameRate.Fps25, FrameRate.Fps50);
        Assert.Equal(0, c.NearestSourceFrame(0));
        Assert.Equal(0, c.NearestSourceFrame(1));
        Assert.Equal(1, c.NearestSourceFrame(2));
        Assert.Equal(1, c.NearestSourceFrame(3));
    }

    [Theory]
    [InlineData(25, 1)]
    [InlineData(60000, 1001)]
    [InlineData(30000, 1001)]
    [InlineData(60, 1)]
    public void ThirtyMinutes_NoDrift_And_NoBackwardsSteps(int num, int den)
    {
        // SPEC §0 Accept: 25p camera and 59.94 file in a 50p project for 30 minutes without drift.
        var source = new FrameRate(num, den);
        var output = FrameRate.Fps50;
        var c = new FrameRateConverter(source, output);
        long frames = 30 * 60 * 50;
        long previous = -1;
        for (long f = 0; f <= frames; f++)
        {
            var s = c.NearestSourceFrame(f);
            Assert.True(s >= previous, "source frame went backwards");
            Assert.True(s - previous <= 2, "skipped more than one source frame");
            previous = s;
        }

        // At the end, source time must match output time within half a source frame.
        var outTime = output.FrameToTicks(frames);
        var srcTime = source.FrameToTicks(previous);
        Assert.True(Math.Abs(outTime - srcTime) <= source.FrameToTicks(1) / 2 + 1);
    }

    [Fact]
    public void Accumulator_MatchesConverter_59_94_into_50()
    {
        var acc = new FrameAccumulator(FrameRate.Fps5994, FrameRate.Fps50);
        long consumed = 0;
        const int Frames = 90_000;
        for (var i = 0; i < Frames; i++)
        {
            consumed += acc.Advance();
        }

        // Exactly floor(90000 * 59.94 / 50) source frames, no drift.
        var expected = (long)Math.Floor(Frames * (60000.0 / 1001.0) / 50.0);
        Assert.Equal(expected, consumed);
    }

    [Fact]
    public void Accumulator_29_97_vs_30_HasNoPeriodicStutter()
    {
        // 29.97 source in a 30 project: exactly one repeat every 1001 frames, evenly spaced.
        var acc = new FrameAccumulator(FrameRate.Fps2997, FrameRate.Fps30);
        var repeats = new List<int>();
        for (var i = 0; i < 3003; i++)
        {
            if (acc.Advance() == 0)
            {
                repeats.Add(i);
            }
        }

        Assert.Equal(3, repeats.Count);
        Assert.Equal(1001, repeats[1] - repeats[0]);
        Assert.Equal(1001, repeats[2] - repeats[1]);
    }

    [Fact]
    public void Blend_ReportsFractionalWeight()
    {
        var c = new FrameRateConverter(FrameRate.Fps25, FrameRate.Fps50);
        var s = c.BlendSourceFrames(1, out var w);
        Assert.Equal(0, s);
        Assert.Equal(0.5f, w, 3);
    }
}

public class FrameSchedulerTests
{
    [Fact]
    public void DueTimes_FollowExactRate()
    {
        var s = new FrameScheduler(FrameRate.Fps5994, 1000);
        for (var i = 0; i < 60000; i++)
        {
            s.BeginFrame(s.NextDueTicks);
            s.EndFrame();
        }

        Assert.Equal(1000 + (1001 * TimeSpan.TicksPerSecond), s.NextDueTicks);
        Assert.Equal(0, s.DroppedFrames);
    }

    [Fact]
    public void LateWake_CountsDroppedFrames()
    {
        var s = new FrameScheduler(FrameRate.Fps50, 0);
        s.BeginFrame(0);
        s.EndFrame();

        // Wake 3.5 frames late (frame 1 was due at 20 ms; now is 90 ms → frame 4).
        var frame = s.BeginFrame(TimeSpan.FromMilliseconds(90).Ticks);
        Assert.Equal(4, frame);
        Assert.Equal(3, s.DroppedFrames);
    }

    [Fact]
    public void Restart_KeepsFrameIndexMonotonic()
    {
        var s = new FrameScheduler(FrameRate.Fps50, 0);
        for (var i = 0; i < 10; i++)
        {
            s.BeginFrame(s.NextDueTicks);
            s.EndFrame();
        }

        var now = s.NextDueTicks;
        s.Restart(FrameRate.Fps5994, now);
        Assert.Equal(10, s.FrameIndex);
        Assert.Equal(now, s.NextDueTicks);
    }
}

public class TimecodeTests
{
    [Fact]
    public void NonDrop_50p()
    {
        var tc = Timecode.FromTimeOfDay(new TimeSpan(0, 10, 30, 15, 500), FrameRate.Fps50);
        Assert.Equal("10:30:15:25", tc.ToString());
    }

    [Theory]
    [InlineData(1800, "00:01:00;02")] // first frame after minute 1 skips 00 and 01
    [InlineData(17982, "00:10:00;00")] // 10 minutes: no drop at minute 10
    [InlineData(1799, "00:00:59;29")]
    public void DropFrame_2997(long frame, string expected)
    {
        Assert.Equal(expected, Timecode.FromFrameCount(frame, FrameRate.Fps2997).ToString());
    }

    [Fact]
    public void DropFrame_OneHourIsExact()
    {
        // 29.97 DF: 107892 frames = 01:00:00;00
        Assert.Equal("01:00:00;00", Timecode.FromFrameCount(107892, FrameRate.Fps2997).ToString());
    }
}
