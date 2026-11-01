using System.Diagnostics;
using System.Numerics;
using Naqsh.Core.Formats;
using Naqsh.Core.Model;
using Naqsh.Engine.Clock;
using Naqsh.Engine.Gpu;
using Vortice.Direct3D11;

namespace Naqsh.Engine.Tests;

public class PortableEngineTests
{
    [Fact]
    public void FitRect_Letterboxes4x3_Into16x9()
    {
        var r = Compositor.FitRect(new DrawSource(null, 640, 480, false, default), 1920, 1080);
        Assert.Equal(0.75f, r.X, 3);
        Assert.Equal(1f, r.Y, 3);
        Assert.Equal(0.125f, r.Z, 3);
        Assert.Equal(0f, r.W, 3);
    }

    [Fact]
    public void FitRect_Pillarboxes_Wide_And_FillFrame()
    {
        var wide = Compositor.FitRect(new DrawSource(null, 2560, 1080, false, default), 1920, 1080);
        Assert.Equal(1f, wide.X, 3);
        Assert.True(wide.Y < 1f && wide.W > 0f);
        Assert.Equal(new Vector4(1, 1, 0, 0), Compositor.FitRect(new DrawSource(null, 1, 1, true, default), 1920, 1080));
    }

    [Fact]
    public void MasterClock_ConvertsStopwatchTicks()
    {
        Assert.Equal(TimeSpan.TicksPerSecond, MasterClock.ToTicks(Stopwatch.Frequency));
        Assert.Equal(TimeSpan.TicksPerSecond * 3600L * 24 * 30, MasterClock.ToTicks(Stopwatch.Frequency * 3600L * 24 * 30));
        var a = MasterClock.Shared.NowTicks;
        var b = MasterClock.Shared.NowTicks;
        Assert.True(b >= a);
    }
}

[Trait("Category", "Windows")]
public class GpuTests
{
    [SkippableFact]
    public void WarpDevice_Supports_FeatureLevel11()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Direct3D 11 requires Windows");
        using var gpu = GpuTestHelpers.CreateWarp();
        Assert.True(gpu.IsWarp);
        Assert.True(gpu.Device.FeatureLevel >= Vortice.Direct3D.FeatureLevel.Level_11_0);
        Assert.False(gpu.IsRemoved);
    }

    [SkippableFact]
    public void Compositor_FadeMidpoint_IsHalfwayBetweenSources()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Direct3D 11 requires Windows");
        using var gpu = GpuTestHelpers.CreateWarp();
        using var comp = new Compositor(gpu);
        using var fp16 = new RenderTarget(gpu.Device, 64, 36, Vortice.DXGI.Format.R16G16B16A16_Float);
        using var bgra = new RenderTarget(gpu.Device, 64, 36, Vortice.DXGI.Format.B8G8R8A8_UNorm);
        var red = new DrawSource(null, 0, 0, true, new Vector4(1, 0, 0, 1));
        var blue = new DrawSource(null, 0, 0, true, new Vector4(0, 0, 1, 1));

        comp.Mix(fp16, red, blue, 0f);
        comp.Copy(fp16, bgra);
        var p0 = GpuTestHelpers.ReadBgra(gpu, bgra, 10, 10);
        Assert.True(p0.R > 250 && p0.B < 3, $"start {p0}");

        comp.Mix(fp16, red, blue, 0.5f);
        comp.Copy(fp16, bgra);
        var p = GpuTestHelpers.ReadBgra(gpu, bgra, 32, 18);
        Assert.True(GpuTestHelpers.Near(p.R, 128) && GpuTestHelpers.Near(p.B, 128) && p.G < 3, $"mid {p}");

        comp.Mix(fp16, red, blue, 1f);
        comp.Copy(fp16, bgra);
        var p1 = GpuTestHelpers.ReadBgra(gpu, bgra, 60, 30);
        Assert.True(p1.B > 250 && p1.R < 3, $"end {p1}");
    }

    [SkippableFact]
    public unsafe void Compositor_LetterboxesTexture_WithStraightAlphaOverBlack()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Direct3D 11 requires Windows");
        using var gpu = GpuTestHelpers.CreateWarp();
        using var comp = new Compositor(gpu);

        // 2x2 green texture at 50 % alpha, square → pillarboxed in 16:9.
        var pixels = new byte[16];
        for (var i = 0; i < 4; i++)
        {
            pixels[(i * 4) + 1] = 255; // G (BGRA)
            pixels[(i * 4) + 3] = 128; // A
        }

        var desc = new Texture2DDescription(Vortice.DXGI.Format.B8G8R8A8_UNorm, 2, 2, 1, 1, BindFlags.ShaderResource, ResourceUsage.Immutable, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
        ID3D11Texture2D tex;
        fixed (byte* p = pixels)
        {
            tex = gpu.Device.CreateTexture2D(desc, new SubresourceData(p, 8, 0));
        }

        using (tex)
        using (var srv = gpu.Device.CreateShaderResourceView(tex))
        using (var fp16 = new RenderTarget(gpu.Device, 160, 90, Vortice.DXGI.Format.R16G16B16A16_Float))
        using (var bgra = new RenderTarget(gpu.Device, 160, 90, Vortice.DXGI.Format.B8G8R8A8_UNorm))
        {
            var src = new DrawSource(srv, 2, 2, false, DrawSource.BlackSlate);
            comp.Mix(fp16, src, src, 0f);
            comp.Copy(fp16, bgra);
            var centre = GpuTestHelpers.ReadBgra(gpu, bgra, 80, 45);
            Assert.True(GpuTestHelpers.Near(centre.G, 128, 3) && centre.R < 3, $"centre {centre}");
            var bar = GpuTestHelpers.ReadBgra(gpu, bgra, 5, 45);
            Assert.True(bar.G < 3 && bar.R < 3 && bar.B < 3, $"bar {bar}");
        }
    }

    [SkippableFact]
    public void SharedSurface_OpensByName_FromSecondDevice()
    {
        // Simulates the UI process opening the engine's NT-handle shared texture by name.
        Skip.IfNot(OperatingSystem.IsWindows(), "Direct3D 11 requires Windows");
        using var engineGpu = GpuTestHelpers.CreateWarp();
        using var uiGpu = GpuTestHelpers.CreateWarp();
        var name = "NaqshTest_" + Guid.NewGuid().ToString("N");
        using var surface = new SharedSurface(engineGpu.Device, 96, 54, name);
        using var opened = uiGpu.Device.OpenSharedResourceByName<ID3D11Texture2D>(name, SharedResourceFlags.Read | SharedResourceFlags.Write);
        using var mutex = opened.QueryInterface<Vortice.DXGI.IDXGIKeyedMutex>();
        Assert.Equal(96u, opened.Description.Width);

        Assert.True(KeyedMutex.TryAcquire(surface.Mutex, 0, 100));
        Assert.False(KeyedMutex.TryAcquire(mutex, 0, 0), "second acquirer must time out while held");
        KeyedMutex.Release(surface.Mutex, 0);
        Assert.True(KeyedMutex.TryAcquire(mutex, 0, 100));
        KeyedMutex.Release(mutex, 0);
    }
}

[Trait("Category", "Windows")]
public class ProductionEngineTests
{
    private static readonly VideoFormat Small = new(320, 180, Core.Timing.FrameRate.Fps50);

    [SkippableFact]
    public void CutAndFade_ReachProgram_AndNotifySources()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Direct3D 11 requires Windows");
        var factory = new FakeSourceFactory();
        using var engine = new ProductionEngine(GpuTestHelpers.SilentLog, factory, new NoRecorderFactory(), Small, GpuTestHelpers.CreateWarp);
        var a = new InputDescriptor { Kind = InputKind.Color, Color = new RgbaColor(1, 0, 0) };
        var b = new InputDescriptor { Kind = InputKind.Color, Color = new RgbaColor(0, 0, 1) };
        engine.AddInput(a);
        engine.AddInput(b);
        engine.SetPreview(a.Id);
        engine.Cut();
        long f = 0;
        engine.RenderFrame(f++, 0);
        Assert.Equal(a.Id, engine.CreateState().Program);

        engine.SetPreview(b.Id);
        engine.Fade(100); // 5 frames at 50p
        for (var i = 0; i < 3; i++)
        {
            engine.RenderFrame(f++, 0);
        }

        Assert.True(engine.CreateState().InTransition);
        for (var i = 0; i < 5; i++)
        {
            engine.RenderFrame(f++, 0);
        }

        var state = engine.CreateState();
        Assert.Equal(b.Id, state.Program);
        Assert.Equal(a.Id, state.Preview);
        Assert.False(state.InTransition);
        Assert.True(factory.Created[0].ProgramChanges >= 2); // on, then off
    }

    [SkippableFact]
    public void Recording_Failure_IsReported_NotThrown()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Direct3D 11 requires Windows");
        using var engine = new ProductionEngine(GpuTestHelpers.SilentLog, new FakeSourceFactory(), new NoRecorderFactory(), Small, GpuTestHelpers.CreateWarp);
        var ex = Assert.Throws<Outputs.RecorderException>(() => engine.StartRecording(Path.GetTempPath()));
        Assert.Equal("NoEncoder", ex.Code);
        Assert.False(engine.IsRecording);
        engine.RenderFrame(0, 0);
    }

    [SkippableFact]
    public void RenderLoopSteps_AreEachAllocationFree()
    {
        // Measures every per-frame step on its own so a failure names the step that allocates.
        Skip.IfNot(OperatingSystem.IsWindows(), "Direct3D 11 requires Windows");
        using var gpu = GpuTestHelpers.CreateWarp();
        using var comp = new Compositor(gpu);
        using var fp16 = new RenderTarget(gpu.Device, 320, 180, Vortice.DXGI.Format.R16G16B16A16_Float);
        using var mon = new SharedSurface(gpu.Device, 160, 90, "NaqshAllocTest_" + Guid.NewGuid().ToString("N"));
        var red = new DrawSource(null, 0, 0, true, new Vector4(1, 0, 0, 1));
        var blue = new DrawSource(null, 0, 0, true, new Vector4(0, 0, 1, 1));
        var switcher = new Core.Switching.Switcher();
        var source = new FakeSource(new InputDescriptor { Kind = InputKind.Color });
        var frame = new Sources.FrameContext(0, 0, Small);

        var results = new List<string>();
        var removed = false;
        DrawSource last = default;
        void Measure(string name, Action<int> step)
        {
            for (var i = 0; i < 200; i++)
            {
                step(i); // warm up / JIT
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                step(i);
            }

            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            if (bytes != 0)
            {
                results.Add($"{name}: {bytes} bytes / 1000 calls");
            }
        }

        Measure("Switcher.Tick", i => switcher.Tick(i));
        Measure("Compositor.Mix", _ => comp.Mix(fp16, red, blue, 0.5f));
        Measure("Compositor.Copy", _ => comp.Copy(fp16, mon.Target));
        Measure("KeyedMutex acquire/release", _ =>
        {
            if (KeyedMutex.TryAcquire(mon.Mutex, 0, 0))
            {
                KeyedMutex.Release(mon.Mutex, 0);
            }
        });
        Measure("Context.Flush", _ => gpu.Context.Flush());
        Measure("Device.DeviceRemovedReason", n => removed |= gpu.IsRemoved);
        Measure("IVideoSource.Update + Current", n =>
        {
            source.Update(gpu.Context, in frame);
            last = source.Current;
        });

        Assert.False(removed);
        Assert.Null(last.View);
        Assert.True(results.Count == 0, "Allocating steps:\n" + string.Join('\n', results));
    }

    [SkippableFact]
    public void SteadyStateRenderLoop_Allocates_ZeroBytes_Over1000Frames()
    {
        // docs/QA.md: steady-state render loop allocates 0 bytes over 1,000 ticks.
        Skip.IfNot(OperatingSystem.IsWindows(), "Direct3D 11 requires Windows");
        using var engine = new ProductionEngine(GpuTestHelpers.SilentLog, new FakeSourceFactory(), new NoRecorderFactory(), Small, GpuTestHelpers.CreateWarp);
        var a = new InputDescriptor { Kind = InputKind.Color, Color = new RgbaColor(1, 0, 0) };
        var b = new InputDescriptor { Kind = InputKind.Color, Color = new RgbaColor(0, 1, 0) };
        engine.AddInput(a);
        engine.AddInput(b);
        engine.SetPreview(a.Id);
        engine.Cut();
        engine.SetPreview(b.Id);
        long f = 0;
        for (var i = 0; i < 300; i++)
        {
            engine.RenderFrame(f++, f * 200_000);
        }

        // A fade is queued before measuring; it runs inside the measured window.
        engine.Fade(2000);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            engine.RenderFrame(f++, f * 200_000);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }
}
