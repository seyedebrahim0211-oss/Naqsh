using Naqsh.Core.Formats;
using Naqsh.Core.Ipc;
using Naqsh.Core.Model;
using Naqsh.Engine.Gpu;
using Vortice.Direct3D11;

namespace Naqsh.Engine.Sources;

/// <summary>Per-frame timing passed to sources.</summary>
public readonly record struct FrameContext(long FrameIndex, long ClockTicks, VideoFormat Format);

/// <summary>
/// A video input. <see cref="Update"/> runs on the render thread once per frame and must not
/// block or allocate; loading/decoding happens on the source's own threads. A source that fails
/// reports <see cref="InputStatus.Offline"/> and the compositor shows a slate — it never throws
/// into the render loop.
/// </summary>
public interface IVideoSource : IDisposable
{
    Guid Id { get; }

    InputDescriptor Descriptor { get; }

    InputStatus Status { get; }

    /// <summary>Clip length for files, else null.</summary>
    double? DurationSeconds { get; }

    /// <summary>Uploads a new frame if one is due. Render thread only.</summary>
    void Update(ID3D11DeviceContext context, in FrameContext frame);

    /// <summary>What to draw right now.</summary>
    DrawSource Current { get; }

    /// <summary>Called on the render thread when the input goes on or off Program (auto-play).</summary>
    void OnProgramChanged(bool onProgram, long clockTicks);
}

/// <summary>Creates sources for descriptors (implemented in Naqsh.Inputs).</summary>
public interface IVideoSourceFactory
{
    /// <param name="projectFolder">Base folder for relative paths, or null.</param>
    IVideoSource Create(InputDescriptor descriptor, GpuDevice gpu, string? projectFolder);
}
