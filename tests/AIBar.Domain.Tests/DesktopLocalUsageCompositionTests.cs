using System.Security.Cryptography;
using AIBar.Application;
using AIBar.Desktop;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class DesktopLocalUsageCompositionTests
{
    [Fact]
    public async Task Both_disabled_startup_touches_no_local_usage_identity_ledger_or_source_root()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-local-disabled-{Guid.NewGuid():N}");
        var home = Path.Combine(root, "synthetic-home");
        var composition = App.CreateComposition(new(root, Path.Combine(root, "codex"), UserProfile: home));
        try
        {
            var presentation = Assert.IsType<BetaAnalyticsPresentation>(composition.Presentation);
            var publications = 0;
            presentation.PropertyChanged += (_, args) => publications += args.PropertyName == nameof(BetaAnalyticsPresentation.LocalUsage) ? 1 : 0;
            await composition.Initialize();
            Assert.Equal("Retained history was not loaded", presentation.LocalUsage.StatusLabel);
            await composition.Reevaluate!(AIBar.Domain.RefreshTrigger.PopoverOpened, default);
            var outcome = await composition.RefreshLocalUsage!(default);

            Assert.All(outcome.Tools, tool => Assert.Equal(LocalUsageSourceStatus.Disabled, tool.Status));
            Assert.Equal(6, publications);
            Assert.False(File.Exists(Path.Combine(root, "local-usage-identity-salt.bin")));
            Assert.False(File.Exists(Path.Combine(root, "local-usage.db")));
            Assert.False(Directory.Exists(home));
        }
        finally
        {
            await composition.Resource.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task OpenCode_opt_in_does_not_touch_disabled_Pi_root_and_disabling_all_keeps_ledger_history()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-local-opencode-{Guid.NewGuid():N}");
        var home = Path.Combine(root, "synthetic-home");
        var roots = LocalUsageSourceRootResolver.Resolve(new(home));
        Directory.CreateDirectory(roots.OpenCodeDataRoot!);
        var composition = App.CreateComposition(new(root, Path.Combine(root, "codex"), UserProfile: home));
        try
        {
            await composition.Initialize();

            var enabled = await composition.Settings!.ToggleOpenCodeLocalUsageAsync(default);

            Assert.Equal(new(true, false), enabled);
            Assert.False(Directory.Exists(roots.PiSessionsRoot));
            Assert.True(File.Exists(Path.Combine(root, "local-usage.db")));

            Assert.Equal(LocalUsagePolicy.Disabled, await composition.Settings.ToggleOpenCodeLocalUsageAsync(default));
            Assert.True(File.Exists(Path.Combine(root, "local-usage.db")));
            Assert.False(Directory.Exists(roots.PiSessionsRoot));
        }
        finally
        {
            await composition.Resource.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Enabled_startup_keeps_salt_clear_preserves_sources_and_lifetime_orders_ledger_last()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-local-enabled-{Guid.NewGuid():N}");
        var home = Path.Combine(root, "synthetic-home");
        var roots = LocalUsageSourceRootResolver.Resolve(new(home));
        var openCodeSentinel = Path.Combine(roots.OpenCodeDataRoot!, "external.bin");
        var piSentinel = Path.Combine(roots.PiSessionsRoot!, "external.bin");
        Directory.CreateDirectory(roots.OpenCodeDataRoot!); Directory.CreateDirectory(roots.PiSessionsRoot!);
        await File.WriteAllBytesAsync(openCodeSentinel, [1, 2, 3]); await File.WriteAllBytesAsync(piSentinel, [4, 5, 6]);
        await new LocalUsageSettings(Path.Combine(root, "local-usage-settings.json")).SaveAsync(new(true, true), default);
        var observations = new List<string>();
        var first = App.CreateComposition(new(root, Path.Combine(root, "codex"), LifecycleObservation: observations.Add, UserProfile: home));
        try
        {
            await first.Initialize();
            Assert.True(File.Exists(Path.Combine(root, "local-usage.db")));
            var initialSalt = SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(root, "local-usage-identity-salt.bin")));
            await first.Resource.DisposeAsync();
            Assert.True(observations.IndexOf("local_usage_coordinator_disposed") < observations.IndexOf("local_usage_ledger_disposed"));

            observations.Clear();
            var second = App.CreateComposition(new(root, Path.Combine(root, "codex"), LifecycleObservation: observations.Add, UserProfile: home));
            try
            {
                await second.Initialize();
                Assert.Equal(initialSalt, SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(root, "local-usage-identity-salt.bin"))));

                await second.Settings!.ClearAiBarDataAsync(default);

                Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(openCodeSentinel));
                Assert.Equal(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(piSentinel));
                Assert.All(new[] { "local-usage-settings.json", "local-usage-identity-salt.bin", "local-usage.db", "local-usage.db-wal", "local-usage.db-shm" },
                    path => Assert.False(File.Exists(Path.Combine(root, path))));
                Assert.All((await second.RefreshLocalUsage!(default)).Tools, tool => Assert.Equal(LocalUsageSourceStatus.Disabled, tool.Status));
            }
            finally { await second.Resource.DisposeAsync(); }
        }
        finally
        {
            await first.Resource.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Disabled_selection_avoids_source_access_restart_uses_custom_and_live_reset_retains_default_history()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-local-custom-{Guid.NewGuid():N}");
        var home = Path.Combine(root, "profile"); var defaultRoot = LocalUsageSourceRootResolver.Resolve(new(home)).OpenCodeDataRoot!;
        var customRoot = Path.Combine(root, "custom-opencode");
        var defaultDb = await CreateOpenCode(defaultRoot, "default"); var customDb = await CreateOpenCode(customRoot, "custom");
        var defaultHash = SHA256.HashData(await File.ReadAllBytesAsync(defaultDb)); var customHash = SHA256.HashData(await File.ReadAllBytesAsync(customDb));
        try
        {
            var first = App.CreateComposition(new(root, Path.Combine(root, "codex"), UserProfile: home));
            await first.Initialize();
            await using (var locked = new FileStream(customDb, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var selected = await first.Settings!.ChooseOpenCodeDataRootAsync(customRoot, default);
                Assert.Equal(new(false, false, customRoot), selected);
                Assert.False(File.Exists(Path.Combine(root, "local-usage.db"))); Assert.False(File.Exists(Path.Combine(root, "local-usage-identity-salt.bin")));
            }
            await first.Resource.DisposeAsync();

            var second = App.CreateComposition(new(root, Path.Combine(root, "codex"), UserProfile: home));
            try
            {
                await second.Initialize();
                Assert.Equal(customRoot, (await second.Settings!.LoadLocalUsagePolicyAsync(default)).OpenCodeDataRoot);
                await second.Settings.ToggleOpenCodeLocalUsageAsync(default);
                var custom = await second.RefreshLocalUsage!(default);
                Assert.Equal(4, Assert.Single(custom.ProjectionFacts.Where(item => item.Scope == UsageProjectionScope.OpenCode)).Tokens.Total);

                await second.Settings.ResetOpenCodeDataRootAsync(default);
                var reset = await second.RefreshLocalUsage!(default);
                Assert.Equal(8, Assert.Single(reset.ProjectionFacts.Where(item => item.Scope == UsageProjectionScope.OpenCode)).Tokens.Total);
                Assert.Null(second.Settings.LocalUsagePolicy.OpenCodeDataRoot);
                Assert.Equal(defaultHash, SHA256.HashData(await File.ReadAllBytesAsync(defaultDb)));
                Assert.Equal(customHash, SHA256.HashData(await File.ReadAllBytesAsync(customDb)));
            }
            finally { await second.Resource.DisposeAsync(); }
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task<string> CreateOpenCode(string root, string id)
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, "opencode.db");
        await using var db = new SqliteConnection($"Data Source={path};Pooling=False"); await db.OpenAsync(); await using var command = db.CreateCommand();
        command.CommandText = "CREATE TABLE message(id TEXT PRIMARY KEY,time_created INTEGER,time_updated INTEGER,data TEXT); CREATE TABLE part(id TEXT PRIMARY KEY,message_id TEXT,time_created INTEGER,time_updated INTEGER,data TEXT); INSERT INTO message VALUES($message,1,1,$messageData); INSERT INTO part VALUES($part,$message,1,1,$partData);";
        command.Parameters.AddWithValue("$message", "message-" + id); command.Parameters.AddWithValue("$part", id);
        command.Parameters.AddWithValue("$messageData", "{\"role\":\"assistant\",\"providerID\":\"provider\",\"modelID\":\"model\"}"); command.Parameters.AddWithValue("$partData", "{\"type\":\"step-finish\",\"reason\":\"stop\",\"tokens\":{\"total\":4,\"input\":1,\"output\":2,\"reasoning\":1,\"cache\":{\"read\":0,\"write\":0}}}");
        await command.ExecuteNonQueryAsync(); return path;
    }
}
