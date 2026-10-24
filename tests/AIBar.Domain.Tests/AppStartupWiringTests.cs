using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class AppStartupWiringTests
{
    [Fact]
    public async Task Offers_run_only_after_initialization_and_requested_panel_activation()
    {
        var order = new List<string>();
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var composition = new App.StartupComposition(new object(), null, new EmptyResource(),
            () => initialized.Task, () => { });
        var startup = App.StartPrimary(() => composition, _ => order.Add("tray"), _ => order.Add("fault"),
            () => order.Add("shutdown"), () => order.Add("show"), _ => { order.Add("offers"); return Task.CompletedTask; });
        Assert.Equal(new[] { "tray" }, order);
        initialized.SetResult();
        await startup;
        Assert.Equal(new[] { "tray", "show", "offers" }, order);
    }

    [Fact]
    public async Task Initialization_failure_does_not_run_offers()
    {
        var offered = false;
        var reported = false;
        var unavailable = false;
        var composition = new App.StartupComposition(new object(), null, new EmptyResource(),
            () => Task.FromException(new InvalidOperationException()), () => unavailable = true);
        await App.StartPrimary(() => composition, _ => { }, _ => reported = true, () => { },
            afterInitialize: _ => { offered = true; return Task.CompletedTask; });
        Assert.True(reported);
        Assert.True(unavailable);
        Assert.False(offered);
    }

    [Fact]
    public void Unavailable_composition_has_no_onboarding_or_dock_capability()
    {
        Assert.Null(App.StartupComposition.Unavailable.Dock);
        Assert.Null(App.StartupComposition.Unavailable.Onboarding);
    }

    private sealed class EmptyResource : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
