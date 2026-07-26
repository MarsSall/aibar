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
    public void Dispose() { process.Thread.Dispose(); process.Process.Dispose(); pipes.Dispose(); port.Dispose(); job.Dispose(); }
}

public sealed record ProcessLaunchResult(SupervisorStatus Status, ProcessLaunch? Launch);

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
    public void Dispose() { _launch?.Dispose(); _launch = null; }
}
