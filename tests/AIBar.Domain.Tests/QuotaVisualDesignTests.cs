using System.Xml.Linq;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;
namespace AIBar.Domain.Tests;
public sealed class QuotaVisualDesignTests
{
    [Fact]
    public void Declares_semantic_tokens_and_an_accessible_compact_card_hierarchy()
    {
        var resources = ReadProjectFile("src/AIBar.Desktop/App.xaml");
        var window = ReadProjectFile("src/AIBar.Desktop/MainWindow.xaml");

        foreach (var key in new[] { "SurfaceBrush", "CardBrush", "TextBrush", "MutedTextBrush", "AccentBrush", "FocusBrush", "SpacingSmall", "SpacingMedium", "CardRadius" })
            Assert.Contains($"x:Key=\"{key}\"", resources, StringComparison.Ordinal);
        Assert.Contains("QuotaCardStyle", resources, StringComparison.Ordinal);
        Assert.Contains("HighContrast", resources, StringComparison.Ordinal);
        var highContrastForegrounds = XDocument.Parse(resources).Descendants().Where(element => element.Name.LocalName == "DataTrigger" && element.Attribute("Value")?.Value == "True").SelectMany(element => element.Elements()).Where(setter => setter.Attribute("Property")?.Value.Contains("Foreground", StringComparison.Ordinal) == true).ToArray();
        Assert.True(highContrastForegrounds.Length >= 2); Assert.All(highContrastForegrounds, setter => Assert.Contains("SystemColors.", setter.Attribute("Value")!.Value, StringComparison.Ordinal));
        Assert.Contains("FocusVisualStyle", resources, StringComparison.Ordinal); Assert.Contains("UseLayoutRounding=\"True\"", window, StringComparison.Ordinal);

        Assert.Contains("AutomationProperties.Name=\"5-hour quota card\"", window, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Weekly quota card\"", window, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"OpenCode and Pi local usage\"", window, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding AutomationLabel}\"", window, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding LocalUsage.Rows}\" Focusable=\"False\"", window, StringComparison.Ordinal);
        var localUsage = XDocument.Parse(window); var expander = localUsage.Descendants().Single(element => element.Name.LocalName == "Expander");
        Assert.Equal("False", expander.Attribute("IsExpanded")?.Value); Assert.Equal("True", expander.Attribute("Focusable")?.Value); Assert.Equal("True", expander.Attribute("IsTabStop")?.Value);
        var detailScroller = expander.Descendants().Single(element => element.Name.LocalName == "ScrollViewer");
        Assert.Equal("180", detailScroller.Attribute("MaxHeight")?.Value); Assert.Equal("Auto", detailScroller.Attribute("VerticalScrollBarVisibility")?.Value);
        var localUsageBreakdowns = localUsage.Descendants().Where(element => element.Attribute("Text")?.Value == "{Binding TokenBreakdownLabel}").ToArray();
        Assert.Equal(2, localUsageBreakdowns.Length); Assert.All(localUsageBreakdowns, breakdown => Assert.Equal("Wrap", breakdown.Attribute("TextWrapping")?.Value));
        Assert.Equal(2, window.Split("Style=\"{StaticResource QuotaCardStyle}\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, window.Split("Style=\"{StaticResource SecondaryCardStyle}\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("AutomationProperties.Name=\"Refresh quota\"", window, StringComparison.Ordinal); Assert.Contains("IsTabStop=\"True\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("<DoubleAnimation", resources, StringComparison.Ordinal);
    }
    [Fact]
    public void Quota_first_styles_tracks_and_secondary_sections_are_semantic_and_non_interactive()
    {
        var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var resources = XDocument.Parse(ReadProjectFile("src/AIBar.Desktop/App.xaml"));
        var window = XDocument.Parse(ReadProjectFile("src/AIBar.Desktop/MainWindow.xaml"));
        XElement Style(string key) => resources.Descendants(presentation + "Style").Single(style => style.Attribute(xaml + "Key")?.Value == key);
        string? Setter(XElement style, string property) => style.Elements(presentation + "Setter").SingleOrDefault(setter => setter.Attribute("Property")?.Value == property)?.Attribute("Value")?.Value;

            foreach (var key in new[] { "QuotaTitleTextStyle", "QuotaStatusTextStyle", "MutedTextStyle", "QuotaLabelTextStyle", "QuotaPercentageTextStyle", "QuotaProgressStyle", "ChromeLessGroupBoxStyle", "SecondaryGroupBoxStyle", "SecondaryCardStyle", "AccessibleExpanderStyle" })
                Assert.NotNull(Style(key));

        var muted = Style("MutedTextStyle");
        Assert.Equal("{DynamicResource MutedTextBrush}", Setter(muted, "Foreground"));
        Assert.Contains("SystemColors.WindowTextBrushKey", muted.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);

        var progressStyle = Style("QuotaProgressStyle");
        Assert.Equal("0", Setter(progressStyle, "Minimum")); Assert.Equal("100", Setter(progressStyle, "Maximum"));
        Assert.Equal("False", Setter(progressStyle, "IsIndeterminate")); Assert.Equal("False", Setter(progressStyle, "Focusable")); Assert.Equal("False", Setter(progressStyle, "IsHitTestVisible"));
        Assert.Contains("PART_Track", progressStyle.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
        Assert.Contains("PART_Indicator", progressStyle.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
        Assert.Contains("SystemColors.HighlightBrushKey", progressStyle.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
        Assert.DoesNotContain("Animation", progressStyle.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);

        var chromeLessGroup = Style("ChromeLessGroupBoxStyle");
        Assert.Empty(chromeLessGroup.Descendants(presentation + "Border"));
        Assert.Equal("{StaticResource KeyboardFocusVisual}", Setter(Style("AccessibleExpanderStyle"), "FocusVisualStyle"));
        Assert.Contains("SystemColors.HighlightBrushKey", Style("KeyboardFocusVisual").ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);

        var textBlocks = window.Descendants(presentation + "TextBlock").ToArray();
        Assert.Equal("{StaticResource QuotaTitleTextStyle}", textBlocks.Single(text => text.Attribute("Text")?.Value == "Quota").Attribute("Style")?.Value);
        Assert.All(textBlocks.Where(text => text.Attribute("Visibility")?.Value.Contains("State.Is", StringComparison.Ordinal) == true), text => Assert.Equal("{StaticResource QuotaStatusTextStyle}", text.Attribute("Style")?.Value));
        Assert.All(textBlocks.Where(text => text.Attribute("Text")?.Value.Contains("PercentageUsed", StringComparison.Ordinal) == true), text => Assert.Equal("{StaticResource QuotaPercentageTextStyle}", text.Attribute("Style")?.Value));
        var progressBars = window.Descendants(presentation + "ProgressBar").ToArray();
        Assert.Equal(2, progressBars.Length);
        Assert.Equal(new[] { "{Binding Primary.PercentageUsed}", "{Binding Weekly.PercentageUsed}" }, progressBars.Select(track => track.Attribute("Value")?.Value));
        Assert.All(progressBars, track => Assert.Equal("{StaticResource QuotaProgressStyle}", track.Attribute("Style")?.Value));
        var groups = window.Descendants(presentation + "GroupBox").ToArray();
        Assert.Equal(4, groups.Length);
        Assert.All(groups.Take(2), group => Assert.Equal("{StaticResource ChromeLessGroupBoxStyle}", group.Attribute("Style")?.Value));
        Assert.All(groups.Skip(2), group => Assert.Equal("{StaticResource SecondaryGroupBoxStyle}", group.Attribute("Style")?.Value));
        Assert.Equal(2, window.Descendants(presentation + "Border").Count(border => border.Attribute("Style")?.Value == "{StaticResource SecondaryCardStyle}"));
        Assert.Equal("{StaticResource AccessibleExpanderStyle}", window.Descendants(presentation + "Expander").Single().Attribute("Style")?.Value);
    }
    [Fact]
    public void Uses_4b_quota_presentation_bindings_without_new_business_state()
    {
        var window = XDocument.Parse(ReadProjectFile("src/AIBar.Desktop/MainWindow.xaml"));
        var values = window.Descendants().Attributes().Select(attribute => attribute.Value).ToArray();
        Assert.Contains("{Binding Primary.Label}", values);
        Assert.Contains(values, value => value.Contains("Primary.PercentageUsed", StringComparison.Ordinal));
        Assert.Contains("{Binding Weekly.Label}", values);
        Assert.Contains(values, value => value.Contains("Weekly.PercentageUsed", StringComparison.Ordinal));
        Assert.Contains(values, value => value.Contains("Weekly.ResetCountdown", StringComparison.Ordinal));
        Assert.Contains("{Binding FreshnessLabel}", values);
        Assert.Contains("{Binding PrivateEndpointDisclosure}", values);
        foreach (var branch in new[] { "State.IsLoading", "State.IsMissingCredential", "State.IsOffline", "State.IsDegraded", "State.IsUnavailable", "State.IsPrivateIntegrationDisabled", "State.IsSafeError" })
            Assert.Contains(values, value => value.Contains(branch, StringComparison.Ordinal));
        var primaryCard = window.Descendants().Single(element => element.Attributes().Any(attribute => attribute.Value == "5-hour quota card"));
        var weeklyCard = window.Descendants().Single(element => element.Attributes().Any(attribute => attribute.Value == "Weekly quota card"));
        Assert.Equal("{Binding State.IsPrimaryAvailable, Converter={StaticResource BooleanToVisibilityConverter}}", primaryCard.Attribute("Visibility")?.Value);
        Assert.Equal("{Binding State.IsWeeklyAvailable, Converter={StaticResource BooleanToVisibilityConverter}}", weeklyCard.Attribute("Visibility")?.Value);
    }
    [Fact]
    public void Runtime_ui_automation_exposes_named_cards_card_availability_and_button_command()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            try
            {
                app = new App(suppressHostStartup: true); app.InitializeComponent();
                app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                Assert.Equal(System.Windows.ShutdownMode.OnExplicitShutdown, app.ShutdownMode);
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
                var provider = new GatedProvider();
                var coordinator = new QuotaRefreshCoordinator(new EmptyStore(), provider, new FixedClock(DateTimeOffset.UtcNow), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
                var host = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(new FixedClock(DateTimeOffset.UtcNow)));
                var window = new MainWindow { DataContext = host }; window.Show();
                var cards = FindVisualChildren<System.Windows.Controls.GroupBox>(window).ToArray();
                Assert.Equal(new[] { "5-hour quota card", "Weekly quota card", "Local Codex data", "OpenCode and Pi local usage" }, cards.Select(card => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(card)!.GetName()));
                var button = FindVisualChildren<System.Windows.Controls.Button>(window).Single();
                Assert.IsAssignableFrom<System.Windows.Input.ICommand>(button.Command);
                Assert.True(button.IsEnabled);
                button.GetType().GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(button, null);
                Assert.True(provider.Entered.Wait(TimeSpan.FromSeconds(2)));
                Assert.False(button.IsEnabled);
                foreach (var availability in new[] { (Primary: false, Weekly: true), (Primary: true, Weekly: false), (Primary: true, Weekly: true), (Primary: false, Weekly: false) })
                {
                    var snapshot = availability.Primary || availability.Weekly ? new QuotaSnapshot(availability.Primary ? new(42, DateTimeOffset.UtcNow.AddHours(5)) : null, availability.Weekly ? new(20, DateTimeOffset.UtcNow.AddDays(7)) : null, DateTimeOffset.UtcNow) : null;
                    var cardCoordinator = new QuotaRefreshCoordinator(new EmptyStore(snapshot), provider, new FixedClock(DateTimeOffset.UtcNow), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
                    var cardHost = new QuotaPresentationHost(cardCoordinator, new QuotaPresentationMapper(new FixedClock(DateTimeOffset.UtcNow))); var initialization = cardCoordinator.InitializeAsync(default).AsTask(); PumpUntil(() => initialization.IsCompleted); Assert.True(initialization.IsCompletedSuccessfully);
                    var analytics = new LocalCodexAnalyticsView(); var cardWindow = new MainWindow { DataContext = new BetaAnalyticsPresentation(cardHost, analytics, LocalUsageHost()) }; cardWindow.Show(); cardWindow.UpdateLayout();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); cardWindow.UpdateLayout();
                    var quotaCards = FindVisualChildren<System.Windows.Controls.GroupBox>(cardWindow).Take(2).ToArray();
                    Assert.Equal(availability.Primary ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed, quotaCards[0].Visibility);
                    Assert.Equal(availability.Weekly ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed, quotaCards[1].Visibility);
                    var usageExpanders = FindVisualChildren<System.Windows.Controls.Expander>(cardWindow).ToArray(); Assert.Equal(3, usageExpanders.Length);
                    var openCode = usageExpanders.Single(item => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(item)!.GetName().StartsWith("OpenCode,", StringComparison.Ordinal));
                    var openCodePeer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(openCode)!;
                    Assert.Equal("OpenCode, 165 retained tokens, Input 11 · Cache 55 (read 22, write 33) · Output 44 · Reasoning 55, Retained history", openCodePeer.GetName());
                    Assert.True(openCode.Focusable); Assert.True(openCode.IsTabStop); Assert.DoesNotContain("model-", openCodePeer.GetName(), StringComparison.Ordinal);
                    var expandCollapse = Assert.IsAssignableFrom<System.Windows.Automation.Provider.IExpandCollapseProvider>(openCodePeer.GetPattern(System.Windows.Automation.Peers.PatternInterface.ExpandCollapse));
                    expandCollapse.Expand(); cardWindow.UpdateLayout(); System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    var visibleDetailText = FindVisualChildren<System.Windows.Controls.TextBlock>(openCode).Where(text => text.IsVisible).ToArray();
                    Assert.Equal(new[] { "model-alpha", "model-zeta" }, visibleDetailText.Where(text => text.Text.StartsWith("model-", StringComparison.Ordinal)).Select(text => text.Text));
                    Assert.Contains(visibleDetailText, text => text.Text == "150 retained tokens"); Assert.Contains(visibleDetailText, text => text.Text == "15 retained tokens");
                    var detailNames = visibleDetailText.Select(text => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(text)?.GetName()).OfType<string>().ToArray();
                    Assert.Contains("2030-01-02, model-alpha, 150 retained tokens, Input 10 · Cache 50 (read 20, write 30) · Output 40 · Reasoning 50", detailNames);
                    expandCollapse.Collapse(); cardWindow.UpdateLayout(); Assert.False(openCode.IsExpanded);
                    Assert.DoesNotContain(FindVisualChildren<System.Windows.Controls.TextBlock>(openCode).Where(text => text.IsVisible), text => text.Text.StartsWith("model-", StringComparison.Ordinal));
                    cardWindow.Close();
                    var cardCleanup = DisposeAsync(analytics, cardHost, cardCoordinator); PumpUntil(() => cardCleanup.IsCompleted); Assert.True(cardCleanup.IsCompletedSuccessfully);
                }
                var disabled = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(new FixedClock(DateTimeOffset.UtcNow)), false);
                var disabledWindow = new MainWindow { DataContext = disabled };
                disabledWindow.Show();
                Assert.Equal(System.Windows.Visibility.Collapsed, FindVisualChildren<System.Windows.Controls.Button>(disabledWindow).Single().Visibility);
                provider.Release(); PumpUntil(() => button.IsEnabled);
                Assert.True(button.IsEnabled); Assert.Equal(1, provider.Calls);
                disabledWindow.Close(); window.Close();
                var cleanup = DisposeAsync(disabled, host, coordinator); PumpUntil(() => cleanup.IsCompleted); Assert.True(cleanup.IsCompletedSuccessfully);
            } catch (Exception exception) { failure = exception; }
            finally
            {
                try
                {
                    if (app is not null)
                    {
                        foreach (System.Windows.Window openWindow in app.Windows.Cast<System.Windows.Window>().ToArray()) openWindow.Close();
                        app.Shutdown();
                    }
                    if (!dispatcher.HasShutdownStarted) dispatcher.InvokeShutdown();
                }
                catch (Exception cleanupException) { failure ??= cleanupException; }
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    [Fact]
    public async Task Primary_startup_and_real_coordinator_failures_are_contained_reported_and_cleaned_up()
        {
            var faults = new List<Exception>(); var resource = new TestResource(); var unavailable = false; var shutdowns = 0; App.StartupComposition? started = null;
            await App.StartPrimary(() => new(new UnavailableQuotaPresentation(), null, resource, () => Task.FromException(new InvalidOperationException("init")), () => unavailable = true), value => started = value, faults.Add, () => shutdowns++);

        Assert.Same(resource, started!.Resource); Assert.True(unavailable); Assert.Single(faults);
        await started.Resource.DisposeAsync(); Assert.Equal(1, resource.Disposals);
        await App.StartPrimary(() => throw new IOException("directory"), value => started = value, faults.Add, () => shutdowns++);
        Assert.IsType<UnavailableQuotaPresentation>(started!.Presentation); Assert.Null(started.RefreshCommand); Assert.Equal(2, faults.Count);
        var command = new ManualRefreshCommand(_ => ValueTask.FromException(new ApplicationException("refresh")), () => true, faults.Add);
        await Assert.ThrowsAsync<ApplicationException>(() => command.ExecuteAsync(default).AsTask()); Assert.True(command.CanExecute); Assert.Equal(3, faults.Count);
        var trayResource = new TestResource(); var initializations = 0;
        await App.StartPrimary(() => new(new UnavailableQuotaPresentation(), null, trayResource, () => { initializations++; return Task.CompletedTask; }, () => { }), _ => throw new InvalidOperationException("tray"), faults.Add, () => shutdowns++);
        Assert.Equal(1, trayResource.Disposals); Assert.Equal(0, initializations); Assert.Equal(1, shutdowns); Assert.Equal(4, faults.Count);

        await using var coordinator = new QuotaRefreshCoordinator(new ThrowingStore(), new GatedProvider(), new FixedClock(DateTimeOffset.UtcNow), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
        await using var host = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(new FixedClock(DateTimeOffset.UtcNow)), report: faults.Add);
        await host.InitializeAsync(default);
        Assert.Equal("Unavailable", host.FreshnessLabel); Assert.Equal("quota_refresh_failed", faults[^1].Message); Assert.DoesNotContain("store secret", faults[^1].Message); Assert.Equal(5, faults.Count);
    }
    private sealed class TestResource : IAsyncDisposable { public int Disposals { get; private set; } public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; } }
    private sealed class ThrowingStore : IQuotaSnapshotStore
    {
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken _) => throw new IOException("store secret");
        public ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
    private sealed class EmptyStore(QuotaSnapshot? snapshot = null) : IQuotaSnapshotStore
    {
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(snapshot);
        public ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
    private sealed class GatedProvider : IQuotaProvider
    {
        private readonly TaskCompletionSource<QuotaProviderResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Entered { get; } = new();
        public int Calls { get; private set; }
        public async ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
        {
            Calls++; Entered.Set(); return await _result.Task.WaitAsync(cancellationToken);
        }
        public void Release() => _result.SetResult(new(null, new(QuotaErrorKind.Unavailable, "quota_disabled")));
    }
    private static LocalUsagePresentationHost LocalUsageHost()
    {
        var facts = new[]
        {
            new DailyToolModelUsageFact(new(2030, 1, 1), "test-zone", TimeSpan.Zero, "test-policy", UsageProjectionScope.OpenCode, "model-zeta", new(1, 2, 3, 4, 5)),
            new DailyToolModelUsageFact(new(2030, 1, 2), "test-zone", TimeSpan.Zero, "test-policy", UsageProjectionScope.OpenCode, "model-alpha", new(10, 20, 30, 40, 50)),
            new DailyToolModelUsageFact(new(2030, 1, 1), "test-zone", TimeSpan.Zero, "test-policy", UsageProjectionScope.Combined, "model-zeta", new(1, 2, 3, 4, 5)),
            new DailyToolModelUsageFact(new(2030, 1, 2), "test-zone", TimeSpan.Zero, "test-policy", UsageProjectionScope.Combined, "model-alpha", new(10, 20, 30, 40, 50))
        };
        var result = new LocalUsageCoordinatorResult(
            [new(UsageTool.OpenCode, LocalUsageSourceStatus.Completed, 2), new(UsageTool.Pi, LocalUsageSourceStatus.Completed, 0)],
            LocalUsageProjectionStatus.Completed, facts);
        var host = new LocalUsagePresentationHost(new(), _ => ValueTask.FromResult(result), (_, _) => ValueTask.FromResult(result), _ => ValueTask.FromResult(result));
        host.StartAsync().GetAwaiter().GetResult();
        return host;
    }
    private static async Task DisposeAsync(params IAsyncDisposable[] resources) { foreach (var resource in resources) await resource.DisposeAsync(); }
    private static void PumpUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            Thread.Sleep(10); var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => frame.Continue = false); System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        Assert.True(condition());
    }
    private static IEnumerable<T> FindVisualChildren<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T typed) yield return typed;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
    private static string ReadProjectFile(string relativePath)
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AIBar.sln"))) directory = directory.Parent;
        if (directory is null)
        {
            directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AIBar.sln"))) directory = directory.Parent;
        }
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}
