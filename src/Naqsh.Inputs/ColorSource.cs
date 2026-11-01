using System.Numerics;
using Naqsh.Core.Ipc;
using Naqsh.Core.Model;
using Naqsh.Engine.Gpu;
using Naqsh.Engine.Sources;
using Vortice.Direct3D11;

namespace Naqsh.Inputs;

/// <summary>Solid colour input. Drawn by the compositor directly (no texture needed).</summary>
public sealed class ColorSource : IVideoSource
{
    private readonly DrawSource _current;

    public ColorSource(InputDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        var c = descriptor.Color;

        // Straight alpha over black, like every other source.
        _current = new DrawSource(null, 0, 0, true, new Vector4(c.R * c.A, c.G * c.A, c.B * c.A, 1f));
    }

    public Guid Id => Descriptor.Id;

    public InputDescriptor Descriptor { get; }

    public InputStatus Status => InputStatus.Ready;

    public double? DurationSeconds => null;

    public DrawSource Current => _current;

    public void Update(ID3D11DeviceContext context, in FrameContext frame)
    {
    }

    public void OnProgramChanged(bool onProgram, long clockTicks)
    {
    }

    public void Dispose()
    {
    }
}
