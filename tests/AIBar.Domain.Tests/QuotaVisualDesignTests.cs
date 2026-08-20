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
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var resources = ReadProjectFile("src/AIBar.Desktop/App.xaml");
        var applicationResources = XDocument.Parse(resources);
        var mergedDictionaries = applicationResources.Descendants().Single(element => element.Name.LocalName == "ResourceDictionary.MergedDictionaries");
        var mergedSource = mergedDictionaries.Elements().Single(element => element.Name.LocalName == "ResourceDictionary").Attribute("Source")!.Value;
        Assert.Equal("Themes/Semantic.Light.xaml", mergedSource);
        var semanticKeys = XDocument.Parse(ReadProjectFile($"src/AIBar.Desktop/{mergedSource}")).Descendants()
            .Select(element => element.Attribute(x + "Key")?.Value).Where(key => key is not null).Cast<string>().Order().ToArray();
        Assert.Equal(new[] { "AccentBrush", "CardBrush", "FocusBrush", "MutedTextBrush", "ProgressTrackBrush", "SecondaryBorderBrush", "SurfaceBrush", "TextBrush" }, semanticKeys);
        var window = ReadProjectFile("src/AIBar.Desktop/MainWindow.xaml");

        foreach (var key in new[] { "SpacingSmall", "SpacingMedium", "CardRadius" })
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
    public void Uses_4a_quota_presentation_bindings_without_new_business_state()
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
        Assert.Null(primaryCard.Attribute("Visibility"));
        Assert.Null(weeklyCard.Attribute("Visibility"));
    }
    [Fact]
    public void Runtime_ui_automation_exposes_named_cards_card_availability_and_button_command()
    {
        WpfTestApplicationHost.Run(app =>
        {
            Assert.Equal(System.Windows.ShutdownMode.OnExplicitShutdown, app.ShutdownMode);
            var provider = new GatedProvider();
            var coordinator = new QuotaRefreshCoordinator(new EmptyStore(), provider, new FixedClock(DateTimeOffset.UtcNow), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
            var host = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(new FixedClock(DateTimeOffset.UtcNow)));
            MainWindow? window = null; MainWindow? disabledWindow = null; QuotaPresentationHost? disabled = null;
            try
            {
                window = new MainWindow { DataContext = host }; window.Show();
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
                    var cardHost = new QuotaPresentationHost(cardCoordinator, new QuotaPresentationMapper(new FixedClock(DateTimeOffset.UtcNow))); var analytics = new LocalCodexAnalyticsView(); MainWindow? cardWindow = null;
                    try
                    {
                        var initialization = cardCoordinator.InitializeAsync(default).AsTask(); PumpUntil(() => initialization.IsCompleted); Assert.True(initialization.IsCompletedSuccessfully);
                        cardWindow = new MainWindow { DataContext = new BetaAnalyticsPresentation(cardHost, analytics, LocalUsageHost()) }; cardWindow.Show(); cardWindow.UpdateLayout();
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); cardWindow.UpdateLayout();
                        var quotaCards = FindVisualChildren<System.Windows.Controls.GroupBox>(cardWindow).Take(2).ToArray();
                        Assert.All(quotaCards, card => Assert.Equal(System.Windows.Visibility.Visible, card.Visibility));
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
                    }
                    finally
                    {
                        cardWindow?.Close();
                        var cardCleanup = DisposeAsync(analytics, cardHost, cardCoordinator); PumpUntil(() => cardCleanup.IsCompleted); Assert.True(cardCleanup.IsCompletedSuccessfully);
                    }
                }
                disabled = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(new FixedClock(DateTimeOffset.UtcNow)), false);
                disabledWindow = new MainWindow { DataContext = disabled };
                disabledWindow.Show();
                Assert.Equal(System.Windows.Visibility.Collapsed, FindVisualChildren<System.Windows.Controls.Button>(disabledWindow).Single().Visibility);
                provider.Release(); PumpUntil(() => button.IsEnabled);
                Assert.True(button.IsEnabled); Assert.Equal(1, provider.Calls);
            }
            finally
            {
                disabledWindow?.Close(); window?.Close();
                var cleanup = DisposeAsync(new IAsyncDisposable?[] { disabled, host, coordinator }.OfType<IAsyncDisposable>().ToArray()); PumpUntil(() => cleanup.IsCompleted); Assert.True(cleanup.IsCompletedSuccessfully);
            }
        });
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
    [Fact]
    public void Semantic_theme_dictionaries_have_exact_key_parity_and_system_high_contrast_colors()
    {
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var dictionaries = new[] { "Light", "Dark", "HighContrast" }
            .Select(name => XDocument.Parse(ReadProjectFile($"src/AIBar.Desktop/Themes/Semantic.{name}.xaml"))).ToArray();
        var keySets = dictionaries.Select(dictionary => dictionary.Descendants().Select(element => element.Attribute(x + "Key")?.Value).Where(key => key is not null).Cast<string>().Order().ToArray()).ToArray();
        Assert.All(keySets.Skip(1), keys => Assert.Equal(keySets[0], keys));
        Assert.Equal(new[] { "AccentBrush", "CardBrush", "FocusBrush", "MutedTextBrush", "ProgressTrackBrush", "SecondaryBorderBrush", "SurfaceBrush", "TextBrush" }, keySets[0]);
            var highContrastMappings = new[]
            {
                ("SurfaceBrush", "{DynamicResource {x:Static SystemColors.WindowColorKey}}"),
                ("CardBrush", "{DynamicResource {x:Static SystemColors.WindowColorKey}}"),
                ("TextBrush", "{DynamicResource {x:Static SystemColors.WindowTextColorKey}}"),
                ("MutedTextBrush", "{DynamicResource {x:Static SystemColors.GrayTextColorKey}}"),
                ("AccentBrush", "{DynamicResource {x:Static SystemColors.HighlightColorKey}}"),
                ("FocusBrush", "{DynamicResource {x:Static SystemColors.HighlightColorKey}}"),
                ("ProgressTrackBrush", "{DynamicResource {x:Static SystemColors.WindowColorKey}}"),
                ("SecondaryBorderBrush", "{DynamicResource {x:Static SystemColors.WindowTextColorKey}}")
            };
            foreach (var (key, color) in highContrastMappings)
                Assert.Equal(color, dictionaries[2].Descendants().Single(element => element.Attribute(x + "Key")?.Value == key).Attribute("Color")?.Value);
    }

    [Fact]
    public void Theme_source_keeps_high_contrast_when_registry_lookup_is_invalid_missing_or_failing()
    {
        Assert.Equal(WindowsTheme.Dark, WindowsThemeSource.Resolve(false, 0));
        Assert.Equal(WindowsTheme.Light, WindowsThemeSource.Resolve(false, () => null));
        Assert.Equal(WindowsTheme.Light, WindowsThemeSource.Resolve(false, "invalid"));
        Assert.Equal(WindowsTheme.Light, WindowsThemeSource.Resolve(false, () => throw new InvalidOperationException()));
        Assert.Equal(WindowsTheme.HighContrast, WindowsThemeSource.Resolve(true, () => throw new InvalidOperationException()));
    }

    [Fact]
    public void Theme_controller_coalesces_off_dispatcher_changes_skips_duplicates_and_recovers_without_losing_resources()
    {
        var resources = new System.Windows.ResourceDictionary { ["Shared"] = "unchanged" };
        var source = new FakeThemeSource(WindowsTheme.Light); var factories = 0; var fail = false;
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        using var controller = new WindowsThemeController(resources, source, dispatcher, theme =>
        {
            factories++;
            if (fail) throw new InvalidOperationException();
            return new System.Windows.ResourceDictionary { ["Theme"] = theme };
        });
        var light = resources.MergedDictionaries.Single(); Assert.Equal(1, factories);
        source.Raise(); Assert.Same(light, resources.MergedDictionaries.Single()); Assert.Equal(1, factories);
        var raisedOnDispatcher = true;
        var worker = new Thread(() =>
        {
            raisedOnDispatcher = dispatcher.CheckAccess();
            source.Current = WindowsTheme.Dark; source.Raise();
            source.Current = WindowsTheme.HighContrast; source.Raise();
        });
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(2))); Assert.False(raisedOnDispatcher);
        Assert.Equal(WindowsTheme.HighContrast, source.Current); Assert.Equal(1, factories);
        PumpUntil(() => ActiveTheme(resources) == WindowsTheme.HighContrast); Assert.Equal(2, factories);
        var highContrast = resources.MergedDictionaries.Single(); source.Raise(); Assert.Same(highContrast, resources.MergedDictionaries.Single()); Assert.Equal(2, factories);
        fail = true; source.Current = WindowsTheme.Light; source.Raise(); Assert.Equal(WindowsTheme.HighContrast, ActiveTheme(resources)); Assert.Equal("unchanged", resources["Shared"]);
        fail = false; source.Current = WindowsTheme.Dark; controller.Reevaluate(); Assert.Equal(WindowsTheme.Dark, ActiveTheme(resources));
        controller.Dispose(); source.Current = WindowsTheme.Light; source.Raise(); Assert.Equal(WindowsTheme.Dark, ActiveTheme(resources)); Assert.Equal(1, source.Disposals);
    }

    [Fact]
    public void Theme_transition_preserves_window_identity_status_focus_automation_percentage_and_reset_semantics()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            System.Windows.Window? window = null;
            try
            {
                var source = new FakeThemeSource(WindowsTheme.Light); var resources = new System.Windows.ResourceDictionary();
                using var controller = new WindowsThemeController(resources, source, System.Windows.Threading.Dispatcher.CurrentDispatcher, theme => new() { ["Theme"] = theme });
                var status = new System.Windows.Controls.TextBlock { Text = "Unavailable" };
                var percentage = new System.Windows.Controls.TextBlock { Text = "42%" };
                var reset = new System.Windows.Controls.TextBlock { Text = "Resets in 5h" };
                var focus = new System.Windows.Controls.Button { Content = "Refresh", Focusable = true };
                System.Windows.Automation.AutomationProperties.SetName(focus, "Refresh quota");
                window = new System.Windows.Window { Content = new System.Windows.Controls.StackPanel { Children = { status, percentage, reset, focus } } };
                var identity = window; var popover = new WpfPopoverRuntime(window, controller);
                source.Current = WindowsTheme.Dark; popover.Show();
                Assert.True(popover.IsVisible); Assert.Same(identity, window); Assert.Equal(WindowsTheme.Dark, ActiveTheme(resources)); Assert.Equal("Unavailable", status.Text); Assert.Equal("42%", percentage.Text); Assert.Equal("Resets in 5h", reset.Text); Assert.True(focus.Focusable); Assert.Equal("Refresh quota", System.Windows.Automation.AutomationProperties.GetName(focus));
            }
            finally { window?.Close(); }
        });
    }

    [Fact]
    public void Unit_4a_popup_hierarchy_keeps_permanent_slots_and_secondary_scrolling()
    {
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var window = XDocument.Parse(ReadProjectFile("src/AIBar.Desktop/MainWindow.xaml"));
        Assert.Equal("AIBar quota", window.Root!.Attribute("Title")?.Value);
        var grid = window.Root.Element(presentation + "Grid")!;
        Assert.Equal(4, grid.Element(presentation + "Grid.RowDefinitions")!.Elements().Count());
        Assert.Equal(new[] { "0", "1", "2", "3" }, grid.Elements().Where(element => element.Name != presentation + "Grid.RowDefinitions").Select(element => element.Attribute("Grid.Row")?.Value ?? "0"));
        Assert.Equal("Continue", grid.Attribute("KeyboardNavigation.TabNavigation")?.Value);
        var cards = window.Descendants(presentation + "GroupBox").Take(2).ToArray();
        Assert.Equal(new[] { "5-hour quota card", "Weekly quota card" }, cards.Select(card => card.Attribute(presentation + "AutomationProperties.Name")?.Value));
        Assert.All(cards, card => { Assert.Null(card.Attribute("Visibility")); Assert.Contains("percentage, reset, and state", card.Attribute(presentation + "AutomationProperties.HelpText")?.Value); });
        var values = window.Descendants().Attributes().Select(attribute => attribute.Value).ToArray();
        Assert.Equal(2, values.Count(value => value.Contains("TargetNullValue=--", StringComparison.Ordinal)));
        Assert.Equal(2, values.Count(value => value.Contains("TargetNullValue=unavailable", StringComparison.Ordinal)));
        Assert.Equal(3, values.Count(value => value == "{Binding FreshnessLabel}"));
        var scrollers = window.Descendants(presentation + "ScrollViewer").ToArray();
        Assert.Equal("SecondaryScrollViewer", scrollers[0].Attribute(x + "Name")?.Value);
        Assert.Equal("2", scrollers[0].Attribute("Grid.Row")?.Value);
        Assert.Equal("OnSecondaryPreviewGotKeyboardFocus", scrollers[0].Attribute("PreviewGotKeyboardFocus")?.Value);
        Assert.All(scrollers.Skip(1), scroller => Assert.Contains(scroller.Ancestors(), ancestor => ancestor == scrollers[0]));
    }

    [Fact]
    public void Unit_4a_popup_accessibility_keeps_summary_live_status_and_noninteractive_progress()
    {
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var window = XDocument.Parse(ReadProjectFile("src/AIBar.Desktop/MainWindow.xaml"));
        var resources = XDocument.Parse(ReadProjectFile("src/AIBar.Desktop/App.xaml"));
        var summary = window.Descendants(presentation + "Grid").Single(grid => grid.Attribute(presentation + "AutomationProperties.Name")?.Value == "Quota summary");
        Assert.Equal("0", summary.Attribute("Grid.Row")?.Value);
        Assert.True(window.Descendants().Count(element => element.Attribute(presentation + "AutomationProperties.LiveSetting")?.Value == "Polite") >= 3);
        var progress = window.Descendants(presentation + "ProgressBar").ToArray();
        Assert.Equal(2, progress.Length); Assert.All(progress, item => { Assert.NotNull(item.Attribute(presentation + "AutomationProperties.Name")); Assert.Equal("{StaticResource QuotaProgressStyle}", item.Attribute("Style")?.Value); });
        var progressStyle = resources.Descendants(presentation + "Style").Single(style => style.Attribute(x + "Key")?.Value == "QuotaProgressStyle");
        Assert.Equal("False", progressStyle.Elements(presentation + "Setter").Single(setter => setter.Attribute("Property")?.Value == "Focusable").Attribute("Value")?.Value);
        Assert.Equal("False", progressStyle.Elements(presentation + "Setter").Single(setter => setter.Attribute("Property")?.Value == "IsHitTestVisible").Attribute("Value")?.Value);
        var expander = window.Descendants(presentation + "Expander").Single();
        Assert.Equal("{StaticResource AccessibleExpanderStyle}", expander.Attribute("Style")?.Value);
        var expanderStyle = resources.Descendants(presentation + "Style").Single(style => style.Attribute(x + "Key")?.Value == "AccessibleExpanderStyle");
        Assert.Equal("{StaticResource KeyboardFocusVisual}", expanderStyle.Elements(presentation + "Setter").Single(setter => setter.Attribute("Property")?.Value == "FocusVisualStyle").Attribute("Value")?.Value);
        var requested = false; Exception? failure = null;
        var thread = new Thread(() => { try { var target = new System.Windows.Controls.Border(); target.RequestBringIntoView += (_, _) => requested = true; var args = new System.Windows.Input.KeyboardFocusChangedEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, 0, null, target) { RoutedEvent = System.Windows.Input.Keyboard.PreviewGotKeyboardFocusEvent }; target.RaiseEvent(args); typeof(MainWindow).GetMethod("OnSecondaryPreviewGotKeyboardFocus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MainWindow)), [null, args]); } catch (Exception exception) { failure = exception; } finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.True(requested);
    }

    [Fact]
    public void Unit_4b_measurement_limits_preserve_the_Unit_3_semantic_surface()
    {
        var window = XDocument.Parse(ReadProjectFile("src/AIBar.Desktop/MainWindow.xaml")).Root!;
        Assert.Equal("300", window.Attribute("MinWidth")?.Value); Assert.Equal("260", window.Attribute("MinHeight")?.Value); Assert.Equal("720", window.Attribute("MaxHeight")?.Value);
        Assert.Contains("SurfaceBrush", ReadProjectFile("src/AIBar.Desktop/Themes/Semantic.Light.xaml"), StringComparison.Ordinal);
        Assert.Contains("SurfaceBrush", ReadProjectFile("src/AIBar.Desktop/Themes/Semantic.Dark.xaml"), StringComparison.Ordinal);
    }

    private static WindowsTheme ActiveTheme(System.Windows.ResourceDictionary resources) => (WindowsTheme)resources.MergedDictionaries.Last()["Theme"];
    private sealed class FakeThemeSource(WindowsTheme current) : IWindowsThemeSource
    {
        public event EventHandler? Changed;
        public WindowsTheme Current { get; set; } = current;
        public int Disposals { get; private set; }
        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
        public void Dispose() => Disposals++;
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
