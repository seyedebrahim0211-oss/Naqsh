using System.Windows;
using Microsoft.Win32;
using Naqsh.Localization;

namespace Naqsh.App.Services;

/// <summary>File/folder pickers and confirmations with localized text.</summary>
public interface IDialogService
{
    string? OpenFile(string titleKey, string filterKey, string patterns);

    string? PickFolder(string titleKey, string? initial);

    bool Confirm(string titleKey, string textKey);
}

public sealed class DialogService : IDialogService
{
    private static LocalizationService L => LocalizationService.Instance;

    public string? OpenFile(string titleKey, string filterKey, string patterns)
    {
        var dlg = new OpenFileDialog
        {
            Title = L.Get(titleKey),
            Filter = $"{L.Get(filterKey)}|{patterns}|{L.Get("Filter_AllFiles")}|*.*",
            CheckFileExists = true,
        };
        return dlg.ShowDialog(Application.Current.MainWindow) == true ? dlg.FileName : null;
    }

    public string? PickFolder(string titleKey, string? initial)
    {
        var dlg = new OpenFolderDialog { Title = L.Get(titleKey), InitialDirectory = initial ?? string.Empty };
        return dlg.ShowDialog(Application.Current.MainWindow) == true ? dlg.FolderName : null;
    }

    public bool Confirm(string titleKey, string textKey)
    {
        var options = L.IsRightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None;
        return MessageBox.Show(
            Application.Current.MainWindow,
            L.Get(textKey),
            L.Get(titleKey),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No,
            options) == MessageBoxResult.Yes;
    }
}
