using System.Security.Cryptography;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class ClearAiBarDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-clear-{Guid.NewGuid():N}");
    private readonly string _codex = Path.Combine(Path.GetTempPath(), $"codex-clear-{Guid.NewGuid():N}");

    [Fact]
    public async Task Clear_cancels_and_awaits_work_deletes_only_allowlisted_data_and_preserves_codex_bytes()
    {
        Directory.CreateDirectory(Path.Combine(_root, "cache"));
        Directory.CreateDirectory(Path.Combine(_root, "logs", "nested"));
        await File.WriteAllTextAsync(Path.Combine(_root, "cache", "quota.json"), "cache");
        await File.WriteAllTextAsync(Path.Combine(_root, "logs", "nested", "log.txt"), "log");
        await File.WriteAllTextAsync(Path.Combine(_root, "aibar.db"), "database");
        await File.WriteAllTextAsync(Path.Combine(_root, "settings.json"), "settings");
        Directory.CreateDirectory(_codex);
        var source = Path.Combine(_codex, "session.jsonl");
        await File.WriteAllTextAsync(source, "synthetic codex source");
        var before = Hash(source); var work = new BlockingWork(); var recreated = 0;
        var service = new ClearAiBarDataService(_root, [work], async token =>
        {
            recreated++; Directory.CreateDirectory(Path.Combine(_root, "cache"));
            await File.WriteAllTextAsync(Path.Combine(_root, "aibar.db"), "empty", token);
        });

        await service.ClearAsync(default);

        Assert.Equal(["cancel", "await", "resume"], work.Events);
        Assert.Equal(1, recreated); Assert.Equal(before, Hash(source));
        Assert.True(Directory.Exists(Path.Combine(_root, "cache")));
        Assert.Equal("empty", await File.ReadAllTextAsync(Path.Combine(_root, "aibar.db")));
        Assert.False(Directory.Exists(Path.Combine(_root, "logs")));
        Assert.False(File.Exists(Path.Combine(_root, "settings.json")));
    }

    [Fact]
    public async Task Clear_is_idempotent_and_recreates_after_each_successful_retry()
    {
        var recreations = 0;
        var service = new ClearAiBarDataService(_root, [], token =>
        {
            recreations++; Directory.CreateDirectory(_root); return ValueTask.CompletedTask;
        });

        await service.ClearAsync(default);
        await service.ClearAsync(default);

        Assert.Equal(2, recreations);
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public async Task Clear_failure_does_not_report_success_and_a_retry_recreates_the_empty_state()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "settings.json"), "old");
        var attempts = 0;
        var service = new ClearAiBarDataService(_root, [], _ =>
        {
            if (++attempts == 1) throw new IOException("synthetic recreation failure");
            return ValueTask.CompletedTask;
        });

        await Assert.ThrowsAsync<IOException>(() => service.ClearAsync(default).AsTask());
        await service.ClearAsync(default);

        Assert.Equal(2, attempts);
        Assert.False(File.Exists(Path.Combine(_root, "settings.json")));
    }

    [Fact]
    public async Task Clear_locked_allowlisted_file_fails_closed_then_retries_after_the_windows_lock_is_released()
    {
        Directory.CreateDirectory(_root);
        var database = Path.Combine(_root, "aibar.db"); var outside = Path.Combine(_codex, "source.jsonl");
        Directory.CreateDirectory(_codex); await File.WriteAllTextAsync(database, "owned"); await File.WriteAllTextAsync(outside, "codex");
        await using (new FileStream(database, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var service = new ClearAiBarDataService(_root, [], _ => ValueTask.CompletedTask);
            await Assert.ThrowsAsync<IOException>(() => service.ClearAsync(default).AsTask());
            Assert.True(File.Exists(database)); Assert.Equal("codex", await File.ReadAllTextAsync(outside));
        }
        await new ClearAiBarDataService(_root, [], _ => ValueTask.CompletedTask).ClearAsync(default);
        Assert.False(File.Exists(database)); Assert.Equal("codex", await File.ReadAllTextAsync(outside));
    }

    [Fact]
    public async Task Clear_recreates_a_valid_empty_database_that_accepts_the_next_synthetic_rescan()
    {
        Directory.CreateDirectory(_root); Directory.CreateDirectory(_codex);
        var database = Path.Combine(_root, "aibar.db"); var source = Path.Combine(_codex, "session.jsonl");
        await File.WriteAllTextAsync(database, "old"); await File.WriteAllTextAsync(source, Line(2) + "\n"); var before = Hash(source);
        var service = new ClearAiBarDataService(_root, [], async token =>
        {
            await using var empty = new SqliteDailyModelUsageStore(database); Assert.Empty(await empty.LoadAsync(token));
        });

        await service.ClearAsync(default);
        await using var store = new SqliteDailyModelUsageStore(database);
        var result = await new AnalyticsScanCoordinator(new SessionJsonlScanner(new SessionCheckpointStore(), "v1"), store, new AnalyticsPolicy(new TimeZoneLocalDayPolicy(TimeZoneInfo.Utc, "v1"))).ScanAsync(source, default);

        Assert.Equal(new TokenTotals(2, 0, 0), Assert.Single(result.Usage).Tokens); Assert.Equal(before, Hash(source));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("cache/../settings.json")]
    public void Clear_rejects_ambiguous_or_traversing_owned_paths(string ownedPath)
    {
        Assert.Throws<ArgumentException>(() => new ClearAiBarDataService(_root, [], _ => ValueTask.CompletedTask, [ownedPath]));
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Line(long input) => $"{{\"timestamp\":\"2030-01-01T00:00:00Z\",\"model\":\"gpt-5\",\"usage\":{{\"input_tokens\":{input},\"cached_input_tokens\":0,\"output_tokens\":0}}}}";
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); if (Directory.Exists(_codex)) Directory.Delete(_codex, true); }

    private sealed class BlockingWork : IAiBarClearWork
    {
        public List<string> Events { get; } = [];
        public ValueTask CancelAndWaitAsync(CancellationToken cancellationToken) { Events.Add("cancel"); Events.Add("await"); return ValueTask.CompletedTask; }
        public void ResumeAfterClear() => Events.Add("resume");
    }
}
