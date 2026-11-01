namespace Naqsh.Outputs;

/// <summary>An H.264 encoder candidate inside the LGPL FFmpeg build.</summary>
public sealed record EncoderCandidate(string FfmpegName, string DisplayName, bool Hardware, bool UsesNv12);

/// <summary>
/// Encoder preference order. Only LGPL-compatible encoders: NVENC, AMF, Quick Sync, the Windows
/// Media Foundation encoder, then Cisco OpenH264. libx264/libx265 (GPL) are never used.
/// </summary>
public static class EncoderCatalog
{
    public static IReadOnlyList<EncoderCandidate> H264 { get; } =
    [
        new("h264_nvenc", "NVIDIA NVENC", true, true),
        new("h264_amf", "AMD AMF", true, true),
        new("h264_qsv", "Intel Quick Sync", true, true),
        new("h264_mf", "Media Foundation", true, true),
        new("libopenh264", "OpenH264", false, false),
    ];

    /// <summary>Encoders that must never be used (GPL).</summary>
    public static IReadOnlySet<string> Forbidden { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "libx264", "libx264rgb", "libx265", "libxvid",
    };

    /// <summary>Target bitrate in bits/s for a format: ~0.12 bits per pixel, clamped 4–80 Mbit/s.</summary>
    public static long BitrateFor(int width, int height, double fps)
    {
        var bits = width * (double)height * fps * 0.12;
        return (long)Math.Clamp(bits, 4_000_000, 80_000_000);
    }

    /// <summary>File name for a new recording (no extension), e.g. "Naqsh_2026-11-01_10-30-00".</summary>
    public static string RecordingBaseName(DateTime localTime) =>
        "Naqsh_" + localTime.ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);
}
