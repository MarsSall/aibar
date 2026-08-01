using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NativeSafeFileHandle = Microsoft.Win32.SafeHandles.SafeFileHandle;
using AIBar.Packaging.Supervisor;

namespace AIBar.Domain.Tests;

[Collection("WindowsProcessHarness")]
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
        var rootStartedName = $"Local\\aibar-root-started-{Guid.NewGuid():N}"; var allowChildName = $"Local\\aibar-allow-child-{Guid.NewGuid():N}"; var readyName = $"Local\\aibar-ready-{Guid.NewGuid():N}"; var rootReadyName = $"Local\\aibar-root-ready-{Guid.NewGuid():N}"; var rootExitedName = $"Local\\aibar-root-exited-{Guid.NewGuid():N}"; var releaseName = $"Local\\aibar-release-{Guid.NewGuid():N}";
        using var rootStarted = new EventWaitHandle(false, EventResetMode.ManualReset, rootStartedName);
        using var allowChild = new EventWaitHandle(false, EventResetMode.ManualReset, allowChildName);
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName);
        using var rootReady = new EventWaitHandle(false, EventResetMode.ManualReset, rootReadyName);
        using var rootExited = new EventWaitHandle(false, EventResetMode.ManualReset, rootExitedName);
        using var release = new EventWaitHandle(false, EventResetMode.ManualReset, releaseName);
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var child = $"try {{ Write-Output 'child-ready'; [Threading.EventWaitHandle]::OpenExisting('{readyName}').Set(); [Threading.EventWaitHandle]::OpenExisting('{releaseName}').WaitOne() }} catch {{ Write-Output 'child-startup-failed'; exit 31 }}";
        var childCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(child));
        var script = $"[Threading.EventWaitHandle]::OpenExisting('{rootStartedName}').Set(); [Threading.EventWaitHandle]::OpenExisting('{allowChildName}').WaitOne(); try {{ $child='{childCommand}'; $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME 'powershell.exe')); $start.UseShellExecute=$false; $start.Arguments=\"-NoProfile -NonInteractive -EncodedCommand $child\"; $childProcess=[Diagnostics.Process]::Start($start); if ($null -eq $childProcess) {{ Write-Output 'child-start-failed'; exit 41 }}; Write-Output 'child-launched'; if (-not [Threading.EventWaitHandle]::OpenExisting('{readyName}').WaitOne(4000)) {{ Write-Output 'child-ready-timeout'; exit 42 }}; [Threading.EventWaitHandle]::OpenExisting('{rootReadyName}').Set(); Write-Output 'root-ready'; exit }} catch {{ Write-Output 'root-startup-failed'; exit 43 }} finally {{ [Threading.EventWaitHandle]::OpenExisting('{rootExitedName}').Set() }}";
        var command = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var supervisor = new ProcessSupervisor(new WindowsProcessSupervisorInterop());
        var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new(powershell, $"\"{powershell}\" -NoProfile -NonInteractive -EncodedCommand {command}")).Launch);

        var observation = supervisor.ObserveAsync(launch, new(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(25), TimeSpan.FromSeconds(2)));
        Assert.True(rootStarted.WaitOne(TimeSpan.FromSeconds(5)));
        allowChild.Set();
        var handshake = WaitHandle.WaitAny([rootReady, rootExited], 5_000);
        if (handshake != 0)
        {
            release.Set();
            var failure = await observation;
            var diagnostics = Encoding.UTF8.GetString(failure.StdoutTail.Concat(failure.StderrTail).ToArray());
            var safeCode = new[] { "child-ready", "child-launched", "root-ready", "child-startup-failed", "child-start-failed", "child-ready-timeout", "root-startup-failed" }.LastOrDefault(diagnostics.Contains) ?? "none";
            Assert.Fail($"Nested helper readiness failed: rootExited={handshake == 1}, exitCode={failure.ExitCode?.ToString() ?? "unavailable"}, safeCode={safeCode}, stdoutBytes={failure.StdoutTail.Length + failure.StdoutDiscardedBytes}, stderrBytes={failure.StderrTail.Length + failure.StderrDiscardedBytes}.");
            return;
        }
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

    [Theory]
    [InlineData(true, SupervisorStatus.Cancelled)]
    [InlineData(false, SupervisorStatus.Timeout)]
    public async Task Containment_classifies_cancellation_and_timeout_only_after_job_quiescence(bool cancel, SupervisorStatus expected)
    {
        var interop = new FakeLaunchInterop { ActiveProcesses = [0, 0] };
        using var supervisor = new ProcessSupervisor(interop);
        var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new("worker.exe", "worker.exe --internal")).Launch);
        using var cancellation = new CancellationTokenSource();
        if (cancel) cancellation.Cancel();

        var result = await supervisor.ObserveAsync(launch, new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(25)), cancellation.Token);

        Assert.Equal(expected, result.Status);
        Assert.Equal(1, interop.TerminateJobCount);
        Assert.True(result.Quiescent);
        Assert.Equal(2, interop.ActiveQueryCount);
        Assert.All(interop.Handles, handle => Assert.True(handle.IsClosed));
    }

    [Fact]
    public async Task Query_and_termination_failure_are_truthfully_unproved_and_do_not_return_success()
    {
        var interop = new FakeLaunchInterop { RootSignaled = true, FailActiveQuery = true, TerminateJobSucceeds = false };
        using var supervisor = new ProcessSupervisor(interop);
        var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new("worker.exe", "worker.exe --internal")).Launch);

        var result = await supervisor.ObserveAsync(launch, new(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromMilliseconds(25)));

        Assert.Equal(SupervisorStatus.QuiescenceUnproved, result.Status);
        Assert.False(result.Succeeded);
        Assert.Equal(1, interop.TerminateJobCount);
        Assert.All(interop.Handles, handle => Assert.True(handle.IsClosed));
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    public async Task Failed_post_containment_repeated_zero_proof_is_truthfully_unproved(bool cancel, bool queryFails, bool nonzero)
    {
        var interop = new FakeLaunchInterop
        {
            ActiveProcesses = nonzero ? [0, 1] : [0, 0],
            FailActiveQuery = queryFails
        };
        using var supervisor = new ProcessSupervisor(interop);
        var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new("worker.exe", "worker.exe --internal")).Launch);
        using var cancellation = new CancellationTokenSource();
        if (cancel) cancellation.Cancel();

        var result = await supervisor.ObserveAsync(launch, new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(25)), cancellation.Token);

        Assert.Equal(SupervisorStatus.QuiescenceUnproved, result.Status);
        Assert.False(result.Quiescent);
        Assert.Equal(1, interop.TerminateJobCount);
        Assert.All(interop.Handles, handle => Assert.True(handle.IsClosed));
    }

    [Fact]
    public async Task Eof_failure_cancels_drains_and_reports_output_drain_failed()
    {
        var stdout = new EofGateStream();
        var interop = new FakeLaunchInterop { RootSignaled = true, ExitCode = 0, ActiveProcesses = [0, 0], Stdout = stdout };
        using var supervisor = new ProcessSupervisor(interop);
        var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new("worker.exe", "worker.exe --internal")).Launch);

        var result = await supervisor.ObserveAsync(launch, new(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromMilliseconds(25)));

        Assert.Equal(SupervisorStatus.OutputDrainFailed, result.Status);
        Assert.False(result.Succeeded);
        Assert.All(interop.Handles, handle => Assert.True(handle.IsClosed));
    }

    [Fact]
    public async Task Cancellation_resistant_drains_are_closed_and_completed_before_return()
    {
        var stdout = new CancellationResistantDrainStream();
        var interop = new FakeLaunchInterop { RootSignaled = true, ExitCode = 0, ActiveProcesses = [0, 0], Stdout = stdout };
        using var supervisor = new ProcessSupervisor(interop);
        var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new("worker.exe", "worker.exe --internal")).Launch);

        var result = await supervisor.ObserveAsync(launch, new(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero));

        Assert.Equal(SupervisorStatus.OutputDrainFailed, result.Status);
        Assert.True(stdout.Disposed);
        Assert.True(stdout.ReadCompleted);
        Assert.Equal("late", Encoding.UTF8.GetString(result.StdoutTail));
        Assert.All(interop.Handles, handle => Assert.True(handle.IsClosed));
    }

    [Fact]
    public async Task Late_cancellation_during_EOF_wait_overrides_success_and_contains_the_job()
    {
        var stdout = new EofGateStream();
        var interop = new FakeLaunchInterop { RootSignaled = true, ExitCode = 0, ActiveProcesses = [0, 0, 0, 0], Stdout = stdout };
        using var supervisor = new ProcessSupervisor(interop); using var cancellation = new CancellationTokenSource();
        var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new("worker.exe", "worker.exe --internal")).Launch);
        var observation = supervisor.ObserveAsync(launch, new(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromSeconds(1)), cancellation.Token);

        await Task.Delay(20); cancellation.Cancel();
        var result = await observation;

        Assert.Equal(SupervisorStatus.Cancelled, result.Status);
        Assert.False(result.Succeeded);
        Assert.Equal(1, interop.TerminateJobCount);
    }

    [Fact]
    public async Task Zero_confirmation_cannot_succeed_after_the_deterministic_deadline()
    {
        var time = new ManualTimeProvider(); var interop = new FakeLaunchInterop { RootSignaled = true, ActiveProcesses = [0, 0, 0] };
        interop.OnActiveQuery = () => time.Advance(TimeSpan.FromTicks(1));
        using var supervisor = new ProcessSupervisor(interop, time); var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new("worker.exe", "worker.exe --internal")).Launch);
        var result = await supervisor.ObserveAsync(launch, new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));
        Assert.Equal(SupervisorStatus.Timeout, result.Status); Assert.False(result.Succeeded); Assert.Equal(3, interop.ActiveQueryCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Pipe_open_failures_are_contained_and_closed(int failingOpen)
    {
        var interop = new FakeLaunchInterop { ActiveProcesses = [0, 0], FailOpenAt = failingOpen };
        using var supervisor = new ProcessSupervisor(interop); var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new("worker.exe", "worker.exe --internal")).Launch);
        var result = await supervisor.ObserveAsync(launch, new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));
        Assert.Equal(SupervisorStatus.OutputDrainFailed, result.Status); Assert.True(result.Quiescent); Assert.Equal(1, interop.TerminateJobCount); Assert.All(interop.Handles, handle => Assert.True(handle.IsClosed));
    }

    [Fact]
    public async Task Initial_drain_failure_is_contained_and_closed()
    {
        var interop = new FakeLaunchInterop { ActiveProcesses = [0, 0], Stdout = new FailingReadStream() };
        using var supervisor = new ProcessSupervisor(interop); var launch = Assert.IsType<ProcessLaunch>(supervisor.Launch(new("worker.exe", "worker.exe --internal")).Launch);
        var result = await supervisor.ObserveAsync(launch, new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));
        Assert.Equal(SupervisorStatus.OutputDrainFailed, result.Status); Assert.True(result.Quiescent); Assert.Equal(1, interop.TerminateJobCount); Assert.All(interop.Handles, handle => Assert.True(handle.IsClosed));
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

    [Fact]
    public void Directory_capability_retains_live_handles_and_refuses_changed_admission_without_mutation()
    {
        var fileSystem = new FakeDirectoryCapabilityFileSystem();
        Assert.True(DirectoryCapability.TryCreate(fileSystem, "C:\\space café", "root", ["restore", "publish"], out var capability, out var status));
        using (var retained = Assert.IsType<DirectoryCapability>(capability))
        {
            Assert.Equal(SupervisorStatus.Success, status);
            Assert.Equal(3, fileSystem.LiveHandleCount);
            var mutations = fileSystem.MutationCount;
            Assert.True(retained.TryValidate(out status));

            foreach (var refusal in new[] { "extra", "duplicate", "missing", "identity", "final-path", "volume", "access" })
            {
                fileSystem.Refusal = refusal;
                Assert.False(retained.TryValidate(out status));
                Assert.Equal(SupervisorStatus.RootIdentityChanged, status);
                Assert.Equal(mutations, fileSystem.MutationCount);
            }

            fileSystem.Refusal = "reparse";
            Assert.False(retained.TryValidate(out status));
            Assert.Equal(SupervisorStatus.ReparseDetected, status);
            Assert.Equal(mutations, fileSystem.MutationCount);
        }
    }

    [Fact]
    public void Directory_capability_is_deterministic_for_reordered_unicode_space_and_containment_observations()
    {
        var fileSystem = new FakeDirectoryCapabilityFileSystem { ReverseEnumeration = true };
        Assert.True(DirectoryCapability.TryCreate(fileSystem, "C:\\space café", "root Ω", ["restore files", "publicación"], out var capability, out var status));
        using (var retained = Assert.IsType<DirectoryCapability>(capability))
        {
            Assert.True(retained.TryValidate(out status));
            Assert.Equal(SupervisorStatus.Success, status);
            fileSystem.Refusal = "containment";
            Assert.False(retained.TryValidate(out status));
            Assert.Equal(SupervisorStatus.RootIdentityChanged, status);
        }
    }

    [Fact]
    public void Windows_directory_capability_retains_a_live_nonreparse_same_volume_admission()
    {
        if (!OperatingSystem.IsWindows()) return;
        var parent = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"aibar c1a café {Guid.NewGuid():N}"));
        try
        {
            Assert.True(DirectoryCapability.TryCreate(new WindowsDirectoryCapabilityFileSystem(), parent.FullName, "root Ω", ["restore files", "publish files"], out var capability, out var status));
            using (var retained = Assert.IsType<DirectoryCapability>(capability))
            {
                Assert.True(retained.TryValidate(out status));
                Assert.Equal(SupervisorStatus.Success, status);
            }
        }
        finally { Directory.Delete(parent.FullName, true); }
    }

    [Fact]
    public void Native_rename_readiness_refuses_invalid_leaf_identity_reparse_volume_share_and_child_set_before_call()
    {
        Assert.Equal(0x00130089u, DirectoryCapability.RenameSourceAccess); Assert.Equal(0x001000A0u, DirectoryCapability.QuarantineParentAccess);
        Assert.All(new[] { "", ".", "..", "bad/name", "bad\\name", "bad:name", "CON" }, leaf => Assert.False(DirectoryCapability.IsSimpleLeaf(leaf)));
        Assert.True(NativeRenameReadiness.IsSuccessfulStatus(0, 0)); Assert.False(NativeRenameReadiness.IsSuccessfulStatus(0, unchecked((int)0x103))); Assert.False(NativeRenameReadiness.IsSuccessfulStatus(unchecked((int)0xC0000001), 0));
        var fileSystem = new FakeDirectoryCapabilityFileSystem(); fileSystem.TryCreateDirectory("C:\\quarantine");
        Assert.True(DirectoryCapability.TryCreateRenameReady(fileSystem, "C:\\source", "root", ["restore", "publish"], "C:\\quarantine", out var capability, out _));
        using var retained = Assert.IsType<DirectoryCapability>(capability);
        foreach (var refusal in new[] { "root-identity", "parent-identity", "reparse", "parent-reparse", "volume", "extra", "share" })
        {
            fileSystem.Refusal = refusal;
            var result = NativeRenameReadiness.Prove(retained, "quarantine-root");
            Assert.Equal(SupervisorStatus.CleanupRefused, result.Status); Assert.Equal(0, result.NativeCallCount);
        }
        fileSystem.Refusal = null;
        var invalid = NativeRenameReadiness.Prove(retained, "bad/name"); Assert.Equal(SupervisorStatus.CleanupRefused, invalid.Status); Assert.Equal(0, invalid.NativeCallCount);
    }

    [Fact]
    public void Windows_native_rename_readiness_proves_relative_success_collision_and_no_residue()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return;
        Assert.True(NativeRenameReadiness.IsCompatibleForCurrentProcess());
        using var parent = new NativeReadinessRoot();
        using (var retained = NativeReadiness(parent.Path, "source"))
        {
            var result = NativeRenameReadiness.Prove(retained, "quarantine-source");
            Assert.Equal(SupervisorStatus.Success, result.Status); Assert.Equal(1, result.NativeCallCount); Assert.True(result.ChildrenReleased);
            var committed = Assert.IsType<CommittedQuarantineCapability>(result.Capability);
            var frozenEvidence = committed.Evidence;
            try
            {
                Assert.Equal(retained.Source.Identity, result.Source!.Identity); Assert.Equal(retained.Source.Identity, committed.Source.Identity); Assert.True(frozenEvidence.HasValidDigest());
                Assert.True(Directory.Exists(Path.Combine(parent.Quarantine, "quarantine-source")));
            }
            finally { committed.Dispose(); retained.Dispose(); }
            Assert.True(committed.HandlesReleased); Assert.False(frozenEvidence.HasValidDigest());
        }
        using (var retained = NativeReadiness(parent.Path, "collision"))
        {
            var target = Path.Combine(parent.Quarantine, "collision-target"); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "sentinel"), "keep");
            var result = NativeRenameReadiness.Prove(retained, "collision-target");
            Assert.Equal(SupervisorStatus.CleanupRefused, result.Status); Assert.Equal(1, result.NativeCallCount); Assert.True(Directory.Exists(Path.Combine(parent.Path, "collision"))); Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "sentinel")));
        }
    }

    [Theory]
    [InlineData(false, 8, true, true, 10)]
    [InlineData(true, 4, true, true, 10)]
    [InlineData(true, 8, false, true, 10)]
    [InlineData(true, 8, true, false, 10)]
    [InlineData(true, 8, true, true, 9)]
    public void Native_readiness_refuses_unsupported_platform_abi_entrypoint_or_information_class_without_a_native_call(bool supportedWindows, int pointerSize, bool layoutsValid, bool entryPointAvailable, int informationClass)
    {
        Assert.False(NativeRenameReadiness.IsCompatible(supportedWindows, pointerSize, layoutsValid, entryPointAvailable, informationClass));
        var fileSystem = new FakeDirectoryCapabilityFileSystem(); fileSystem.TryCreateDirectory("C:\\quarantine");
        Assert.True(DirectoryCapability.TryCreateRenameReady(fileSystem, "C:\\source", "root", ["child"], "C:\\quarantine", out var capability, out _));
        using var retained = Assert.IsType<DirectoryCapability>(capability);

        var result = NativeRenameReadiness.Prove(retained, "quarantine-root", () => false);

        Assert.Equal(SupervisorStatus.CleanupRefused, result.Status); Assert.Equal(0, result.NativeCallCount); Assert.False(result.ChildrenReleased);
        Assert.True(retained.TryValidateRenameReady(out _));
    }

    [Fact]
    public void C1b0_readiness_refusal_preserves_the_unissued_commit_boundary()
    {
        var assembly = typeof(DirectoryCapability).Assembly;
        Assert.Equal("AIBar.Packaging.Supervisor.Program", assembly.EntryPoint!.DeclaringType!.FullName);
        var fileSystem = new FakeDirectoryCapabilityFileSystem(); fileSystem.TryCreateDirectory("C:\\quarantine");
        Assert.True(DirectoryCapability.TryCreateRenameReady(fileSystem, "C:\\source", "root", ["child"], "C:\\quarantine", out var capability, out _));
        using var retained = Assert.IsType<DirectoryCapability>(capability);

        var result = NativeRenameReadiness.Prove(retained, "quarantine-root", () => false);

        Assert.Equal(SupervisorStatus.CleanupRefused, result.Status); Assert.Equal(0, result.NativeCallCount); Assert.False(result.ChildrenReleased);
        Assert.True(retained.TryValidateRenameReady(out _));
    }

    [Fact]
    public void C1b1_commit_refuses_invalid_leaf_before_native_call()
    {
        var fileSystem = new FakeDirectoryCapabilityFileSystem(); fileSystem.TryCreateDirectory("C:\\quarantine");
        Assert.True(DirectoryCapability.TryCreateRenameReady(fileSystem, "C:\\source", "root", ["child"], "C:\\quarantine", out var capability, out _));
        using var retained = Assert.IsType<DirectoryCapability>(capability);
        var result = Cleanup.Commit(retained, "bad/name");
        Assert.Equal(SupervisorStatus.CleanupRefused, result.Status); Assert.Equal(0, result.NativeCallCount); Assert.False(result.ChildrenReleased);
        Assert.True(retained.TryValidateRenameReady(out _));
    }

    [Fact]
    public void Committed_child_evidence_freezes_handle_derived_records_before_the_one_way_release()
    {
        var fileSystem = new FakeDirectoryCapabilityFileSystem(); fileSystem.TryCreateDirectory("C:\\quarantine");
        Assert.True(DirectoryCapability.TryCreateRenameReady(fileSystem, "C:\\source", "root", ["restore", "publish"], "C:\\quarantine", out var capability, out _));
        using var retained = Assert.IsType<DirectoryCapability>(capability);

        Assert.True(retained.TryFreezeChildEvidence(out var evidence));
        using (var frozen = Assert.IsType<CommittedChildEvidence>(evidence))
        {
            Assert.Equal("CommittedChildEvidence/v1", frozen.Version);
            Assert.Equal(32, frozen.Digest.Length); Assert.Equal(32, frozen.CorrelationKey.Length);
            Assert.Equal(2, frozen.Children.Count); Assert.True(frozen.HasValidDigest());
            Assert.All(frozen.Children, child => { Assert.Equal(32, child.LeafTag.Length); Assert.Equal(16, child.FileId.Length); Assert.Equal(CommittedChildObjectKind.Directory, child.Kind); Assert.False(child.IsReparsePoint); });
            var expectedTag = System.Security.Cryptography.HMACSHA256.HashData(frozen.CorrelationKey, Encoding.Unicode.GetBytes("restore"));
            Assert.Contains(frozen.Children, child => child.LeafTag.SequenceEqual(expectedTag));
            var exposedDigest = frozen.Digest; exposedDigest[0] ^= 0xff;
            Assert.True(frozen.HasValidDigest());
        }

        Assert.True(retained.ReleaseChildHandles());
        Assert.False(retained.TryFreezeChildEvidence(out _));
    }

    [Fact]
    public void Committed_child_evidence_rejects_oversized_or_collision_checked_input_before_any_commit()
    {
        var fileSystem = new FakeDirectoryCapabilityFileSystem(); fileSystem.TryCreateDirectory("C:\\quarantine");
        Assert.False(DirectoryCapability.TryCreateRenameReady(fileSystem, "C:\\source", "root", ["same", "SAME"], "C:\\quarantine", out _, out _));
        Assert.False(DirectoryCapability.TryCreateRenameReady(fileSystem, "C:\\source", "root", [new string('x', 256)], "C:\\quarantine", out _, out _));
        Assert.Equal(1, fileSystem.MutationCount);

        var duplicateFileSystem = new FakeDirectoryCapabilityFileSystem { DuplicateChildIdentity = true }; duplicateFileSystem.TryCreateDirectory("C:\\quarantine");
        Assert.True(DirectoryCapability.TryCreateRenameReady(duplicateFileSystem, "C:\\source", "root", ["restore", "publish"], "C:\\quarantine", out var duplicateCapability, out _));
        using var retained = Assert.IsType<DirectoryCapability>(duplicateCapability);
        Assert.False(retained.TryFreezeChildEvidence(out _));
    }

    [Fact]
    public void C1b1_commit_preserves_retained_handle_relative_success()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return;
        using var parent = new NativeReadinessRoot(); using var retained = NativeReadiness(parent.Path, "source");
        var result = Cleanup.Commit(retained, "quarantine-source");
        Assert.Equal(SupervisorStatus.Success, result.Status); Assert.Equal(1, result.NativeCallCount); Assert.True(result.ChildrenReleased);
        Assert.Equal(retained.Source.Identity, result.Source!.Identity); Assert.True(Directory.Exists(Path.Combine(parent.Quarantine, "quarantine-source")));
    }

    [Fact]
    public void C1b1_commit_refuses_collision_without_replacement()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return;
        using var parent = new NativeReadinessRoot(); using var retained = NativeReadiness(parent.Path, "collision");
        var target = Path.Combine(parent.Quarantine, "collision-target"); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "sentinel"), "keep");
        var result = Cleanup.Commit(retained, "collision-target");
        Assert.Equal(SupervisorStatus.CleanupRefused, result.Status); Assert.Equal(1, result.NativeCallCount); Assert.True(result.ChildrenReleased);
        Assert.True(Directory.Exists(Path.Combine(parent.Path, "collision"))); Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "sentinel")));
    }

        [Fact]
        public void Red_c1c_atomic_take_keeps_released_peers_owned_when_wrapper_is_abandoned()
        {
            using var committed = CreateCommittedCapability();

            Assert.True(committed.TrySplit(out var handoff));
            Assert.True(handoff!.TryTakeBoth(out var tree, out var evidence));
            var root = tree!.RootHandle;
            var frozenEvidence = evidence!.Evidence;
            handoff.Dispose();

            Assert.False(root.IsClosed);
            Assert.True(frozenEvidence.HasValidDigest());
            tree.Dispose();
            evidence.Dispose();
        }

        [Fact]
        public void Red_c1c_post_detach_failure_contains_every_unpublished_resource()
        {
            using var committed = CreateCommittedCapability(CapabilityFacetFailurePoint.PostDetach);
            var root = committed.RootHandle;
            var parent = committed.QuarantineParentHandle;
            var frozenEvidence = committed.Evidence;

            Assert.False(committed.TrySplit(out var handoff));
            Assert.Null(handoff);
            Assert.Equal(CommittedQuarantineCapabilityState.Disposed, committed.State);
            Assert.True(root.IsClosed);
            Assert.True(parent.IsClosed);
            Assert.False(frozenEvidence.HasValidDigest());
        }

        [Fact]
        public void C1c_seeded_sensitive_observation_never_enters_state_diagnostics()
        {
            const string seededPath = "C:\\sensitive-account\\private-root";
            using var committed = CreateCommittedCapability(sourceParent: seededPath);

            Assert.True(committed.TrySplit(out var handoff));
            Assert.DoesNotContain(seededPath, committed.State.ToString(), StringComparison.OrdinalIgnoreCase);
            handoff!.Dispose();
        }

        [Fact]
        public void C1c_split_publishes_one_paired_handoff_and_leaves_original_inert()
        {
            using var committed = CreateCommittedCapability();

            Assert.True(committed.TrySplit(out var handoff));
            Assert.Equal(CommittedQuarantineCapabilityState.Split, committed.State);
            Assert.NotNull(handoff);
            Assert.False(committed.TrySplit(out _));
            committed.Dispose();
            Assert.True(handoff!.TryTakeBoth(out var tree, out var evidence));
            Assert.True(CapabilityFacetBinding.Matches(tree!, evidence!));
            tree!.Dispose();
            Assert.True(evidence!.Evidence.HasValidDigest());
            evidence.Dispose();
        }

        [Fact]
        public async Task C1c_repeated_concurrent_split_and_split_dispose_have_one_winner_without_partial_handoff()
        {
            for (var schedule = 0; schedule < 16; schedule++)
            {
                using var committed = CreateCommittedCapability();
                var start = new ManualResetEventSlim();
                var attempts = Enumerable.Range(0, 8).Select(_ => Task.Run(() => { start.Wait(); return committed.TrySplit(out var handoff) ? handoff : null; })).ToArray();
                start.Set();
                var results = await Task.WhenAll(attempts);

                var winner = Assert.Single(results.Where(result => result is not null));
                Assert.True(winner!.TryTakeBoth(out var tree, out var evidence));
                Assert.NotNull(tree); Assert.NotNull(evidence);
                tree!.Dispose(); evidence!.Dispose();

                using var raced = CreateCommittedCapability();
                var split = Task.Run(() => raced.TrySplit(out var pair) ? pair : null);
                var dispose = Task.Run(raced.Dispose);
                var racePair = await split;
                await dispose;
                Assert.True(racePair is null || raced.State == CommittedQuarantineCapabilityState.Split);
                Assert.True(racePair is not null || raced.State == CommittedQuarantineCapabilityState.Disposed);
                racePair?.Dispose();
            }
        }

        [Fact]
        public void C1c_prepublication_failures_preserve_original_ownership_without_visible_partial_pair()
        {
            foreach (var point in Enum.GetValues<CapabilityFacetFailurePoint>().Where(point => point != CapabilityFacetFailurePoint.PostDetach))
            {
                using var committed = CreateCommittedCapability(point);
                Assert.False(committed.TrySplit(out var handoff));
                Assert.Null(handoff);
                Assert.Equal(CommittedQuarantineCapabilityState.Whole, committed.State);
                Assert.True(committed.Evidence.HasValidDigest());
            }
        }

        [Fact]
        public void C1c_facets_dispose_independently_reject_cross_handoffs_and_preserve_orphaned_evidence()
        {
            using var first = CreateCommittedCapability(); using var second = CreateCommittedCapability();
            Assert.True(first.TrySplit(out var firstPair)); Assert.True(second.TrySplit(out var secondPair));
            Assert.True(firstPair!.TryTakeBoth(out var firstTree, out var firstEvidence));
            Assert.True(secondPair!.TryTakeBoth(out var secondTree, out var secondEvidence));
            var tree = Assert.IsAssignableFrom<IRetainedTreeCapabilityFacet>(firstTree);
            var evidence = Assert.IsAssignableFrom<ICommittedEvidenceCapabilityFacet>(firstEvidence);
            var secondHandoffTree = Assert.IsAssignableFrom<IRetainedTreeCapabilityFacet>(secondTree);
            var crossHandoffEvidence = Assert.IsAssignableFrom<ICommittedEvidenceCapabilityFacet>(secondEvidence);
            Assert.True(CapabilityFacetBinding.Matches(tree, evidence));
            Assert.False(CapabilityFacetBinding.Matches(tree, crossHandoffEvidence));

            var root = tree.RootHandle; var parent = tree.QuarantineParentHandle; var retainedEvidence = evidence.Evidence;
            tree.Dispose(); tree.Dispose();
            Assert.True(root.IsClosed); Assert.True(parent.IsClosed);
            Assert.True(retainedEvidence.HasValidDigest());
            evidence.Dispose(); evidence.Dispose();
            Assert.Equal(1, retainedEvidence.DisposeCount);
            Assert.False(retainedEvidence.HasValidDigest());
            var secondRoot = secondHandoffTree.RootHandle;
            crossHandoffEvidence.Dispose();
            Assert.False(secondRoot.IsClosed);
            secondHandoffTree.Dispose(); secondHandoffTree.Dispose();
            Assert.True(secondRoot.IsClosed);
            Assert.DoesNotContain("C:\\source", first.State.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Windows_c1c_real_producer_handoff_preserves_evidence_when_tree_is_disposed_first()
        {
            if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return;
            using var parent = new NativeReadinessRoot();
            using var retained = NativeReadiness(parent.Path, "source");
            using var committed = Assert.IsType<CommittedQuarantineCapability>(Cleanup.Commit(retained, "quarantine-source").Capability);
            Assert.True(committed.TrySplit(out var handoff));
            Assert.True(handoff!.TryTakeBoth(out var tree, out var evidence));
            var root = tree!.RootHandle; var frozenEvidence = evidence!.Evidence;
            tree.Dispose();
            Assert.True(root.IsClosed);
            Assert.True(frozenEvidence.HasValidDigest());
            evidence.Dispose();
            Assert.False(frozenEvidence.HasValidDigest());
        }

        private static CommittedQuarantineCapability CreateCommittedCapability(CapabilityFacetFailurePoint? failurePoint = null, string sourceParent = "C:\\source")
        {
            var fileSystem = new FakeDirectoryCapabilityFileSystem(); fileSystem.TryCreateDirectory("C:\\quarantine");
            Assert.True(DirectoryCapability.TryCreateRenameReady(fileSystem, sourceParent, "root", ["child"], "C:\\quarantine", out var capability, out _));
            var owner = Assert.IsType<DirectoryCapability>(capability);
            Assert.True(owner.TryFreezeChildEvidence(out var evidence));
            Assert.True(owner.ReleaseChildHandles());
            var committed = Assert.IsType<CommittedQuarantineCapability>(owner.TransferCommitted(Assert.IsType<CommittedChildEvidence>(evidence)));
            committed.SetFailureInjectionForTests(point => point == failurePoint);
            return committed;
        }

        private static DirectoryCapability NativeReadiness(string parent, string leaf)
        {
            Assert.True(DirectoryCapability.TryCreateRenameReady(new WindowsDirectoryCapabilityFileSystem(), parent, leaf, ["child"], Path.Combine(parent, "quarantine"), out var capability, out var status));
            Assert.Equal(SupervisorStatus.Success, status); return Assert.IsType<DirectoryCapability>(capability);
        }
    private sealed class NativeReadinessRoot : IDisposable
    {
        public string Path { get; } = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"aibar-c1b0-{Guid.NewGuid():N}")).FullName;
        public string Quarantine { get; }
        public NativeReadinessRoot() => Quarantine = Directory.CreateDirectory(System.IO.Path.Combine(Path, "quarantine")).FullName;
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }

    private sealed class FakeClock : ISupervisorClock { public long ElapsedMilliseconds => 0; }
    private sealed class FakeRandom : ISupervisorRandom { public void Fill(Span<byte> bytes) => bytes.Clear(); }
    private sealed class FakeInterop : ISupervisorInterop { }

    private sealed class FakeDirectoryCapabilityFileSystem : IDirectoryCapabilityFileSystem
    {
        private readonly Dictionary<string, DirectoryObservation> _nodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<IntPtr, string> _handles = [];
        public int MutationCount { get; private set; }
        public int LiveHandleCount { get; private set; }
        public string? Refusal { get; set; }
        public bool ReverseEnumeration { get; set; }
        public bool DuplicateChildIdentity { get; set; }
        public bool TryCreateDirectory(string path)
        {
            if (_nodes.ContainsKey(path)) return false;
            _nodes[path] = new(new(7, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(path))[..16])), path, false); MutationCount++; return true;
        }
        public NativeSafeFileHandle? OpenDirectory(string path, uint desiredAccess)
        {
            if (!_nodes.ContainsKey(path) || Refusal == "access") return null;
            var handle = new NativeSafeFileHandle(new IntPtr(_handles.Count + 1), false); _handles[handle.DangerousGetHandle()] = path; LiveHandleCount++; return handle;
        }
        public bool TryObserve(NativeSafeFileHandle handle, out DirectoryObservation observation)
        {
            observation = default!;
            if (Refusal == "access" || !_handles.TryGetValue(handle.DangerousGetHandle(), out var path) || path is null || !_nodes.TryGetValue(path, out var current) || current is null) return false;
            observation = current;
            if (DuplicateChildIdentity && path.EndsWith("publish", StringComparison.OrdinalIgnoreCase)) observation = observation with { Identity = _nodes[_nodes.Keys.Single(node => node.EndsWith("restore", StringComparison.OrdinalIgnoreCase))].Identity };
            if (path.EndsWith("publish", StringComparison.OrdinalIgnoreCase))
            {
                if (Refusal == "identity") observation = observation with { Identity = new(7, "substituted") };
                if (Refusal == "final-path") observation = observation with { FinalPath = observation.FinalPath + "-changed" };
                if (Refusal == "volume") observation = observation with { Identity = new(8, observation.Identity.FileId) };
            }
            if (Refusal == "root-identity" && path.EndsWith("root", StringComparison.OrdinalIgnoreCase)) observation = observation with { Identity = new(7, "substituted-root") };
            if (Refusal == "parent-identity" && path.EndsWith("quarantine", StringComparison.OrdinalIgnoreCase)) observation = observation with { Identity = new(7, "substituted-parent") };
            if (Refusal == "volume" && path.EndsWith("quarantine", StringComparison.OrdinalIgnoreCase)) observation = observation with { Identity = new(8, observation.Identity.FileId) };
            if (Refusal == "containment" && path != _nodes.Keys.First()) observation = observation with { FinalPath = "C:\\elsewhere" };
            if (Refusal == "reparse" || Refusal == "parent-reparse" && path.EndsWith("quarantine", StringComparison.OrdinalIgnoreCase)) observation = observation with { IsReparsePoint = true };
            return true;
        }
        public bool HasRequiredRenameShare(NativeSafeFileHandle source, NativeSafeFileHandle parent) => Refusal != "share";
        public bool TryEnumerateDirectChildren(NativeSafeFileHandle root, out IReadOnlyList<string> names)
        {
            if (Refusal == "access" || !_handles.TryGetValue(root.DangerousGetHandle(), out var rootPath)) { names = []; return false; }
            var prefix = rootPath + Path.DirectorySeparatorChar;
            names = _nodes.Keys.Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path[prefix.Length..].IndexOf(Path.DirectorySeparatorChar) < 0).Select(path => path[prefix.Length..]).Where(name => Refusal != "missing" || name != "publish").Concat(Refusal == "extra" ? ["extra"] : Refusal == "duplicate" ? ["publish"] : []).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            if (ReverseEnumeration) names = names.Reverse().ToArray();
            return true;
        }
    }

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
        public int TerminateJobCount { get; private set; }
        public bool TerminateJobSucceeds { get; set; } = true;
        public bool FailActiveQuery { get; set; }
        public int FailOpenAt { get; set; }
        public Action? OnActiveQuery { get; set; }
        private int _openCount;
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
        public bool TerminateJob(SafeJobHandle job) { TerminateJobCount++; Calls.Add("TerminateJob"); return TerminateJobSucceeds; }
        public bool TryGetExitCode(SafeProcessHandle process, out int exitCode) { ExitCodeQueryCount++; exitCode = ExitCode; return true; }
        public bool IsProcessSignaled(SafeProcessHandle process) => RootSignaled;
        public bool TryGetActiveProcesses(SafeJobHandle job, out uint activeProcesses) { ActiveQueryCount++; QueryTimestamps.Add(Stopwatch.GetTimestamp()); activeProcesses = ActiveProcesses.Count == 0 ? 0 : ActiveProcesses[0]; if (ActiveProcesses.Count > 0) ActiveProcesses.RemoveAt(0); OnActiveQuery?.Invoke(); return !FailActiveQuery; }
        public bool ObserveCompletionPacket(SafeCompletionPortHandle port) { if (Packets.Count == 0) return false; var packet = Packets[0]; Packets.RemoveAt(0); return packet; }
        public Stream OpenReadPipe(SafePipeHandle pipe) { if (++_openCount == FailOpenAt) throw new InvalidOperationException(); return pipe is TrackingPipeHandle { Name: "parent-stdout" } ? Stdout : Stderr; }
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

    private sealed class CancellationResistantDrainStream : Stream
    {
        private readonly TaskCompletionSource<byte[]> _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _returnedPayload;
        public bool Disposed { get; private set; }
        public bool ReadCompleted { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_returnedPayload) return 0;
            var payload = await _released.Task;
            payload.CopyTo(buffer);
            _returnedPayload = true;
            ReadCompleted = true;
            return payload.Length;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !Disposed)
            {
                Disposed = true;
                _ = ReleaseAsync();
            }
            base.Dispose(disposing);
        }
        private async Task ReleaseAsync() { await Task.Delay(10); _released.TrySetResult(Encoding.UTF8.GetBytes("late")); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan value) => _timestamp += value.Ticks;
    }

    private sealed class FailingReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(new InvalidOperationException());
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
