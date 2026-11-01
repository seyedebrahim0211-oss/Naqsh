using Naqsh.Core.Model;
using Naqsh.Engine.Gpu;
using Naqsh.Engine.Sources;
using Serilog;

namespace Naqsh.Inputs;

/// <summary>Creates the right <see cref="IVideoSource"/> for an input descriptor.</summary>
public sealed class InputSourceFactory : IVideoSourceFactory
{
    private readonly ILogger _log;

    public InputSourceFactory(ILogger log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public IVideoSource Create(InputDescriptor descriptor, GpuDevice gpu, string? projectFolder)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(gpu);
        return descriptor.Kind switch
        {
            InputKind.Color => new ColorSource(descriptor),
            InputKind.Image => new ImageSource(descriptor, gpu, ResolvePath(descriptor, projectFolder), _log),
            InputKind.Video => new VideoFileSource(descriptor, gpu, ResolvePath(descriptor, projectFolder), _log),
            _ => throw new NotSupportedException($"Input kind {descriptor.Kind} is not supported yet."),
        };
    }

    private static string ResolvePath(InputDescriptor d, string? projectFolder)
    {
        if (string.IsNullOrWhiteSpace(d.Path))
        {
            throw new ArgumentException("File input without a path.", nameof(d));
        }

        return MediaPaths.Resolve(d.Path, projectFolder);
    }
}
