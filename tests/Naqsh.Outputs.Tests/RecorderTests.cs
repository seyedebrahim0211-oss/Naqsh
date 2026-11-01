using Naqsh.Core.Formats;
using Naqsh.Core.Timing;
using Naqsh.Engine.Gpu;
using Naqsh.Engine.Media;
using Serilog;

namespace Naqsh.Outputs.Tests;

public class EncoderCatalogTests
{
    [Fact]
    public void NoGplEncoder_IsEverUsed()
    {
        // CLAUDE.md rule 9: LGPL only; libx264/libx265 are GPL.
        foreach (var e in EncoderCatalog.H264)
        {
            Assert.DoesNotContain(e.FfmpegName, EncoderCatalog.Forbidden);
        }
    }

    [Fact]
    public void HardwareEncoders_ArePreferred_OpenH264IsLastResort()
    {
        Assert.Equal("h264_nvenc", EncoderCatalog.H264[0].FfmpegName);
        Assert.Equal("libopenh264", EncoderCatalog.H264[^1].FfmpegName);
        Assert.All(EncoderCatalog.H264.Take(EncoderCatalog.H264.Count - 1), e => Assert.True(e.Hardware));
    }

    [Theory]
    [InlineData(1920, 1080, 50, 12_441_600)]
    [InlineData(3840, 2160, 60, 59_719_680)]
    [InlineData(320, 180, 25, 4_000_000)]
    public void Bitrate_ScalesWithFormat(int w, int h, double fps, long expected)
    {
        Assert.Equal(expected, EncoderCatalog.BitrateFor(w, h, fps));
    }

    [Fact]
    public void RecordingName_IsSortableAndAscii()
    {
        Assert.Equal("Naqsh_2026-11-01_09-05-03", EncoderCatalog.RecordingBaseName(new DateTime(2026, 11, 1, 9, 5, 3)));
    }
}

[Trait("Category", "Windows")]
public class RecorderTests
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    [SkippableFact]
    public async Task Record_100Frames_ProducesMp4_InPersianFolder()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Direct3D 11 requires Windows");
        Skip.IfNot(FfmpegRuntime.EnsureLoaded(Log), "LGPL FFmpeg not installed (set NAQSH_FFMPEG_PATH)");
        var dir = Directory.CreateTempSubdirectory("ضبط-نقش-");
        try
        {
            var format = new VideoFormat(640, 360, FrameRate.Fps50);
            using var gpu = GpuDevice.Create(Log, forceWarp: true);
            using var comp = new Compositor(gpu);
            using var fp16 = new RenderTarget(gpu.Device, 640, 360, Vortice.DXGI.Format.R16G16B16A16_Float);
            using var bgra = new RenderTarget(gpu.Device, 640, 360, Vortice.DXGI.Format.B8G8R8A8_UNorm);
            RecordingResultHolder result;
            using (var rec = ProgramRecorder.Start(gpu, format, dir.FullName, Log))
            {
                for (var i = 0; i < 100; i++)
                {
                    var red = new DrawSource(null, 0, 0, true, new System.Numerics.Vector4(i / 100f, 0, 0, 1));
                    comp.Mix(fp16, red, red, 0);
                    comp.Copy(fp16, bgra);
                    rec.SubmitFrame(gpu.Context, bgra, i);
                }

                result = new RecordingResultHolder(await rec.StopAsync(CancellationToken.None));
            }

            Assert.Null(result.Value.ErrorCode);
            Assert.True(result.Value.Remuxed);
            Assert.EndsWith(".mp4", result.Value.FilePath, StringComparison.Ordinal);
            Assert.True(File.Exists(result.Value.FilePath));
            Assert.True(result.Value.FramesWritten >= 95, $"frames {result.Value.FramesWritten}");
            Assert.False(File.Exists(Path.ChangeExtension(result.Value.FilePath, ".mkv")));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    private sealed record RecordingResultHolder(Engine.Outputs.RecordingResult Value);
}
