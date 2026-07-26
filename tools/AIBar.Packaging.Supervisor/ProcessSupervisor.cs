using System.Diagnostics;

namespace AIBar.Packaging.Supervisor;

public interface IProcessSupervisorInterop
{
    SafeJobHandle? CreateJob();
    bool ConfigureJob(SafeJobHandle job);
    SafeCompletionPortHandle? CreateCompletionPort();
    bool AssociateCompletionPort(SafeJobHandle job, SafeCompletionPortHandle port);
    SupervisorPipes? CreatePipes();
    SupervisorProcess? CreateSuspended(ProcessLaunchRequest request, SupervisorPipes pipes, IReadOnlyList<SafePipeHandle> inheritedHandles);
    bool AssignProcessToJob(SafeJobHandle job, SafeProcessHandle process);
    bool ResumeThread(SafeThreadHandle thread);
    void TerminateProcess(SafeProcessHandle process);
    bool TerminateJob(SafeJobHandle job);
    bool TryGetExitCode(SafeProcessHandle process, out int exitCode);
    bool IsProcessSignaled(SafeProcessHandle process);
    bool TryGetActiveProcesses(SafeJobHandle job, out uint activeProcesses);
    bool ObserveCompletionPacket(SafeCompletionPortHandle port);
    Stream OpenReadPipe(SafePipeHandle pipe);
}

public sealed record ProcessLaunchRequest(string ApplicationName, string CommandLine);
public sealed record SupervisorProcess(SafeProcessHandle Process, SafeThreadHandle Thread);
public sealed record SupervisorPipes(SafePipeHandle ParentStdin, SafePipeHandle ParentStdout, SafePipeHandle ParentStderr, SafePipeHandle ChildStdin, SafePipeHandle ChildStdout, SafePipeHandle ChildStderr) : IDisposable
{
    public void Dispose() { ParentStdin.Dispose(); ParentStdout.Dispose(); ParentStderr.Dispose(); ChildStdin.Dispose(); ChildStdout.Dispose(); ChildStderr.Dispose(); }
}

public sealed class ProcessLaunch(SafeJobHandle job, SafeCompletionPortHandle port, SupervisorPipes pipes, SupervisorProcess process, IProcessSupervisorInterop interop) : IDisposable
{
    public SafeProcessHandle RootProcess => process.Process;
    public bool TryGetRootExitCode(out int exitCode) => interop.TryGetExitCode(process.Process, out exitCode);
    internal SafeJobHandle Job => job;
    internal SafeCompletionPortHandle Port => port;
    internal SupervisorPipes Pipes => pipes;
    public void Dispose() { process.Thread.Dispose(); process.Process.Dispose(); pipes.Dispose(); port.Dispose(); job.Dispose(); }
}

public sealed record ProcessLaunchResult(SupervisorStatus Status, ProcessLaunch? Launch);
public sealed record CompletionOptions(TimeSpan Deadline, TimeSpan ObservationInterval, TimeSpan EofGrace);
public sealed record CompletionObservation(SupervisorStatus Status, bool Succeeded, bool RootExited, int? ExitCode, bool Quiescent, bool EofCompleted, byte[] StdoutTail, long StdoutDiscardedBytes, byte[] StderrTail, long StderrDiscardedBytes);

public sealed class ProcessSupervisor(IProcessSupervisorInterop interop, TimeProvider? timeProvider = null) : IDisposable
{
    private readonly IProcessSupervisorInterop _interop = interop;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private ProcessLaunch? _launch;

    public ProcessLaunchResult Launch(ProcessLaunchRequest request)
    {
        SafeJobHandle? job = null;
        SafeCompletionPortHandle? port = null;
        SupervisorPipes? pipes = null;
        SupervisorProcess? process = null;
        try
        {
        job = _interop.CreateJob();
        if (job is null) return new(SupervisorStatus.JobCreateFailed, null);
        if (job.IsInvalid) { job.Dispose(); return new(SupervisorStatus.JobCreateFailed, null); }
        if (!_interop.ConfigureJob(job)) return Fail(SupervisorStatus.JobConfigFailed, job);
        port = _interop.CreateCompletionPort();
        if (port is null) return Fail(SupervisorStatus.JobConfigFailed, job);
        if (port.IsInvalid) { port.Dispose(); return Fail(SupervisorStatus.JobConfigFailed, job); }
        if (!_interop.AssociateCompletionPort(job, port)) return Fail(SupervisorStatus.JobConfigFailed, job, port);
        pipes = _interop.CreatePipes();
        if (pipes is null) return Fail(SupervisorStatus.ProcessStartFailed, job, port);
        process = _interop.CreateSuspended(request, pipes, [pipes.ChildStdin, pipes.ChildStdout, pipes.ChildStderr]);
        if (process is null) return Fail(SupervisorStatus.ProcessStartFailed, job, port, pipes);
        pipes.ChildStdin.Dispose(); pipes.ChildStdout.Dispose(); pipes.ChildStderr.Dispose();
        if (!_interop.AssignProcessToJob(job, process.Process))
        {
            _interop.TerminateProcess(process.Process); return Fail(SupervisorStatus.JobAssignFailed, job, port, pipes, process);
        }
        if (!_interop.ResumeThread(process.Thread)) { _interop.TerminateProcess(process.Process); return Fail(SupervisorStatus.ProcessResumeFailed, job, port, pipes, process); }
        _launch = new(job, port, pipes, process, _interop);
        return new(SupervisorStatus.InternalUnknown, _launch);
        }
        catch { return Fail(SupervisorStatus.ProcessStartFailed, job, port, pipes, process); }
    }

    private static ProcessLaunchResult Fail(SupervisorStatus status, SafeJobHandle? job, SafeCompletionPortHandle? port = null, SupervisorPipes? pipes = null, SupervisorProcess? process = null)
    { process?.Thread.Dispose(); process?.Process.Dispose(); pipes?.Dispose(); port?.Dispose(); job?.Dispose(); return new(status, null); }

    public async Task<CompletionObservation> ObserveAsync(ProcessLaunch launch, CompletionOptions options, CancellationToken cancellationToken = default)
    {
        var stdout = new BoundedDiagnosticTail(64 * 1024); var stderr = new BoundedDiagnosticTail(64 * 1024);
        using var drainCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Stream? stdoutStream = null; Stream? stderrStream = null; Task drains = Task.CompletedTask;
        var started = _timeProvider.GetTimestamp(); var deadline = options.Deadline < TimeSpan.Zero ? TimeSpan.Zero : options.Deadline;
        var rootExited = false; int? exitCode = null; var quiescent = false; SupervisorStatus? status = null;
        try
        {
            try
            {
                stdoutStream = _interop.OpenReadPipe(launch.Pipes.ParentStdout); var stdoutDrain = DrainAsync(stdoutStream, stdout, drainCancellation.Token);
                stderrStream = _interop.OpenReadPipe(launch.Pipes.ParentStderr); var stderrDrain = DrainAsync(stderrStream, stderr, drainCancellation.Token);
                drains = Task.WhenAll(stdoutDrain, stderrDrain);
                if (drains.IsFaulted) throw new InvalidOperationException();
            }
            catch { status = SupervisorStatus.OutputDrainFailed; }
            while (_timeProvider.GetElapsedTime(started) <= deadline && !quiescent && status is null)
            {
                if (cancellationToken.IsCancellationRequested) { status = SupervisorStatus.Cancelled; break; }
                _ = _interop.ObserveCompletionPacket(launch.Port);
                if (_interop.IsProcessSignaled(launch.RootProcess))
                {
                    rootExited = true;
                    if (exitCode is null)
                    {
                        if (!_interop.TryGetExitCode(launch.RootProcess, out var code)) { status = SupervisorStatus.QuiescenceUnproved; break; }
                        exitCode = code;
                    }
                    if (!_interop.TryGetActiveProcesses(launch.Job, out var activeProcesses)) { status = SupervisorStatus.QuiescenceUnproved; break; }
                    quiescent = activeProcesses == 0 && await ConfirmZeroAsync(launch, options.ObservationInterval, started, deadline).ConfigureAwait(false);
                }
                if (!quiescent && options.ObservationInterval > TimeSpan.Zero) await Task.Delay(options.ObservationInterval, cancellationToken).ConfigureAwait(false);
                else if (!quiescent) await Task.Yield();
            }
            if (status is null && !quiescent) status = cancellationToken.IsCancellationRequested ? SupervisorStatus.Cancelled : SupervisorStatus.Timeout;
        }
        catch (OperationCanceledException) { status = SupervisorStatus.Cancelled; }
        catch { status = SupervisorStatus.QuiescenceUnproved; }
        if (status is not null)
        {
            try { if (!_interop.TerminateJob(launch.Job)) status = SupervisorStatus.QuiescenceUnproved; else quiescent = await ProveQuiescenceAsync(launch, options.ObservationInterval).ConfigureAwait(false); }
            catch { status = SupervisorStatus.QuiescenceUnproved; }
            if (!quiescent) status = SupervisorStatus.QuiescenceUnproved;
            try { rootExited |= _interop.IsProcessSignaled(launch.RootProcess); if (exitCode is null && _interop.TryGetExitCode(launch.RootProcess, out var code)) exitCode = code; } catch { status = SupervisorStatus.QuiescenceUnproved; }
        }
        var eofCompleted = await FinishDrainsAsync(drains, stdoutStream, stderrStream, drainCancellation, options.EofGrace).ConfigureAwait(false);
        if (status is null && cancellationToken.IsCancellationRequested)
        {
            status = _interop.TerminateJob(launch.Job) ? SupervisorStatus.Cancelled : SupervisorStatus.QuiescenceUnproved;
            quiescent = status == SupervisorStatus.Cancelled && await ProveQuiescenceAsync(launch, options.ObservationInterval).ConfigureAwait(false);
            if (!quiescent) status = SupervisorStatus.QuiescenceUnproved;
        }
        if (status is null && !eofCompleted) status = SupervisorStatus.OutputDrainFailed;
        var terminal = status ?? (rootExited && exitCode == 0 && quiescent ? SupervisorStatus.Success : exitCode is not null ? SupervisorStatus.ProcessFailed : SupervisorStatus.QuiescenceUnproved);
        try { launch.Dispose(); } catch { terminal = SupervisorStatus.QuiescenceUnproved; } finally { if (ReferenceEquals(_launch, launch)) _launch = null; }
        return new(terminal, terminal == SupervisorStatus.Success, rootExited, exitCode, quiescent, eofCompleted, stdout.Bytes, stdout.DiscardedBytes, stderr.Bytes, stderr.DiscardedBytes);
    }

    // Unit 3 invokes the same proof after it has requested Job termination.
    public Task<CompletionObservation> ObserveTerminationQuiescenceAsync(ProcessLaunch launch, CompletionOptions options) => ObserveAsync(launch, options);

    private async Task<bool> ProveQuiescenceAsync(ProcessLaunch launch, TimeSpan interval)
    {
        if (!_interop.TryGetActiveProcesses(launch.Job, out var first) || first != 0) return false;
        if (interval > TimeSpan.Zero) await Task.Delay(interval).ConfigureAwait(false); else await Task.Yield();
        return _interop.TryGetActiveProcesses(launch.Job, out var second) && second == 0;
    }

    private async Task<bool> ConfirmZeroAsync(ProcessLaunch launch, TimeSpan interval, long started, TimeSpan deadline)
    {
        if (interval > TimeSpan.Zero) await Task.Delay(interval).ConfigureAwait(false); else await Task.Yield();
        return _timeProvider.GetElapsedTime(started) <= deadline && _interop.TryGetActiveProcesses(launch.Job, out var second) && second == 0;
    }

    private static async Task DrainAsync(Stream stream, BoundedDiagnosticTail tail, CancellationToken cancellationToken)
    {
        await using (stream.ConfigureAwait(false)) { var buffer = new byte[4096]; while (true) { var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); if (read == 0) return; tail.Append(buffer.AsSpan(0, read)); } }
    }

    private static async Task<bool> FinishDrainsAsync(Task drains, Stream? stdout, Stream? stderr, CancellationTokenSource cancellation, TimeSpan grace)
    {
        try { if (!drains.IsCompleted) await drains.WaitAsync(grace > TimeSpan.Zero ? grace : TimeSpan.Zero, cancellation.Token).ConfigureAwait(false); await drains.ConfigureAwait(false); return true; }
        catch
        {
            cancellation.Cancel(); stdout?.Dispose(); stderr?.Dispose();
            try
            {
                await drains.WaitAsync(grace > TimeSpan.Zero ? grace : TimeSpan.FromMilliseconds(1)).ConfigureAwait(false);
            }
            catch { }
            try { await drains.ConfigureAwait(false); }
            catch { }
            return false;
        }
    }
    public void Dispose() { _launch?.Dispose(); _launch = null; }
}
