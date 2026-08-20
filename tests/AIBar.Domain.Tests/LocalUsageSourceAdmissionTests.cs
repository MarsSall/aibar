using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class LocalUsageSourceAdmissionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-admission-{Guid.NewGuid():N}");
    private string SourcePath => Path.Combine(_root, "opencode.db");
    private string LedgerPath => Path.Combine(_root, "ledger.db");
    private string SettingsPath => Path.Combine(_root, "settings.json");

    [Fact]
    public async Task Unchanged_proof_admits_prepare_and_manual_registration_is_explicitly_unverified()
    {
        var (fileSystem, registration) = Registration(); var prepareCalls = 0;
        await using var runtime = Runtime(registration, (item, checkpoint, _, _) => { prepareCalls++; return ValueTask.FromResult(Empty(item, checkpoint)); });
        var result = Assert.Single((await runtime.StartAsync()).Sources);
        Assert.Equal(LocalUsageSourceStatus.Completed, result.Status); Assert.Equal(1, prepareCalls); Assert.True(fileSystem.Calls > 0);
        Assert.Equal(LocalUsageRegistrationAdmissionPolicy.DiscoveryProof, registration.AdmissionPolicy);
        Assert.Equal(LocalUsageRegistrationAdmissionPolicy.UnverifiedManual, Manual().AdmissionPolicy);
    }

    [Fact]
    public void Physical_temp_source_uses_stable_Windows_identity_without_reading_content()
    {
        Directory.CreateDirectory(_root); File.WriteAllText(SourcePath, "not-read");
        var proof = LocalUsageSourceAdmission.Capture(new PhysicalLocalUsageDiscoveryFileSystem(), _root, SourcePath,
            LocalUsageAdmissionLayout.Direct, default)!;
        Assert.Equal(LocalUsageAdmissionStatus.Admitted, proof.Validate(default));
        File.Move(SourcePath, SourcePath + ".old"); File.WriteAllText(SourcePath, "replacement");
        Assert.Equal(LocalUsageAdmissionStatus.PathChanged, proof.Validate(default));
    }

    [Fact]
    public async Task Replacement_before_open_fails_closed_without_prepare_or_commit()
    {
        var (fileSystem, registration) = Registration(); fileSystem.Replace(SourcePath); var prepareCalls = 0;
        await using var runtime = Runtime(registration, (item, checkpoint, _, _) => { prepareCalls++; return ValueTask.FromResult(Empty(item, checkpoint)); });
        var result = Assert.Single((await runtime.StartAsync()).Sources);
        Assert.Equal(LocalUsageSourceStatus.RebuildRequired, result.Status); Assert.Equal([LocalUsageWarning.PathChanged], result.WarningCodes);
        Assert.Equal(0, prepareCalls); await AssertNoCommit();
    }

    [Fact]
    public async Task Replacement_during_prepare_reports_truthful_read_but_commits_nothing()
    {
        var (fileSystem, registration) = Registration(); var prepareCalls = 0;
        ValueTask<LocalUsagePreparedBatch> Prepare(LocalUsageSourceRegistration item, UsageSourceCheckpoint? checkpoint, int _, CancellationToken __)
        { prepareCalls++; fileSystem.Replace(SourcePath); return ValueTask.FromResult(new LocalUsagePreparedBatch([], 1, 1, 0, [], Next(item))); }
        await using var runtime = Runtime(registration, Prepare); var result = Assert.Single((await runtime.StartAsync()).Sources);
        Assert.Equal((LocalUsageSourceStatus.RebuildRequired, 1, 0), (result.Status, result.RecordsRead, result.EventsCommitted));
        Assert.Equal(1, prepareCalls); await AssertNoCommit();
    }

    [Fact]
    public async Task Production_adapter_revalidates_immediately_after_path_open()
    {
        Directory.CreateDirectory(_root); await File.WriteAllTextAsync(SourcePath, "not-a-database");
        await new LocalUsageSettings(SettingsPath).SaveAsync(new(true, false), default); var admission = new SequencedAdmission();
        var registration = new LocalUsageSourceRegistration(UsageTool.OpenCode, LocalUsageAdapterKind.OpenCodeSqliteV1, SourcePath, Id(), 1, admission);
        await using var ledger = new SqliteUsageEventLedger(LedgerPath); await using var runtime = new LocalUsageRuntime(new(SettingsPath), ledger, [registration]);
        var result = Assert.Single((await runtime.StartAsync()).Sources);
        Assert.Equal(LocalUsageSourceStatus.RebuildRequired, result.Status); Assert.Equal(2, admission.Calls); await AssertNoCommit();
    }

    [Fact]
    public async Task Pi_adapter_revalidates_immediately_after_path_open()
    {
        Directory.CreateDirectory(_root); await File.WriteAllTextAsync(SourcePath, "{\"type\":\"session\",\"version\":3,\"id\":\"synthetic\"}\n");
        await new LocalUsageSettings(SettingsPath).SaveAsync(new(false, true), default); var admission = new SequencedAdmission();
        var registration = new LocalUsageSourceRegistration(UsageTool.Pi, LocalUsageAdapterKind.PiJsonlV3, SourcePath, Id(), 3, admission);
        await using var ledger = new SqliteUsageEventLedger(LedgerPath); await using var runtime = new LocalUsageRuntime(new(SettingsPath), ledger, [registration]);
        Assert.Equal(LocalUsageSourceStatus.RebuildRequired, Assert.Single((await runtime.StartAsync()).Sources).Status); Assert.Equal(2, admission.Calls); await AssertNoCommit(UsageTool.Pi);
    }

    [Fact]
    public async Task Operational_admission_failure_is_isolated_as_unavailable()
    {
        var (fileSystem, registration) = Registration(); fileSystem.Fail(SourcePath); var calls = 0;
        await using var runtime = Runtime(registration, (item, checkpoint, _, _) => { calls++; return ValueTask.FromResult(Empty(item, checkpoint)); });
        var result = Assert.Single((await runtime.StartAsync()).Sources);
        Assert.Equal(LocalUsageSourceStatus.Unavailable, result.Status); Assert.Equal([LocalUsageWarning.Unavailable], result.WarningCodes); Assert.Equal(0, calls); await AssertNoCommit();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Candidate_or_ancestor_reparse_change_fails_closed(bool candidate)
    {
        var (fileSystem, registration) = Registration(); fileSystem.Reparse(candidate ? SourcePath : _root); var calls = 0;
        await using var runtime = Runtime(registration, (item, checkpoint, _, _) => { calls++; return ValueTask.FromResult(Empty(item, checkpoint)); });
        Assert.Equal(LocalUsageSourceStatus.RebuildRequired, Assert.Single((await runtime.StartAsync()).Sources).Status); Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Disabled_source_performs_zero_admission_and_prepare_calls()
    {
        var (fileSystem, registration) = Registration(enabled: false); fileSystem.ResetCalls(); var prepareCalls = 0;
        await using var runtime = Runtime(registration, (item, checkpoint, _, _) => { prepareCalls++; return ValueTask.FromResult(Empty(item, checkpoint)); });
        Assert.Equal(LocalUsageSourceStatus.Disabled, Assert.Single((await runtime.StartAsync()).Sources).Status);
        Assert.Equal((0, 0), (fileSystem.Calls, prepareCalls));
    }

    [Fact]
    public async Task Admission_cancellation_propagates_without_prepare_or_commit()
    {
        using var cancellation = new CancellationTokenSource(); var (_, registration) = Registration(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => registration.Admission!.Validate(cancellation.Token));
        await AssertNoCommit();
    }

    [Fact]
    public async Task Runtime_stop_cancels_blocked_admission_without_prepare()
    {
        Directory.CreateDirectory(_root); await new LocalUsageSettings(SettingsPath).SaveAsync(new(true, false), default);
        var admission = new BlockingAdmission(); var manual = Manual(); var registration = new LocalUsageSourceRegistration(
            manual.Tool, manual.Adapter, manual.Path, manual.Identity, manual.SourceSchemaVersion, admission); var calls = 0;
        await using var runtime = Runtime(registration, (item, checkpoint, _, _) => { calls++; return ValueTask.FromResult(Empty(item, checkpoint)); });
        var run = runtime.StartAsync().AsTask(); await admission.Entered.Task; await runtime.StopAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run); Assert.Equal(0, calls); await AssertNoCommit();
    }

    [Fact]
    public void Proof_is_private_immutable_and_does_not_render_evidence()
    {
        var (fileSystem, registration) = Registration(); var proof = registration.Admission!; var text = proof.ToString();
        Assert.DoesNotContain(_root, text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(proof.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public));
        fileSystem.Replace(SourcePath); Assert.Equal(LocalUsageAdmissionStatus.PathChanged, proof.Validate(default));
        Assert.DoesNotContain(typeof(LocalUsageSourceRunResult).GetProperties(), property => property.Name.Contains("Admission", StringComparison.OrdinalIgnoreCase));
    }

    private (SyntheticAdmissionFileSystem FileSystem, LocalUsageSourceRegistration Registration) Registration(bool enabled = true)
    {
        Directory.CreateDirectory(_root); File.WriteAllText(SourcePath, "synthetic");
        var fs = new SyntheticAdmissionFileSystem(_root, SourcePath); var proof = LocalUsageSourceAdmission.Capture(fs, _root, SourcePath, LocalUsageAdmissionLayout.Direct, default)!;
        var registration = new LocalUsageSourceRegistration(UsageTool.OpenCode, LocalUsageAdapterKind.OpenCodeSqliteV1, SourcePath, Id(), 1, proof);
        new LocalUsageSettings(SettingsPath).SaveAsync(new(enabled, false), default).AsTask().GetAwaiter().GetResult(); return (fs, registration);
    }
    private LocalUsageRuntime Runtime(LocalUsageSourceRegistration registration,
        Func<LocalUsageSourceRegistration, UsageSourceCheckpoint?, int, CancellationToken, ValueTask<LocalUsagePreparedBatch>> prepare)
        => new(new LocalUsageSettings(SettingsPath), new SqliteUsageEventLedger(LedgerPath), [registration], 10, prepare);
    private LocalUsageSourceRegistration Manual() => new(UsageTool.OpenCode, LocalUsageAdapterKind.OpenCodeSqliteV1, SourcePath, Id(), 1);
    private static UsageSourceIdentity Id() => new(Enumerable.Repeat((byte)7, 32).ToArray());
    private static UsageSourceCheckpoint Next(LocalUsageSourceRegistration item) => new(item.Identity, item.Tool, 1, 0, new string('A', 64));
    private static LocalUsagePreparedBatch Empty(LocalUsageSourceRegistration item, UsageSourceCheckpoint? checkpoint) => new([], 0, 0, 0, [], checkpoint ?? Next(item));
    private async Task AssertNoCommit(UsageTool tool = UsageTool.OpenCode) { await using var ledger = new SqliteUsageEventLedger(LedgerPath); Assert.Equal(0, await ledger.CountAsync()); Assert.Null(await ledger.LoadCheckpointAsync(Id(), tool)); }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class SyntheticAdmissionFileSystem : ILocalUsageDiscoveryFileSystem
    {
        private readonly Dictionary<string, LocalUsagePathMetadata> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failures = new(StringComparer.OrdinalIgnoreCase); internal int Calls;
        internal SyntheticAdmissionFileSystem(string root, string source)
        {
            byte id = 1; _entries[source] = Metadata(LocalUsagePathKind.File, id++);
            for (var current = root; current is not null; current = Path.GetDirectoryName(current)) _entries[current] = Metadata(LocalUsagePathKind.Directory, id++);
        }
        public LocalUsagePathMetadata Inspect(string path, CancellationToken token)
        { Calls++; token.ThrowIfCancellationRequested(); if (_failures.Contains(path)) throw new IOException(); return _entries.GetValueOrDefault(path); }
        public IEnumerable<string> Enumerate(string directory, CancellationToken token) => throw new NotSupportedException();
        internal void ResetCalls() => Calls = 0;
        internal void Replace(string path) { var prior = _entries[path]; _entries[path] = prior with { Identity = new(9, Guid.NewGuid()) }; }
        internal void Reparse(string path) { var prior = _entries[path]; _entries[path] = prior with { IsReparsePoint = true }; }
        internal void Fail(string path) => _failures.Add(path);
        private static LocalUsagePathMetadata Metadata(LocalUsagePathKind kind, byte id) => new(kind, false, new(9, new Guid(Enumerable.Repeat(id, 16).ToArray())));
    }

    private sealed class BlockingAdmission : ILocalUsageSourceAdmission
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LocalUsageAdmissionStatus Validate(CancellationToken token)
        { Entered.SetResult(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return LocalUsageAdmissionStatus.Admitted; }
    }
    private sealed class SequencedAdmission : ILocalUsageSourceAdmission
    {
        internal int Calls;
        public LocalUsageAdmissionStatus Validate(CancellationToken token) { token.ThrowIfCancellationRequested(); return ++Calls == 1 ? LocalUsageAdmissionStatus.Admitted : LocalUsageAdmissionStatus.PathChanged; }
    }
}
