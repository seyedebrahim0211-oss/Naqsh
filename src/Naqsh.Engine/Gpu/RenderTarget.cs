using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Naqsh.Engine.Gpu;

/// <summary>A texture usable both as render target and shader input.</summary>
public sealed class RenderTarget : IDisposable
{
    public RenderTarget(ID3D11Device device, int width, int height, Format format, ResourceOptionFlags misc = ResourceOptionFlags.None)
    {
        ArgumentNullException.ThrowIfNull(device);
        Width = width;
        Height = height;
        Format = format;
        var desc = new Texture2DDescription(
            format,
            (uint)width,
            (uint)height,
            1,
            1,
            BindFlags.RenderTarget | BindFlags.ShaderResource,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            1,
            0,
            misc);
        Texture = device.CreateTexture2D(desc);
        Rtv = device.CreateRenderTargetView(Texture);
        Srv = device.CreateShaderResourceView(Texture);
    }

    public int Width { get; }

    public int Height { get; }

    public Format Format { get; }

    public ID3D11Texture2D Texture { get; }

    public ID3D11RenderTargetView Rtv { get; }

    public ID3D11ShaderResourceView Srv { get; }

    public void Dispose()
    {
        Srv.Dispose();
        Rtv.Dispose();
        Texture.Dispose();
    }
}

/// <summary>
/// An 8-bit BGRA render target shared with the UI process by name (NT handle + keyed mutex).
/// The UI opens it with ID3D11Device1::OpenSharedResourceByName.
/// </summary>
public sealed class SharedSurface : IDisposable
{
    private readonly IntPtr _handle;

    public SharedSurface(ID3D11Device device, int width, int height, string name)
    {
        Name = name;
        Target = new RenderTarget(
            device,
            width,
            height,
            Format.B8G8R8A8_UNorm,
            ResourceOptionFlags.SharedNTHandle | ResourceOptionFlags.SharedKeyedMutex);
        Mutex = Target.Texture.QueryInterface<IDXGIKeyedMutex>();
        using var res = Target.Texture.QueryInterface<IDXGIResource1>();
        _handle = res.CreateSharedHandle(null, Vortice.DXGI.SharedResourceFlags.Read | Vortice.DXGI.SharedResourceFlags.Write, name);
    }

    public string Name { get; }

    public RenderTarget Target { get; }

    public IDXGIKeyedMutex Mutex { get; }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_handle);
        }

        Mutex.Dispose();
        Target.Dispose();
    }
}
