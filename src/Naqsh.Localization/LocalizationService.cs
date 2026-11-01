using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Naqsh.Core.Settings;

namespace Naqsh.Localization;

/// <summary>
/// Single source of every user-visible string. Supports runtime switching between fa-IR (RTL) and
/// en-US (LTR): UI bindings to the indexer refresh through <see cref="PropertyChanged"/>.
/// Call <see cref="SetLanguage"/> and the setters on the UI thread.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public const string ResourceBaseName = "Naqsh.Localization.Resources.Strings";

    private static readonly ResourceManager Resources = new(ResourceBaseName, typeof(LocalizationService).Assembly);

    private CultureInfo _culture = CultureInfo.GetCultureInfo(AppSettings.Persian);
    private DigitStyle _digits;
    private CalendarStyle _calendar;
    private bool _clock24h = true;

    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language or a formatting option changed.</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>Called when a key is missing (for logging). The UI shows "[key]".</summary>
    public static Action<string>? MissingKeyReporter { get; set; }

    public CultureInfo Culture => _culture;

    public string Language => _culture.Name;

    public bool IsRightToLeft => _culture.TextInfo.IsRightToLeft;

    public bool UsePersianDigits => _digits switch
    {
        DigitStyle.Persian => true,
        DigitStyle.Latin => false,
        _ => IsRightToLeft,
    };

    public bool UseJalali => _calendar switch
    {
        CalendarStyle.Jalali => true,
        CalendarStyle.Gregorian => false,
        _ => IsRightToLeft,
    };

    public bool Clock24h => _clock24h;

    /// <summary>Localized string for <paramref name="key"/>; used by XAML bindings ("[Key]").</summary>
    public string this[string key] => Get(key);

    public static IReadOnlyList<string> SupportedLanguages { get; } = [AppSettings.Persian, AppSettings.English];

    public string Get(string key)
    {
        var s = Resources.GetString(key, _culture);
        if (s is null)
        {
            MissingKeyReporter?.Invoke(key);
            return "[" + key + "]";
        }

        return s;
    }

    /// <summary>Formats a localized pattern; numeric arguments are rendered with the selected digits.</summary>
    public string Format(string key, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var formatted = new object?[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            formatted[i] = args[i] switch
            {
                null => null,
                string s => s,
                IFormattable f => Digits(f.ToString(null, CultureInfo.InvariantCulture)),
                var o => o.ToString(),
            };
        }

        return string.Format(_culture, Get(key), formatted);
    }

    /// <summary>Converts Latin digits in <paramref name="text"/> to the selected digit style.</summary>
    public string Digits(string text) => UsePersianDigits ? NumberFormatter.ToPersianDigits(text) : text;

    public void SetLanguage(string language)
    {
        if (!AppSettings.IsSupportedLanguage(language))
        {
            throw new ArgumentException($"Unsupported language '{language}'.", nameof(language));
        }

        if (string.Equals(_culture.Name, language, StringComparison.Ordinal))
        {
            return;
        }

        _culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentUICulture = _culture;
        CultureInfo.CurrentUICulture = _culture;
        RaiseAll();
    }

    public void SetFormatting(DigitStyle digits, CalendarStyle calendar, bool clock24h)
    {
        _digits = digits;
        _calendar = calendar;
        _clock24h = clock24h;
        RaiseAll();
    }

    /// <summary>All keys defined in the neutral (en-US) resources.</summary>
    public static IReadOnlyList<string> AllKeys() => KeysFor(CultureInfo.InvariantCulture);

    /// <summary>Keys defined directly in the resource set of <paramref name="culture"/> (no fallback).</summary>
    public static IReadOnlyList<string> KeysFor(CultureInfo culture)
    {
        var set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        var keys = new List<string>();
        if (set is null)
        {
            return keys;
        }

        foreach (DictionaryEntry e in set)
        {
            keys.Add((string)e.Key);
        }

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    /// <summary>Value of <paramref name="key"/> in exactly <paramref name="culture"/>'s resources (no fallback).</summary>
    public static string? ValueFor(string key, CultureInfo culture) =>
        Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)?.GetString(key);

    private void RaiseAll()
    {
        // "Item[]" refreshes every indexer binding; empty name refreshes everything else.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
