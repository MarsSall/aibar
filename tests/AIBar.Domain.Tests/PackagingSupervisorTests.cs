using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AIBar.Packaging.Supervisor;

namespace AIBar.Domain.Tests;

public sealed class PackagingSupervisorTests
{
    [Theory]
    [InlineData("{\"protocolVersion\":2,\"operation\":\"package-recovery\",\"timeoutMilliseconds\":1000,\"arguments\":{\"disableBuildServers\":true}}")]
    [InlineData("{\"protocolVersion\":1,\"operation\":\"unknown\",\"timeoutMilliseconds\":1000,\"arguments\":{\"disableBuildServers\":true}}")]
    [InlineData("{\"protocolVersion\":1,\"operation\":\"package-recovery\",\"timeoutMilliseconds\":0,\"arguments\":{\"disableBuildServers\":true}}")]
    [InlineData("{\"protocolVersion\":1,\"operation\":\"package-recovery\",\"timeoutMilliseconds\":1000,\"arguments\":{\"disableBuildServers\":true},\"pid\":42}")]
    [InlineData("{\"protocolVersion\":1,\"operation\":\"package-recovery\",\"timeoutMilliseconds\":1000,\"arguments\":{\"disableBuildServers\":true,\"root\":\"C:\\\\secret\"}}")]
    public void Closed_protocol_rejects_invalid_or_sensitive_requests(string json)
    {
        var response = SupervisorProtocol.Handle(Encoding.UTF8.GetBytes(json));

        Assert.Equal(SupervisorStatus.InvalidRequest, response.Status);
        Assert.Null(response.ChildExitCode);
        Assert.DoesNotContain("secret", SupervisorProtocol.Serialize(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Protocol_enforces_length_bounds_and_emits_only_closed_response_fields()
    {
        var oversized = Encoding.UTF8.GetBytes(new string('x', SupervisorProtocol.MaximumRequestBytes + 1));
        var response = SupervisorProtocol.Handle(oversized);
        var serialized = SupervisorProtocol.Serialize(response);

        Assert.Equal(SupervisorStatus.InvalidRequest, response.Status);
        Assert.Equal("{\"protocolVersion\":1,\"status\":\"invalid-request\",\"durationMilliseconds\":0,\"stdoutBytes\":0,\"stderrBytes\":0,\"stdoutTruncated\":false,\"stderrTruncated\":false}", serialized);
    }

    [Fact]
    public async Task Protocol_rejects_an_over_limit_stream_before_EOF_without_unbounded_buffering()
    {
        await using var input = new NeverEndingStream(SupervisorProtocol.MaximumRequestBytes + 1);

        var response = await SupervisorProtocol.HandleAsync(input);

        Assert.Equal(SupervisorStatus.InvalidRequest, response.Status);
        Assert.Equal(SupervisorProtocol.MaximumRequestBytes + 1, input.BytesRead);
        Assert.True(input.MaximumRequestedRead <= 1024);
    }

    [Theory]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("1.5")]
    public void Protocol_rejects_invalid_numeric_protocol_versions_without_diagnostics(string protocolVersion)
    {
        var json = $"{{\"protocolVersion\":{protocolVersion},\"operation\":\"package-recovery\",\"timeoutMilliseconds\":1000,\"arguments\":{{\"disableBuildServers\":true}}}}";

        var response = SupervisorProtocol.Handle(Encoding.UTF8.GetBytes(json));
        var serialized = SupervisorProtocol.Serialize(response);

        Assert.Equal(SupervisorStatus.InvalidRequest, response.Status);
        Assert.DoesNotContain("Exception", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(":\\", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Protocol_rejects_duplicate_fields_and_is_deterministic_for_reordered_closed_fields()
    {
        const string reordered = "{\"arguments\":{\"disableBuildServers\":true},\"timeoutMilliseconds\":1000,\"operation\":\"package-recovery\",\"protocolVersion\":1}";
        const string duplicate = "{\"protocolVersion\":1,\"protocolVersion\":1,\"operation\":\"package-recovery\",\"timeoutMilliseconds\":1000,\"arguments\":{\"disableBuildServers\":true}}";

        Assert.Equal(SupervisorProtocol.Serialize(SupervisorProtocol.Handle(Encoding.UTF8.GetBytes(reordered))), SupervisorProtocol.Serialize(SupervisorProtocol.Handle(Encoding.UTF8.GetBytes(reordered))));
        Assert.Equal(SupervisorStatus.InvalidRequest, SupervisorProtocol.Handle(Encoding.UTF8.GetBytes(duplicate)).Status);
    }

    [Fact]
    public void State_machine_accepts_only_the_deterministic_happy_path()
    {
        var machine = new SupervisorStateMachine(new FakeClock(), new FakeRandom(), new FakeInterop());

        foreach (var transition in new[] { SupervisorTransition.Validated, SupervisorTransition.JobCreated, SupervisorTransition.WorkerStarted, SupervisorTransition.Assigned, SupervisorTransition.Resumed, SupervisorTransition.RootExited, SupervisorTransition.QuiescenceObserved })
            machine.Advance(transition);
        machine.Complete(SupervisorStatus.Success);

        Assert.Equal(SupervisorState.Completed, machine.State);
        Assert.Equal(SupervisorStatus.Success, machine.TerminalStatus);
        Assert.Throws<InvalidOperationException>(() => machine.Advance(SupervisorTransition.Validated));
    }

    [Fact]
    public void Cancellation_wins_before_a_terminal_result_but_not_after_one()
    {
        var cancelled = new SupervisorStateMachine(new FakeClock(), new FakeRandom(), new FakeInterop());
        cancelled.Advance(SupervisorTransition.Validated); cancelled.Cancel(); cancelled.Fail(SupervisorStatus.ProcessFailed);
        var failed = new SupervisorStateMachine(new FakeClock(), new FakeRandom(), new FakeInterop());
        failed.Fail(SupervisorStatus.ProcessFailed); failed.Cancel();

        Assert.Equal(SupervisorStatus.Cancelled, cancelled.TerminalStatus);
        Assert.Equal(SupervisorStatus.ProcessFailed, failed.TerminalStatus);
    }

    [Fact]
    public void Reordered_or_duplicate_callbacks_are_rejected_without_changing_state()
    {
        var machine = new SupervisorStateMachine(new FakeClock(), new FakeRandom(), new FakeInterop());

        Assert.Throws<InvalidOperationException>(() => machine.Advance(SupervisorTransition.Resumed));
        machine.Advance(SupervisorTransition.Validated);
        Assert.Throws<InvalidOperationException>(() => machine.Advance(SupervisorTransition.Validated));
        Assert.Equal(SupervisorState.Validated, machine.State);
    }

    [Fact]
    public void Diagnostics_retain_a_bounded_tail_and_count_discarded_bytes()
    {
        var diagnostics = new BoundedDiagnosticTail(4);
        diagnostics.Append(Encoding.UTF8.GetBytes("abcdef"));

        Assert.Equal("cdef", Encoding.UTF8.GetString(diagnostics.Bytes));
        Assert.Equal(2, diagnostics.DiscardedBytes);
        Assert.True(diagnostics.Truncated);
    }

    [Theory]
    [InlineData(LaunchFault.CreateJob, SupervisorStatus.JobCreateFailed, "CreateJob")]
    [InlineData(LaunchFault.ConfigureJob, SupervisorStatus.JobConfigFailed, "CreateJob,ConfigureJob")]
    [InlineData(LaunchFault.CreatePort, SupervisorStatus.JobConfigFailed, "CreateJob,ConfigureJob,CreatePort")]
    [InlineData(LaunchFault.AssociatePort, SupervisorStatus.JobConfigFailed, "CreateJob,ConfigureJob,CreatePort,AssociatePort")]
    [InlineData(LaunchFault.CreatePipes, SupervisorStatus.ProcessStartFailed, "CreateJob,ConfigureJob,CreatePort,AssociatePort,CreatePipes")]
    [InlineData(LaunchFault.CreateProcess, SupervisorStatus.ProcessStartFailed, "CreateJob,ConfigureJob,CreatePort,AssociatePort,CreatePipes,CreateProcess")]
    [InlineData(LaunchFault.CreateProcessException, SupervisorStatus.ProcessStartFailed, "CreateJob,ConfigureJob,CreatePort,AssociatePort,CreatePipes,CreateProcess")]
    [InlineData(LaunchFault.Assign, SupervisorStatus.JobAssignFailed, "CreateJob,ConfigureJob,CreatePort,AssociatePort,CreatePipes,CreateProcess,Assign,TerminateProcess")]
    [InlineData(LaunchFault.Resume, SupervisorStatus.ProcessResumeFailed, "CreateJob,ConfigureJob,CreatePort,AssociatePort,CreatePipes,CreateProcess,Assign,Resume,TerminateProcess")]
    public void Launch_fails_closed_before_or_at_resume_and_closes_every_owned_handle(LaunchFault fault, SupervisorStatus expected, string calls)
    {
        var interop = new FakeLaunchInterop(fault);
        using var supervisor = new ProcessSupervisor(interop);

        var result = supervisor.Launch(new("worker.exe", "worker.exe --internal"));

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Launch);
        Assert.Equal(calls, string.Join(',', interop.Calls));
        Assert.All(interop.Handles, handle => Assert.True(handle.IsClosed));
    }

    [Fact]
    public void Launch_retains_the_root_handle_for_exit_code_retrieval_until_disposal()
    {
        var interop = new FakeLaunchInterop();
        using var supervisor = new ProcessSupervisor(interop);
        var result = supervisor.Launch(new("worker.exe", "worker.exe --internal"));

        Assert.True(result.Launch!.TryGetRootExitCode(out var exitCode));
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Launch_allowlists_only_child_pipe_handles_and_assigns_before_resume()
    {
        var interop = new FakeLaunchInterop();
        using var supervisor = new ProcessSupervisor(interop);

        var result = supervisor.Launch(new("worker.exe", "worker.exe --internal"));

        Assert.Equal(SupervisorStatus.InternalUnknown, result.Status);
        Assert.NotNull(result.Launch);
        Assert.Equal(new[] { "child-stdin", "child-stdout", "child-stderr" }, interop.InheritedHandles);
        Assert.Equal("Assign", interop.Calls[^2]);
        Assert.Equal("Resume", interop.Calls[^1]);
        result.Launch!.Dispose();
        Assert.All(interop.Handles, handle => Assert.True(handle.IsClosed));
    }

    [Fact]
    public void Windows_native_launch_assigns_the_resumed_helper_to_its_job_and_closes_it_on_disposal()
    {
        if (!OperatingSystem.IsWindows()) return;
        var readyName = $"aibar-ready-{Guid.NewGuid():N}";
        var releaseName = $"aibar-release-{Guid.NewGuid():N}";
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName);
        using var release = new EventWaitHandle(false, EventResetMode.ManualReset, releaseName);
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var script = $"$ready = [Threading.EventWaitHandle]::OpenExisting('{readyName}'); $ready.Set(); [Threading.EventWaitHandle]::OpenExisting('{releaseName}').WaitOne()";
        using var supervisor = new ProcessSupervisor(new WindowsProcessSupervisorInterop());

        var result = supervisor.Launch(new(powershell, $"\"{powershell}\" -NoProfile -NonInteractive -Command \"{script}\""));

        var launch = Assert.IsType<ProcessLaunch>(result.Launch);
        Assert.Equal(SupervisorStatus.InternalUnknown, result.Status);
        Assert.True(ready.WaitOne(TimeSpan.FromSeconds(10)));
        Assert.True(IsProcessInJob(launch.RootProcess, IntPtr.Zero, out var inJob));
        Assert.True(inJob);
        var processId = GetProcessId(launch.RootProcess);
        using var helper = Process.GetProcessById((int)processId);
        launch.Dispose();
        Assert.True(launch.RootProcess.IsClosed);
        Assert.True(helper.WaitForExit(10_000));
    }

    [Fact]
    public async Task Windows_quiescence_waits_for_an_event_gated_descendant_after_root_exit()
    {
        if (!OperatingSystem.IsWindows()) return;
        var readyName = $"Local\\aibar-ready-{Guid.NewGuid():N}"; var rootExitedName = $"Local\\aibar-root-exited-{Guid.NewGuid():N}"; var releaseName = $"Local\\aibar-release-{Guid.NewGuid():N}";
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName);
        using var rootExited = new EventWaitHandle(false, EventResetMode.ManualReset, rootExitedName);
        using var release = new EventWaitHandle(false, EventResetMode.ManualReset, releaseName);
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var child = $"[Threading.EventWaitHandle]::OpenExisting('{readyName}').Set(); [Threading.EventWaitHandle]::OpenExisting('{releaseName}').WaitOne()";
        var childCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(child));
        var script = $"$child='{childCommand}'; $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME 'powershell.exe')); $start.UseShellExecute=$false; $start.Arguments=\"-NoProfile -NonInteractive -EncodedCommand $child\"; [void][Diagnostics.Process]::Start($start); [Threading.EventWaitHandle]::OpenExisting('{readyName}').WaitOne(); [Threading.EventWaitHandle]::OpenExisting('{rootExitedName}').Set(); exit";
        var command = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var supervisor = new ProcessSupervisor(new WindowsProcessSupervisorInterop());
        var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new(powershell, $"\"{powershell}\" -NoProfile -NonInteractive -EncodedCommand {command}")).Launch);

        var observation = supervisor.ObserveAsync(launch, new(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(25), TimeSpan.FromSeconds(2)));
        Assert.True(ready.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.True(rootExited.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.False(observation.IsCompleted);
        release.Set();
        Assert.True((await observation).Succeeded);
    }

    [Fact]
    public async Task Completion_requires_root_exit_and_two_separated_authoritative_zero_queries()
    {
        var interop = new FakeLaunchInterop { RootSignaled = true, ExitCode = 0, ActiveProcesses = [1, 0, 0] };
        using var supervisor = new ProcessSupervisor(interop);
        var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new("worker.exe", "worker.exe --internal")).Launch);

        var result = await supervisor.ObserveAsync(launch, new(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), TimeSpan.Zero));

        Assert.True(result.Succeeded);
        Assert.Equal(3, interop.ActiveQueryCount);
        Assert.Equal(1, interop.ExitCodeQueryCount);
        Assert.True(Stopwatch.GetElapsedTime(interop.QueryTimestamps[1], interop.QueryTimestamps[2]) >= TimeSpan.FromMilliseconds(8));
    }

    [Fact]
    public async Task Completion_packets_and_saturated_streams_cannot_decide_quiescence()
    {
        var interop = new FakeLaunchInterop
        {
            RootSignaled = true,
            ExitCode = 0,
            ActiveProcesses = [1, 0, 0],
            Packets = [true, false, true, true],
            Stdout = new MemoryStream(Enumerable.Repeat((byte)'o', 65_537).ToArray()),
            Stderr = new MemoryStream(Enumerable.Repeat((byte)'e', 65_537).ToArray())
        };
        using var supervisor = new ProcessSupervisor(interop);
        var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new("worker.exe", "worker.exe --internal")).Launch);

        var result = await supervisor.ObserveAsync(launch, new(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero));

        Assert.True(result.Succeeded);
        Assert.Equal(64 * 1024, result.StdoutTail.Length);
        Assert.Equal(1, result.StdoutDiscardedBytes);
        Assert.Equal(64 * 1024, result.StderrTail.Length);
        Assert.Equal(1, result.StderrDiscardedBytes);
        Assert.Equal(3, interop.ActiveQueryCount);
    }

    [Fact]
    public async Task Completion_packets_are_advisory_when_lost_duplicated_or_reordered()
    {
        foreach (var packets in new[] { new List<bool>(), new List<bool> { true, true }, new List<bool> { false, true, false } })
        {
            var interop = new FakeLaunchInterop { RootSignaled = true, ExitCode = 0, ActiveProcesses = [1, 0, 0], Packets = packets };
            using var supervisor = new ProcessSupervisor(interop);
            var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new("worker.exe", "worker.exe --internal")).Launch);
            var result = await supervisor.ObserveAsync(launch, new(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero));

            Assert.True(result.Succeeded);
            Assert.Equal(3, interop.ActiveQueryCount);
        }
    }

    [Fact]
    public async Task Completion_limits_post_quiescence_EOF_grace()
    {
        var stdout = new EofGateStream();
        var interop = new FakeLaunchInterop { RootSignaled = true, ExitCode = 0, ActiveProcesses = [0, 0], Stdout = stdout };
        using var supervisor = new ProcessSupervisor(interop);
        var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new("worker.exe", "worker.exe --internal")).Launch);
        var started = Stopwatch.GetTimestamp();

        var result = await supervisor.ObserveAsync(launch, new(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromMilliseconds(25)));
        stdout.Complete();

        Assert.False(result.Succeeded);
        Assert.True(result.Quiescent);
        Assert.False(result.EofCompleted);
        Assert.True(Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public async Task Windows_event_gated_saturation_drains_both_production_pipes_without_deadlock()
    {
        if (!OperatingSystem.IsWindows()) return;
        var readyName = $"aibar-ready-{Guid.NewGuid():N}"; var releaseName = $"aibar-release-{Guid.NewGuid():N}";
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName);
        using var release = new EventWaitHandle(false, EventResetMode.ManualReset, releaseName);
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var script = "$bytes=New-Object byte[] 65537; [Console]::OpenStandardOutput().Write($bytes,0,$bytes.Length); [Console]::Error.Write(('e' * 65537)); [void][Threading.EventWaitHandle]::OpenExisting('" + readyName + "').Set(); [void][Threading.EventWaitHandle]::OpenExisting('" + releaseName + "').WaitOne()";
        using var supervisor = new ProcessSupervisor(new WindowsProcessSupervisorInterop());
        var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new(powershell, $"\"{powershell}\" -NoProfile -NonInteractive -Command \"{script}\"")).Launch);

        var observation = supervisor.ObserveAsync(launch, new(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(25), TimeSpan.FromSeconds(1)));
        Assert.True(ready.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.False(observation.IsCompleted);
        release.Set();
        var result = await observation;

        Assert.True(result.Succeeded);
        Assert.Equal(64 * 1024, result.StdoutTail.Length);
        Assert.Equal(1, result.StdoutDiscardedBytes);
        Assert.Equal(64 * 1024, result.StderrTail.Length);
        Assert.Equal(1, result.StderrDiscardedBytes);
    }

    private sealed class FakeClock : ISupervisorClock { public long ElapsedMilliseconds => 0; }
    private sealed class FakeRandom : ISupervisorRandom { public void Fill(Span<byte> bytes) => bytes.Clear(); }
    private sealed class FakeInterop : ISupervisorInterop { }

    public enum LaunchFault { None, CreateJob, ConfigureJob, CreatePort, AssociatePort, CreatePipes, CreateProcess, CreateProcessException, Assign, Resume }

    private sealed class FakeLaunchInterop(LaunchFault fault = LaunchFault.None) : IProcessSupervisorInterop
    {
        public LaunchFault Fault { get; } = fault;
        public List<string> Calls { get; } = [];
        public List<SupervisorSafeHandle> Handles { get; } = [];
        public IReadOnlyList<string> InheritedHandles { get; private set; } = [];
        public bool RootSignaled { get; set; }
        public int ExitCode { get; set; }
        public List<uint> ActiveProcesses { get; set; } = [];
        public List<bool> Packets { get; set; } = [];
        public Stream Stdout { get; set; } = new MemoryStream();
        public Stream Stderr { get; set; } = new MemoryStream();
        public int ActiveQueryCount { get; private set; }
        public int ExitCodeQueryCount { get; private set; }
        public List<long> QueryTimestamps { get; } = [];
        private T Track<T>(T handle) where T : SupervisorSafeHandle { Handles.Add(handle); return handle; }
        public SafeJobHandle? CreateJob() { Calls.Add("CreateJob"); return Fault == LaunchFault.CreateJob ? null : Track(new TrackingJobHandle()); }
        public bool ConfigureJob(SafeJobHandle job) { Calls.Add("ConfigureJob"); return Fault != LaunchFault.ConfigureJob; }
        public SafeCompletionPortHandle? CreateCompletionPort() { Calls.Add("CreatePort"); return Fault == LaunchFault.CreatePort ? null : Track(new TrackingPortHandle()); }
        public bool AssociateCompletionPort(SafeJobHandle job, SafeCompletionPortHandle port) { Calls.Add("AssociatePort"); return Fault != LaunchFault.AssociatePort; }
        public SupervisorPipes? CreatePipes() => CreatePipesCore();
        private SupervisorPipes? CreatePipesCore()
        {
            Calls.Add("CreatePipes");
            return Fault == LaunchFault.CreatePipes ? null : new(Track(new TrackingPipeHandle("parent-stdin")), Track(new TrackingPipeHandle("parent-stdout")), Track(new TrackingPipeHandle("parent-stderr")), Track(new TrackingPipeHandle("child-stdin")), Track(new TrackingPipeHandle("child-stdout")), Track(new TrackingPipeHandle("child-stderr")));
        }
        public SupervisorProcess? CreateSuspended(ProcessLaunchRequest request, SupervisorPipes pipes, IReadOnlyList<SafePipeHandle> inheritedHandles)
        {
            Calls.Add("CreateProcess"); InheritedHandles = inheritedHandles.Select(handle => ((TrackingPipeHandle)handle).Name).ToArray();
            if (Fault == LaunchFault.CreateProcessException) throw new InvalidOperationException();
            return Fault == LaunchFault.CreateProcess ? null : new(Track(new TrackingProcessHandle()), Track(new TrackingThreadHandle()));
        }
        public bool AssignProcessToJob(SafeJobHandle job, SafeProcessHandle process) { Calls.Add("Assign"); return Fault != LaunchFault.Assign; }
        public bool ResumeThread(SafeThreadHandle thread) { Calls.Add("Resume"); return Fault != LaunchFault.Resume; }
        public void TerminateProcess(SafeProcessHandle process) => Calls.Add("TerminateProcess");
        public bool TryGetExitCode(SafeProcessHandle process, out int exitCode) { ExitCodeQueryCount++; exitCode = ExitCode; return true; }
        public bool IsProcessSignaled(SafeProcessHandle process) => RootSignaled;
        public bool TryGetActiveProcesses(SafeJobHandle job, out uint activeProcesses) { ActiveQueryCount++; QueryTimestamps.Add(Stopwatch.GetTimestamp()); activeProcesses = ActiveProcesses.Count == 0 ? 0 : ActiveProcesses[0]; if (ActiveProcesses.Count > 0) ActiveProcesses.RemoveAt(0); return true; }
        public bool ObserveCompletionPacket(SafeCompletionPortHandle port) { if (Packets.Count == 0) return false; var packet = Packets[0]; Packets.RemoveAt(0); return packet; }
        public Stream OpenReadPipe(SafePipeHandle pipe) => pipe is TrackingPipeHandle { Name: "parent-stdout" } ? Stdout : Stderr;
    }

    private sealed class TrackingJobHandle : SafeJobHandle { public TrackingJobHandle() : base(new IntPtr(1), true) { } protected override bool ReleaseHandle() => true; }
    private sealed class TrackingPortHandle : SafeCompletionPortHandle { public TrackingPortHandle() : base(new IntPtr(1), true) { } protected override bool ReleaseHandle() => true; }
    private sealed class TrackingPipeHandle : SafePipeHandle { public TrackingPipeHandle(string name) : base(new IntPtr(1), true) { Name = name; } public string Name { get; } protected override bool ReleaseHandle() => true; }
    private sealed class TrackingProcessHandle : SafeProcessHandle { public TrackingProcessHandle() : base(new IntPtr(1), true) { } protected override bool ReleaseHandle() => true; }
    private sealed class TrackingThreadHandle : SafeThreadHandle { public TrackingThreadHandle() : base(new IntPtr(1), true) { } protected override bool ReleaseHandle() => true; }

    private sealed class EofGateStream : MemoryStream
    {
        private readonly TaskCompletionSource _eof = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete() => _eof.TrySetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position < Length) return await base.ReadAsync(buffer, cancellationToken);
            await _eof.Task.WaitAsync(cancellationToken);
            return 0;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(SafeProcessHandle process, IntPtr job, [MarshalAs(UnmanagedType.Bool)] out bool result);
    [DllImport("kernel32.dll")]
    private static extern uint GetProcessId(SafeProcessHandle process);

    private sealed class NeverEndingStream(int availableBytes) : Stream
    {
        public int BytesRead { get; private set; }
        public int MaximumRequestedRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            MaximumRequestedRead = Math.Max(MaximumRequestedRead, buffer.Length);
            var count = Math.Min(buffer.Length, availableBytes - BytesRead);
            buffer[..count].Fill((byte)'x');
            BytesRead += count;
            return count == 0 ? throw new InvalidOperationException("The bounded reader requested bytes after its limit.") : count;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
