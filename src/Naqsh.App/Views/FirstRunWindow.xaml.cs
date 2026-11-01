using System.Windows;
using Naqsh.App.Services;
using Naqsh.Core.Formats;
using Naqsh.Core.Settings;
using Naqsh.Localization;

namespace Naqsh.App.Views;

/// <summary>First-run screen: UI language (switches live) and broadcast region.</summary>
public partial class FirstRunWindow : Window
{
    private readonly SettingsService _settings;

    public FirstRunWindow(SettingsService settings)
    {
        _settings = settings;
        InitializeComponent();
        var persian = settings.Current.Language == AppSettings.Persian;
        Persian.IsChecked = persian;
        English.IsChecked = !persian;
        Pal.IsChecked = settings.Current.Region == BroadcastRegion.Pal50;
        Ntsc.IsChecked = settings.Current.Region == BroadcastRegion.Ntsc60;
    }

    private void OnLanguageChecked(object sender, RoutedEventArgs e)
    {
        // Preview the choice immediately; it is saved on Start.
        LocalizationService.Instance.SetLanguage(sender == English ? AppSettings.English : AppSettings.Persian);
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        var lang = English.IsChecked == true ? AppSettings.English : AppSettings.Persian;
        var region = Ntsc.IsChecked == true ? BroadcastRegion.Ntsc60 : BroadcastRegion.Pal50;
        _settings.Update(s => s with { Language = lang, Region = region, FirstRunCompleted = true });
        DialogResult = true;
    }
}
