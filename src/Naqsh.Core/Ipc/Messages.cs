using System.Text.Json.Serialization;
using Naqsh.Core.Formats;
using Naqsh.Core.Model;

namespace Naqsh.Core.Ipc;

/// <summary>Base of every UI ↔ engine message. Serialized as JSON with a "$type" discriminator.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(HelloRequest), "hello")]
[JsonDerivedType(typeof(GoodbyeRequest), "goodbye")]
[JsonDerivedType(typeof(AddInputRequest), "addInput")]
[JsonDerivedType(typeof(RemoveInputRequest), "removeInput")]
[JsonDerivedType(typeof(SetPreviewRequest), "setPreview")]
[JsonDerivedType(typeof(CutRequest), "cut")]
[JsonDerivedType(typeof(FadeRequest), "fade")]
[JsonDerivedType(typeof(DirectRequest), "direct")]
[JsonDerivedType(typeof(StartRecordingRequest), "startRecording")]
[JsonDerivedType(typeof(StopRecordingRequest), "stopRecording")]
[JsonDerivedType(typeof(SetFormatRequest), "setFormat")]
[JsonDerivedType(typeof(HelloResponse), "helloResponse")]
[JsonDerivedType(typeof(StateEvent), "state")]
[JsonDerivedType(typeof(StatsEvent), "stats")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
public abstract record IpcMessage;

// ---- UI → engine ----------------------------------------------------------------------------

public sealed record HelloRequest(int ProtocolVersion, int UiProcessId, string? UiExecutablePath) : IpcMessage;

/// <summary>Clean UI exit. With <paramref name="ShutdownEngine"/> the engine stops; otherwise it keeps running.</summary>
public sealed record GoodbyeRequest(bool ShutdownEngine) : IpcMessage;

public sealed record AddInputRequest(InputDescriptor Input) : IpcMessage;

public sealed record RemoveInputRequest(Guid InputId) : IpcMessage;

public sealed record SetPreviewRequest(Guid InputId) : IpcMessage;

public sealed record CutRequest : IpcMessage;

public sealed record FadeRequest(int DurationMs) : IpcMessage;

public sealed record DirectRequest(Guid InputId) : IpcMessage;

public sealed record StartRecordingRequest(string Folder) : IpcMessage;

public sealed record StopRecordingRequest : IpcMessage;

/// <summary>Changes the project format. Refused while on air.</summary>
public sealed record SetFormatRequest(VideoFormat Format) : IpcMessage;

// ---- engine → UI ----------------------------------------------------------------------------

public sealed record HelloResponse(
    int ProtocolVersion,
    string AdapterName,
    long AdapterLuid,
    bool IsWarp,
    string PreviewSharedName,
    string ProgramSharedName,
    int MonitorWidth,
    int MonitorHeight) : IpcMessage;

[JsonConverter(typeof(JsonStringEnumConverter<InputStatus>))]
public enum InputStatus
{
    Loading = 0,
    Ready = 1,

    /// <summary>File missing or unreadable; a slate is shown instead.</summary>
    Offline = 2,

    /// <summary>A required component (e.g. FFmpeg) is not installed.</summary>
    ComponentMissing = 3,
}

public sealed record InputState(InputDescriptor Input, InputStatus Status, double? DurationSeconds);

public sealed record RecordingState(bool Active, string? FilePath, DateTimeOffset? StartedUtc, string? Encoder);

public sealed record StateEvent(
    VideoFormat Format,
    IReadOnlyList<InputState> Inputs,
    Guid Preview,
    Guid Program,
    bool InTransition,
    RecordingState Recording) : IpcMessage;

public sealed record StatsEvent(
    double Fps,
    long RenderedFrames,
    long DroppedFrames,
    double LastFrameMs,
    double MaxFrameMs,
    long RecordingDroppedFrames,
    string Timecode) : IpcMessage;

/// <summary>An error for the operator. <paramref name="Code"/> is a resource key suffix (UI localizes it); Detail is English.</summary>
public sealed record ErrorEvent(string Code, string? Detail) : IpcMessage;

public static class IpcProtocol
{
    public const int Version = 1;

    /// <summary>Pipe name is per Windows session so two users never share an engine.</summary>
    public static string PipeName => $"Naqsh.Engine.v{Version}.{Environment.UserName}";

    public const int MaxMessageBytes = 4 * 1024 * 1024;
}
