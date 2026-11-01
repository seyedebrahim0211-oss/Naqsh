using Naqsh.Core.Formats;
using Naqsh.Engine.Gpu;
using Vortice.Direct3D11;

namespace Naqsh.Engine.Outputs;

/// <summary>Result of a finished recording.</summary>
public sealed record RecordingResult(string FilePath, bool Remuxed, long FramesWritten, long FramesDropped, string? ErrorCode);

/// <summary>
/// Program recorder (implemented in Naqsh.Outputs). <see cref="SubmitFrame"/> runs on the render
/// thread and must not block: frames are copied to staging textures and encoded on another thread.
/// </summary>
public interface IRecorder : IDisposable
{
    string FilePath { get; }

    string EncoderName { get; }

    DateTimeOffset StartedUtc { get; }

    long DroppedFrames { get; }

    /// <summary>Set when encoding failed; the engine stops the recorder and tells the UI.</summary>
    string? FailureCode { get; }

    void SubmitFrame(ID3D11DeviceContext context, RenderTarget programBgra, long frameIndex);

    /// <summary>Flushes, closes and remuxes to MP4. Never called on the render thread.</summary>
    Task<RecordingResult> StopAsync(CancellationToken ct);
}

public interface IRecorderFactory
{
    /// <summary>Opens an encoder and the output file. Throws <see cref="RecorderException"/> with a resource code.</summary>
    IRecorder Start(GpuDevice gpu, VideoFormat format, string folder);
}

/// <summary>A recording failure with a UI resource code (e.g. "FfmpegMissing", "NoEncoder").</summary>
public sealed class RecorderException : Exception
{
    public RecorderException()
    {
        Code = "Recording";
    }

    public RecorderException(string message)
        : base(message)
    {
        Code = "Recording";
    }

    public RecorderException(string message, Exception inner)
        : base(message, inner)
    {
        Code = "Recording";
    }

    public RecorderException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
