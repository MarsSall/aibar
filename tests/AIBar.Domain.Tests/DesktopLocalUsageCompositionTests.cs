using AIBar.Application;
using AIBar.Desktop;

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
}
