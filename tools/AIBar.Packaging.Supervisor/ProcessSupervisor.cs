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
public sealed record CompletionObservation(bool Succeeded, bool RootExited, int? ExitCode, bool Quiescent, bool EofCompleted, byte[] StdoutTail, long StdoutDiscardedBytes, byte[] StderrTail, long StderrDiscardedBytes);

public sealed class ProcessSupervisor(IProcessSupervisorInterop interop) : IDisposable
{
    private readonly IProcessSupervisorInterop _interop = interop;
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

    public async Task<CompletionObservation> ObserveAsync(ProcessLaunch launch, CompletionOptions options)
    {
        var stdout = new BoundedDiagnosticTail(64 * 1024); var stderr = new BoundedDiagnosticTail(64 * 1024);
        var stdoutDrain = DrainAsync(_interop.OpenReadPipe(launch.Pipes.ParentStdout), stdout);
        var stderrDrain = DrainAsync(_interop.OpenReadPipe(launch.Pipes.ParentStderr), stderr);
        var started = Stopwatch.GetTimestamp(); var deadline = options.Deadline < TimeSpan.Zero ? TimeSpan.Zero : options.Deadline;
        var rootExited = false; int? exitCode = null; var consecutiveZeroes = 0; var quiescent = false;
        while (Stopwatch.GetElapsedTime(started) <= deadline && !quiescent)
        {
            _ = _interop.ObserveCompletionPacket(launch.Port);
            if (_interop.IsProcessSignaled(launch.RootProcess))
            {
                rootExited = true;
                if (exitCode is null)
                {
                    if (!_interop.TryGetExitCode(launch.RootProcess, out var code)) break;
                    exitCode = code;
                }
                if (!_interop.TryGetActiveProcesses(launch.Job, out var activeProcesses)) break;
                consecutiveZeroes = activeProcesses == 0 ? consecutiveZeroes + 1 : 0;
                quiescent = consecutiveZeroes == 2;
            }
            if (!quiescent && options.ObservationInterval > TimeSpan.Zero) await Task.Delay(options.ObservationInterval).ConfigureAwait(false);
            else if (!quiescent) await Task.Yield();
        }
        var drains = Task.WhenAll(stdoutDrain, stderrDrain);
        var eofCompleted = await WaitForEofAsync(drains, options.EofGrace).ConfigureAwait(false);
        return new(rootExited && exitCode == 0 && quiescent && eofCompleted, rootExited, exitCode, quiescent, eofCompleted, stdout.Bytes, stdout.DiscardedBytes, stderr.Bytes, stderr.DiscardedBytes);
    }

    // Unit 3 invokes the same proof after it has requested Job termination.
    public Task<CompletionObservation> ObserveTerminationQuiescenceAsync(ProcessLaunch launch, CompletionOptions options) => ObserveAsync(launch, options);

    private static async Task DrainAsync(Stream stream, BoundedDiagnosticTail tail)
    {
        await using (stream.ConfigureAwait(false))
        {
            var buffer = new byte[4096];
            while (true) { var read = await stream.ReadAsync(buffer).ConfigureAwait(false); if (read == 0) return; tail.Append(buffer.AsSpan(0, read)); }
        }
    }

    private static async Task<bool> WaitForEofAsync(Task drains, TimeSpan grace)
    {
        if (drains.IsCompleted) { await drains.ConfigureAwait(false); return true; }
        if (grace <= TimeSpan.Zero) return false;
        try { await drains.WaitAsync(grace).ConfigureAwait(false); return true; } catch (Exception) { return false; }
    }
    public void Dispose() { _launch?.Dispose(); _launch = null; }
}
