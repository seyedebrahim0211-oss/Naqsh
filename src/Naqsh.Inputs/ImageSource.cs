using Naqsh.Core.Ipc;
using Naqsh.Core.Model;
using Naqsh.Engine.Gpu;
using Naqsh.Engine.Sources;
using Serilog;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.WIC;

namespace Naqsh.Inputs;

/// <summary>
/// Still image (PNG with alpha, JPG, BMP, TIFF, GIF first frame) decoded with WIC on a background
/// thread and uploaded once as an immutable BGRA texture. A missing file shows a slate and is
/// retried with backoff, so a project never fails because of one file.
/// </summary>
public sealed class ImageSource : IVideoSource
{
    private const int MaxDimension = 16384;
    private readonly ILogger _log;
    private readonly string _path;
    private readonly CancellationTokenSource _cts = new();
    private volatile StatusBox _state = new(InputStatus.Loading, DrawSource.Black with { Slate = DrawSource.OfflineSlate }, null, null);

    public ImageSource(InputDescriptor descriptor, GpuDevice gpu, string fullPath, ILogger log)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        ArgumentNullException.ThrowIfNull(gpu);
        _path = fullPath;
        _log = log;
        var device = gpu.Device;
        _ = Task.Run(() => LoadWithRetryAsync(device, _cts.Token));
    }

    public Guid Id => Descriptor.Id;

    public InputDescriptor Descriptor { get; }

    public InputStatus Status => _state.Status;

    public double? DurationSeconds => null;

    public DrawSource Current => _state.Draw;

    public void Update(ID3D11DeviceContext context, in FrameContext frame)
    {
    }

    public void OnProgramChanged(bool onProgram, long clockTicks)
    {
    }

    public void Dispose()
    {
        _cts.Cancel();
        var s = _state;
        _state = new StatusBox(InputStatus.Offline, DrawSource.Black, null, null);
        s.View?.Dispose();
        s.Texture?.Dispose();
        _cts.Dispose();
    }

    /// <summary>Decodes an image file to straight-alpha BGRA pixels.</summary>
    public static byte[] Decode(string path, out int width, out int height)
    {
        using var factory = new IWICImagingFactory();
        using var decoder = factory.CreateDecoderFromFileName(path, FileAccess.Read, DecodeOptions.CacheOnLoad);
        using var frame = decoder.GetFrame(0);
        using var converter = factory.CreateFormatConverter();
        converter.Initialize(frame, Vortice.WIC.PixelFormat.Format32bppBGRA, BitmapDitherType.None, null!, 0, BitmapPaletteType.Custom).CheckError();
        var size = converter.Size;
        width = size.Width;
        height = size.Height;
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
        {
            throw new InvalidDataException($"Unsupported image size {width}x{height}.");
        }

        var stride = width * 4;
        var pixels = new byte[stride * height];
        converter.CopyPixels((uint)stride, pixels);
        return pixels;
    }

    private async Task LoadWithRetryAsync(ID3D11Device1 device, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pixels = Decode(_path, out var w, out var h);
                var tex = CreateTexture(device, pixels, w, h);
                var srv = device.CreateShaderResourceView(tex);
                if (ct.IsCancellationRequested)
                {
                    srv.Dispose();
                    tex.Dispose();
                    return;
                }

                _state = new StatusBox(InputStatus.Ready, new DrawSource(srv, w, h, false, DrawSource.BlackSlate), tex, srv);
                _log.Information("Image loaded: {Width}x{Height} {Id}", w, h, Id);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or SharpGen.Runtime.SharpGenException)
            {
                if (_state.Status != InputStatus.Offline)
                {
                    _log.Warning(ex, "Image {Id} unavailable; retrying", Id);
                }

                _state = new StatusBox(InputStatus.Offline, DrawSource.Black with { Slate = DrawSource.OfflineSlate }, null, null);
            }

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
        }
    }

    private static unsafe ID3D11Texture2D CreateTexture(ID3D11Device1 device, byte[] pixels, int w, int h)
    {
        var desc = new Texture2DDescription(
            Format.B8G8R8A8_UNorm,
            (uint)w,
            (uint)h,
            1,
            1,
            BindFlags.ShaderResource,
            ResourceUsage.Immutable,
            CpuAccessFlags.None,
            1,
            0,
            ResourceOptionFlags.None);
        fixed (byte* p = pixels)
        {
            return device.CreateTexture2D(desc, new SubresourceData(p, (uint)(w * 4), 0));
        }
    }

    private sealed record StatusBox(InputStatus Status, DrawSource Draw, ID3D11Texture2D? Texture, ID3D11ShaderResourceView? View);
}
