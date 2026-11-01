using FFmpeg.AutoGen;
using Serilog;

namespace Naqsh.Engine.Media;

/// <summary>
/// Loads the LGPL FFmpeg shared libraries at runtime from the "ffmpeg" folder next to the
/// executable (or NAQSH_FFMPEG_PATH). The app works without them: file inputs and recording
/// report "FfmpegMissing" instead.
/// </summary>
public static class FfmpegRuntime
{
    public const string PathEnvironmentVariable = "NAQSH_FFMPEG_PATH";

    private static readonly object Gate = new();
    private static bool? _available;

    public static string? LoadedFrom { get; private set; }

    public static string? Version { get; private set; }

    /// <summary>Folder searched for avcodec-62.dll etc.</summary>
    public static string SearchFolder =>
        Environment.GetEnvironmentVariable(PathEnvironmentVariable) is { Length: > 0 } p
            ? p
            : Path.Combine(AppContext.BaseDirectory, "ffmpeg");

    /// <summary>True if FFmpeg loaded. Safe to call from any thread; tries once.</summary>
    public static bool EnsureLoaded(ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);
        lock (Gate)
        {
            if (_available is { } a)
            {
                return a;
            }

            try
            {
                var folder = SearchFolder;
                if (!Directory.Exists(folder))
                {
                    log.Warning("FFmpeg folder not found: {Folder}", folder);
                    _available = false;
                    return false;
                }

                ffmpeg.RootPath = folder;
                DynamicallyLoadedBindings.Initialize();
                Version = ffmpeg.av_version_info();
                LoadedFrom = folder;
                var config = ffmpeg.avcodec_configuration() ?? string.Empty;
                if (config.Contains("--enable-gpl", StringComparison.Ordinal))
                {
                    // Licensing rule 9: only LGPL builds are allowed.
                    log.Error("The FFmpeg build in {Folder} is GPL; an LGPL build is required", folder);
                    _available = false;
                    return false;
                }

                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
                log.Information("FFmpeg {Version} loaded from {Folder}", Version, folder);
                _available = true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or NotSupportedException or InvalidOperationException)
            {
                log.Warning(ex, "FFmpeg could not be loaded");
                _available = false;
            }

            return _available.Value;
        }
    }

    /// <summary>FFmpeg error code to English text (for logs).</summary>
    public static unsafe string ErrorText(int error)
    {
        const int size = 256;
        var buf = stackalloc byte[size];
        ffmpeg.av_strerror(error, buf, size);
        return new string((sbyte*)buf);
    }
}
