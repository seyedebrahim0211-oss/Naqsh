using System.Runtime.InteropServices;
using Naqsh.Core.Timing;
using Serilog;

namespace Naqsh.Engine.Clock;

/// <summary>Renders one frame. Called on the render thread; must not allocate in steady state.</summary>
public interface IFrameRenderer
{
    void RenderFrame(long frameIndex, long clockTicks);
}

/// <summary>Snapshot of render-loop timing for the status bar.</summary>
public readonly record struct RenderStats(long RenderedFrames, long DroppedFrames, double LastFrameMs, double MaxFrameMs);

/// <summary>
/// The render thread: registered with MMCSS "Playback" and paced by a high-resolution waitable
/// timer against <see cref="MasterClock"/>. Due times come from <see cref="FrameScheduler"/> so pacing
/// never drifts. Exceptions from the renderer are logged and the loop keeps running.
/// </summary>
public sealed class RenderLoop : IDisposable
{
    private readonly MasterClock _clock;
    private readonly IFrameRenderer _renderer;
    private readonly ILogger _log;
    private readonly Thread _thread;
    private volatile bool _running;
    private FrameRate _rate;
    private FrameRate? _pendingRate;
    private readonly object _rateLock = new();

    private long _rendered;
    private long _dropped;
    private long _lastFrameTicks;
    private long _maxFrameTicks;
    private long _errors;

    public RenderLoop(MasterClock clock, FrameRate rate, IFrameRenderer renderer, ILogger log)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _rate = rate;
        _thread = new Thread(Run)
        {
            Name = "Naqsh render",
            IsBackground = true,
            Priority = ThreadPriority.Highest,
        };
    }

    public FrameRate Rate => _rate;

    public int ManagedThreadId => _thread.ManagedThreadId;

    public void Start()
    {
        _running = true;
        _thread.Start();
    }

    public void SetRate(FrameRate rate)
    {
        lock (_rateLock)
        {
            _pendingRate = rate;
        }
    }

    /// <summary>Reads stats; resets the max frame time.</summary>
    public RenderStats ReadStats()
    {
        var max = Interlocked.Exchange(ref _maxFrameTicks, 0);
        return new RenderStats(
            Interlocked.Read(ref _rendered),
            Interlocked.Read(ref _dropped),
            Interlocked.Read(ref _lastFrameTicks) / 10_000.0,
            max / 10_000.0);
    }

    public void Dispose()
    {
        _running = false;
        if (_thread.IsAlive && !_thread.Join(TimeSpan.FromSeconds(2)))
        {
            _log.Warning("Render thread did not stop in time");
        }
    }

    private void Run()
    {
        var mmcss = IntPtr.Zero;
        var timer = IntPtr.Zero;
        var lowResPeriod = false;
        try
        {
            uint taskIndex = 0;
            mmcss = NativeMethods.AvSetMmThreadCharacteristics("Playback", ref taskIndex);
            if (mmcss == IntPtr.Zero)
            {
                _log.Warning("MMCSS registration failed (error {Error})", Marshal.GetLastPInvokeError());
            }

            timer = NativeMethods.CreateWaitableTimerEx(IntPtr.Zero, null, NativeMethods.CreateWaitableTimerHighResolution, NativeMethods.TimerAllAccess);
            if (timer == IntPtr.Zero)
            {
                // Windows 10 before 1803: fall back to a normal timer with 1 ms system resolution.
                _log.Warning("High-resolution waitable timer unavailable; using 1 ms timer resolution");
                _ = NativeMethods.timeBeginPeriod(1);
                lowResPeriod = true;
                timer = NativeMethods.CreateWaitableTimerEx(IntPtr.Zero, null, 0, NativeMethods.TimerAllAccess);
            }

            Loop(timer);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Fatal(ex, "Render loop terminated");
            throw;
        }
        finally
        {
            if (timer != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(timer);
            }

            if (lowResPeriod)
            {
                _ = NativeMethods.timeEndPeriod(1);
            }

            if (mmcss != IntPtr.Zero)
            {
                NativeMethods.AvRevertMmThreadCharacteristics(mmcss);
            }
        }
    }

    private void Loop(IntPtr timer)
    {
        var scheduler = new FrameScheduler(_rate, _clock.NowTicks);
        while (_running)
        {
            if (_pendingRate is not null)
            {
                lock (_rateLock)
                {
                    if (_pendingRate is { } r)
                    {
                        _rate = r;
                        scheduler.Restart(r, _clock.NowTicks);
                        _pendingRate = null;
                    }
                }
            }

            var wait = scheduler.NextDueTicks - _clock.NowTicks;
            if (wait > 0)
            {
                var due = -wait; // negative = relative, 100 ns units
                if (NativeMethods.SetWaitableTimer(timer, in due, 0, IntPtr.Zero, IntPtr.Zero, false))
                {
                    _ = NativeMethods.WaitForSingleObject(timer, NativeMethods.Infinite);
                }
            }

            var start = _clock.NowTicks;
            var frame = scheduler.BeginFrame(start);
            try
            {
                _renderer.RenderFrame(frame, start);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Contain every failure: log (rate-limited) and keep the clock running.
                if (Interlocked.Increment(ref _errors) <= 20 || _errors % 1000 == 0)
                {
                    _log.Error(ex, "Frame {Frame} failed", frame);
                }
            }

            scheduler.EndFrame();
            var took = _clock.NowTicks - start;
            Interlocked.Exchange(ref _lastFrameTicks, took);
            if (took > Interlocked.Read(ref _maxFrameTicks))
            {
                Interlocked.Exchange(ref _maxFrameTicks, took);
            }

            Interlocked.Increment(ref _rendered);
            Interlocked.Exchange(ref _dropped, scheduler.DroppedFrames);
        }
    }
}
