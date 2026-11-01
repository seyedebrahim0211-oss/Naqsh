using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace Naqsh.Engine.Gpu;

/// <summary>A picture to draw: a shader view plus its size, or a slate colour when it has no picture.</summary>
public readonly record struct DrawSource(ID3D11ShaderResourceView? View, int Width, int Height, bool FillFrame, Vector4 Slate)
{
    public static readonly Vector4 BlackSlate = new(0, 0, 0, 1);

    /// <summary>Night-lapis slate shown for offline inputs (docs/DESIGN.md bg.panel #10203A).</summary>
    public static readonly Vector4 OfflineSlate = new(0x10 / 255f, 0x20 / 255f, 0x3A / 255f, 1);

    public static DrawSource Black => new(null, 0, 0, true, BlackSlate);
}

/// <summary>
/// GPU compositor: A/B mix with aspect-fit into FP16 targets and conversion/scaling into 8-bit
/// targets. All methods are allocation-free and must be called on the render thread.
/// </summary>
public sealed class Compositor : IDisposable
{
    private readonly ID3D11DeviceContext _ctx;
    private readonly ID3D11VertexShader _vs;
    private readonly ID3D11PixelShader _psMix;
    private readonly ID3D11PixelShader _psCopy;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11Buffer _constants;
    private MixConstants _cb;

    public Compositor(GpuDevice gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        var device = gpu.Device;
        _ctx = gpu.Context;
        var vsCode = Compiler.Compile(Shaders.Source, "VSMain", "naqsh.hlsl", "vs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
        var mixCode = Compiler.Compile(Shaders.Source, "PSMix", "naqsh.hlsl", "ps_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
        var copyCode = Compiler.Compile(Shaders.Source, "PSCopy", "naqsh.hlsl", "ps_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
        _vs = device.CreateVertexShader(vsCode.Span);
        _psMix = device.CreatePixelShader(mixCode.Span);
        _psCopy = device.CreatePixelShader(copyCode.Span);
        _sampler = device.CreateSamplerState(SamplerDescription.LinearClamp);
        _constants = device.CreateBuffer(
            (uint)Marshal.SizeOf<MixConstants>(),
            BindFlags.ConstantBuffer,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            ResourceOptionFlags.None,
            0);
    }

    /// <summary>Draws A mixed towards B by <paramref name="mix"/> into <paramref name="target"/>.</summary>
    public void Mix(RenderTarget target, in DrawSource a, in DrawSource b, float mix)
    {
        ArgumentNullException.ThrowIfNull(target);
        _cb.RectA = FitRect(a, target.Width, target.Height);
        _cb.RectB = FitRect(b, target.Width, target.Height);
        _cb.SlateA = a.Slate;
        _cb.SlateB = b.Slate;
        _cb.Mix = Math.Clamp(mix, 0f, 1f);
        _cb.HasA = a.View is null ? 0f : 1f;
        _cb.HasB = b.View is null ? 0f : 1f;
        _ctx.UpdateSubresource(in _cb, _constants, 0, 0, 0, null);

        Begin(target);
        _ctx.PSSetShader(_psMix);
        _ctx.PSSetShaderResource(0, a.View!);
        _ctx.PSSetShaderResource(1, b.View!);
        _ctx.Draw(3, 0);
        _ctx.PSSetShaderResource(0, null!);
        _ctx.PSSetShaderResource(1, null!);
    }

    /// <summary>Scales/converts <paramref name="source"/> into <paramref name="target"/> (e.g. FP16 → BGRA8 monitor).</summary>
    public void Copy(RenderTarget source, RenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Begin(target);
        _ctx.PSSetShader(_psCopy);
        _ctx.PSSetShaderResource(0, source.Srv);
        _ctx.Draw(3, 0);
        _ctx.PSSetShaderResource(0, null!);
    }

    public void Dispose()
    {
        _constants.Dispose();
        _sampler.Dispose();
        _psCopy.Dispose();
        _psMix.Dispose();
        _vs.Dispose();
    }

    /// <summary>Aspect-fit placement of a source in a target as (scale.xy, offset.xy) in UV space.</summary>
    public static Vector4 FitRect(in DrawSource s, int targetWidth, int targetHeight)
    {
        if (s.FillFrame || s.Width <= 0 || s.Height <= 0 || targetWidth <= 0 || targetHeight <= 0)
        {
            return new Vector4(1, 1, 0, 0);
        }

        var targetAspect = (double)targetWidth / targetHeight;
        var sourceAspect = (double)s.Width / s.Height;
        if (sourceAspect > targetAspect)
        {
            var sy = (float)(targetAspect / sourceAspect);
            return new Vector4(1, sy, 0, (1 - sy) / 2);
        }

        var sx = (float)(sourceAspect / targetAspect);
        return new Vector4(sx, 1, (1 - sx) / 2, 0);
    }

    private void Begin(RenderTarget target)
    {
        _ctx.OMSetRenderTargets(target.Rtv, null);
        _ctx.RSSetViewport(0, 0, target.Width, target.Height, 0, 1);
        _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _ctx.VSSetShader(_vs);
        _ctx.PSSetSampler(0, _sampler);
        _ctx.PSSetConstantBuffer(0, _constants);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MixConstants
    {
        public Vector4 RectA;
        public Vector4 RectB;
        public Vector4 SlateA;
        public Vector4 SlateB;
        public float Mix;
        public float HasA;
        public float HasB;
        public float Pad;
    }
}
