using System.Numerics;
using Naqsh.Core.Ipc;
using Naqsh.Core.Model;
using Naqsh.Engine.Gpu;
using Naqsh.Engine.Sources;
using Serilog;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Naqsh.Engine.Tests;

internal static class GpuTestHelpers
{
    public static ILogger SilentLog { get; } = new LoggerConfiguration().CreateLogger();

    public static GpuDevice CreateWarp() => GpuDevice.Create(SilentLog, forceWarp: true);

    /// <summary>Reads one BGRA8 pixel as (R, G, B, A) bytes.</summary>
    public static unsafe (byte R, byte G, byte B, byte A) ReadBgra(GpuDevice gpu, RenderTarget rt, int x, int y)
    {
        var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)rt.Width, (uint)rt.Height, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, 1, 0, ResourceOptionFlags.None);
        using var staging = gpu.Device.CreateTexture2D(desc);
        gpu.Context.CopyResource(staging, rt.Texture);
        gpu.Context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped).CheckError();
        try
        {
            var p = (byte*)mapped.DataPointer + (y * mapped.RowPitch) + (x * 4);
            return (p[2], p[1], p[0], p[3]);
        }
        finally
        {
            gpu.Context.Unmap(staging, 0);
        }
    }

    public static bool Near(byte actual, int expected, int tolerance = 2) => Math.Abs(actual - expected) <= tolerance;
}

/// <summary>A source showing a flat colour (no texture), for engine tests.</summary>
internal sealed class FakeSource : IVideoSource
{
    public FakeSource(InputDescriptor d)
    {
        Descriptor = d;
        Current = new DrawSource(null, 0, 0, true, new Vector4(d.Color.R, d.Color.G, d.Color.B, 1));
    }

    public Guid Id => Descriptor.Id;

    public InputDescriptor Descriptor { get; }

    public InputStatus Status => InputStatus.Ready;

    public double? DurationSeconds => null;

    public DrawSource Current { get; }

    public int ProgramChanges { get; private set; }

    public void Update(ID3D11DeviceContext context, in FrameContext frame)
    {
    }

    public void OnProgramChanged(bool onProgram, long clockTicks) => ProgramChanges++;

    public void Dispose()
    {
    }
}

internal sealed class FakeSourceFactory : IVideoSourceFactory
{
    public List<FakeSource> Created { get; } = [];

    public IVideoSource Create(InputDescriptor descriptor, GpuDevice gpu, string? projectFolder)
    {
        var s = new FakeSource(descriptor);
        Created.Add(s);
        return s;
    }
}

internal sealed class NoRecorderFactory : Outputs.IRecorderFactory
{
    public Outputs.IRecorder Start(GpuDevice gpu, Core.Formats.VideoFormat format, string folder) =>
        throw new Outputs.RecorderException("NoEncoder", "test");
}
