using System.Diagnostics;
using System.IO.Pipes;
using Naqsh.Core.Diagnostics;
using Naqsh.Core.Ipc;
using Naqsh.Engine.Clock;
using Naqsh.Engine.Outputs;
using Serilog;

namespace Naqsh.Engine.Host;

/// <summary>
/// Named-pipe server for the UI. One UI at a time. Sends state on change, stats at 4 Hz and
/// errors as they happen. An unexpected disconnect is reported to the <see cref="UiWatchdog"/>.
/// </summary>
internal sealed class IpcServer
{
    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(5);
    private readonly ProductionEngine _engine;
    private readonly RenderLoop _loop;
    private readonly UiWatchdog _watchdog;
    private readonly ILogger _log;
    private readonly Stopwatch _idle = Stopwatch.StartNew();
    private volatile bool _connected;

    public IpcServer(ProductionEngine engine, RenderLoop loop, UiWatchdog watchdog, ILogger log)
    {
        _engine = engine;
        _loop = loop;
        _watchdog = watchdog;
        _log = log;
    }

    public bool ClientConnected => _connected;

    public TimeSpan IdleFor => _connected ? TimeSpan.Zero : _idle.Elapsed;

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeServerStream(
                    IpcProtocol.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await using (pipe.ConfigureAwait(false))
                {
                    await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    _connected = true;
                    _watchdog.OnUiConnected();
                    var clean = await HandleClientAsync(pipe, ct).ConfigureAwait(false);
                    _connected = false;
                    _idle.Restart();
                    if (!clean && !ct.IsCancellationRequested)
                    {
                        _watchdog.OnUiLost();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException ex)
            {
                _connected = false;
                _log.Warning(ex, "Pipe error");
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Returns true when the UI said goodbye (clean exit).</summary>
    private async Task<bool> HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var channel = new MessageChannel(pipe);
        using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            using (var helloCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                helloCts.CancelAfter(HelloTimeout);
                if (await channel.ReceiveAsync(helloCts.Token).ConfigureAwait(false) is not HelloRequest hello)
                {
                    _log.Warning("UI did not send hello; closing");
                    return true;
                }

                if (hello.ProtocolVersion != IpcProtocol.Version)
                {
                    _log.Warning("UI protocol {Ui} != engine {Engine}", hello.ProtocolVersion, IpcProtocol.Version);
                }

                _watchdog.SetUiPath(hello.UiExecutablePath);
                _log.Information("UI attached (pid {Pid})", hello.UiProcessId);
            }

            await channel.SendAsync(_engine.CreateHello(), ct).ConfigureAwait(false);
            await channel.SendAsync(_engine.CreateState(), ct).ConfigureAwait(false);
            var sender = Task.Run(() => SendLoopAsync(channel, clientCts.Token), clientCts.Token);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var msg = await channel.ReceiveAsync(ct).ConfigureAwait(false);
                    if (msg is null)
                    {
                        _log.Warning("UI disconnected unexpectedly");
                        return false;
                    }

                    if (msg is GoodbyeRequest bye)
                    {
                        _log.Information("UI detached cleanly (shutdown engine: {Shutdown})", bye.ShutdownEngine);
                        if (bye.ShutdownEngine)
                        {
                            Program.RequestShutdown();
                        }

                        return true;
                    }

                    await DispatchAsync(msg, channel, ct).ConfigureAwait(false);
                }

                return true;
            }
            finally
            {
                await clientCts.CancelAsync().ConfigureAwait(false);
                try
                {
                    await sender.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (IOException)
                {
                }
            }
        }
        catch (IOException ex)
        {
            _log.Warning(ex, "UI pipe broken");
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task DispatchAsync(IpcMessage msg, MessageChannel channel, CancellationToken ct)
    {
        switch (msg)
        {
            case AddInputRequest r:
                _engine.AddInput(r.Input);
                break;
            case RemoveInputRequest r:
                _engine.RemoveInput(r.InputId);
                break;
            case SetPreviewRequest r:
                _engine.SetPreview(r.InputId);
                break;
            case CutRequest:
                _engine.Cut();
                break;
            case FadeRequest r:
                _engine.Fade(r.DurationMs);
                break;
            case DirectRequest r:
                _engine.Direct(r.InputId);
                break;
            case SetFormatRequest r:
                _engine.SetFormat(r.Format);
                break;
            case StartRecordingRequest r:
                await StartRecordingAsync(r.Folder, channel, ct).ConfigureAwait(false);
                break;
            case StopRecordingRequest:
                _ = Task.Run(() => StopRecordingAsync(channel, ct), ct);
                break;
            default:
                _log.Warning("Unexpected message {Type}", msg.GetType().Name);
                break;
        }
    }

    private async Task StartRecordingAsync(string folder, MessageChannel channel, CancellationToken ct)
    {
        try
        {
            var target = string.IsNullOrWhiteSpace(folder) ? NaqshPaths.DefaultRecordings : folder;
            await Task.Run(() => _engine.StartRecording(target), ct).ConfigureAwait(false);
        }
        catch (RecorderException ex)
        {
            _log.Error(ex, "Recording could not start ({Code})", ex.Code);
            await channel.SendAsync(new ErrorEvent(ex.Code, ex.Message), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log.Error(ex, "Recording could not start");
            await channel.SendAsync(new ErrorEvent("Recording", ex.Message), ct).ConfigureAwait(false);
        }
    }

    private async Task StopRecordingAsync(MessageChannel channel, CancellationToken ct)
    {
        try
        {
            await _engine.StopRecordingAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            _log.Error(ex, "Stopping the recording failed");
            try
            {
                await channel.SendAsync(new ErrorEvent("Recording", ex.Message), ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task SendLoopAsync(MessageChannel channel, CancellationToken ct)
    {
        var stateVersion = _engine.StateVersion;
        var surfaceVersion = _engine.SurfaceVersion;
        var statsClock = Stopwatch.StartNew();
        long lastRendered = 0;
        var lastStatsTicks = statsClock.ElapsedTicks;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(33, ct).ConfigureAwait(false);
            if (_engine.SurfaceVersion != surfaceVersion)
            {
                surfaceVersion = _engine.SurfaceVersion;
                await channel.SendAsync(_engine.CreateHello(), ct).ConfigureAwait(false);
            }

            if (_engine.StateVersion != stateVersion)
            {
                stateVersion = _engine.StateVersion;
                await channel.SendAsync(_engine.CreateState(), ct).ConfigureAwait(false);
            }

            while (_engine.TryDequeueError(out var err) && err is not null)
            {
                await channel.SendAsync(err, ct).ConfigureAwait(false);
            }

            var elapsed = Stopwatch.GetElapsedTime(lastStatsTicks, statsClock.ElapsedTicks);
            if (elapsed >= TimeSpan.FromMilliseconds(250))
            {
                var stats = _loop.ReadStats();
                var fps = (stats.RenderedFrames - lastRendered) / elapsed.TotalSeconds;
                lastRendered = stats.RenderedFrames;
                lastStatsTicks = statsClock.ElapsedTicks;
                await channel.SendAsync(_engine.CreateStats(stats, fps), ct).ConfigureAwait(false);
            }
        }
    }
}
