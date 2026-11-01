using System.Windows;
using Naqsh.App.Services;
using Naqsh.App.ViewModels;
using Naqsh.Core.Diagnostics;
using Naqsh.Core.Formats;
using Naqsh.Core.Settings;

namespace Naqsh.App.Views;

/// <summary>Settings: language, digits, calendar, clock, recording folder, project format. Applied immediately.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly MainViewModel _vm;
    private bool _loading = true;

    public SettingsWindow(SettingsService settings, MainViewModel vm)
    {
        _settings = settings;
        _vm = vm;
        InitializeComponent();
        var s = settings.Current;
        LangFa.IsChecked = s.Language == AppSettings.Persian;
        LangEn.IsChecked = s.Language == AppSettings.English;
        DigitsFollow.IsChecked = s.Digits == DigitStyle.FollowLanguage;
        DigitsFa.IsChecked = s.Digits == DigitStyle.Persian;
        DigitsLatin.IsChecked = s.Digits == DigitStyle.Latin;
        CalFollow.IsChecked = s.Calendar == CalendarStyle.FollowLanguage;
        CalJalali.IsChecked = s.Calendar == CalendarStyle.Jalali;
        CalGregorian.IsChecked = s.Calendar == CalendarStyle.Gregorian;
        Clock24.IsChecked = s.Clock24h;
        FolderText.Text = s.RecordingFolder ?? NaqshPaths.DefaultRecordings;
        Formats.ItemsSource = VideoFormat.ProjectFormats;
        Formats.SelectedItem = VideoFormat.ProjectFormats.FirstOrDefault(f => f == vm.CurrentFormat);
        Formats.IsEnabled = !vm.IsRecording;
        FormatLocked.Visibility = vm.IsRecording ? Visibility.Visible : Visibility.Collapsed;
        _loading = false;
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var lang = LangEn.IsChecked == true ? AppSettings.English : AppSettings.Persian;
        var digits = DigitsFa.IsChecked == true ? DigitStyle.Persian : DigitsLatin.IsChecked == true ? DigitStyle.Latin : DigitStyle.FollowLanguage;
        var cal = CalJalali.IsChecked == true ? CalendarStyle.Jalali : CalGregorian.IsChecked == true ? CalendarStyle.Gregorian : CalendarStyle.FollowLanguage;
        _settings.Update(s => s with { Language = lang, Digits = digits, Calendar = cal, Clock24h = Clock24.IsChecked == true });
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var folder = new DialogService().PickFolder("Settings_RecordingFolder", FolderText.Text);
        if (folder is null)
        {
            return;
        }

        FolderText.Text = folder;
        _settings.Update(s => s with { RecordingFolder = folder });
    }

    private async void OnFormatSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading || Formats.SelectedItem is not VideoFormat f || f == _vm.CurrentFormat)
        {
            return;
        }

        await _vm.SetFormatAsync(f).ConfigureAwait(true);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
