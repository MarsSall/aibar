namespace AIBar.Packaging.Supervisor;

public enum SupervisorState { Created, Validated, JobCreated, WorkerStarted, Assigned, Resumed, RootExited, QuiescenceObserved, Completed }
public enum SupervisorTransition { Validated, JobCreated, WorkerStarted, Assigned, Resumed, RootExited, QuiescenceObserved }
public interface ISupervisorClock { long ElapsedMilliseconds { get; } }
public interface ISupervisorRandom { void Fill(Span<byte> bytes); }
public interface ISupervisorInterop { }

public sealed class SupervisorStateMachine(ISupervisorClock clock, ISupervisorRandom randomness, ISupervisorInterop interop)
{
    private readonly ISupervisorClock _clock = clock;
    private readonly ISupervisorRandom _randomness = randomness;
    private readonly ISupervisorInterop _interop = interop;
    public SupervisorState State { get; private set; } = SupervisorState.Created;
    public SupervisorStatus? TerminalStatus { get; private set; }

    public void Advance(SupervisorTransition transition)
    {
        if (TerminalStatus is not null || (SupervisorState)transition + 1 != State + 1) throw new InvalidOperationException("Invalid supervisor transition.");
        State = (SupervisorState)transition + 1;
    }

    public void Complete(SupervisorStatus status)
    {
        if (TerminalStatus is not null) return;
        if (status == SupervisorStatus.Success && State != SupervisorState.QuiescenceObserved) throw new InvalidOperationException("Success requires quiescence.");
        TerminalStatus = status; State = SupervisorState.Completed;
    }

    public void Fail(SupervisorStatus status) => Complete(status == SupervisorStatus.Success ? SupervisorStatus.InternalUnknown : status);
    public void Cancel() => Complete(SupervisorStatus.Cancelled);
}

public sealed class BoundedDiagnosticTail(int capacity)
{
    private readonly Queue<byte> _bytes = new();
    public long DiscardedBytes { get; private set; }
    public bool Truncated => DiscardedBytes > 0;
    public byte[] Bytes => _bytes.ToArray();

    public void Append(ReadOnlySpan<byte> bytes)
    {
        if (capacity <= 0) { DiscardedBytes += bytes.Length; return; }
        foreach (var value in bytes) { if (_bytes.Count == capacity) { _bytes.Dequeue(); DiscardedBytes++; } _bytes.Enqueue(value); }
    }
}
