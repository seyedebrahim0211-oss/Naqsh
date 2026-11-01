using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Naqsh.App.Services;
using Naqsh.App.ViewModels;
using Naqsh.App.Views;
using Naqsh.Core.Diagnostics;
using Naqsh.Core.Formats;
using Naqsh.Core.Settings;
using Naqsh.Localization;
using Serilog;

namespace Naqsh.App;

/// <summary>Naqsh.exe — the UI process. Program output lives in Naqsh.Engine.exe.</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Process", "ui")
            .WriteTo.File(
                Path.Combine(NaqshPaths.Logs, "ui-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                formatProvider: CultureInfo.InvariantCulture,
                shared: true)
            .CreateLogger();
        LocalizationService.MissingKeyReporter = key => Log.Warning("Missing resource key {Key}", key);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled UI exception");

        var reattach = e.Args.Contains("--reattach", StringComparer.Ordinal);
        Log.Information("Naqsh UI starting (reattach: {Reattach})", reattach);

        var settings = new SettingsService(new SettingsStore(SettingsStore.DefaultFolder));
        settings.Apply();

        if (!settings.Current.FirstRunCompleted && !reattach)
        {
            var firstRun = new FirstRunWindow(settings);
            if (firstRun.ShowDialog() != true)
            {
                Shutdown(0);
                return;
            }
        }

        var engineArgs = VideoFormat.DefaultFor(settings.Current.Region).Rate.IsFractional ? "--rate 59.94" : string.Empty;
        var engine = new EngineClient(Log.Logger, engineArgs);
        var vm = new MainViewModel(engine, settings, new DialogService(), Dispatcher);
        var main = new MainWindow(vm, settings);
        MainWindow = main;
        main.Closed += async (_, _) =>
        {
            await engine.DisposeAsync().ConfigureAwait(true);
            Shutdown(0);
        };
        main.Show();
        engine.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Naqsh UI exiting");
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // UI bugs must never take the operator's screen down mid-show; output is in the engine anyway.
        Log.Error(e.Exception, "Unhandled exception on the UI thread");
        e.Handled = true;
    }
}
