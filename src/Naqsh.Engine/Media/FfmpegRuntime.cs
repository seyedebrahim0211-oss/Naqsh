using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Serilog;

namespace Naqsh.Engine.Media;

/// <summary>
/// Loads the LGPL FFmpeg 9 shared libraries at runtime from the "ffmpeg" folder next to the
/// executable (or NAQSH_FFMPEG_PATH). Sub-folders are searched too, so "ffmpeg\bin" or the folder
/// extracted from a release zip also work. The app works without FFmpeg: file inputs and
/// recording report <see cref="LastProblem"/> instead. A failed load is retried after a few
/// seconds, so copying the DLLs while the app is running is enough.
/// </summary>
public static partial class FfmpegRuntime
{
    public const string PathEnvironmentVariable = "NAQSH_FFMPEG_PATH";

    /// <summary>The DLL that identifies a matching FFmpeg 9 build.</summary>
    public static readonly string MarkerDll = $"avcodec-{ffmpeg.LibraryVersionMap["avcodec"]}.dll";

    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(3);
    private static readonly object Gate = new();
    private static bool _available;
    private static DateTime _lastAttemptUtc = DateTime.MinValue;

    public static string? LoadedFrom { get; private set; }

    public static string? Version { get; private set; }

    /// <summary>
    /// Why FFmpeg is unavailable, as a UI resource code: "FfmpegMissing", "FfmpegWrongVersion",
    /// "FfmpegGpl" or "FfmpegLoadFailed". Null when loaded.
    /// </summary>
    public static string? LastProblem { get; private set; } = "FfmpegMissing";

    /// <summary>English detail for <see cref="LastProblem"/> (shown to the operator with the folder path).</summary>
    public static string? LastProblemDetail { get; private set; }

    /// <summary>Root folder searched for <see cref="MarkerDll"/>.</summary>
    public static string SearchFolder =>
        Environment.GetEnvironmentVariable(PathEnvironmentVariable) is { Length: > 0 } p
            ? p
            : Path.Combine(AppContext.BaseDirectory, "ffmpeg");

    /// <summary>True if FFmpeg is loaded. Thread-safe; a failure is retried after a few seconds.</summary>
    public static bool EnsureLoaded(ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);
        lock (Gate)
        {
            if (_available)
            {
                return true;
            }

            if (DateTime.UtcNow - _lastAttemptUtc < RetryAfter)
            {
                return false;
            }

            _lastAttemptUtc = DateTime.UtcNow;
            var root = SearchFolder;
            var folder = FindLibraryFolder(root, out var otherVersion);
            if (folder is null)
            {
                Fail(log, otherVersion is null ? "FfmpegMissing" : "FfmpegWrongVersion", root, otherVersion);
                return false;
            }

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Let each FFmpeg DLL find its sibling DLLs.
                    SetDllDirectory(folder);
                }

                ffmpeg.RootPath = folder;
                DynamicallyLoadedBindings.Initialize();
                Version = ffmpeg.av_version_info();
                var config = ffmpeg.avcodec_configuration() ?? string.Empty;
                if (config.Contains("--enable-gpl", StringComparison.Ordinal))
                {
                    // Licensing rule 9: only LGPL builds are allowed.
                    Fail(log, "FfmpegGpl", folder, Version);
                    return false;
                }

                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
                LoadedFrom = folder;
                LastProblem = null;
                LastProblemDetail = null;
                _available = true;
                log.Information("FFmpeg {Version} loaded from {Folder}", Version, folder);
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or NotSupportedException or InvalidOperationException or BadImageFormatException)
            {
                log.Warning(ex, "FFmpeg could not be loaded from {Folder}", folder);
                Fail(log, "FfmpegLoadFailed", folder, ex.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// Finds the folder holding <see cref="MarkerDll"/> under <paramref name="root"/> (up to three
    /// levels deep). When only another FFmpeg version is present, reports its DLL name.
    /// </summary>
    public static string? FindLibraryFolder(string root, out string? otherVersionDll)
    {
        otherVersionDll = null;
        if (!Directory.Exists(root))
        {
            return null;
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        var match = Directory.EnumerateFiles(root, MarkerDll, options).OrderBy(p => p.Length).FirstOrDefault();
        if (match is not null)
        {
            return Path.GetDirectoryName(match);
        }

        otherVersionDll = Directory.EnumerateFiles(root, "avcodec-*.dll", options).Select(Path.GetFileName).FirstOrDefault();
        return null;
    }

    /// <summary>FFmpeg error code to English text (for logs).</summary>
    public static unsafe string ErrorText(int error)
    {
        const int size = 256;
        var buf = stackalloc byte[size];
        ffmpeg.av_strerror(error, buf, size);
        return new string((sbyte*)buf);
    }

    private static void Fail(ILogger log, string code, string folder, string? extra)
    {
        if (LastProblem != code || LastProblemDetail != folder)
        {
            log.Warning("FFmpeg unavailable ({Code}) in {Folder} {Extra}; expected {Marker} (FFmpeg 9 LGPL shared)", code, folder, extra, MarkerDll);
        }

        LastProblem = code;
        LastProblemDetail = folder;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "SetDllDirectoryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDllDirectory(string path);
}
