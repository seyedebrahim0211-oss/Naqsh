using System.Text.Json;
using System.Text.Json.Serialization;
using Naqsh.Core.Formats;

namespace Naqsh.Core.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<DigitStyle>))]
public enum DigitStyle
{
    FollowLanguage = 0,
    Persian = 1,
    Latin = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<CalendarStyle>))]
public enum CalendarStyle
{
    FollowLanguage = 0,
    Jalali = 1,
    Gregorian = 2,
}

/// <summary>Per-user application settings (UI side). Stored as versioned JSON.</summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public const string Persian = "fa-IR";
    public const string English = "en-US";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool FirstRunCompleted { get; init; }

    /// <summary>UI language: "fa-IR" or "en-US".</summary>
    public string Language { get; init; } = Persian;

    [JsonConverter(typeof(JsonStringEnumConverter<BroadcastRegion>))]
    public BroadcastRegion Region { get; init; } = BroadcastRegion.Pal50;

    public DigitStyle Digits { get; init; } = DigitStyle.FollowLanguage;

    public CalendarStyle Calendar { get; init; } = CalendarStyle.FollowLanguage;

    /// <summary>Use 24-hour clock (default) or 12-hour.</summary>
    public bool Clock24h { get; init; } = true;

    public string? RecordingFolder { get; init; }

    public int FadeDurationMs { get; init; } = 1000;

    public static bool IsSupportedLanguage(string? lang) =>
        string.Equals(lang, Persian, StringComparison.Ordinal) || string.Equals(lang, English, StringComparison.Ordinal);
}

/// <summary>Reads and writes <see cref="AppSettings"/> atomically.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SettingsStore(string folder)
    {
        Folder = folder;
        FilePath = System.IO.Path.Combine(folder, "settings.json");
    }

    public string Folder { get; }

    public string FilePath { get; }

    /// <summary>Default per-user folder: %APPDATA%\Naqsh.</summary>
    public static string DefaultFolder =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Naqsh");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
            if (!AppSettings.IsSupportedLanguage(s.Language))
            {
                s = s with { Language = AppSettings.Persian };
            }

            return s with { SchemaVersion = AppSettings.CurrentSchemaVersion };
        }
        catch (JsonException)
        {
            // Corrupt settings never block start-up; keep a copy for the support bundle.
            File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
