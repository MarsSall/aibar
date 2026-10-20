using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class ClaudePanelTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static QuotaSnapshot? Snapshot(bool primary = true, bool weekly = true) => !primary && !weekly ? null : new(
        primary ? new(31, Now.AddHours(2)) : null,
        weekly ? new(67, Now.AddDays(3)) : null, Now);

    // c: errors are normalized, never rendered from raw SafeCode, tokens, paths or plans.
    [Theory]
    [InlineData(QuotaErrorKind.Authentication, "quota_authentication", "Open Claude Code on this PC to renew the session")]
    [InlineData(QuotaErrorKind.Unavailable, "quota_credential_missing", "Open Claude Code on this PC to renew the session")]
    [InlineData(QuotaErrorKind.Unavailable, "quota_credential_unusable", "Open Claude Code on this PC to renew the session")]
    [InlineData(QuotaErrorKind.Service, "quota_service", "Service unavailable")]
    [InlineData(QuotaErrorKind.Unavailable, "quota_unavailable", "Quota unavailable")]
    [InlineData(QuotaErrorKind.Service, "synthetic-token C:\\synthetic\\login.json Max-plan", "Service unavailable")]
    public void Mapper_normalizes_login_and_service_failures(QuotaErrorKind kind, string code, string expected)
    {
        var mapped = new ClaudeQuotaPresentationMapper(new Clock()).Map(
            new(null, FreshnessState.Unavailable, false, new(kind, code), null), true);
        Assert.Equal(expected, mapped.WarningLabel);
        Assert.False(mapped.IsPrimaryAvailable || mapped.IsWeeklyAvailable);
        AssertSafe(mapped);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Mapper_preserves_each_window_and_clamps_elapsed_resets(bool primary, bool weekly)
    {
        var mapped = new ClaudeQuotaPresentationMapper(new Clock()).Map(
            new(Snapshot(primary, weekly), FreshnessState.Current, false, null, null), true);
        Assert.Equal(primary, mapped.IsPrimaryAvailable);
        Assert.Equal(weekly, mapped.IsWeeklyAvailable);
        Assert.Equal(primary ? 31m : (decimal?)null, mapped.Primary.PercentageUsed);
        Assert.Equal(weekly ? 67m : (decimal?)null, mapped.Weekly.PercentageUsed);
        Assert.Equal(primary ? "02h 00m" : null, mapped.Primary.ResetCountdown);
        Assert.Equal(weekly ? "3d 00h 00m" : null, mapped.Weekly.ResetCountdown);
        var expired = new ClaudeQuotaPresentationMapper(new Clock()).Map(
            new(new(new(10, Now.AddMinutes(-1)), null, Now), FreshnessState.Current, false, null, null), true);
        Assert.Equal("00h 00m", expired.Primary.ResetCountdown);
    }

    [Fact]
    public void Mapper_distinguishes_loading_stale_cache_and_revoked_consent()
    {
        var mapper = new ClaudeQuotaPresentationMapper(new Clock());
        var loading = mapper.Map(new(null, FreshnessState.Unavailable, true, null, null), true);
        Assert.True(loading.IsLoading); Assert.Equal("Loading", loading.FreshnessLabel);
        var staleState = new QuotaRefreshState(new(Snapshot()!.Primary, Snapshot()!.Weekly, Now.AddMinutes(-12)),
            FreshnessState.Stale, false, new(QuotaErrorKind.Service, "quota_service"), null);
        var stale = mapper.Map(staleState, true);
        Assert.True(stale.IsCached && stale.IsDegraded);
        Assert.Equal("Stale", stale.FreshnessLabel);
        Assert.Equal("Cached 00h 12m", stale.CachedAgeLabel);
        Assert.Equal(67m, stale.Weekly.PercentageUsed);
        var disabled = mapper.Map(staleState, false);
        Assert.True(disabled.IsPrivateIntegrationDisabled);
        Assert.False(disabled.IsPrimaryAvailable || disabled.IsWeeklyAvailable);
        Assert.Null(disabled.Primary.PercentageUsed); Assert.Null(disabled.Weekly.PercentageUsed);
        Assert.Null(disabled.WarningLabel); Assert.Null(disabled.CachedAgeLabel);
    }

    // a/f: real App composition, same live window; not an anonymous opt-in fixture.
    [Fact]
    public void Default_off_panel_is_unchanged_and_live_enable_revoke_updates_headings_and_automation()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            using var panel = new PanelFixture();
            var root = Assert.IsType<Grid>(panel.Window.Content);
            var children = root.Children.Cast<UIElement>().ToArray();
            Assert.Equal(2, root.RowDefinitions.Count);
            Assert.Equal(new[] { 0, 1 }, children.Select(Grid.GetRow));
            var cards = Assert.IsType<StackPanel>(panel.Window.FindName("QuotaCards"));
            Assert.Equal(new[] { "5-hour quota card", "Weekly quota card" },
                Visuals<GroupBox>(cards).Where(item => item.IsVisible).Select(AutomationProperties.GetName));
            Assert.Equal(new[] { 11d, 20d }, Visuals<ProgressBar>(cards).Where(item => item.IsVisible).Select(item => item.Value));
            AssertNoClaude(panel.Window);
            panel.Enable();
            Assert.Same(root, panel.Window.Content);
            Assert.Equal(children, root.Children.Cast<UIElement>());
            Assert.Equal(2, root.RowDefinitions.Count);
            Assert.Equal("Claude", panel.Window.SelectedProvider);
            Assert.DoesNotContain("Codex", VisibleText(cards));
            Assert.Contains("Claude", VisibleText(cards));
            Assert.Equal(new[] { 31d, 67d }, Visuals<ProgressBar>(cards).Where(item => item.IsVisible).Select(item => item.Value));
            panel.Window.SelectProvider("Codex"); panel.Window.UpdateLayout();
            Assert.Equal(new[] { 11d, 20d }, Visuals<ProgressBar>(cards).Where(item => item.IsVisible).Select(item => item.Value));
            panel.Window.SelectProvider("Claude"); panel.Window.UpdateLayout();
            panel.Revoke();
            AssertNoClaude(panel.Window);
            Assert.Equal(new[] { 11d, 20d }, Visuals<ProgressBar>(cards).Where(item => item.IsVisible).Select(item => item.Value));
            panel.Enable();
            Assert.Contains("Claude", VisibleText(cards));
        });
    }

    // b/d: inherited integrated meter styles, exact provider order, all semantic themes.
    [Theory]
    [InlineData(WindowsTheme.Light)]
    [InlineData(WindowsTheme.Dark)]
    [InlineData(WindowsTheme.HighContrast)]
    public void Enabled_meters_share_integrated_styles_semantics_and_automation_order(WindowsTheme theme)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var original = app.Resources.MergedDictionaries[0];
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
                using var panel = new PanelFixture(); panel.Enable();
                var cards = Assert.IsType<StackPanel>(panel.Window.FindName("QuotaCards"));
                var meters = Visuals<GroupBox>(cards).ToArray();
                Assert.Equal(new[] { "5-hour quota card", "Weekly quota card", "Claude 5-hour quota card", "Claude weekly quota card" }, meters.Select(AutomationProperties.GetName));
                foreach (var card in meters.Skip(2))
                {
                    Assert.Equal(KeyboardNavigation.GetTabIndex(meters[0]), KeyboardNavigation.GetTabIndex(card));
                    var border = Assert.IsType<Border>(card.Content);
                    Assert.Same(panel.Window.Resources["PanelQuotaCardStyle"], border.Style);
                    Assert.Equal(new Thickness(0), border.BorderThickness);
                    var bar = Assert.Single(Visuals<ProgressBar>(card));
                    Assert.Same(panel.Window.FindResource("QuotaProgressStyle"), bar.Style.BasedOn);
                    Assert.False(bar.Focusable || bar.IsHitTestVisible);
                    // Both Claude fixtures are below 75%: green, or the system highlight in high contrast.
                    var expectedBar = theme == WindowsTheme.HighContrast ? SystemColors.HighlightColor : (Color)ColorConverter.ConvertFromString("#C4703F");
                    Assert.Equal(expectedBar, ColorOf(bar.Foreground));
                    var percentage = Assert.Single(Visuals<TextBlock>(card).Where(text => text.Name == "MeterPercent"));
                    // S9 normalizes the 2x reference's percentage text to 11 DIPs.
                    Assert.Equal(22d, percentage.FontSize); // S10 display percentage replaces the small below-bar value.
                    Assert.Equal(ColorOf(panel.Window.FindResource("TextBrush")), ColorOf(percentage.Foreground));
                    Assert.Contains(VisibleText(card), text => text.StartsWith("Resets in ", StringComparison.Ordinal));
                }
                var bars = Visuals<ProgressBar>(cards).Where(item => item.IsVisible).ToArray();
                Assert.Equal(new[] { "Claude 5-hour quota progress", "Claude weekly quota progress" },
                    bars.Select(item => new ProgressBarAutomationPeer(item).GetName()));
                var names = AutomationNames(panel.Window);
                Assert.DoesNotContain("5-hour quota progress", names);
                Assert.Contains("Claude weekly quota progress", names);
                Assert.Contains("Claude 5-hour quota progress", names);
                panel.Window.SelectProvider("Codex"); panel.Window.UpdateLayout();
                names = AutomationNames(panel.Window);
                Assert.Contains("5-hour quota progress", names);
                Assert.Contains("Weekly quota progress", names);
                Assert.DoesNotContain("Claude weekly quota progress", names);
                var details = Assert.IsType<Button>(panel.Window.FindName("CostButton"));
                details.Focus(); Assert.True(details.IsKeyboardFocused);
                Assert.True(details.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                Assert.Equal("Usage Dashboard", AutomationProperties.GetName(Assert.IsAssignableFrom<DependencyObject>(Keyboard.FocusedElement)));
            }
            finally { app.Resources.MergedDictionaries[0] = original; }
        });
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Absent_Claude_windows_are_collapsed_and_omitted_from_automation(bool primary, bool weekly)
    {
        WpfTestApplicationHost.Run(_ =>
        {
            using var panel = new PanelFixture(primary, weekly); panel.Enable();
            panel.Window.SelectProvider("Claude"); panel.Window.UpdateLayout();
            var cards = Visuals<GroupBox>(panel.Window).Where(item => AutomationProperties.GetName(item).StartsWith("Claude ", StringComparison.Ordinal)).ToArray();
            Assert.Equal(2, cards.Length);
            Assert.Equal(new[] { primary ? Visibility.Visible : Visibility.Collapsed, weekly ? Visibility.Visible : Visibility.Collapsed }, cards.Select(item => item.Visibility));
            var names = AutomationNames(panel.Window);
            foreach (var card in cards.Where(item => item.Visibility == Visibility.Collapsed))
            {
                Assert.False(card.IsVisible); Assert.Equal(0, card.ActualHeight);
                Assert.DoesNotContain(AutomationProperties.GetName(card), names);
            }
        });
    }

    // c: exercise the actual presentation subscription, including in-flight loading.
    [Theory]
    [InlineData(QuotaErrorKind.Authentication, "Open Claude Code on this PC to renew the session")]
    [InlineData(QuotaErrorKind.Service, "Service unavailable")]
    [InlineData(QuotaErrorKind.Unavailable, "Quota unavailable")]
    public void Runtime_state_changes_render_safe_warnings_and_stale_cached_values(QuotaErrorKind failure, string expected)
    {
        WpfTestApplicationHost.Run(_ =>
        {
            using var panel = new PanelFixture(); panel.Enable();
            panel.Provider.Result = new(null, new(failure, "synthetic-token C:\\synthetic\\login.json Max-plan"));
            panel.Provider.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var refresh = panel.Composition.ClaudeQuota.RefreshAsync(RefreshTrigger.Manual, default).AsTask();
            try
            {
                Pump();
                Assert.Contains(VisibleText(panel.Window), text => text.Contains("Loading", StringComparison.Ordinal));
            }
            finally { panel.Provider.Pending.TrySetResult(panel.Provider.Result); }
            Wait(refresh); Pump();
            var text = string.Join("\n", VisibleText(panel.Window));
            Assert.Contains(expected, text, StringComparison.Ordinal);
            Assert.Contains("Stale", text, StringComparison.Ordinal);
            Assert.Contains("67%", text, StringComparison.Ordinal);
            foreach (var forbidden in new[] { "synthetic-token", "C:\\synthetic", "Max-plan" })
            {
                Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
                Assert.DoesNotContain(AutomationNames(panel.Window), name => name.Contains(forbidden, StringComparison.Ordinal));
            }
        });
    }

    [Theory]
    [InlineData(QuotaErrorKind.Authentication, "Open Claude Code on this PC to renew the session")]
    [InlineData(QuotaErrorKind.Service, "Service unavailable")]
    [InlineData(QuotaErrorKind.Unavailable, "Quota unavailable")]
    public void Failure_without_cache_renders_a_safe_status_without_Claude_meters(QuotaErrorKind failure, string expected)
    {
        WpfTestApplicationHost.Run(_ =>
        {
            using var panel = new PanelFixture();
            panel.Provider.Result = new(null, new(failure, "synthetic-token C:\\synthetic\\login.json Max-plan"));
            panel.Enable();
            Assert.Contains(expected, VisibleText(panel.Window));
            Assert.DoesNotContain(Visuals<PanelMeter>(panel.Window), meter => meter.IsVisible && meter.Provider == "Claude");
            Assert.DoesNotContain(VisibleText(panel.Window), text => text.Contains("synthetic", StringComparison.Ordinal) || text.Contains("Max-plan", StringComparison.Ordinal));
        });
    }

    // e: use the button's bound command, not the already-composed popover lifecycle callback.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Refresh_button_also_refreshes_Claude_only_when_enabled_and_copy_remains_Codex_only(bool enabled)
    {
        WpfTestApplicationHost.Run(_ =>
        {
            using var panel = new PanelFixture();
            if (enabled) panel.Enable();
            var view = Assert.IsType<BetaAnalyticsPresentation>(panel.Composition.Presentation);
            var copy = view.CopySummaryCommand;
            var beforeState = view.State;
            var calls = panel.Provider.Calls;
            var refresh = Visuals<Button>(panel.Window).Single(item => AutomationProperties.GetName(item) == "Refresh quota");
            Assert.Same(view.RefreshCommand, refresh.Command);
            Assert.True(refresh.Command.CanExecute(null));
            refresh.Command.Execute(null);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (!view.RefreshCommand.CanExecute && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(5); }
            Pump();
            Assert.True(view.RefreshCommand.CanExecute);
            Assert.Equal(calls + (enabled ? 1 : 0), panel.Provider.Calls);
            Assert.True(view.State.IsMissingCredential); // Codex manual refresh read only the synthetic missing login.
            Assert.Same(copy, view.CopySummaryCommand);
            Assert.Same(copy, view.QuotaPresentation.CopySummaryCommand);
            // Cached Codex values and resets are unchanged; pace is derived from the clock and freshness, so compare it apart.
            Assert.Equal(beforeState.Primary with { Pace = null }, view.State.Primary with { Pace = null });
            Assert.Equal(beforeState.Weekly with { Pace = null }, view.State.Weekly with { Pace = null });
            Assert.All(new[] { view.State.Primary.Pace, view.State.Weekly.Pace }.Where(pace => pace is not null), pace => Assert.Equal("Pace: Not current", pace!.Text));
            string? summary = null;
            new QuotaSummaryCopyCommand(() => view.State, text => summary = text).Execute(null);
            Assert.NotNull(summary);
            Assert.DoesNotContain("Claude", summary, StringComparison.Ordinal);
            Assert.Contains("11%", summary, StringComparison.Ordinal);
        });
    }

    private static void AssertSafe(BetaPresentationState state)
    {
        var text = System.Text.Json.JsonSerializer.Serialize(state);
        foreach (var forbidden in new[] { "synthetic-token", "synthetic", "Max-plan" })
            Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
    }
    private static void AssertNoClaude(MainWindow window)
    {
        // Codex-only panel: the CODEX section stays, nothing names Claude in any casing.
        Assert.Contains("Codex", VisibleText(window));
        Assert.DoesNotContain(VisibleText(window), text => text.Contains("Claude", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Visuals<FrameworkElement>(window), item => item.IsVisible && AutomationProperties.GetName(item).StartsWith("Claude", StringComparison.Ordinal));
        Assert.DoesNotContain(AutomationNames(window), name => name.Contains("Claude", StringComparison.OrdinalIgnoreCase));
    }
    private static string[] AutomationNames(MainWindow window) => AutomationElement.FromHandle(new WindowInteropHelper(window).EnsureHandle())
        .FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition).Cast<AutomationElement>().Select(item => item.Current.Name).ToArray();
    // Read rendered inline content: TextBlock.Text can stay empty for <Run> children.
    private static IEnumerable<string> VisibleText(DependencyObject root) => Visuals<TextBlock>(root).Where(item => item.IsVisible)
        .Select(item => new System.Windows.Documents.TextRange(item.ContentStart, item.ContentEnd).Text);
    private static Color ColorOf(object brush) => Assert.IsType<SolidColorBrush>(brush).Color;
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(5); }
        Assert.True(task.IsCompleted, "Panel operation exceeded its test bound");
        task.GetAwaiter().GetResult(); Pump();
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed class Provider(QuotaSnapshot? snapshot) : IQuotaProvider
    {
        public int Calls { get; private set; }
        public QuotaProviderResult Result { get; set; } = new(snapshot, null);
        public TaskCompletionSource<QuotaProviderResult>? Pending { get; set; }
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token)
        { Calls++; return Pending is null ? ValueTask.FromResult(Result) : new(Pending.Task.WaitAsync(token)); }
    }
    private sealed class PanelFixture : IDisposable
    {
        // Synthetic fixture only: never points at an actual profile or credential file.
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-claude-panel-{Guid.NewGuid():N}");
        public Provider Provider { get; }
        public App.StartupComposition Composition { get; }
        public MainWindow Window { get; }
        public PanelFixture(bool primary = true, bool weekly = true)
        {
            Directory.CreateDirectory(_root);
            var settings = new ConsentSettings(Path.Combine(_root, "settings.json"));
            Wait(settings.SaveAsync(true, default).AsTask());
            var now = DateTimeOffset.UtcNow;
            var store = new SqliteQuotaSnapshotStore(Path.Combine(_root, "quota.db"));
            Wait(store.SaveAsync(new(new(11, now.AddHours(2)), new(20, now.AddDays(3)), now), default).AsTask());
            Wait(store.DisposeAsync().AsTask());
            Provider = new(Snapshot(primary, weekly));
            Composition = App.CreateComposition(new App.CompositionSeams(DataDirectory: _root,
                CodexHome: Path.Combine(_root, "synthetic-codex"), UserProfile: Path.Combine(_root, "synthetic-profile"),
                ClaudeProviderFactory: _ => Provider));
            Wait(Composition.Initialize());
            Window = new MainWindow { Width = 420, Height = 720, SizeToContent = SizeToContent.Manual, DataContext = Composition.Presentation };
            Window.Show(); Pump(); Window.UpdateLayout();
        }
        public void Enable()
        {
            Wait(Composition.ClaudeQuota.GrantConsentAsync(default).AsTask());
            Window.SelectProvider("Claude");
            Window.UpdateLayout();
            // Let pending bindings (including inline Runs) resolve, like the existing panel tests do.
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Window.UpdateLayout();
        }
        public void Revoke() { Wait(Composition.ClaudeQuota.RevokeConsentAsync(default).AsTask()); Window.UpdateLayout(); }
        public void Dispose()
        {
            Provider.Pending?.TrySetResult(Provider.Result);
            Window.Close(); Wait(Composition.Resource.DisposeAsync().AsTask());
            Directory.Delete(_root, true);
        }
    }
}
