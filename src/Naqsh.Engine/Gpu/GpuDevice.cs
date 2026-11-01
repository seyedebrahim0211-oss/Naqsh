using Serilog;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Naqsh.Engine.Gpu;

/// <summary>
/// The engine's single shared D3D11 device. Picks the high-performance adapter on hybrid laptops
/// (IDXGIFactory6::EnumAdapterByGpuPreference). Setting NAQSH_GPU_ADAPTER=WARP forces the software
/// rasterizer (used by CI tests).
/// </summary>
public sealed class GpuDevice : IDisposable
{
    public const string AdapterEnvironmentVariable = "NAQSH_GPU_ADAPTER";

    private static readonly FeatureLevel[] FeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
    ];

    private GpuDevice(ID3D11Device1 device, ID3D11DeviceContext context, string adapterName, long luid, bool isWarp)
    {
        Device = device;
        Context = context;
        AdapterName = adapterName;
        AdapterLuid = luid;
        IsWarp = isWarp;
    }

    public ID3D11Device1 Device { get; }

    /// <summary>Immediate context; used only by the render thread.</summary>
    public ID3D11DeviceContext Context { get; }

    public string AdapterName { get; }

    public long AdapterLuid { get; }

    public bool IsWarp { get; }

    /// <summary>True when the device was removed/reset (TDR) and must be recreated.</summary>
    public bool IsRemoved => Device.DeviceRemovedReason.Failure;

    public static bool WarpRequested =>
        string.Equals(Environment.GetEnvironmentVariable(AdapterEnvironmentVariable), "WARP", StringComparison.OrdinalIgnoreCase);

    public static GpuDevice Create(ILogger log, bool? forceWarp = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        var warp = forceWarp ?? WarpRequested;
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory6>();
        if (!warp)
        {
            for (uint i = 0; ; i++)
            {
                IDXGIAdapter1? adapter = null;
                if (factory.EnumAdapterByGpuPreference(i, GpuPreference.HighPerformance, out adapter).Failure || adapter is null)
                {
                    break;
                }

                using (adapter)
                {
                    var desc = adapter.Description1;
                    if ((desc.Flags & AdapterFlags.Software) != 0)
                    {
                        continue;
                    }

                    var created = D3D11.D3D11CreateDevice(
                        adapter,
                        DriverType.Unknown,
                        DeviceCreationFlags.BgraSupport,
                        FeatureLevels,
                        out var device,
                        out var level,
                        out var context);
                    if (created.Success && device is not null && context is not null)
                    {
                        log.Information(
                            "GPU: {Adapter} (vendor 0x{Vendor:X4}, {VramMb} MB, feature level {Level})",
                            desc.Description,
                            desc.VendorId,
                            (long)(ulong)desc.DedicatedVideoMemory / (1024 * 1024),
                            level);
                        return Wrap(device, context, desc.Description, desc.Luid, false);
                    }

                    log.Warning("GPU adapter {Adapter} failed D3D11 feature level 11_0: {Result}", desc.Description, created);
                }
            }

            log.Warning("No hardware D3D11 adapter found; falling back to WARP");
        }

        var warpResult = D3D11.D3D11CreateDevice(
            IntPtr.Zero,
            DriverType.Warp,
            DeviceCreationFlags.BgraSupport,
            FeatureLevels,
            out var warpDevice,
            out var warpContext);
        warpResult.CheckError();
        using var dxgiDevice = warpDevice!.QueryInterface<IDXGIDevice>();
        using var warpAdapter = dxgiDevice.GetAdapter();
        var wd = warpAdapter.Description;
        log.Information("GPU: WARP software rasterizer ({Adapter})", wd.Description);
        return Wrap(warpDevice, warpContext!, wd.Description, wd.Luid, true);
    }

    public void Dispose()
    {
        Context.ClearState();
        Context.Flush();
        Context.Dispose();
        Device.Dispose();
    }

    private static GpuDevice Wrap(ID3D11Device device, ID3D11DeviceContext context, string name, Vortice.Luid luid, bool warp)
    {
        var device1 = device.QueryInterface<ID3D11Device1>();
        device.Dispose();
        using (var mt = context.QueryInterfaceOrNull<ID3D11Multithread>())
        {
            // Inputs create textures from loader threads; protect the immediate context anyway.
            mt?.SetMultithreadProtected(true);
        }

        var l = ((long)luid.HighPart << 32) | luid.LowPart;
        return new GpuDevice(device1, context, name, l, warp);
    }
}
