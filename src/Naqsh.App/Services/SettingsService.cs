using Naqsh.Core.Settings;
using Naqsh.Localization;

namespace Naqsh.App.Services;

/// <summary>Holds the current <see cref="AppSettings"/>, persists them and applies them to localization.</summary>
public sealed class SettingsService
{
    private readonly SettingsStore _store;

    public SettingsService(SettingsStore store)
    {
        _store = store;
        Current = store.Load();
    }

    public event Action<AppSettings>? Changed;

    public AppSettings Current { get; private set; }

    public string FilePath => _store.FilePath;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Current = change(Current);
        _store.Save(Current);
        Apply();
        Changed?.Invoke(Current);
    }

    /// <summary>Applies language and formatting to <see cref="LocalizationService"/> (UI thread).</summary>
    public void Apply()
    {
        var loc = LocalizationService.Instance;
        loc.SetLanguage(Current.Language);
        loc.SetFormatting(Current.Digits, Current.Calendar, Current.Clock24h);
    }
}
