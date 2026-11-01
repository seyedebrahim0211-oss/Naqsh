using System.Globalization;

namespace Naqsh.Core.Timing;

/// <summary>SMPTE time-of-day timecode. Drop-frame is used for 29.97 and 59.94.</summary>
public readonly record struct Timecode(int Hours, int Minutes, int Seconds, int Frames, bool DropFrame)
{
    /// <summary>Timecode for a time of day (time since local midnight).</summary>
    public static Timecode FromTimeOfDay(TimeSpan timeOfDay, FrameRate rate)
    {
        var frameCount = rate.TicksToFrame(timeOfDay.Ticks);
        return FromFrameCount(frameCount, rate);
    }

    /// <summary>Converts an absolute frame count to timecode (SMPTE 12M drop-frame for x/1001 rates).</summary>
    public static Timecode FromFrameCount(long frameCount, FrameRate rate)
    {
        var fps = rate.NominalFps;
        var drop = rate.IsFractional;
        var framesPerDay = (long)fps * 86400;
        if (drop)
        {
            var dropPerMinute = fps / 15; // 2 for 30, 4 for 60
            var framesPer10Min = (fps * 600) - (dropPerMinute * 9);
            var framesPerMinute = (fps * 60) - dropPerMinute;
            framesPerDay = framesPer10Min * 144L;
            frameCount %= framesPerDay;
            var d = frameCount / framesPer10Min;
            var m = frameCount % framesPer10Min;
            frameCount += (dropPerMinute * 9 * d) +
                          (m > dropPerMinute ? dropPerMinute * ((m - dropPerMinute) / framesPerMinute) : 0);
        }
        else
        {
            frameCount %= framesPerDay;
        }

        var frames = (int)(frameCount % fps);
        var totalSeconds = frameCount / fps;
        return new Timecode(
            (int)(totalSeconds / 3600 % 24),
            (int)(totalSeconds / 60 % 60),
            (int)(totalSeconds % 60),
            frames,
            drop);
    }

    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Hours:00}:{Minutes:00}:{Seconds:00}{(DropFrame ? ';' : ':')}{Frames:00}");
    }
}
