using System.Text;
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

    private sealed class FakeClock : ISupervisorClock { public long ElapsedMilliseconds => 0; }
    private sealed class FakeRandom : ISupervisorRandom { public void Fill(Span<byte> bytes) => bytes.Clear(); }
    private sealed class FakeInterop : ISupervisorInterop { }

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
