using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class QuotaSummaryCopyTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Copies_only_available_quota_from_one_state_with_invariant_used_percentages(bool primary, bool weekly)
    {
        var state = State(primary, weekly);
        var originalCulture = CultureInfo.CurrentCulture;
        var writes = new List<string>();
        var reads = 0;
        var command = Create(() => { reads++; return state; }, writes.Add);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.True(command.CanExecute(null));
            command.Execute(null);
            Assert.Equal(2, reads);
            var expected = string.Join(Environment.NewLine,
                "AIBar quota",
                weekly ? "Weekly quota: 20.34% used; reset in 7d 00h 00m" : "Weekly quota: unavailable",
                primary ? "5-hour quota: 42.12% used; reset in 01h 00m" : "5-hour quota: unavailable",
                "Status: Current");
            Assert.Equal(expected, Assert.Single(writes));
            Assert.Equal("Copied", command.Feedback);
            Assert.True(command.CanExecute(null));
            Assert.Equal(state, State(primary, weekly));
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Fact]
    public void Disabled_missing_and_empty_states_never_write_or_report_success()
    {
        foreach (var state in new BetaPresentationState?[] { null, State(false, false), State() with { IsPrivateIntegrationDisabled = true } })
        {
            var writes = 0;
            var command = Create(() => state, _ => writes++);
            Assert.False(command.CanExecute(null));
            command.Execute(null);
            Assert.Equal(0, writes);
            Assert.True(string.IsNullOrEmpty(command.Feedback));
        }
        var fallback = new UnavailableQuotaPresentation();
        Assert.False(fallback.CopySummaryCommand.CanExecute(null));
    }

    [Fact]
    public void Changed_state_is_copied_with_safe_cached_context_and_feedback_is_invalidated()
    {
        var state = State();
        var writes = new List<string>();
        var command = Create(() => state, writes.Add);
        var changes = new List<string?>();
        Assert.IsAssignableFrom<INotifyPropertyChanged>(command).PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        var availabilityEvents = 0;
        command.CanExecuteChanged += (_, _) => availabilityEvents++;
        command.Execute(null);
        state = State() with { FreshnessLabel = "Stale", CachedAgeLabel = "Cached 00h 11m", WarningLabel = "Network unavailable", DiagnosticCode = "private-secret" };
        command.RefreshState();
        Assert.Equal(1, availabilityEvents);
        Assert.True(string.IsNullOrEmpty(command.Feedback));
        command.Execute(null);
        Assert.Equal(2, writes.Count);
        Assert.EndsWith(string.Join(Environment.NewLine, "Status: Stale", "Cached 00h 11m", "Network unavailable"), writes[1]);
        Assert.DoesNotContain("private-secret", writes[1], StringComparison.Ordinal);
        Assert.Contains("Feedback", changes);
        state = state with { IsPrivateIntegrationDisabled = true };
        command.RefreshState();
        Assert.Equal(2, availabilityEvents);
        Assert.False(command.CanExecute(null));
        command.Execute(null);
        Assert.Equal(2, writes.Count);
    }

    [Fact]
    public void Busy_clipboard_is_contained_without_retry_or_quota_mutation_and_next_click_can_succeed()
    {
        var state = State();
        var writes = new List<string>();
        var calls = 0;
        var command = Create(() => state, text =>
        {
            calls++;
            if (calls == 1) throw new COMException("private clipboard failure", unchecked((int)0x800401D0));
            writes.Add(text);
        });
        command.Execute(null);
        Assert.Equal(1, calls);
        Assert.Empty(writes);
        Assert.Equal("Clipboard unavailable", command.Feedback);
        Assert.Equal(State(), state);
        Assert.True(command.CanExecute(null));
        command.Execute(null);
        Assert.Equal(2, calls);
        Assert.Single(writes);
        Assert.Equal("Copied", command.Feedback);
    }

    [Fact]
    public void Visible_copy_button_uses_injected_writer_and_announces_feedback_without_real_clipboard_access()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var writes = new List<string>();
            var command = Create(() => State(), writes.Add);
            var window = new MainWindow { DataContext = new { CopySummaryCommand = command } };
            try
            {
                window.Show(); window.UpdateLayout();
                var button = Descendants<System.Windows.Controls.Button>(window).Single(item => System.Windows.Automation.AutomationProperties.GetName(item) == "Copy quota summary");
                Assert.Same(command, button.Command);
                Assert.True(button.IsEnabled && button.IsTabStop);
                var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(button);
                Assert.IsAssignableFrom<System.Windows.Automation.Provider.IInvokeProvider>(peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                Assert.Single(writes);
                var feedback = Assert.Single(Descendants<System.Windows.Controls.TextBlock>(window).Where(text => text.Text == "Copied"));
                Assert.Equal(System.Windows.Automation.AutomationLiveSetting.Polite, System.Windows.Automation.AutomationProperties.GetLiveSetting(feedback));
                window.DataContext = new UnavailableQuotaPresentation();
                window.UpdateLayout();
                Assert.False(button.IsEnabled);
                Assert.Single(writes);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Host_and_wrapper_share_copy_availability_without_changing_refresh_or_disposed_state()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var enabled = true;
            var coordinator = new QuotaRefreshCoordinator(new Store(), new Provider(), new Clock(), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.FromDays(1));
            var host = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(new Clock()), () => enabled);
            var analytics = new LocalCodexAnalyticsView();
            var result = new LocalUsageCoordinatorResult([], LocalUsageProjectionStatus.Completed, []);
            var local = new LocalUsagePresentationHost(new(), _ => ValueTask.FromResult(result), (_, _) => ValueTask.FromResult(result), _ => ValueTask.FromResult(result));
            var wrapper = new BetaAnalyticsPresentation(host, analytics, local);
            try
            {
                coordinator.InitializeAsync(default).AsTask().GetAwaiter().GetResult();
                Assert.Same(host.CopySummaryCommand, wrapper.CopySummaryCommand);
                Assert.Same(host.RefreshCommand, wrapper.RefreshCommand);
                Assert.True(host.CopySummaryCommand.CanExecute(null));
                var events = 0;
                host.CopySummaryCommand.CanExecuteChanged += (_, _) => events++;
                enabled = false; host.RefreshAvailabilityChanged();
                Assert.Equal(1, events);
                Assert.False(host.CopySummaryCommand.CanExecute(null));
                Assert.False(host.RefreshCommand.CanExecute);
                enabled = true; host.RefreshAvailabilityChanged();
                Assert.True(host.CopySummaryCommand.CanExecute(null));
                host.DisposeAsync().GetAwaiter().GetResult();
                Assert.False(host.CopySummaryCommand.CanExecute(null));
            }
            finally
            {
                host.DisposeAsync().GetAwaiter().GetResult();
                analytics.DisposeAsync().AsTask().GetAwaiter().GetResult();
                coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private sealed class Store : IQuotaSnapshotStore
    {
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken token) => ValueTask.FromResult<QuotaSnapshot?>(new(new(42, Now.AddHours(1)), new(20, Now.AddDays(7)), Now));
        public ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken token) => ValueTask.CompletedTask;
    }
    private sealed class Provider : IQuotaProvider
    {
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token) => throw new InvalidOperationException("Copy must not refresh quota");
    }

    private static QuotaSummaryCopyCommand Create(Func<BetaPresentationState?> state, Action<string> writer) => new(state, writer);
    private static BetaPresentationState State(bool primary = true, bool weekly = true)
    {
        var snapshot = primary || weekly ? new QuotaSnapshot(primary ? new(42.12m, Now.AddHours(1)) : null, weekly ? new(20.34m, Now.AddDays(7)) : null, Now) : null;
        return new QuotaPresentationMapper(new Clock()).Map(new(snapshot, FreshnessState.Current, false, null, null));
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
}
