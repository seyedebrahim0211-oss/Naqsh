using System.Globalization;
using Naqsh.Core.Timing;

namespace Naqsh.Core.Formats;

/// <summary>Broadcast region; decides the default frame-rate family on first run.</summary>
public enum BroadcastRegion
{
    /// <summary>25/50 Hz (Iran, Europe, most of Asia).</summary>
    Pal50 = 0,

    /// <summary>29.97/59.94 Hz (Americas, Japan).</summary>
    Ntsc60 = 1,
}

/// <summary>A video format: resolution, frame rate and scan type.</summary>
public sealed record VideoFormat(int Width, int Height, FrameRate Rate, bool Interlaced = false)
{
    public static VideoFormat Hd1080p50 { get; } = new(1920, 1080, FrameRate.Fps50);

    public static VideoFormat Hd1080p5994 { get; } = new(1920, 1080, FrameRate.Fps5994);

    /// <summary>Default for new projects (docs/SPEC.md §0).</summary>
    public static VideoFormat Default => Hd1080p50;

    public static VideoFormat DefaultFor(BroadcastRegion region) =>
        region == BroadcastRegion.Ntsc60 ? Hd1080p5994 : Hd1080p50;

    /// <summary>Project formats offered in the UI (interlaced is output-only, see SPEC §0).</summary>
    public static IReadOnlyList<VideoFormat> ProjectFormats { get; } = BuildProjectFormats();

    public double AspectRatio => (double)Width / Height;

    /// <summary>Short technical name, e.g. "1080p50" or "2160p59.94". Never localized.</summary>
    public string ShortName => string.Create(
        CultureInfo.InvariantCulture,
        $"{Height}{(Interlaced ? "i" : "p")}{Rate}");

    public override string ToString() => ShortName;

    public void Validate()
    {
        if (Width < 16 || Height < 16 || Width > 7680 || Height > 4320 || (Width % 2) != 0 || (Height % 2) != 0)
        {
            throw new InvalidOperationException($"Invalid video size {Width}x{Height}.");
        }
    }

    private static List<VideoFormat> BuildProjectFormats()
    {
        var list = new List<VideoFormat>();
        foreach (var (w, h) in new[] { (1280, 720), (1920, 1080), (3840, 2160) })
        {
            foreach (var r in FrameRate.Standard)
            {
                list.Add(new VideoFormat(w, h, r));
            }
        }

        return list;
    }
}
