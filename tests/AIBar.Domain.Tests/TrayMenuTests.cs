using System.Reflection;
using AIBar.Application;
using AIBar.Desktop;
using Forms = System.Windows.Forms;

namespace AIBar.Domain.Tests;

public sealed class TrayMenuTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Menu_contains_only_actions_in_order_and_refresh_tracks_capability(bool refreshAvailable)
    {
        await using var tray = new WindowsTrayRuntime();
        tray.SetRefreshAvailable(refreshAvailable);
        tray.SetSettingsAvailable(true);
        Assert.Equal(refreshAvailable
            ? new[] { "Refresh", "Settings…", "Start with Windows", "Exit AIBar" }
            : new[] { "Settings…", "Start with Windows", "Exit AIBar" }, Texts(tray));
        tray.SetRefreshAvailable(!refreshAvailable);
        tray.SetSettingsAvailable(true);
        Assert.Equal(!refreshAvailable
            ? new[] { "Refresh", "Settings…", "Start with Windows", "Exit AIBar" }
            : new[] { "Settings…", "Start with Windows", "Exit AIBar" }, Texts(tray));
    }

    [Fact]
    public async Task Settings_and_exit_raise_their_actions_and_startup_uses_registration_readback()
    {
        await using var tray = new WindowsTrayRuntime();
        var startup = new Startup();
        var commands = new NativeSettingsCommands(startup, new Clear(), new());
        var settings = 0; var exits = 0;
        tray.SettingsRequested += () => settings++;
        tray.ExitRequested += () => exits++;
        tray.StartupToggleRequested += () => tray.SetStartupEnabled(commands.ToggleStartupAsync(default).AsTask().GetAwaiter().GetResult());
        tray.SetSettingsAvailable(true);
        Item(tray, "Settings…").PerformClick();
        Item(tray, "Exit AIBar").PerformClick();
        Assert.Equal(1, settings); Assert.Equal(1, exits);
        var item = Item(tray, "Start with Windows");
        Assert.False(item.CheckOnClick); Assert.False(item.Checked);
        item.PerformClick(); Assert.True(item.Checked); Assert.True(startup.Enabled);
        item.PerformClick(); Assert.False(item.Checked); Assert.False(startup.Enabled);
        startup.AcceptChanges = false;
        item.PerformClick(); Assert.False(item.Checked); Assert.False(startup.Enabled);
    }

    private static Forms.ContextMenuStrip Menu(WindowsTrayRuntime tray) =>
        (Forms.ContextMenuStrip)typeof(WindowsTrayRuntime).GetField("_menu", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tray)!;
    private static string?[] Texts(WindowsTrayRuntime tray) => Menu(tray).Items.Cast<Forms.ToolStripItem>().Select(item => item.Text).ToArray();
    private static Forms.ToolStripMenuItem Item(WindowsTrayRuntime tray, string text) => Assert.Single(Menu(tray).Items.OfType<Forms.ToolStripMenuItem>(), item => item.Text == text);
    private sealed class Startup : IStartupRegistration
    {
        public bool Enabled; public bool AcceptChanges = true;
        public ValueTask<bool> IsEnabledAsync(CancellationToken token) => ValueTask.FromResult(Enabled);
        public ValueTask<bool> SetEnabledAsync(bool enabled, CancellationToken token)
        {
            if (AcceptChanges) Enabled = enabled;
            return ValueTask.FromResult(Enabled);
        }
    }
    private sealed class Clear : IAiBarDataClearCommand
    {
        public ValueTask ClearAsync(CancellationToken token) => ValueTask.CompletedTask;
    }
}
