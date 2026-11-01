using System.Diagnostics;
using Serilog;

namespace Naqsh.Engine.Host;

/// <summary>
/// Restarts the UI (Naqsh.exe --reattach) when it disappears without saying goodbye. Backoff
/// 1, 2, 4, 8, 16 s; gives up after 5 attempts within 5 minutes. Output continues regardless.
/// </summary>
internal sealed class UiWatchdog
{
    public const string ReattachArgument = "--reattach";
    private const int MaxAttempts = 5;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private readonly List<DateTime> _attempts = [];
    private string? _uiPath;
    private CancellationTokenSource? _pending;

    public UiWatchdog(ILogger log)
    {
        _log = log;
    }

    public void SetUiPath(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            _uiPath = path;
        }
    }

    public void OnUiConnected()
    {
        lock (_gate)
        {
            _pending?.Cancel();
            _pending = null;
        }
    }

    public void OnUiLost()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            _attempts.RemoveAll(t => now - t > TimeSpan.FromMinutes(5));
            if (_uiPath is null)
            {
                _log.Warning("UI lost but its path is unknown; cannot restart it");
                return;
            }

            if (_attempts.Count >= MaxAttempts)
            {
                _log.Error("UI crashed {Count} times in 5 minutes; not restarting again. Output continues", _attempts.Count);
                return;
            }

            var delay = TimeSpan.FromSeconds(Math.Pow(2, _attempts.Count));
            _attempts.Add(now);
            _pending?.Cancel();
            var cts = new CancellationTokenSource();
            _pending = cts;
            var path = _uiPath;
            _log.Warning("UI lost; restarting it in {Delay}", delay);
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                    using var p = Process.Start(new ProcessStartInfo(path, ReattachArgument) { UseShellExecute = false });
                    _log.Information("UI restarted (pid {Pid})", p?.Id);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    _log.Error(ex, "UI restart failed");
                }
            });
        }
    }
}
