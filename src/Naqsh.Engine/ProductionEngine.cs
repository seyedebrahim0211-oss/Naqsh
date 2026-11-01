using System.Collections.Concurrent;
using System.Runtime;
using Naqsh.Core.Formats;
using Naqsh.Core.Ipc;
using Naqsh.Core.Model;
using Naqsh.Core.Switching;
using Naqsh.Core.Timing;
using Naqsh.Engine.Clock;
using Naqsh.Engine.Gpu;
using Naqsh.Engine.Outputs;
using Naqsh.Engine.Sources;
using Serilog;
using Vortice.DXGI;

namespace Naqsh.Engine;

/// <summary>
/// The engine: owns the GPU device, inputs, switcher, compositor, UI monitor surfaces and the
/// recorder. <see cref="RenderFrame"/> runs on the render thread; every other public method may be
/// called from IPC threads and is marshalled to the render thread through a command queue.
/// </summary>
public sealed class ProductionEngine : IFrameRenderer, IDisposable
{
    public const int MaxInputs = 64;
    public const int MonitorWidth = 960;

    private readonly ILogger _log;
    private readonly IVideoSourceFactory _sourceFactory;
    private readonly IRecorderFactory _recorderFactory;
    private readonly Func<GpuDevice> _deviceFactory;
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly ConcurrentQueue<ErrorEvent> _errors = new();
    private readonly IVideoSource?[] _sources = new IVideoSource?[MaxInputs];
    private readonly Switcher _switcher = new();
    private readonly object _stateLock = new();
    private readonly object _gpuLock = new();
    private readonly ManualResetEventSlim _recorderDetached = new(false);
    private readonly string _surfacePrefix;

    private GpuDevice _gpu = null!;
    private Compositor _compositor = null!;
    private RenderTarget _program = null!;
    private RenderTarget _preview = null!;
    private RenderTarget _programBgra = null!;
    private SharedSurface _programMonitor = null!;
    private SharedSurface _previewMonitor = null!;
    private int _surfaceGeneration;

    private VideoFormat _format;
    private int _switcherVersionSeen = -1;
    private Guid _liveA;
    private Guid _liveB;
    private long _frameCounter;

    private volatile IRecorder? _recorder;
    private volatile bool _detachRecorder;
    private int _stateVersion;
    private int _surfaceVersion;

    // Published copies of switcher state for IPC threads.
    private Guid _publishedPreview;
    private Guid _publishedProgram;
    private bool _publishedInTransition;

    public ProductionEngine(
        ILogger log,
        IVideoSourceFactory sourceFactory,
        IRecorderFactory recorderFactory,
        VideoFormat format,
        Func<GpuDevice> deviceFactory)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _recorderFactory = recorderFactory ?? throw new ArgumentNullException(nameof(recorderFactory));
        _deviceFactory = deviceFactory ?? throw new ArgumentNullException(nameof(deviceFactory));
        _format = format ?? throw new ArgumentNullException(nameof(format));
        _format.Validate();
        _surfacePrefix = $"Naqsh_{Environment.ProcessId}";
        _gpu = _deviceFactory();
        CreateGpuResources();
    }

    /// <summary>Raised (on the render thread) when the project frame rate must change.</summary>
    public event Action<FrameRate>? RateChangeRequested;

    public VideoFormat Format => _format;

    public GpuDevice Gpu => _gpu;

    /// <summary>Changes whenever inputs, Preview/Program or recording state change.</summary>
    public int StateVersion => Volatile.Read(ref _stateVersion);

    /// <summary>Changes when the shared monitor surfaces were recreated (UI must reopen them).</summary>
    public int SurfaceVersion => Volatile.Read(ref _surfaceVersion);

    public bool IsRecording => _recorder is not null;

    /// <summary>Frames rendered by <see cref="RenderFrame"/> (for tests).</summary>
    public long FrameCounter => Interlocked.Read(ref _frameCounter);

    // ---------------------------------------------------------------- commands (any thread)

    public void AddInput(InputDescriptor descriptor, string? projectFolder = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Post(() =>
        {
            var slot = Array.IndexOf(_sources, null);
            if (slot < 0)
            {
                _log.Warning("Input limit {Max} reached", MaxInputs);
                return;
            }

            try
            {
                _sources[slot] = _sourceFactory.Create(descriptor, _gpu, projectFolder);
                _log.Information("Input added: {Kind} {Id}", descriptor.Kind, descriptor.Id);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.Error(ex, "Input {Id} could not be created", descriptor.Id);
                ReportError("InputFailed", ex.Message);
            }

            BumpState();
        });
    }

    public void RemoveInput(Guid id) => Post(() =>
    {
        for (var i = 0; i < _sources.Length; i++)
        {
            if (_sources[i] is { } s && s.Id == id)
            {
                _sources[i] = null;
                _switcher.Clear(id);

                // Disposal may join decoder threads: never on the render thread.
                _ = Task.Run(s.Dispose);
                _log.Information("Input removed: {Id}", id);
            }
        }

        BumpState();
    });

    public void SetPreview(Guid id) => Post(() => _switcher.SetPreview(id));

    public void Cut() => Post(() => _switcher.RequestCut());

    public void Fade(int durationMs) => Post(() =>
        _switcher.RequestFade(_format.Rate.FramesForDuration(TimeSpan.FromMilliseconds(Math.Clamp(durationMs, 1, 10_000)))));

    public void Direct(Guid id) => Post(() => _switcher.RequestDirect(id));

    public void SetFormat(VideoFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        format.Validate();
        Post(() =>
        {
            if (_recorder is not null)
            {
                ReportError("OnAirLocked", "Format change refused while recording");
                return;
            }

            if (format == _format)
            {
                return;
            }

            lock (_gpuLock)
            {
                DisposeGpuResources();
                _format = format;
                CreateGpuResources();
            }

            RateChangeRequested?.Invoke(format.Rate);
            _log.Information("Project format changed to {Format}", format.ShortName);
            BumpState();
        });
    }

    /// <summary>Starts recording Program. Call from a non-render thread.</summary>
    public string StartRecording(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        if (_recorder is not null)
        {
            return _recorder.FilePath;
        }

        IRecorder rec;
        lock (_gpuLock)
        {
            rec = _recorderFactory.Start(_gpu, _format, folder);
        }

        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        _detachRecorder = false;
        _recorder = rec;
        _log.Information("Recording started: {Path} ({Encoder})", rec.FilePath, rec.EncoderName);
        BumpState();
        return rec.FilePath;
    }

    /// <summary>Stops recording. Call from a non-render thread.</summary>
    public async Task<RecordingResult?> StopRecordingAsync(CancellationToken ct)
    {
        var rec = _recorder;
        if (rec is null)
        {
            return null;
        }

        // Hand-shake with the render thread so it never touches the recorder again.
        _recorderDetached.Reset();
        _detachRecorder = true;
        if (!_recorderDetached.Wait(TimeSpan.FromSeconds(1), ct))
        {
            _log.Warning("Render thread did not acknowledge recorder detach in time");
        }

        _recorder = null;
        _detachRecorder = false;
        RecordingResult result;
        try
        {
            result = await rec.StopAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            rec.Dispose();
            GCSettings.LatencyMode = GCLatencyMode.Interactive;
            BumpState();
        }

        _log.Information(
            "Recording stopped: {Path}, {Frames} frames, {Dropped} dropped, remuxed={Remuxed}",
            result.FilePath,
            result.FramesWritten,
            result.FramesDropped,
            result.Remuxed);
        if (result.ErrorCode is { } code)
        {
            ReportError(code, null);
        }

        return result;
    }

    // ---------------------------------------------------------------- snapshots (any thread)

    public HelloResponse CreateHello()
    {
        lock (_gpuLock)
        {
            return new HelloResponse(
                IpcProtocol.Version,
                _gpu.AdapterName,
                _gpu.AdapterLuid,
                _gpu.IsWarp,
                _previewMonitor.Name,
                _programMonitor.Name,
                _programMonitor.Target.Width,
                _programMonitor.Target.Height);
        }
    }

    public StateEvent CreateState()
    {
        lock (_stateLock)
        {
            var inputs = new List<InputState>();
            foreach (var s in _sources)
            {
                if (s is not null)
                {
                    inputs.Add(new InputState(s.Descriptor, s.Status, s.DurationSeconds));
                }
            }

            var rec = _recorder;
            return new StateEvent(
                _format,
                inputs,
                _publishedPreview,
                _publishedProgram,
                _publishedInTransition,
                rec is null
                    ? new RecordingState(false, null, null, null)
                    : new RecordingState(true, rec.FilePath, rec.StartedUtc, rec.EncoderName));
        }
    }

    public StatsEvent CreateStats(RenderStats stats, double fps)
    {
        var tc = Timecode.FromTimeOfDay(DateTime.Now.TimeOfDay, _format.Rate);
        return new StatsEvent(
            fps,
            stats.RenderedFrames,
            stats.DroppedFrames,
            stats.LastFrameMs,
            stats.MaxFrameMs,
            _recorder?.DroppedFrames ?? 0,
            tc.ToString());
    }

    public bool TryDequeueError(out ErrorEvent? error) => _errors.TryDequeue(out error);

    public ProjectDocument CreateProjectSnapshot()
    {
        lock (_stateLock)
        {
            var inputs = new List<InputDescriptor>();
            foreach (var s in _sources)
            {
                if (s is not null)
                {
                    inputs.Add(s.Descriptor);
                }
            }

            return new ProjectDocument
            {
                Format = _format,
                Inputs = inputs,
                Preview = _publishedPreview,
                Program = _publishedProgram,
            };
        }
    }

    // ---------------------------------------------------------------- render thread

    /// <summary>Renders one frame. Allocation-free in steady state.</summary>
    public void RenderFrame(long frameIndex, long clockTicks)
    {
        while (_commands.TryDequeue(out var cmd))
        {
            try
            {
                lock (_stateLock)
                {
                    cmd();
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.Error(ex, "Engine command failed");
                ReportError("Generic", ex.Message);
            }
        }

        if ((frameIndex % 25) == 0 && _gpu.IsRemoved)
        {
            RecoverDevice();
            return;
        }

        try
        {
            DrawFrame(frameIndex, clockTicks);
        }
        catch (SharpGen.Runtime.SharpGenException ex) when (IsDeviceLost(ex))
        {
            RecoverDevice();
        }

        Interlocked.Increment(ref _frameCounter);
    }

    public void Dispose()
    {
        var rec = _recorder;
        _recorder = null;
        rec?.Dispose();
        foreach (var s in _sources)
        {
            s?.Dispose();
        }

        DisposeGpuResources();
        _gpu.Dispose();
        _recorderDetached.Dispose();
    }

    private void DrawFrame(long frameIndex, long clockTicks)
    {
        var ctx = _gpu.Context;
        var frame = new FrameContext(frameIndex, clockTicks, _format);
        for (var i = 0; i < _sources.Length; i++)
        {
            _sources[i]?.Update(ctx, frame);
        }

        var mix = _switcher.Tick(frameIndex);
        UpdateLiveSources(mix, clockTicks);
        if (_switcher.Version != _switcherVersionSeen)
        {
            _switcherVersionSeen = _switcher.Version;
            _publishedPreview = _switcher.Preview;
            _publishedProgram = _switcher.Program;
            _publishedInTransition = _switcher.InTransition;
            BumpState();
        }

        var a = Resolve(mix.A);
        var b = Resolve(mix.B);
        _compositor.Mix(_program, a, b, mix.Mix);
        var pv = Resolve(_switcher.Preview);
        _compositor.Mix(_preview, pv, pv, 0f);

        CopyToMonitor(_program, _programMonitor);
        CopyToMonitor(_preview, _previewMonitor);

        var rec = _recorder;
        if (rec is not null)
        {
            if (_detachRecorder)
            {
                _recorderDetached.Set();
            }
            else
            {
                _compositor.Copy(_program, _programBgra);
                rec.SubmitFrame(ctx, _programBgra, frameIndex);
                if (rec.FailureCode is not null && !_failureReported)
                {
                    _failureReported = true;
                    _ = Task.Run(() => StopAfterFailureAsync(rec));
                }
            }
        }
        else if (_detachRecorder)
        {
            _recorderDetached.Set();
        }

        ctx.Flush();
    }

    private bool _failureReported;

    private async Task StopAfterFailureAsync(IRecorder rec)
    {
        try
        {
            ReportError(rec.FailureCode ?? "Recording", null);
            await StopRecordingAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(ex, "Stopping a failed recording also failed");
        }
        finally
        {
            _failureReported = false;
        }
    }

    private void CopyToMonitor(RenderTarget source, SharedSurface monitor)
    {
        // Never wait: if the UI is reading the surface, skip this monitor frame.
        if (KeyedMutex.TryAcquire(monitor.Mutex, 0, 0))
        {
            _compositor.Copy(source, monitor.Target);
            KeyedMutex.Release(monitor.Mutex, 0);
        }
    }

    private void UpdateLiveSources(in ProgramMix mix, long clockTicks)
    {
        var newA = mix.A;
        var newB = mix.IsMixing ? mix.B : mix.A;
        if (newA == _liveA && newB == _liveB)
        {
            return;
        }

        // Leaving Program.
        if (_liveA != Guid.Empty && _liveA != newA && _liveA != newB)
        {
            Find(_liveA)?.OnProgramChanged(false, clockTicks);
        }

        if (_liveB != Guid.Empty && _liveB != _liveA && _liveB != newA && _liveB != newB)
        {
            Find(_liveB)?.OnProgramChanged(false, clockTicks);
        }

        // Entering Program.
        if (newA != Guid.Empty && newA != _liveA && newA != _liveB)
        {
            Find(newA)?.OnProgramChanged(true, clockTicks);
        }

        if (newB != Guid.Empty && newB != newA && newB != _liveA && newB != _liveB)
        {
            Find(newB)?.OnProgramChanged(true, clockTicks);
        }

        _liveA = newA;
        _liveB = newB;
    }

    private DrawSource Resolve(Guid id)
    {
        if (id == Guid.Empty)
        {
            return DrawSource.Black;
        }

        var s = Find(id);
        return s is null ? DrawSource.Black : s.Current;
    }

    private IVideoSource? Find(Guid id)
    {
        for (var i = 0; i < _sources.Length; i++)
        {
            var s = _sources[i];
            if (s is not null && s.Id == id)
            {
                return s;
            }
        }

        return null;
    }

    private void RecoverDevice()
    {
        var reason = _gpu.Device.DeviceRemovedReason;
        _log.Error("GPU device removed ({Reason}); recreating", reason);
        var started = MasterClock.Shared.NowTicks;
        var rec = _recorder;
        if (rec is not null)
        {
            // The recorder's staging textures belong to the lost device; finalize the file.
            _recorder = null;
            _ = Task.Run(async () =>
            {
                try
                {
                    await rec.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    rec.Dispose();
                    GCSettings.LatencyMode = GCLatencyMode.Interactive;
                }
            });
        }

        lock (_gpuLock)
        {
            var descriptors = new List<InputDescriptor>();
            for (var i = 0; i < _sources.Length; i++)
            {
                if (_sources[i] is { } s)
                {
                    descriptors.Add(s.Descriptor);
                    s.Dispose();
                    _sources[i] = null;
                }
            }

            DisposeGpuResources();
            _gpu.Dispose();
            _gpu = _deviceFactory();
            CreateGpuResources();
            for (var i = 0; i < descriptors.Count; i++)
            {
                _sources[i] = _sourceFactory.Create(descriptors[i], _gpu, null);
            }
        }

        _liveA = Guid.Empty;
        _liveB = Guid.Empty;
        _log.Information("GPU recovered in {Ms} ms", (MasterClock.Shared.NowTicks - started) / 10_000);
        ReportError("GpuReset", reason.ToString());
        BumpState();
    }

    private static bool IsDeviceLost(SharpGen.Runtime.SharpGenException ex) =>
        ex.ResultCode == ResultCode.DeviceRemoved ||
        ex.ResultCode == ResultCode.DeviceReset ||
        ex.ResultCode == ResultCode.DeviceHung;

    private void CreateGpuResources()
    {
        var device = _gpu.Device;
        var w = _format.Width;
        var h = _format.Height;
        _compositor = new Compositor(_gpu);
        _program = new RenderTarget(device, w, h, Vortice.DXGI.Format.R16G16B16A16_Float);
        _preview = new RenderTarget(device, w, h, Vortice.DXGI.Format.R16G16B16A16_Float);
        _programBgra = new RenderTarget(device, w, h, Vortice.DXGI.Format.B8G8R8A8_UNorm);
        var mh = Math.Max(2, (int)Math.Round(MonitorWidth * (double)h / w / 2) * 2);
        var gen = ++_surfaceGeneration;
        _programMonitor = new SharedSurface(device, MonitorWidth, mh, $"{_surfacePrefix}_{gen}_Program");
        _previewMonitor = new SharedSurface(device, MonitorWidth, mh, $"{_surfacePrefix}_{gen}_Preview");
        Interlocked.Increment(ref _surfaceVersion);
    }

    private void DisposeGpuResources()
    {
        _previewMonitor?.Dispose();
        _programMonitor?.Dispose();
        _programBgra?.Dispose();
        _preview?.Dispose();
        _program?.Dispose();
        _compositor?.Dispose();
    }

    private void Post(Action command) => _commands.Enqueue(command);

    private void BumpState() => Interlocked.Increment(ref _stateVersion);

    private void ReportError(string code, string? detail)
    {
        _errors.Enqueue(new ErrorEvent(code, detail));
        while (_errors.Count > 32 && _errors.TryDequeue(out _))
        {
        }
    }
}
