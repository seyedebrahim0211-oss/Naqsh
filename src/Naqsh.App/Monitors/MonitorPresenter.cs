using Naqsh.Engine.Gpu;
using Serilog;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Naqsh.App.Monitors;

/// <summary>What a monitor shows: the engine's shared surface name and GPU adapter.</summary>
public sealed record MonitorSource(string SurfaceName, long AdapterLuid, bool IsWarp, int Width, int Height);

/// <summary>
/// Presents one engine surface into a child HWND with a DXGI flip-model swap chain on its own
/// thread, paced by the swap chain's frame-latency waitable object (vsync). Opens the engine's
/// NT-handle shared texture by name and copies it under the keyed mutex. Never blocks the UI thread.
/// </summary>
internal sealed class MonitorPresenter : IDisposable
{
    private static readonly Color4 MonitorBlack = new(0x05 / 255f, 0x0B / 255f, 0x14 / 255f, 1f);
    private readonly IntPtr _hwnd;
    private readonly ILogger _log;
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private volatile bool _stop;
    private volatile MonitorSource? _source;

    private ID3D11Device1? _device;
    private ID3D11DeviceContext? _ctx;
    private long _deviceLuid = -1;
    private IDXGISwapChain2? _swapChain;
    private IntPtr _waitable;
    private ID3D11Texture2D? _backBuffer;
    private ID3D11RenderTargetView? _backRtv;
    private int _swapWidth;
    private int _swapHeight;
    private ID3D11Texture2D? _shared;
    private IDXGIKeyedMutex? _sharedMutex;
    private string? _sharedName;

    public MonitorPresenter(IntPtr hwnd, ILogger log)
    {
        _hwnd = hwnd;
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "Naqsh monitor" };
        _thread.Start();
    }

    public void SetSource(MonitorSource? source)
    {
        _source = source;
        _wake.Set();
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread.Join(TimeSpan.FromSeconds(2));
        _wake.Dispose();
    }

    private void Run()
    {
        while (!_stop)
        {
            try
            {
                PresentLoop();
            }
            catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or InvalidOperationException or ArgumentException)
            {
                _log.Warning(ex, "Monitor presenter error; recreating");
                ReleaseAll();
                _wake.WaitOne(500);
            }
        }

        ReleaseAll();
    }

    private void PresentLoop()
    {
        while (!_stop)
        {
            var src = _source;
            EnsureDevice(src);
            EnsureShared(src);
            var w = _shared is not null ? src!.Width : 960;
            var h = _shared is not null ? src!.Height : 540;
            EnsureSwapChain(w, h);

            _ = NativeMethods.WaitForSingleObject(_waitable, 100);
            if (_shared is not null && _sharedMutex is not null)
            {
                if (KeyedMutex.TryAcquire(_sharedMutex, 0, 4))
                {
                    _ctx!.CopyResource(_backBuffer!, _shared);
                    KeyedMutex.Release(_sharedMutex, 0);
                }
            }
            else
            {
                _ctx!.ClearRenderTargetView(_backRtv!, MonitorBlack);
            }

            var hr = _swapChain!.Present(1, PresentFlags.None);
            if (hr.Failure)
            {
                throw new InvalidOperationException($"Present failed: {hr}");
            }

            if (_shared is null)
            {
                // Nothing to show yet: re-check for a surface twice a second.
                _wake.WaitOne(500);
            }
        }
    }

    private void EnsureDevice(MonitorSource? src)
    {
        var luid = src?.AdapterLuid ?? 0;
        if (_device is not null && (src is null || luid == _deviceLuid))
        {
            return;
        }

        ReleaseAll();
        ID3D11Device? device = null;
        ID3D11DeviceContext? ctx = null;
        if (src is not null && !src.IsWarp)
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory4>();
            if (factory.EnumAdapterByLuid(Vortice.Luid.FromInt64(luid), out IDXGIAdapter1? adapter).Success && adapter is not null)
            {
                using (adapter)
                {
                    D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0], out device, out ctx).CheckError();
                }
            }
        }

        if (device is null)
        {
            var driver = src?.IsWarp == true ? DriverType.Warp : DriverType.Hardware;
            D3D11.D3D11CreateDevice(IntPtr.Zero, driver, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0], out device, out ctx).CheckError();
        }

        _device = device!.QueryInterface<ID3D11Device1>();
        device.Dispose();
        _ctx = ctx;
        _deviceLuid = luid;
    }

    private void EnsureShared(MonitorSource? src)
    {
        var name = src?.SurfaceName;
        if (name == _sharedName)
        {
            return;
        }

        _sharedMutex?.Dispose();
        _shared?.Dispose();
        _sharedMutex = null;
        _shared = null;
        _sharedName = null;
        if (name is null)
        {
            return;
        }

        try
        {
            _shared = _device!.OpenSharedResourceByName<ID3D11Texture2D>(name, Vortice.Direct3D11.SharedResourceFlags.Read | Vortice.Direct3D11.SharedResourceFlags.Write);
            _sharedMutex = _shared.QueryInterface<IDXGIKeyedMutex>();
            _sharedName = name;
            _log.Information("Monitor opened surface {Name}", name);
        }
        catch (SharpGen.Runtime.SharpGenException ex)
        {
            // The engine may not have created it yet; retry on the next loop.
            _log.Debug(ex, "Surface {Name} not available yet", name);
            _shared = null;
        }
    }

    private void EnsureSwapChain(int w, int h)
    {
        if (_swapChain is not null && w == _swapWidth && h == _swapHeight)
        {
            return;
        }

        _backRtv?.Dispose();
        _backBuffer?.Dispose();
        if (_swapChain is null)
        {
            using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var factory = adapter.GetParent<IDXGIFactory2>();
            var desc = new SwapChainDescription1
            {
                Width = (uint)w,
                Height = (uint)h,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Ignore,
                Flags = SwapChainFlags.FrameLatencyWaitableObject,
            };
            using var sc1 = factory.CreateSwapChainForHwnd(_device, _hwnd, desc, null, null);
            factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAll);
            _swapChain = sc1.QueryInterface<IDXGISwapChain2>();
            _swapChain.MaximumFrameLatency = 1;
            _waitable = _swapChain.FrameLatencyWaitableObject;
        }
        else
        {
            _swapChain.ResizeBuffers(2, (uint)w, (uint)h, Format.B8G8R8A8_UNorm, SwapChainFlags.FrameLatencyWaitableObject).CheckError();
        }

        _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _backRtv = _device!.CreateRenderTargetView(_backBuffer);
        _swapWidth = w;
        _swapHeight = h;
    }

    private void ReleaseAll()
    {
        _sharedMutex?.Dispose();
        _shared?.Dispose();
        _backRtv?.Dispose();
        _backBuffer?.Dispose();
        if (_waitable != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_waitable);
            _waitable = IntPtr.Zero;
        }

        _swapChain?.Dispose();
        _ctx?.ClearState();
        _ctx?.Dispose();
        _device?.Dispose();
        _sharedMutex = null;
        _shared = null;
        _sharedName = null;
        _backRtv = null;
        _backBuffer = null;
        _swapChain = null;
        _ctx = null;
        _device = null;
        _deviceLuid = -1;
        _swapWidth = 0;
        _swapHeight = 0;
    }
}
