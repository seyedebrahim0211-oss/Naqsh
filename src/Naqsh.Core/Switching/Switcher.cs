namespace Naqsh.Core.Switching;

public enum TransitionKind
{
    Cut = 0,
    Fade = 1,
}

/// <summary>What the compositor must draw on Program for one frame: A blended towards B by <see cref="Mix"/>.</summary>
public readonly record struct ProgramMix(Guid A, Guid B, float Mix)
{
    public bool IsMixing => Mix > 0f && Mix < 1f && A != B;
}

/// <summary>
/// Preview/Program switcher state machine. Runs on the render thread only; every method is
/// allocation-free. A transition requested while another is running is queued and starts as soon
/// as the running one completes (docs/SPEC.md §6). After a transition, Preview takes the previous
/// Program source so the operator can switch back.
/// </summary>
public sealed class Switcher
{
    private const int QueueCapacity = 4;
    private readonly TransitionRequest[] _queue = new TransitionRequest[QueueCapacity];
    private int _queueHead;
    private int _queueCount;

    private bool _running;
    private TransitionKind _runningKind;
    private Guid _from;
    private Guid _to;
    private long _startFrame = -1;
    private int _durationFrames;

    public Guid Preview { get; private set; }

    public Guid Program { get; private set; }

    public bool InTransition => _running;

    /// <summary>Transition progress 0..1 (0 when idle).</summary>
    public float Position { get; private set; }

    /// <summary>Increments on every state change the UI must know about (not on progress).</summary>
    public int Version { get; private set; }

    public int QueuedCount => _queueCount;

    public void SetPreview(Guid input)
    {
        if (Preview == input)
        {
            return;
        }

        Preview = input;
        Version++;
    }

    /// <summary>Removes an input from Preview/Program (shows black) — used when an input is deleted.</summary>
    public void Clear(Guid input)
    {
        var changed = false;
        if (Preview == input)
        {
            Preview = Guid.Empty;
            changed = true;
        }

        if (Program == input)
        {
            Program = Guid.Empty;
            changed = true;
        }

        if (_running && (_from == input || _to == input))
        {
            _running = false;
            Position = 0;
            changed = true;
        }

        if (changed)
        {
            Version++;
        }
    }

    /// <summary>Requests a cut from Preview to Program. Returns false if the queue is full.</summary>
    public bool RequestCut() => Enqueue(new TransitionRequest(TransitionKind.Cut, 0));

    /// <summary>Requests a fade of <paramref name="durationFrames"/> frames. Returns false if the queue is full.</summary>
    public bool RequestFade(int durationFrames) =>
        Enqueue(new TransitionRequest(TransitionKind.Fade, Math.Max(1, durationFrames)));

    /// <summary>Puts an input straight on Program with a cut (Preview is unchanged).</summary>
    public bool RequestDirect(Guid input) =>
        Enqueue(new TransitionRequest(TransitionKind.Cut, 0, input, true));

    /// <summary>Advances the state machine to <paramref name="frameIndex"/> and returns the mix to draw.</summary>
    public ProgramMix Tick(long frameIndex)
    {
        if (!_running && _queueCount > 0)
        {
            Start(Dequeue(), frameIndex);
        }

        if (!_running)
        {
            return new ProgramMix(Program, Program, 0f);
        }

        var elapsed = frameIndex - _startFrame + 1;
        if (_runningKind == TransitionKind.Cut || elapsed >= _durationFrames)
        {
            Complete();

            // A queued transition starts on the next frame so the completed one is seen at full.
            return new ProgramMix(Program, Program, 0f);
        }

        Position = (float)elapsed / _durationFrames;
        return new ProgramMix(_from, _to, Position);
    }

    private void Start(TransitionRequest r, long frameIndex)
    {
        _runningKind = r.Kind;
        _from = Program;
        _to = r.HasTarget ? r.Target : Preview;
        _durationFrames = r.DurationFrames;
        _startFrame = frameIndex;
        _running = true;
        _directTarget = r.HasTarget;
        Position = 0;
        Version++;
    }

    private bool _directTarget;

    private void Complete()
    {
        var oldProgram = Program;
        Program = _to;
        if (!_directTarget)
        {
            Preview = oldProgram;
        }

        _running = false;
        Position = 0;
        Version++;
    }

    private bool Enqueue(TransitionRequest r)
    {
        if (_queueCount == QueueCapacity)
        {
            return false;
        }

        _queue[(_queueHead + _queueCount) % QueueCapacity] = r;
        _queueCount++;
        return true;
    }

    private TransitionRequest Dequeue()
    {
        var r = _queue[_queueHead];
        _queueHead = (_queueHead + 1) % QueueCapacity;
        _queueCount--;
        return r;
    }

    private readonly record struct TransitionRequest(
        TransitionKind Kind,
        int DurationFrames,
        Guid Target = default,
        bool HasTarget = false);
}
