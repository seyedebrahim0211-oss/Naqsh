using Naqsh.Core.Ipc;
using Naqsh.Core.Model;
using Naqsh.Engine.Gpu;
using Serilog;

namespace Naqsh.Inputs.Tests;

public class ColorSourceTests
{
    [Fact]
    public void ColorSource_IsSolidSlate_PremultipliedOverBlack()
    {
        using var s = new ColorSource(new InputDescriptor { Kind = InputKind.Color, Color = new RgbaColor(1f, 0.5f, 0f, 0.5f) });
        Assert.Equal(InputStatus.Ready, s.Status);
        Assert.Null(s.Current.View);
        Assert.True(s.Current.FillFrame);
        Assert.Equal(0.5f, s.Current.Slate.X, 3);
        Assert.Equal(0.25f, s.Current.Slate.Y, 3);
        Assert.Equal(1f, s.Current.Slate.W, 3);
    }
}

[Trait("Category", "Windows")]
public class FileInputTests
{
    // 2x1 PNG: opaque red, then half-transparent blue.
    private const string TinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8AAQg0AD3oDfnfpf5cAAAAASUVORK5CYII=";
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    [SkippableFact]
    public void ImageDecode_PersianPath_StraightAlphaBgra()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "WIC requires Windows");
        var dir = Directory.CreateTempSubdirectory("نقش-تصویر-");
        try
        {
            var path = Path.Combine(dir.FullName, "لوگو.png");
            File.WriteAllBytes(path, Convert.FromBase64String(TinyPng));
            var px = ImageSource.Decode(path, out var w, out var h);
            Assert.Equal(2, w);
            Assert.Equal(1, h);
            Assert.Equal(new byte[] { 0, 0, 255, 255, 255, 0, 0, 128 }, px); // BGRA, straight alpha
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [SkippableFact]
    public async Task ImageSource_LoadsOnBackgroundThread()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Direct3D 11 requires Windows");
        var dir = Directory.CreateTempSubdirectory("naqsh-img-");
        try
        {
            var path = Path.Combine(dir.FullName, "a.png");
            File.WriteAllBytes(path, Convert.FromBase64String(TinyPng));
            using var gpu = GpuDevice.Create(Log, forceWarp: true);
            using var src = new ImageSource(new InputDescriptor { Kind = InputKind.Image, Path = path }, gpu, path, Log);
            for (var i = 0; i < 100 && src.Status != InputStatus.Ready; i++)
            {
                await Task.Delay(50);
            }

            Assert.Equal(InputStatus.Ready, src.Status);
            Assert.NotNull(src.Current.View);
            Assert.Equal(2, src.Current.Width);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [SkippableFact]
    public async Task MissingFiles_ShowSlate_NeverThrow()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Direct3D 11 requires Windows");
        using var gpu = GpuDevice.Create(Log, forceWarp: true);
        var missing = Path.Combine(Path.GetTempPath(), "naqsh-missing-" + Guid.NewGuid().ToString("N"), "ندارد.png");
        var factory = new InputSourceFactory(Log);
        using var image = factory.Create(new InputDescriptor { Kind = InputKind.Image, Path = missing }, gpu, null);
        using var video = factory.Create(new InputDescriptor { Kind = InputKind.Video, Path = Path.ChangeExtension(missing, ".mp4") }, gpu, null);
        await Task.Delay(500);
        Assert.Equal(InputStatus.Offline, image.Status);
        Assert.Contains(video.Status, new[] { InputStatus.Offline, InputStatus.ComponentMissing });
        Assert.Null(image.Current.View);
        Assert.Equal(DrawSource.OfflineSlate, image.Current.Slate);
        video.Update(gpu.Context, new Engine.Sources.FrameContext(0, 0, Core.Formats.VideoFormat.Default));
        Assert.Null(video.Current.View);
    }
}
