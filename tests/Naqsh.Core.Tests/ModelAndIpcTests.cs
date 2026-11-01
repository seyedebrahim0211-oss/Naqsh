using System.IO.Pipes;
using Naqsh.Core.Formats;
using Naqsh.Core.Ipc;
using Naqsh.Core.Model;
using Naqsh.Core.Settings;

namespace Naqsh.Core.Tests;

public class ProjectSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesInputs()
    {
        var doc = new ProjectDocument
        {
            Format = VideoFormat.Hd1080p5994,
            Inputs =
            [
                new InputDescriptor { Kind = InputKind.Color, Name = "رنگ لاجوردی", Color = RgbaColor.FromBytes(0x26, 0x4F, 0x9C) },
                new InputDescriptor { Kind = InputKind.Video, Name = "Clip", Path = "media/خبر.mp4" },
            ],
        };
        var json = ProjectSerializer.Serialize(doc);
        var back = ProjectSerializer.Deserialize(json, out var migrated);
        Assert.Null(migrated);
        Assert.Equal(doc.Format, back.Format);
        Assert.Equal(2, back.Inputs.Count);
        Assert.Equal("رنگ لاجوردی", back.Inputs[0].Name);
        Assert.Equal(InputKind.Video, back.Inputs[1].Kind);
        Assert.Equal("media/خبر.mp4", back.Inputs[1].Path);
        Assert.Contains("\"kind\": \"Video\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_FromV0_RenamesFields()
    {
        const string V0 = """{ "fadeMs": 750, "inputs": [] }""";
        var doc = ProjectSerializer.Deserialize(V0, out var migrated);
        Assert.Equal(0, migrated);
        Assert.Equal(750, doc.FadeDurationMs);
        Assert.Equal(ProjectDocument.CurrentSchemaVersion, doc.SchemaVersion);
    }

    [Fact]
    public void NewerSchema_IsRejected()
    {
        Assert.Throws<InvalidDataException>(() => ProjectSerializer.Deserialize("""{ "schemaVersion": 99 }""", out _));
    }

    [Fact]
    public async Task LoadAsync_BacksUpBeforeMigration()
    {
        var dir = Directory.CreateTempSubdirectory("naqsh-نقش-");
        try
        {
            var path = Path.Combine(dir.FullName, "پروژه.naqsh.json");
            await File.WriteAllTextAsync(path, """{ "fadeMs": 500 }""");
            var doc = await ProjectSerializer.LoadAsync(path, CancellationToken.None);
            Assert.Equal(500, doc.FadeDurationMs);
            Assert.True(File.Exists(path + ".v0.bak"));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void MediaPaths_RelativeAndPersian()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "نمایش");
        var full = Path.Combine(baseDir, "ویدیو", "تیتراژ.mov");
        var stored = MediaPaths.ToStored(full, baseDir);
        Assert.Equal(Path.Combine("ویدیو", "تیتراژ.mov"), stored);
        Assert.Equal(full, MediaPaths.Resolve(stored, baseDir));

        var outside = Path.Combine(Path.GetTempPath(), "other", "a.png");
        Assert.Equal(outside, MediaPaths.ToStored(outside, baseDir));
    }

    [Fact]
    public void RgbaColor_Hex()
    {
        Assert.True(RgbaColor.TryParseHex("#E0A93B", out var c));
        Assert.Equal("#E0A93B", c.ToHex());
        Assert.False(RgbaColor.TryParseHex("nope", out _));
    }
}

public class FormatTests
{
    [Fact]
    public void Default_Is1080p50()
    {
        Assert.Equal("1080p50", VideoFormat.Default.ShortName);
        Assert.Equal("1080p59.94", VideoFormat.DefaultFor(BroadcastRegion.Ntsc60).ShortName);
    }

    [Fact]
    public void ProjectFormats_CoverSpecSizesAndRates()
    {
        Assert.Equal(18, VideoFormat.ProjectFormats.Count);
        Assert.Contains(VideoFormat.ProjectFormats, f => f.ShortName == "2160p59.94");
        Assert.Contains(VideoFormat.ProjectFormats, f => f.ShortName == "720p25");
    }

    [Fact]
    public void Validate_RejectsOddSizes()
    {
        Assert.Throws<InvalidOperationException>(() => new VideoFormat(1919, 1080, Timing.FrameRate.Fps50).Validate());
    }
}

public class SettingsTests
{
    [Fact]
    public void Store_RoundTrips_AndRecoversFromCorruption()
    {
        var dir = Directory.CreateTempSubdirectory("naqsh-settings-");
        try
        {
            var store = new SettingsStore(dir.FullName);
            Assert.False(store.Load().FirstRunCompleted);
            Assert.Equal(AppSettings.Persian, store.Load().Language);
            store.Save(new AppSettings { FirstRunCompleted = true, Language = AppSettings.English, Digits = DigitStyle.Persian });
            var s = store.Load();
            Assert.True(s.FirstRunCompleted);
            Assert.Equal(AppSettings.English, s.Language);
            Assert.Equal(DigitStyle.Persian, s.Digits);

            File.WriteAllText(store.FilePath, "{ broken");
            Assert.False(store.Load().FirstRunCompleted);
            Assert.True(File.Exists(store.FilePath + ".corrupt"));

            File.WriteAllText(store.FilePath, """{ "language": "de-DE" }""");
            Assert.Equal(AppSettings.Persian, store.Load().Language);
        }
        finally
        {
            dir.Delete(true);
        }
    }
}

public class IpcTests
{
    [Fact]
    public void Messages_RoundTrip_Polymorphically()
    {
        IpcMessage[] messages =
        [
            new HelloRequest(IpcProtocol.Version, 42, "C:\\برنامه\\Naqsh.exe"),
            new AddInputRequest(new InputDescriptor { Kind = InputKind.Image, Path = "تصویر.png", Name = "لوگو" }),
            new FadeRequest(1500),
            new CutRequest(),
            new StateEvent(VideoFormat.Default, [], Guid.NewGuid(), Guid.Empty, false, new RecordingState(true, "a.mkv", DateTimeOffset.UnixEpoch, "NVENC")),
            new StatsEvent(50, 100, 0, 1.2, 3.4, 0, "10:00:00:00"),
            new ErrorEvent("NoEncoder", null),
        ];
        foreach (var m in messages)
        {
            var bytes = MessageChannel.Encode(m);
            var back = MessageChannel.Decode(bytes.AsSpan(4));
            Assert.Equal(m.GetType(), back.GetType());
        }

        var add = (AddInputRequest)MessageChannel.Decode(MessageChannel.Encode(messages[1]).AsSpan(4));
        Assert.Equal("لوگو", add.Input.Name);
        Assert.Equal("تصویر.png", add.Input.Path);
    }

    [Fact]
    public async Task NamedPipe_Loopback_HelloExchange()
    {
        var name = "naqsh-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, IpcProtocol.PipeBufferBytes, IpcProtocol.PipeBufferBytes);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accept = server.WaitForConnectionAsync();
        await client.ConnectAsync(5000);
        await accept;

        await using var serverChannel = new MessageChannel(server);
        await using var clientChannel = new MessageChannel(client);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await clientChannel.SendAsync(new HelloRequest(IpcProtocol.Version, 1, null), cts.Token);
        var hello = await serverChannel.ReceiveAsync(cts.Token);
        Assert.IsType<HelloRequest>(hello);
        await serverChannel.SendAsync(new HelloResponse(1, "WARP", 0, true, "p", "g", 960, 540), cts.Token);
        var resp = Assert.IsType<HelloResponse>(await clientChannel.ReceiveAsync(cts.Token));
        Assert.Equal(960, resp.MonitorWidth);

        // Closing one side ends the other side's receive with null (clean disconnect detection).
        await client.DisposeAsync();
        Assert.Null(await serverChannel.ReceiveAsync(cts.Token));
    }

    [Fact]
    public void Decode_RejectsGarbage()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => MessageChannel.Decode("{\"$type\":\"nope\"}"u8));
    }
}
