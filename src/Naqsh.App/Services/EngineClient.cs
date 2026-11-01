using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using Naqsh.Core.Ipc;
using Serilog;

namespace Naqsh.App.Services;

public enum EngineConnection
{
    Connecting,
    Connected,
    Lost,
}

/// <summary>
/// UI side of the engine pipe. Connects to a running Naqsh.Engine.exe or starts one, then keeps
/// the connection alive (reconnecting and restarting the engine if needed). Events are raised on a
/// background thread; view models marshal to the dispatcher.
/// </summary>
public sealed class EngineClient : IAsyncDisposable
{
    private const string EngineExe = "Naqsh.Engine.exe";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(700);
    private readonly ILogger _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _engineArgs;
    private MessageChannel? _channel;
    private Task? _runTask;
    private bool _startFailureReported;

    public EngineClient(ILogger log, string engineArgs)
    {
        _log = log;
        _engineArgs = engineArgs;
    }

    public event Action<EngineConnection>? ConnectionChanged;

    public event Action<IpcMessage>? MessageReceived;

    public EngineConnection State { get; private set; } = EngineConnection.Connecting;

    public void Start() => _runTask = Task.Run(() => RunAsync(_cts.Token));

    /// <summary>Sends a request; failures are logged (the connection loop recovers).</summary>
    public async Task SendAsync(IpcMessage message)
    {
        var ch = _channel;
        if (ch is null)
        {
            _log.Warning("Engine not connected; dropped {Type}", message.GetType().Name);
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await ch.SendAsync(message, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            _log.Warning(ex, "Send {Type} failed", message.GetType().Name);
        }
    }

    /// <summary>Tells the engine the UI is leaving. With <paramref name="shutdownEngine"/> the engine exits too.</summary>
    public async Task GoodbyeAsync(bool shutdownEngine)
    {
        await SendAsync(new GoodbyeRequest(shutdownEngine)).ConfigureAwait(false);
        await _cts.CancelAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_runTask is not null)
        {
            try
            {
                await _runTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var startedEngine = false;
        while (!ct.IsCancellationRequested)
        {
            SetState(State == EngineConnection.Connected ? EngineConnection.Lost : State);
            var pipe = new NamedPipeClientStream(".", IpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectCts.CancelAfter(ConnectTimeout);
                    await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                if (!startedEngine || Process.GetProcessesByName("Naqsh.Engine").Length == 0)
                {
                    startedEngine = TryStartEngine();
                    if (!startedEngine && !_startFailureReported)
                    {
                        _startFailureReported = true;
                        MessageReceived?.Invoke(new ErrorEvent("EngineStart", null));
                    }
                }

                await Task.Delay(300, ct).ConfigureAwait(false);
                continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warning(ex, "Pipe connect failed");
                await pipe.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(500, ct).ConfigureAwait(false);
                continue;
            }

            var channel = new MessageChannel(pipe);
            try
            {
                await channel.SendAsync(new HelloRequest(IpcProtocol.Version, Environment.ProcessId, Environment.ProcessPath), ct).ConfigureAwait(false);
                _channel = channel;
                SetState(EngineConnection.Connected);
                _log.Information("Connected to engine");
                while (!ct.IsCancellationRequested)
                {
                    var msg = await channel.ReceiveAsync(ct).ConfigureAwait(false);
                    if (msg is null)
                    {
                        break;
                    }

                    try
                    {
                        MessageReceived?.Invoke(msg);
                    }
#pragma warning disable CA1031 // A UI handler bug must never kill the connection loop.
                    catch (Exception ex)
#pragma warning restore CA1031
                    {
                        _log.Error(ex, "Handling {Type} failed", msg.GetType().Name);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                _log.Warning(ex, "Engine connection error");
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
            finally
            {
                _channel = null;
                await channel.DisposeAsync().ConfigureAwait(false);
            }

            if (!ct.IsCancellationRequested)
            {
                _log.Warning("Engine connection lost; reconnecting");
                SetState(EngineConnection.Lost);
                startedEngine = false;
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }

    private bool TryStartEngine()
    {
        var path = Path.Combine(AppContext.BaseDirectory, EngineExe);
        if (!File.Exists(path))
        {
            _log.Error("Engine executable not found at {Path}", path);
            return false;
        }

        try
        {
            using var p = Process.Start(new ProcessStartInfo(path, _engineArgs) { UseShellExecute = false });
            _log.Information("Engine started (pid {Pid})", p?.Id);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.Error(ex, "Engine could not be started");
            return false;
        }
    }

    private void SetState(EngineConnection state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        ConnectionChanged?.Invoke(state);
    }
}
