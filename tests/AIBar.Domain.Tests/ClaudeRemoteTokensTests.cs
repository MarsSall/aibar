using System.Text;
using AIBar.Application;

namespace AIBar.Domain.Tests;

public sealed class ClaudeRemoteTokensTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private const string Valid = """
        {"schemaVersion":1,"generatedAt":"2026-10-19T12:00:00Z","localDay":"2026-10-19","state":"current","days":[{"day":"2026-10-19","model":"claude-opus-5-5","input":1000000,"cachedInput":0,"cacheWrite":0,"output":0}]}
        """;
    private static AIBar.Domain.UsageCostSummary Parse(string json, DateTimeOffset? now = null) => ClaudeRemoteTokens.Parse(Encoding.UTF8.GetBytes(json), now ?? Now);

    [Fact]
    public void Valid_aggregate_is_priced_and_freshness_boundary_is_inclusive()
    {
        Assert.Equal(4m, Parse(Valid).Today.Cost);
        Assert.Equal(4m, Parse(Valid, Now.AddHours(2)).Today.Cost);
        Assert.Null(Parse(Valid, Now.AddHours(2).AddTicks(1)).Today.Cost);
        Assert.Null(Parse(Valid, Now.AddTicks(-1)).Today.Cost);
    }

    [Theory]
    [InlineData("2026-10-19T12:00:00Z", "October 19 2026 12:00Z")]
    [InlineData("2026-10-19T12:00:00Z", "2026-10-19T12:00:00+00:00")]
    [InlineData("\"generatedAt\":\"2026-10-19T12:00:00Z\"", "\"generatedAt\":null")]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":2")]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":1,\"secret\":\"private\"")]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")]
    [InlineData("\"input\":1000000", "\"input\":-1")]
    [InlineData("\"input\":1000000", "\"input\":true")]
    [InlineData("\"input\":1000000", "\"input\":1.5")]
    [InlineData("\"input\":1000000", "\"input\":9223372036854775808")]
    [InlineData("\"input\":1000000", "\"input\":1,\"input\":2")]
    [InlineData("\"output\":0", "\"output\":0,\"prompt\":\"private\"")]
    [InlineData("\"output\":0", "\"extra\":0")]
    [InlineData("claude-opus-5-5", "../private/path")]
    [InlineData("\"day\":\"2026-10-19\"", "\"day\":\"2026-09-19\"")]
    [InlineData("\"day\":\"2026-10-19\"", "\"day\":\"2026-10-20\"")]
    [InlineData("\"state\":\"current\"", "\"state\":\"unknown\"")]
    public void Strict_reader_rejects_invalid_documents_without_showing_zero(string from, string to)
    {
        Assert.Null(Parse(Valid.Replace(from, to)).Today.Cost);
    }

    [Fact]
    public void Partial_unknown_and_unavailable_never_masquerade_as_complete_zero()
    {
        Assert.True(Parse(Valid.Replace("current", "partial")).Today.Partial);
        Assert.Null(Parse(Valid.Replace("claude-opus-5-5", "new-model")).Today.Cost);
        Assert.Null(Parse(Valid.Replace("current", "unavailable")).Today.Cost);
        Assert.Null(ClaudeRemoteTokens.Parse(new byte[ClaudeRemoteTokens.MaxBytes + 1], Now).Today.Cost);
        Assert.Null(Parse("null").Today.Cost);
    }

    [Fact]
    public async Task Reader_bounds_file_reads_and_handles_missing_invalid_and_reparse_files()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aibar-token-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "claude-remote-tokens.json");
        var link = Path.Combine(directory, "link.json");
        try
        {
            Assert.Null((await ClaudeRemoteTokens.ReadAsync(path, Now)).Today.Cost);
            await File.WriteAllTextAsync(path, Valid);
            Assert.Equal(4m, (await ClaudeRemoteTokens.ReadAsync(path, Now)).Today.Cost);
            await File.WriteAllTextAsync(path, Valid.PadRight(ClaudeRemoteTokens.MaxBytes));
            Assert.Equal(4m, (await ClaudeRemoteTokens.ReadAsync(path, Now)).Today.Cost);
            await File.AppendAllTextAsync(path, " ");
            Assert.Null((await ClaudeRemoteTokens.ReadAsync(path, Now)).Today.Cost);
            await File.WriteAllTextAsync(path, "not json");
            Assert.Null((await ClaudeRemoteTokens.ReadAsync(path, Now)).Today.Cost);
            // Windows symlink creation may require Developer Mode; don't make that a test prerequisite.
            try { File.CreateSymbolicLink(link, path); }
            catch (UnauthorizedAccessException) { return; }
            catch (IOException) { return; }
            await File.WriteAllTextAsync(path, Valid);
            Assert.Null((await ClaudeRemoteTokens.ReadAsync(link, Now)).Today.Cost);
        }
        finally
        {
            if (File.Exists(link)) File.Delete(link);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Duplicate_day_model_rows_and_overflow_are_rejected()
    {
        var start = Valid.IndexOf("[{", StringComparison.Ordinal) + 1;
        var end = Valid.LastIndexOf(']');
        var row = Valid[start..end];
        Assert.Null(Parse(Valid.Replace(row, row + "," + row)).Today.Cost);
        Assert.Null(Parse(Valid.Replace("1000000", long.MaxValue.ToString()).Replace("\"output\":0", "\"output\":1")).Today.Cost);
    }
}
