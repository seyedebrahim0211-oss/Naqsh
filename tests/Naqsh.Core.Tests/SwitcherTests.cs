using Naqsh.Core.Collections;
using Naqsh.Core.Switching;
using Naqsh.Core.Timing;

namespace Naqsh.Core.Tests;

public class SwitcherTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    [Fact]
    public void Cut_SwapsPreviewAndProgram()
    {
        var s = new Switcher();
        s.SetPreview(A);
        s.RequestCut();
        var mix = s.Tick(0);
        Assert.Equal(A, s.Program);
        Assert.Equal(Guid.Empty, s.Preview);
        Assert.Equal(A, mix.A);
        Assert.False(mix.IsMixing);

        s.SetPreview(B);
        s.RequestCut();
        s.Tick(1);
        Assert.Equal(B, s.Program);
        Assert.Equal(A, s.Preview);
    }

    [Fact]
    public void Fade_ProgressesLinearly_AndCompletes()
    {
        var s = new Switcher();
        s.SetPreview(A);
        s.RequestCut();
        s.Tick(0);
        s.SetPreview(B);
        s.RequestFade(4);

        var m1 = s.Tick(1);
        Assert.Equal(A, m1.A);
        Assert.Equal(B, m1.B);
        Assert.Equal(0.25f, m1.Mix, 3);
        Assert.Equal(0.5f, s.Tick(2).Mix, 3);
        Assert.Equal(0.75f, s.Tick(3).Mix, 3);
        Assert.True(s.InTransition);

        var done = s.Tick(4);
        Assert.False(s.InTransition);
        Assert.Equal(B, s.Program);
        Assert.Equal(A, s.Preview);
        Assert.Equal(B, done.A);
        Assert.Equal(0f, done.Mix);
    }

    [Fact]
    public void Transition_RequestedDuringAnother_IsQueued()
    {
        var s = new Switcher();
        s.SetPreview(A);
        s.RequestCut();
        s.Tick(0);
        s.SetPreview(B);
        s.RequestFade(10);
        s.Tick(1);

        // A cut requested mid-fade must not interrupt it.
        s.RequestCut();
        for (var f = 2; f < 10; f++)
        {
            var m = s.Tick(f);
            Assert.True(m.IsMixing);
        }

        s.Tick(10); // fade completes: Program = B, Preview = A
        Assert.Equal(B, s.Program);
        s.Tick(11); // queued cut runs: Program = A
        Assert.Equal(A, s.Program);
        Assert.Equal(0, s.QueuedCount);
    }

    [Fact]
    public void Direct_TakesInputWithoutTouchingPreview()
    {
        var s = new Switcher();
        s.SetPreview(A);
        s.RequestDirect(C);
        s.Tick(0);
        Assert.Equal(C, s.Program);
        Assert.Equal(A, s.Preview);
    }

    [Fact]
    public void Clear_RemovesDeletedInput()
    {
        var s = new Switcher();
        s.SetPreview(A);
        s.RequestCut();
        s.Tick(0);
        s.Clear(A);
        Assert.Equal(Guid.Empty, s.Program);
    }

    [Fact]
    public void QueueFull_RejectsRequests()
    {
        var s = new Switcher();
        Assert.True(s.RequestCut());
        Assert.True(s.RequestCut());
        Assert.True(s.RequestCut());
        Assert.True(s.RequestCut());
        Assert.False(s.RequestCut());
    }

    [Fact]
    public void SteadyStateLoop_AllocatesNothing()
    {
        // CLAUDE.md rule 4: the per-frame path (scheduler + switcher) must not allocate.
        var s = new Switcher();
        var sched = new FrameScheduler(FrameRate.Fps50, 0);
        s.SetPreview(A);
        s.RequestCut();
        s.Tick(0);
        s.SetPreview(B);

        // Warm up (JIT).
        RunFrames(s, ref sched, 100);
        var before = GC.GetAllocatedBytesForCurrentThread();
        RunFrames(s, ref sched, 1000);
        var after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void SpscRing_IsFifo_AndAllocationFree()
    {
        var ring = new SpscRing<long>(4);
        Assert.True(ring.TryEnqueue(1));
        Assert.True(ring.TryEnqueue(2));
        Assert.True(ring.TryDequeue(out var a));
        Assert.Equal(1, a);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            ring.TryEnqueue(i);
            ring.TryDequeue(out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(ring.TryEnqueue(3));
        Assert.True(ring.TryEnqueue(4));
        Assert.True(ring.TryEnqueue(5));
        Assert.False(ring.TryEnqueue(6));
    }

    private static void RunFrames(Switcher s, ref FrameScheduler sched, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var f = sched.BeginFrame(sched.NextDueTicks);
            if (f % 50 == 0)
            {
                s.RequestFade(20);
            }

            _ = s.Tick(f);
            sched.EndFrame();
        }
    }
}
