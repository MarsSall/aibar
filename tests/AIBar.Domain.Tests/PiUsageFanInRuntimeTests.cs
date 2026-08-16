using System.Text;
using System.Text.Json;
using AIBar.Application;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class PiUsageFanInRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-pi-fan-in-{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(_root, "settings.json");
    private string LedgerPath => Path.Combine(_root, "ledger.db");

    [Fact]
    public async Task Disabled_gates_all_source_access_and_input_and_result_are_private_and_strict()
    {
        Directory.CreateDirectory(_root); var proof = new Proof(); var secret = Path.Combine(_root, "private.jsonl");
        await new LocalUsageSettings(SettingsPath).SaveAsync(new(true, false), default);
        var input = Input(Candidate(secret, 1, proof)); await using var ledger = new SqliteUsageEventLedger(LedgerPath); var candidateCalls = 0;
        var result = await new PiUsageFanInRuntime(new(SettingsPath), ledger, input, beforeCandidate: _ => candidateCalls++).RunAsync();

        Assert.Equal((PiUsageFanInStatus.Disabled, 0, 0, 0), (result.Status, result.CandidatesAttempted, proof.Calls, candidateCalls));
        Assert.Equal(0, await ledger.CountAsync());
        var json = JsonSerializer.Serialize(result); Assert.DoesNotContain(secret, json); Assert.DoesNotContain("identity", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(typeof(PiUsageFanInResult).GetProperties(), property => new[] { "path", "identity", "raw", "exception", "project", "session", "content" }.Any(term => property.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(typeof(PiUsageFanInResult).GetProperties(), property => property.Name == "RecordsCommitted");
        var manual = new LocalUsageSourceRegistration(UsageTool.Pi, LocalUsageAdapterKind.PiJsonlV3, secret, Id(9), 3);
        Assert.Throws<ArgumentException>(() => new PiUsageFanInInput([manual]));
        Assert.Throws<ArgumentException>(() => new PiUsageFanInInput([]));
        Assert.Throws<ArgumentException>(() => new PiUsageFanInInput([Registration(secret, 1), Registration(Path.Combine(_root, "other.jsonl"), 1)]));
        Assert.Throws<ArgumentException>(() => new PiUsageFanInInput([Registration(secret, 1), Registration(secret.ToUpperInvariant(), 2)]));
        Assert.Throws<ArgumentException>(() => new PiUsageFanInInput([new(UsageTool.OpenCode, LocalUsageAdapterKind.OpenCodeSqliteV1, secret, Id(3), 1, new Proof())]));
        Assert.Throws<InvalidOperationException>(() => new LocalUsageDiscoveryResult([new(UsageTool.Pi, LocalUsageDiscoveryStatus.Unavailable, 0, 0, [])], []).CreatePiFanInInput());
        Assert.Throws<ArgumentException>(() => new LocalUsageDiscoveryResult([new(UsageTool.Pi, LocalUsageDiscoveryStatus.Ready, 1, 0, [])], [new(UsageTool.Pi, LocalUsageAdapterKind.OpenCodeSqliteV1, secret, Id(4), 1, new Proof())]).CreatePiFanInInput());
        Assert.Throws<ArgumentException>(() => new LocalUsageDiscoveryResult([new(UsageTool.Pi, LocalUsageDiscoveryStatus.Ready, 1, 0, [])], [new(UsageTool.Pi, LocalUsageAdapterKind.PiJsonlV3, secret, Id(4), 2, new Proof())]).CreatePiFanInInput());
    }

    [Fact]
    public async Task Two_files_replay_append_and_project_with_independent_checkpoint_and_event_id_namespaces()
    {
        Directory.CreateDirectory(_root); var a = await Session("a.jsonl", "same"); var b = await Session("b.jsonl", "same");
        var settings = new LocalUsageSettings(SettingsPath); await settings.SaveAsync(new(true, true), default);
        await using var ledger = new SqliteUsageEventLedger(LedgerPath); var runtime = new PiUsageFanInRuntime(settings, ledger, Input(Candidate(b, 2), Candidate(a, 1)));

        var initial = await runtime.RunAsync();
        Assert.Equal((PiUsageFanInStatus.Completed, 2, 2, 2, 2), (initial.Status, initial.CandidatesAttempted, initial.CandidatesCompleted, initial.RecordsCheckpointed, initial.EventsCommitted));
        Assert.Equal(2, await ledger.CountAsync()); Assert.Equal(0, (await runtime.RunAsync()).EventsCommitted);
        await File.AppendAllTextAsync(a, Assistant("append") + "\n", new UTF8Encoding(false));
        Assert.Equal(1, (await runtime.RunAsync()).EventsCommitted); Assert.Equal(3, await ledger.CountAsync());
        var first = await ledger.LoadCheckpointAsync(Id(1), UsageTool.Pi); var second = await ledger.LoadCheckpointAsync(Id(2), UsageTool.Pi);
        Assert.NotNull(first); Assert.NotNull(second); Assert.NotEqual(first!.State, second!.State);

        await AddOpenCodeEvent(ledger); var facts = await new UsageEventProjector(ledger, new(TimeZoneInfo.Utc, "fan-in-v1")).RebuildAsync();
        Assert.Equal(9, Assert.Single(facts.Where(item => item.Scope == UsageProjectionScope.Pi)).Tokens.Total);
        Assert.Equal(5, Assert.Single(facts.Where(item => item.Scope == UsageProjectionScope.OpenCode)).Tokens.Total);
        Assert.Equal(14, Assert.Single(facts.Where(item => item.Scope == UsageProjectionScope.Combined)).Tokens.Total);
    }

    [Fact]
    public async Task Warning_bearing_commits_are_partial_and_only_checkpointed_records_are_counted()
    {
        Directory.CreateDirectory(_root); var warning = Path.Combine(_root, "warning.jsonl");
        await File.WriteAllTextAsync(warning, Header() + "\n{}\n" + Assistant("valid") + "\n", new UTF8Encoding(false));
        var settings = new LocalUsageSettings(SettingsPath); await settings.SaveAsync(new(false, true), default);
        await using var ledger = new SqliteUsageEventLedger(LedgerPath);

        var result = await new PiUsageFanInRuntime(settings, ledger, Input(Candidate(warning, 1))).RunAsync();

        Assert.Equal((PiUsageFanInStatus.Partial, 1, 0, 1, 0, 0, 0, 2, 1),
            (result.Status, result.CandidatesAttempted, result.CandidatesCompleted, result.CandidatesPartial, result.CandidatesFailed,
                result.CandidatesRebuildRequired, result.CandidatesUnavailable, result.RecordsCheckpointed, result.EventsCommitted));
        Assert.Contains(LocalUsageWarning.MalformedRecord, result.WarningCodes); Assert.NotNull(await ledger.LoadCheckpointAsync(Id(1), UsageTool.Pi));
    }

    [Fact]
    public async Task All_unavailable_candidates_are_counted_separately_from_failures()
    {
        Directory.CreateDirectory(_root); var settings = new LocalUsageSettings(SettingsPath); await settings.SaveAsync(new(false, true), default);
        await using var ledger = new SqliteUsageEventLedger(LedgerPath);

        var result = await new PiUsageFanInRuntime(settings, ledger, Input(Candidate(Path.Combine(_root, "a.jsonl"), 1), Candidate(Path.Combine(_root, "b.jsonl"), 2))).RunAsync();

        Assert.Equal((PiUsageFanInStatus.Unavailable, 2, 0, 0, 0, 0, 2, 0),
            (result.Status, result.CandidatesAttempted, result.CandidatesCompleted, result.CandidatesPartial, result.CandidatesFailed,
                result.CandidatesRebuildRequired, result.CandidatesUnavailable, result.RecordsCheckpointed));
    }

    [Fact]
    public async Task Malformed_and_path_changed_files_are_isolated_from_prior_and_later_commits()
    {
        Directory.CreateDirectory(_root); var a = await Session("a.jsonl", "a"); var b = Path.Combine(_root, "b.jsonl");
        await File.WriteAllTextAsync(b, Header() + "\n" + new string('x', PiJsonlUsageSourceAdapter.MaximumRecordBytes + 1) + "\n");
        var c = await Session("c.jsonl", "c"); var d = await Session("d.jsonl", "d");
        var changed = new Proof(LocalUsageAdmissionStatus.Admitted, LocalUsageAdmissionStatus.Admitted, LocalUsageAdmissionStatus.PathChanged);
        var settings = new LocalUsageSettings(SettingsPath); await settings.SaveAsync(new(false, true), default); await using var ledger = new SqliteUsageEventLedger(LedgerPath);

        var result = await new PiUsageFanInRuntime(settings, ledger, Input(Candidate(d, 4), Candidate(c, 3, changed), Candidate(b, 2), Candidate(a, 1))).RunAsync();

        Assert.Equal((PiUsageFanInStatus.Partial, 4, 2, 0, 0, 1, 1, 2), (result.Status, result.CandidatesAttempted, result.CandidatesCompleted, result.CandidatesPartial, result.CandidatesFailed, result.CandidatesRebuildRequired, result.CandidatesUnavailable, result.EventsCommitted));
        Assert.Equal(result.WarningCodes.Order(), result.WarningCodes); Assert.Contains(LocalUsageWarning.OversizedRecord, result.WarningCodes); Assert.Contains(LocalUsageWarning.PathChanged, result.WarningCodes);
        Assert.NotNull(await ledger.LoadCheckpointAsync(Id(1), UsageTool.Pi)); Assert.Null(await ledger.LoadCheckpointAsync(Id(2), UsageTool.Pi));
        Assert.Null(await ledger.LoadCheckpointAsync(Id(3), UsageTool.Pi)); Assert.NotNull(await ledger.LoadCheckpointAsync(Id(4), UsageTool.Pi)); Assert.Equal(2, await ledger.CountAsync());
        await using var db = Open(LedgerPath); Assert.Equal(1L, await Scalar(db, "SELECT COUNT(*) FROM usage_source WHERE state=2 AND (warning_flags & 2048) != 0;"));
    }

    [Fact]
    public async Task Cancellation_between_ordinal_files_preserves_the_first_commit()
    {
        Directory.CreateDirectory(_root); var a = await Session("a.jsonl", "a"); var z = await Session("z.jsonl", "z");
        var settings = new LocalUsageSettings(SettingsPath); await settings.SaveAsync(new(false, true), default); await using var ledger = new SqliteUsageEventLedger(LedgerPath);
        using var cancellation = new CancellationTokenSource(); var runtime = new PiUsageFanInRuntime(settings, ledger, Input(Candidate(z, 2), Candidate(a, 1)), 1000, ordinal => { if (ordinal == 1) cancellation.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.RunAsync(cancellation.Token).AsTask());
        Assert.NotNull(await ledger.LoadCheckpointAsync(Id(1), UsageTool.Pi)); Assert.Null(await ledger.LoadCheckpointAsync(Id(2), UsageTool.Pi)); Assert.Equal(1, await ledger.CountAsync());
    }

    [Fact]
    public async Task Concurrent_runs_serialize_and_a_cancelled_queued_caller_does_not_cancel_the_owner()
    {
        Directory.CreateDirectory(_root); var file = await Session("one.jsonl", "one"); var settings = new LocalUsageSettings(SettingsPath); await settings.SaveAsync(new(false, true), default);
        await using var ledger = new SqliteUsageEventLedger(LedgerPath); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); var calls = 0;
        var runtime = new PiUsageFanInRuntime(settings, ledger, Input(Candidate(file, 1)), 1000, _ => { if (Interlocked.Increment(ref calls) == 1) { entered.Set(); release.Wait(); } });

        var first = runtime.RunAsync().AsTask(); Assert.True(entered.Wait(TimeSpan.FromSeconds(5))); using var cancellation = new CancellationTokenSource();
        var queued = runtime.RunAsync(cancellation.Token).AsTask(); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued); release.Set();
        Assert.Equal(PiUsageFanInStatus.Completed, (await first).Status); Assert.Equal(1, calls); Assert.Equal(1, await ledger.CountAsync());
    }

    private PiUsageFanInInput Input(params LocalUsageDiscoveredCandidate[] candidates) => new LocalUsageDiscoveryResult([new(UsageTool.Pi, candidates.Length == 1 ? LocalUsageDiscoveryStatus.Ready : LocalUsageDiscoveryStatus.RequiresFanIn, candidates.Length, 0, [])], candidates).CreatePiFanInInput();
    private LocalUsageDiscoveredCandidate Candidate(string path, byte id, ILocalUsageSourceAdmission? proof = null) => new(UsageTool.Pi, LocalUsageAdapterKind.PiJsonlV3, path, Id(id), 3, proof ?? new Proof());
    private LocalUsageSourceRegistration Registration(string path, byte id) => new(UsageTool.Pi, LocalUsageAdapterKind.PiJsonlV3, path, Id(id), 3, new Proof());
    private async Task<string> Session(string name, string id) { var path = Path.Combine(_root, name); await File.WriteAllTextAsync(path, Header() + "\n" + Assistant(id) + "\n", new UTF8Encoding(false)); return path; }
    private static UsageSourceIdentity Id(byte value) => new(Enumerable.Repeat(value, 32).ToArray());
    private static string Header() => JsonSerializer.Serialize(new { type = "session", version = 3, id = "synthetic", timestamp = At });
    private static string Assistant(string id) => JsonSerializer.Serialize(new { type = "message", id, timestamp = At, message = new { role = "assistant", api = "api", provider = "provider", model = "model", timestamp = At.ToUnixTimeMilliseconds(), stopReason = "stop", usage = new { input = 1, output = 2, reasoning = 1, cacheRead = 0, cacheWrite = 0, totalTokens = 3, cost = new { total = 0 } } } });
    private static async Task AddOpenCodeEvent(SqliteUsageEventLedger ledger) { var source = Id(8); await ledger.UpsertSourceAsync(new(source, UsageTool.OpenCode, 1, 1, At, At, UsageSourceState.Active, 0)); await ledger.UpsertBatchAsync([new(new(Enumerable.Repeat((byte)8, 32).ToArray()), source, new(Enumerable.Repeat((byte)9, 32).ToArray()), UsageTool.OpenCode, UsageEventKind.AssistantStep, UsagePurpose.Primary, UsageOutcome.Success, UsageFidelity.SuccessfulSettledStep, UsageFinishReason.Stop, At, At, At, new(null, null, "model"), new(2, 0, 0, 3, 0, 5), new(null, null), 1, false)]); }
    private static SqliteConnection Open(string path) { var db = new SqliteConnection($"Data Source={path};Pooling=False"); db.Open(); return db; }
    private static async Task<object?> Scalar(SqliteConnection db, string sql) { await using var command = db.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(); }
    private sealed class Proof(params LocalUsageAdmissionStatus[] statuses) : ILocalUsageSourceAdmission { private int _calls; public int Calls => _calls; public LocalUsageAdmissionStatus Validate(CancellationToken token) { token.ThrowIfCancellationRequested(); var index = Interlocked.Increment(ref _calls) - 1; return statuses.Length == 0 ? LocalUsageAdmissionStatus.Admitted : statuses[Math.Min(index, statuses.Length - 1)]; } }
    private static readonly DateTimeOffset At = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
