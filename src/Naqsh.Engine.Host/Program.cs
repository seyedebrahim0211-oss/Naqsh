using System.Globalization;
using Naqsh.Core.Diagnostics;
using Naqsh.Core.Formats;
using Naqsh.Core.Ipc;
using Naqsh.Engine;
using Naqsh.Engine.Clock;
using Naqsh.Engine.Gpu;
using Naqsh.Inputs;
using Naqsh.Outputs;
using Serilog;

namespace Naqsh.Engine.Host;

/// <summary>
/// Naqsh.Engine.exe: render, outputs and the IPC server. Runs independently of the UI process so
/// Program output and recording continue if the UI crashes or is closed.
/// </summary>
internal static class Program
{
    private static readonly ManualResetEventSlim ShutdownRequested = new(false);

    [STAThread]
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Process", "engine")
            .WriteTo.File(
                Path.Combine(NaqshPaths.Logs, "engine-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                formatProvider: CultureInfo.InvariantCulture,
                shared: true)
            .CreateLogger();
        CrashHandler.Install(Log.Logger, "engine");

        using var single = new Mutex(true, $@"Local\Naqsh.Engine.{Environment.UserName}", out var createdNew);
        if (!createdNew)
        {
            Log.Information("Another engine instance is already running; exiting");
            return 0;
        }

        try
        {
            return Run(args);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Fatal(ex, "Engine failed to start");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static void RequestShutdown() => ShutdownRequested.Set();

    private static int Run(string[] args)
    {
        Log.Information("Naqsh engine {Version} starting (pid {Pid}), args: {Args}", typeof(Program).Assembly.GetName().Version, Environment.ProcessId, string.Join(' ', args));
        var format = VideoFormat.Default;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--rate" && args[i + 1] == "59.94")
            {
                format = VideoFormat.Hd1080p5994;
            }
        }

        using var engine = new ProductionEngine(
            Log.Logger,
            new InputSourceFactory(Log.Logger),
            new ProgramRecorderFactory(Log.Logger),
            format,
            () => GpuDevice.Create(Log.Logger));
        using var loop = new RenderLoop(MasterClock.Shared, format.Rate, engine, Log.Logger);
        engine.RateChangeRequested += loop.SetRate;
        loop.Start();

        var watchdog = new UiWatchdog(Log.Logger);
        var server = new IpcServer(engine, loop, watchdog, Log.Logger);
        using var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        // Exit when asked, or when no UI has been attached for a while and nothing is on air.
        while (!ShutdownRequested.Wait(TimeSpan.FromSeconds(5)))
        {
            if (!server.ClientConnected && !engine.IsRecording && server.IdleFor > TimeSpan.FromMinutes(2))
            {
                Log.Information("No UI attached for 2 minutes and nothing on air; exiting");
                break;
            }
        }

        Log.Information("Engine shutting down");
        cts.Cancel();
        if (engine.IsRecording)
        {
            engine.StopRecordingAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        try
        {
            serverTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        return 0;
    }
}
