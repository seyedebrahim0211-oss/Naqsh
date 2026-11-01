using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Naqsh.App.Services;
using Naqsh.App.ViewModels;

namespace Naqsh.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly SettingsService _settings;
    private bool _closingConfirmed;

    public MainWindow(MainViewModel vm, SettingsService settings)
    {
        _vm = vm;
        _settings = settings;
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        DataContext = vm;
        InitializeComponent();
    }

    public ICommand OpenSettingsCommand { get; }

    protected override async void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnClosing(e);
        if (_closingConfirmed)
        {
            return;
        }

        if (_vm.IsRecording && !new DialogService().Confirm("Confirm_Exit_Title", "Confirm_Exit_Text"))
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _closingConfirmed = true;
        await _vm.ShutdownAsync().ConfigureAwait(true);
        Close();
    }

    private void OnAddColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.DataContext = _vm;
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.FlowDirection = FlowDirection;
            menu.IsOpen = true;
        }
    }

    private void OpenSettings()
    {
        var w = new SettingsWindow(_settings, _vm) { Owner = this };
        w.ShowDialog();
    }
}
