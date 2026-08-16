using AIBar.Application;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class LocalUsageRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-runtime-{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(_root, "settings.json");
    private string LedgerPath => Path.Combine(_root, "ledger.db");
    private string OpenCodePath => Path.Combine(_root, "opencode.db");
    private string PiPath => Path.Combine(_root, "pi.jsonl");

    [Fact]
    public async Task Rejects_ambiguous_or_mismatched_explicit_registrations()
    {
        Directory.CreateDirectory(_root); var settings = new LocalUsageSettings(SettingsPath); await using var ledger = new SqliteUsageEventLedger(LedgerPath);
        Assert.Throws<ArgumentException>(() => new LocalUsageSourceRegistration(UsageTool.OpenCode, LocalUsageAdapterKind.PiJsonlV3, PiPath, Id(1), 3));
        Assert.Throws<ArgumentException>(() => new LocalUsageSourceRegistration(UsageTool.OpenCode, LocalUsageAdapterKind.OpenCodeSqliteV1, OpenCodePath, Id(1), 2));
        Assert.Throws<ArgumentException>(() => new LocalUsageSourceRegistration(UsageTool.OpenCode, LocalUsageAdapterKind.OpenCodeSqliteV1, " ", Id(1), 1));
        Assert.Throws<ArgumentException>(() => new LocalUsageSourceRegistration(UsageTool.OpenCode, LocalUsageAdapterKind.OpenCodeSqliteV1, Path.GetPathRoot(_root)!, Id(1), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalUsageSourceRegistration((UsageTool)99, LocalUsageAdapterKind.OpenCodeSqliteV1, OpenCodePath, Id(1), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalUsageSourceRegistration(UsageTool.OpenCode, (LocalUsageAdapterKind)99, OpenCodePath, Id(1), 1));
    }

    private static UsageSourceIdentity Id(byte value) => new(Enumerable.Repeat(value, 32).ToArray());
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
