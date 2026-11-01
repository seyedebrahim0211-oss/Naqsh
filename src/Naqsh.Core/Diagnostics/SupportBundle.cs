using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Naqsh.Core.Diagnostics;

/// <summary>Well-known per-user folders.</summary>
public static class NaqshPaths
{
    /// <summary>%LOCALAPPDATA%\Naqsh</summary>
    public static string LocalData =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Naqsh");

    public static string Logs => Path.Combine(LocalData, "logs");

    public static string Crashes => Path.Combine(LocalData, "crashes");

    /// <summary>Default recordings folder: Videos\Naqsh.</summary>
    public static string DefaultRecordings =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Naqsh");
}

/// <summary>
/// Builds an exportable support bundle (zip): logs, settings, crash dumps list and hardware info.
/// Everything inside is English.
/// </summary>
public static class SupportBundle
{
    public static string Create(string outputFolder, string? settingsFile, IReadOnlyDictionary<string, string>? extraInfo = null)
    {
        Directory.CreateDirectory(outputFolder);
        var path = Path.Combine(outputFolder, $"naqsh-support-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        if (Directory.Exists(NaqshPaths.Logs))
        {
            foreach (var log in Directory.EnumerateFiles(NaqshPaths.Logs, "*.log"))
            {
                AddShared(zip, log, "logs/" + Path.GetFileName(log));
            }
        }

        if (settingsFile is not null && File.Exists(settingsFile))
        {
            AddShared(zip, settingsFile, "settings.json");
        }

        var info = zip.CreateEntry("system.txt");
        using (var w = new StreamWriter(info.Open(), Encoding.UTF8))
        {
            w.Write(DescribeSystem(extraInfo));
        }

        return path;
    }

    public static string DescribeSystem(IReadOnlyDictionary<string, string>? extra)
    {
        var sb = new StringBuilder();
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Time (UTC): {DateTime.UtcNow:O}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $".NET: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"CPU cores: {Environment.ProcessorCount}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Memory (GC available): {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024)} MB");
        if (extra is not null)
        {
            foreach (var kv in extra)
            {
                sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{kv.Key}: {kv.Value}");
            }
        }

        return sb.ToString();
    }

    private static void AddShared(ZipArchive zip, string file, string entryName)
    {
        // Log files are open by Serilog; read with sharing.
        using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var entry = zip.CreateEntry(entryName);
        using var dst = entry.Open();
        src.CopyTo(dst);
    }
}
