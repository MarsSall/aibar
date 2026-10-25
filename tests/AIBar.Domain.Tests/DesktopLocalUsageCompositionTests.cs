using System.Security.Cryptography;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class DesktopLocalUsageCompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Production_never_scans_local_usage_even_with_persisted_opt_in(bool enabled)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-local-unused-{Guid.NewGuid():N}");
        var home = Path.Combine(root, "synthetic-home");
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "local-usage-settings.json");
        await new LocalUsageSettings(settingsPath).SaveAsync(new(enabled, enabled), default);
        var settingsHash = SHA256.HashData(await File.ReadAllBytesAsync(settingsPath));
        var composition = App.CreateComposition(new(root, Path.Combine(root, "codex"), UserProfile: home));
        try
        {
            var presentation = Assert.IsType<BetaAnalyticsPresentation>(composition.Presentation);
            var publications = 0;
            presentation.PropertyChanged += (_, args) => publications += args.PropertyName == nameof(BetaAnalyticsPresentation.LocalUsage) ? 1 : 0;
            await composition.Initialize();
            await composition.Reevaluate!(RefreshTrigger.PopoverOpened, default);
            Assert.False(composition.Settings!.LocalUsageAvailable);
            Assert.Equal(0, publications);
            Assert.False(File.Exists(Path.Combine(root, "local-usage-identity-salt.bin")));
            Assert.False(File.Exists(Path.Combine(root, "local-usage.db")));
            Assert.False(Directory.Exists(home));
            Assert.Equal(settingsHash, SHA256.HashData(await File.ReadAllBytesAsync(settingsPath)));
        }
        finally
        {
            await composition.Resource.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Existing_local_usage_files_survive_startup_and_restart_until_clear_which_preserves_sources()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-local-retained-{Guid.NewGuid():N}");
        var home = Path.Combine(root, "synthetic-home");
        var roots = LocalUsageSourceRootResolver.Resolve(new(home));
        var openCodeSentinel = Path.Combine(roots.OpenCodeDataRoot!, "external.bin");
        var piSentinel = Path.Combine(roots.PiSessionsRoot!, "external.bin");
        Directory.CreateDirectory(roots.OpenCodeDataRoot!); Directory.CreateDirectory(roots.PiSessionsRoot!);
        await File.WriteAllBytesAsync(openCodeSentinel, [1, 2, 3]); await File.WriteAllBytesAsync(piSentinel, [4, 5, 6]);
        var owned = new[] { "local-usage-settings.json", "local-usage-identity-salt.bin", "local-usage.db", "local-usage.db-wal", "local-usage.db-shm" };
        await new LocalUsageSettings(Path.Combine(root, owned[0])).SaveAsync(new(true, true, roots.OpenCodeDataRoot), default);
        foreach (var path in owned.Skip(1)) await File.WriteAllBytesAsync(Path.Combine(root, path), [7, 8, 9]);
        var hashes = new Dictionary<string, byte[]>();
        foreach (var path in owned) hashes[path] = SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(root, path)));
        try
        {
            for (var restart = 0; restart < 2; restart++)
            {
                var composition = App.CreateComposition(new(root, Path.Combine(root, "codex"), UserProfile: home));
                try
                {
                    await composition.Initialize();
                    await composition.Reevaluate!(RefreshTrigger.PopoverOpened, default);
                    foreach (var path in owned) Assert.Equal(hashes[path], SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(root, path))));
                    if (restart == 1)
                    {
                        await composition.Settings!.ClearAiBarDataAsync(default);
                        Assert.All(owned, path => Assert.False(File.Exists(Path.Combine(root, path))));
                    }
                    Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(openCodeSentinel));
                    Assert.Equal(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(piSentinel));
                }
                finally { await composition.Resource.DisposeAsync(); }
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
