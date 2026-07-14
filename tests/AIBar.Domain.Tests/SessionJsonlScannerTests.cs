using System.Text;
using AIBar.Application;

namespace AIBar.Domain.Tests;

public sealed class SessionJsonlScannerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aibar-jsonl-{Guid.NewGuid():N}.jsonl");

    [Fact]
    public async Task Invalid_checkpoint_offsets_rebuild_from_zero()
    {
        await File.WriteAllTextAsync(_path, Line("trusted", 1, 2, 3) + "\n");
        var store = new SessionCheckpointStore();
        var info = new FileInfo(_path);
        foreach (var offset in new[] { -1L, info.Length + 1, 1L })
        {
            await store.CommitAsync(_path, new(info.CreationTimeUtc.Ticks, info.Length, info.LastWriteTimeUtc.Ticks, offset, "v1"), default);
            var result = await new SessionJsonlScanner(store, "v1").ScanAsync(_path, default);
            Assert.True(result.RebuildRequired);
            Assert.Equal("trusted", Assert.Single(result.Records).Model);
        }
    }

    [Fact]
    public async Task Identity_only_checkpoint_mismatch_rebuilds_even_when_length_and_time_match()
    {
        await File.WriteAllTextAsync(_path, Line("trusted", 1, 0, 0) + "\n");
        var store = new SessionCheckpointStore();
        var scanner = new SessionJsonlScanner(store, "v1");
        await scanner.ScanAsync(_path, default);
        var checkpoint = await store.LoadAsync(_path, default);
        await store.CommitAsync(_path, checkpoint! with { Identity = checkpoint.Identity + 1 }, default);

        var result = await scanner.ScanAsync(_path, default);

        Assert.True(result.RebuildRequired);
        Assert.Equal("trusted", Assert.Single(result.Records).Model);
    }

    [Fact]
    public async Task Extracts_only_minimal_fields_clamps_counters_and_maps_untrusted_models_to_unknown()
    {
        await File.WriteAllTextAsync(_path,
            Line("trusted", -1, 2, -3) + "\n" +
            Line("not trusted", 4, 5, 6) + "\n" +
            "{\"timestamp\":\"2030-01-03T00:00:00Z\",\"usage\":{\"input_tokens\":7,\"cached_input_tokens\":8,\"output_tokens\":9}}\n");

        var records = (await new SessionJsonlScanner(new SessionCheckpointStore(), "v1").ScanAsync(_path, default)).Records;
        Assert.Collection(records,
            record =>
            {
                Assert.Equal(DateTimeOffset.Parse("2030-01-01T00:00:00Z"), record.Timestamp);
                Assert.Equal("trusted", record.Model);
                Assert.Equal((0, 2, 0), (record.InputTokens, record.CachedInputTokens, record.OutputTokens));
            },
            record => Assert.Equal(("Unknown", 4L, 5L, 6L), (record.Model, record.InputTokens, record.CachedInputTokens, record.OutputTokens)),
            record => Assert.Equal(("Unknown", 7L, 8L, 9L), (record.Model, record.InputTokens, record.CachedInputTokens, record.OutputTokens)));
    }

    [Fact]
    public async Task Changing_input_during_read_returns_path_free_warning_and_preserves_checkpoint()
    {
        await File.WriteAllTextAsync(_path, Line("first", 1, 0, 0) + "\n");
        var store = new SessionCheckpointStore();
        var initial = new SessionJsonlScanner(store, "v1");
        await initial.ScanAsync(_path, default);
        var checkpoint = await store.LoadAsync(_path, default);
        var mutate = true;
        var scanner = new SessionJsonlScanner(store, "v1", () =>
        {
            if (!mutate) return;
            mutate = false;
            File.AppendAllText(_path, Line("later", 2, 0, 0) + "\n");
        });

        await File.AppendAllTextAsync(_path, Line("next", 1, 0, 0) + "\n");
        var result = await scanner.ScanAsync(_path, default);

        Assert.Empty(result.Records);
        Assert.Equal(["session_file_changed"], result.WarningCodes);
        Assert.Equal(checkpoint, await store.LoadAsync(_path, default));
    }

    [Theory]
    [InlineData("{\"timestamp\":42,\"model\":\"bad-time\"}")]
    [InlineData("{\"timestamp\":\"2030-01-01T00:00:00Z\",\"model\":42}")]
    public async Task Non_string_minimal_fields_are_malformed_and_do_not_abort_the_scan(string malformed)
    {
        await File.WriteAllTextAsync(_path, malformed + "\n" + Line("valid", 1, 0, 0) + "\n");

        var result = await new SessionJsonlScanner(new SessionCheckpointStore(), "v1").ScanAsync(_path, default);

        Assert.Equal("valid", Assert.Single(result.Records).Model);
        Assert.Equal(["session_malformed_record"], result.WarningCodes);
    }

    [Fact]
    public async Task Incomplete_tail_keeps_checkpoint_at_last_complete_boundary_and_remains_reported_when_unchanged()
    {
        var complete = Line("first", 1, 0, 0) + "\n";
        await File.WriteAllTextAsync(_path, complete + Line("tail", 2, 0, 0));
        var store = new SessionCheckpointStore();
        var scanner = new SessionJsonlScanner(store, "v1");

        var result = await scanner.ScanAsync(_path, default);
        var checkpoint = await store.LoadAsync(_path, default);
        var unchanged = await scanner.ScanAsync(_path, default);

        Assert.Equal("first", Assert.Single(result.Records).Model);
        Assert.Contains("session_incomplete_tail", result.WarningCodes);
        Assert.Equal(Encoding.UTF8.GetByteCount(complete), checkpoint!.Offset);
        Assert.Empty(unchanged.Records);
        Assert.Equal(["session_incomplete_tail"], unchanged.WarningCodes);
        Assert.Equal(checkpoint, await store.LoadAsync(_path, default));
    }

    [Fact]
    public async Task Cancellation_before_commit_preserves_existing_checkpoint()
    {
        await File.WriteAllTextAsync(_path, Line("first", 1, 0, 0) + "\n");
        var store = new SessionCheckpointStore();
        await new SessionJsonlScanner(store, "v1").ScanAsync(_path, default);
        var checkpoint = await store.LoadAsync(_path, default);
        await File.AppendAllTextAsync(_path, Line("next", 2, 0, 0) + "\n");
        using var cancellation = new CancellationTokenSource();
        var scanner = new SessionJsonlScanner(store, "v1", cancellation.Cancel);

        await Assert.ThrowsAsync<OperationCanceledException>(() => scanner.ScanAsync(_path, cancellation.Token).AsTask());
        Assert.Equal(checkpoint, await store.LoadAsync(_path, default));
    }

    private static string Line(string model, long input, long cached, long output) =>
        $"{{\"timestamp\":\"2030-01-01T00:00:00Z\",\"model\":\"{model}\",\"usage\":{{\"input_tokens\":{input},\"cached_input_tokens\":{cached},\"output_tokens\":{output}}}}}";

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
