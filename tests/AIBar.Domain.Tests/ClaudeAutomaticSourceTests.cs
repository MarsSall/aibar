using System.Reflection;
using System.Text.Json;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;
using Forms = System.Windows.Forms;

namespace AIBar.Domain.Tests;

// T4 / S3. Synthetic fixtures only; never open a real Claude login or make network requests.
public sealed class ClaudeAutomaticSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-claude-auto-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private string SettingsPath => Path.Combine(_root, "settings.json");
    private string RemotePath => Path.Combine(_root, "claude-remote-usage.json");
    private ConsentSettings Settings => new(SettingsPath);

    // a: exact wire values, backward compatibility and independent consent.
    [Theory]
    [InlineData(ClaudeQuotaSource.Automatic, "automatic")]
    [InlineData(ClaudeQuotaSource.ThisPc, "this-pc")]
    [InlineData(ClaudeQuotaSource.OtherMachine, "other-machine")]
    public async Task All_source_values_round_trip_without_changing_consents(ClaudeQuotaSource source, string wire)
    {
        await Settings.SaveAsync(true, default);
        await Settings.SaveClaudeAsync(false, default);
        await Settings.SaveClaudeSourceAsync(source, default);
        using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath)))
            Assert.Equal(wire, json.RootElement.GetProperty("claudeSource").GetString());
        Assert.Equal(source, await Settings.LoadClaudeSourceAsync(default));
        Assert.True(await Settings.LoadAsync(default));
        Assert.False(await Settings.LoadClaudeAsync(default));
        await Settings.SaveClaudeAsync(true, default);
        await Settings.SaveAsync(false, default);
        Assert.Equal(source, await Settings.LoadClaudeSourceAsync(default));
        Assert.False(await Settings.LoadAsync(default));
        Assert.True(await Settings.LoadClaudeAsync(default));
    }

    [Theory]
    [InlineData("automatic", ClaudeQuotaSource.Automatic)]
    [InlineData("this-pc", ClaudeQuotaSource.ThisPc)]
    [InlineData("other-machine", ClaudeQuotaSource.OtherMachine)]
    public async Task Existing_explicit_settings_load_without_migration(string wire, ClaudeQuotaSource source)
    {
        Directory.CreateDirectory(_root);
        var text = "{\"schema\":1,\"privateCodexConsent\":true,\"privateClaudeConsent\":true,\"claudeSource\":\"" + wire + "\"}";
        await File.WriteAllTextAsync(SettingsPath, text);
        Assert.Equal(source, await Settings.LoadClaudeSourceAsync(default));
        Assert.Equal(text, await File.ReadAllTextAsync(SettingsPath));
        Assert.True(await Settings.LoadAsync(default));
        Assert.True(await Settings.LoadClaudeAsync(default));
    }

    [Fact]
    public async Task Codex_only_file_round_trip_keeps_its_shape_and_defaults_to_automatic()
    {
        await Settings.SaveAsync(true, default);
        var before = await File.ReadAllTextAsync(SettingsPath);
        Assert.Equal(ClaudeQuotaSource.Automatic, await Settings.LoadClaudeSourceAsync(default));
        Assert.False(await Settings.LoadClaudeAsync(default));
        await Settings.SaveAsync(true, default);
        Assert.Equal(before, await File.ReadAllTextAsync(SettingsPath));
        using var json = JsonDocument.Parse(before);
        Assert.Equal(new[] { "schema", "privateCodexConsent" }, json.RootElement.EnumerateObject().Select(p => p.Name));
    }

    // b: metadata, not payload freshness or validity, determines automatic availability.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Availability_is_based_on_file_presence_not_payload(bool exists)
    {
        if (exists) { Directory.CreateDirectory(_root); await File.WriteAllTextAsync(RemotePath, "not valid usage JSON"); }
        Assert.Equal(exists, ClaudeRemoteUsageProvider.IsAvailableForAutomatic(RemotePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Automatic_detection_rejects_reparse_file_or_parent(bool parent)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(RemotePath, "synthetic");
        var blocked = parent ? _root : RemotePath;
        Assert.False(ClaudeRemoteUsageProvider.IsAvailableForAutomatic(RemotePath,
            path => path == blocked ? FileAttributes.ReparsePoint : FileAttributes.Normal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initialization_resolves_before_constructing_a_provider(bool present)
    {
        await Settings.SaveClaudeAsync(true, default);
        var locals = 0; var remotes = 0;
        await using var runtime = Runtime(new Store(), _ => { locals++; return new Provider(11); },
            _ => { remotes++; return new Provider(73); }, () => present);
        Assert.Equal(0, locals + remotes);
        await runtime.InitializeAsync(default);
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(ClaudeQuotaSource.Automatic, runtime.Source);
        Assert.Equal(present ? ClaudeQuotaSource.OtherMachine : ClaudeQuotaSource.ThisPc, runtime.ResolvedSource);
        Assert.Equal(present ? 0 : 1, locals);
        Assert.Equal(present ? 1 : 0, remotes);
        Assert.Equal(present ? 73m : 11m, runtime.State.Snapshot!.Primary!.PercentageUsed);
    }

    [Theory]
    [InlineData(RefreshTrigger.Manual)]
    [InlineData(RefreshTrigger.Poll)]
    public async Task Every_refresh_redetects_appearance_and_removal_even_with_current_cache(RefreshTrigger trigger)
    {
        var present = false; var local = new Provider(11); var remote = new Provider(73); var store = new Store();
        await using var runtime = Runtime(store, _ => local, _ => remote, () => present);
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        Assert.Equal(11m, runtime.State.Snapshot!.Primary!.PercentageUsed);
        var clears = store.Clears; var changes = 0;
        runtime.SourceChanged += () => changes++;
        present = true;
        await runtime.RefreshAsync(trigger, default); // must bypass old provider's current-cache poll throttle
        Assert.Equal(73m, runtime.State.Snapshot!.Primary!.PercentageUsed);
        Assert.Equal(ClaudeQuotaSource.OtherMachine, runtime.ResolvedSource);
        Assert.True(store.Clears > clears);
        clears = store.Clears;
        present = false;
        await runtime.RefreshAsync(trigger, default);
        Assert.Equal(11m, runtime.State.Snapshot!.Primary!.PercentageUsed);
        Assert.Equal(ClaudeQuotaSource.ThisPc, runtime.ResolvedSource);
        Assert.True(store.Clears > clears);
        Assert.Equal(2, changes);
        Assert.Equal(ClaudeQuotaSource.Automatic, runtime.Source);
        Assert.False(File.Exists(SettingsPath) && (await File.ReadAllTextAsync(SettingsPath)).Contains("claudeSource"));
    }

    [Fact]
    public async Task Polling_timer_redetects_appearance_and_removal_without_manual_refresh()
    {
        var time = new VirtualTime(); var present = false;
        await using var runtime = Runtime(new Store(), _ => new Provider(11), _ => new Provider(73), () => present, time);
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        present = true;
        time.Advance(TimeSpan.FromMinutes(5));
        await Until(() => runtime.State.Snapshot?.Primary?.PercentageUsed == 73);
        present = false;
        time.Advance(TimeSpan.FromMinutes(5));
        await Until(() => runtime.State.Snapshot?.Primary?.PercentageUsed == 11);
        Assert.Equal(ClaudeQuotaSource.Automatic, runtime.Source);
    }

    [Fact]
    public async Task Resolved_remote_never_constructs_or_opens_the_local_credential_reader()
    {
        var files = new RemoteFiles(CurrentDocument(Now));
        await using var runtime = Runtime(new Store(), _ => throw new InvalidOperationException("Local credential path must not be reached"),
            policy => Remote(policy, files), () => true);
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(ClaudeQuotaSource.OtherMachine, runtime.ResolvedSource);
        Assert.NotEmpty(files.Paths);
        Assert.All(files.Paths, path => Assert.Equal(RemotePath, path));
        Assert.Equal(FreshnessState.Current, runtime.State.Freshness);
    }

    // c/e: stale/invalid/auth-failed files remain remote, with resolved-source guidance.
    [Theory]
    [InlineData("stale")]
    [InlineData("invalid")]
    [InlineData("authentication-required")]
    public async Task Existing_bad_remote_never_falls_back_or_reuses_local_cache(string kind)
    {
        var present = false; var locals = 0;
        var files = new RemoteFiles(kind switch
        {
            "stale" => CurrentDocument(Now.AddMinutes(-16)),
            "invalid" => "{bad",
            _ => "{\"schemaVersion\":1,\"generatedAt\":\"2030-01-01T12:00:00Z\",\"state\":\"authentication-required\",\"fiveHour\":null,\"weekly\":null}"
        });
        var store = new Store();
        await using var runtime = Runtime(store, _ => { locals++; return new Provider(11); }, policy => Remote(policy, files), () => present);
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        Assert.NotNull(runtime.State.Snapshot);
        present = true;
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(1, locals);
        Assert.Equal(ClaudeQuotaSource.OtherMachine, runtime.ResolvedSource);
        Assert.Null(runtime.State.Snapshot); // previous local cache cannot masquerade as remote
        Assert.Null(store.Snapshot);
        Assert.Equal(FreshnessState.Unavailable, runtime.State.Freshness);
        var panel = new ClaudeQuotaPresentationMapper(new Clock()).Map(runtime.State, true, runtime.ResolvedSource);
        Assert.Contains("other machine", panel.WarningLabel!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("this PC", panel.WarningLabel!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Remote_failure_keeps_its_own_cache_stale_without_falling_back(bool invalid)
    {
        var locals = 0; var files = new RemoteFiles(CurrentDocument(Now));
        await using var runtime = Runtime(new Store(), _ => { locals++; return new Provider(11); }, policy => Remote(policy, files), () => true);
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        Assert.Equal(73m, runtime.State.Snapshot!.Primary!.PercentageUsed);
        files.Text = invalid ? "{bad" : CurrentDocument(Now.AddMinutes(-16));
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(0, locals);
        Assert.Equal(ClaudeQuotaSource.OtherMachine, runtime.ResolvedSource);
        Assert.Equal(73m, runtime.State.Snapshot!.Primary!.PercentageUsed);
        Assert.Equal(FreshnessState.Stale, runtime.State.Freshness);
        var panel = new ClaudeQuotaPresentationMapper(new Clock()).Map(runtime.State, true, runtime.ResolvedSource);
        Assert.Contains("other machine", panel.WarningLabel!, StringComparison.OrdinalIgnoreCase);
    }

    // d: cancellation-ignoring providers and stores must be drained before the new authority wins.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detection_switch_cancels_and_rejects_late_provider_results_in_both_directions(bool initiallyRemote)
    {
        var present = initiallyRemote; var blocked = new BlockingProvider(); var replacement = new Provider(73); var store = new Store();
        await Settings.SaveClaudeAsync(true, default);
        await using var runtime = Runtime(store, _ => initiallyRemote ? replacement : blocked,
            _ => initiallyRemote ? blocked : replacement, () => present);
        await runtime.InitializeAsync(default);
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        present = !present;
        var switching = runtime.RefreshAsync(RefreshTrigger.Manual, default).AsTask();
        try
        {
            await Until(() => blocked.Token.IsCancellationRequested);
            Assert.False(switching.IsCompleted);
            Assert.Equal(0, replacement.Calls);
        }
        finally
        {
            blocked.Release.TrySetResult(Result(11));
            await switching.WaitAsync(TimeSpan.FromSeconds(2));
        }
        Assert.Equal(73m, runtime.State.Snapshot!.Primary!.PercentageUsed);
        Assert.Equal(73m, store.Snapshot!.Primary!.PercentageUsed);
        Assert.DoesNotContain(store.Saved, s => s.Primary?.PercentageUsed == 11);
        Assert.True(store.Clears > 0);
        Assert.Equal(ClaudeQuotaSource.Automatic, runtime.Source);
        Assert.Equal(initiallyRemote ? ClaudeQuotaSource.ThisPc : ClaudeQuotaSource.OtherMachine, runtime.ResolvedSource);
    }

    [Fact]
    public async Task Detection_switch_drains_entered_persistence_then_clears_before_remote_refresh()
    {
        var present = false; var remote = new Provider(73); var store = new Store { BlockFirstSave = true };
        await Settings.SaveClaudeAsync(true, default);
        await using var runtime = Runtime(store, _ => new Provider(11), _ => remote, () => present);
        await runtime.InitializeAsync(default);
        await store.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        present = true;
        var switching = runtime.RefreshAsync(RefreshTrigger.Poll, default).AsTask();
        try
        {
            await Until(() => store.SaveToken.IsCancellationRequested);
            Assert.False(switching.IsCompleted);
            Assert.Equal(0, remote.Calls);
        }
        finally
        {
            store.ReleaseSave.TrySetResult();
            await switching.WaitAsync(TimeSpan.FromSeconds(2));
        }
        Assert.Equal(new[] { "save:11", "clear", "save:73" }, store.Operations);
        Assert.Equal(73m, store.Snapshot!.Primary!.PercentageUsed);
        Assert.Equal(73m, runtime.State.Snapshot!.Primary!.PercentageUsed);
    }

    [Fact]
    public async Task Only_resolved_remote_has_a_freshness_deadline_and_removal_cancels_it()
    {
        var time = new VirtualTime(); var present = true;
        await using var runtime = Runtime(new Store(), _ => new Provider(11), _ => new Provider(73, Now.AddMinutes(-14)), () => present, time);
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        Assert.Equal(2, time.ActiveTimers); // polling plus generatedAt + fifteen minutes
        present = false;
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(1, time.ActiveTimers); // local polling only
        var changes = 0;
        runtime.StateChanged += () => changes++;
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, changes);
        Assert.Equal(FreshnessState.Current, runtime.State.Freshness);
    }

    [Fact]
    public async Task Automatically_resolved_remote_expires_at_its_deadline_without_a_poll()
    {
        // Expire before S13's one-minute poll so this remains a deadline-only test.
        var time = new VirtualTime(); var remote = new Provider(73, Now.AddMinutes(-14).AddSeconds(-30));
        await using var runtime = Runtime(new Store(), _ => new Provider(11), _ => remote, () => true, time);
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        Assert.Equal(FreshnessState.Current, runtime.State.Freshness);
        var reads = remote.Calls; var changes = 0;
        runtime.StateChanged += () => changes++;
        time.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(0, changes);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, changes);
        Assert.Equal(FreshnessState.Stale, runtime.State.Freshness);
        Assert.Equal(73m, runtime.State.Snapshot!.Primary!.PercentageUsed);
        Assert.Equal(reads, remote.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authentication_guidance_follows_resolution_not_the_automatic_selection(bool present)
    {
        var failure = new FailureProvider();
        await using var runtime = Runtime(new Store(), _ => failure, _ => failure, () => present);
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        var panel = new ClaudeQuotaPresentationMapper(new Clock()).Map(runtime.State, true, runtime.ResolvedSource);
        var expected = present ? "Open Claude Code on the other machine to renew the session" : "Open Claude Code on this PC to renew the session";
        Assert.Equal(expected, panel.WarningLabel);
        Assert.Equal(expected, panel.ErrorLabel);
        using var host = new ClaudeQuotaPresentationHost(runtime, new ClaudeQuotaPresentationMapper(new Clock()));
        Assert.Equal(expected, host.State.WarningLabel);
        Assert.Equal(expected, host.State.ErrorLabel);
    }

    [Fact]
    public void Disclosure_mentions_automatic_detection_and_keeps_read_only_privacy_promises()
    {
        foreach (var text in new[] { "automatic", "detect", "this PC", "other machine", "no credentials", "read-only", "never refresh" })
            Assert.Contains(text, ClaudeQuotaRuntime.ConsentDisclosure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tray_has_no_Claude_configuration_and_runtime_still_routes_all_source_choices()
    {
        await using var tray = new WindowsTrayRuntime();
        tray.SetSettingsAvailable(true);
        var menu = (Forms.ContextMenuStrip)typeof(WindowsTrayRuntime).GetField("_menu", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tray)!;
        Assert.DoesNotContain(menu.Items.Cast<Forms.ToolStripItem>(), item => item.Text!.Contains("Claude"));
        var local = new Provider(11); var remote = new Provider(73);
        await using var runtime = Runtime(new Store(), _ => local, _ => remote, () => true);
        await runtime.InitializeAsync(default);
        Assert.Equal(ClaudeQuotaSource.Automatic, runtime.Source);
        await runtime.GrantConsentAsync(default);
        foreach (var source in new[] { ClaudeQuotaSource.Automatic, ClaudeQuotaSource.ThisPc, ClaudeQuotaSource.OtherMachine })
        {
            await runtime.SetSourceAsync(source, default);
            Assert.Equal(source, runtime.Source);
            Assert.Equal(source == ClaudeQuotaSource.ThisPc ? ClaudeQuotaSource.ThisPc : ClaudeQuotaSource.OtherMachine, runtime.ResolvedSource);
            Assert.Equal(source == ClaudeQuotaSource.ThisPc ? 11m : 73m, runtime.State.Snapshot!.Primary!.PercentageUsed);
        }
    }

    [Theory]
    [InlineData(ClaudeQuotaSource.ThisPc, true)]
    [InlineData(ClaudeQuotaSource.OtherMachine, false)]
    public async Task Explicit_modes_ignore_detection_until_user_selects_automatic(ClaudeQuotaSource forced, bool present)
    {
        await Settings.SaveClaudeSourceAsync(forced, default);
        var local = new Provider(11); var remote = new Provider(73);
        await using var runtime = Runtime(new Store(), _ => local, _ => remote, () => present);
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        Assert.Equal(forced, runtime.ResolvedSource);
        Assert.Equal(forced == ClaudeQuotaSource.ThisPc ? 11m : 73m, runtime.State.Snapshot!.Primary!.PercentageUsed);
        await runtime.SetSourceAsync(ClaudeQuotaSource.Automatic, default);
        Assert.Equal(ClaudeQuotaSource.Automatic, runtime.Source);
        Assert.Equal(present ? ClaudeQuotaSource.OtherMachine : ClaudeQuotaSource.ThisPc, runtime.ResolvedSource);
        Assert.Equal(present ? 73m : 11m, runtime.State.Snapshot!.Primary!.PercentageUsed);
        Assert.True(await Settings.LoadClaudeAsync(default));
        Assert.False(await Settings.LoadAsync(default));
    }

    // g: exercise real composition's owned data path, not a global user-profile remote file.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Default_composition_is_lazy_and_detects_only_its_owned_remote_path(bool present)
    {
        Directory.CreateDirectory(_root);
        if (present) await File.WriteAllTextAsync(RemotePath, "presence is sufficient for selection");
        var locals = 0; var remotes = 0;
        var composition = App.CreateComposition(new App.CompositionSeams(DataDirectory: _root,
            CodexHome: Path.Combine(_root, "synthetic-codex"), UserProfile: Path.Combine(_root, "synthetic-profile"),
            ClaudeProviderFactory: _ => { locals++; return new Provider(11); },
            ClaudeRemoteProviderFactory: _ => { remotes++; return new Provider(73); }));
        await using var resource = composition.Resource;
        Assert.Equal(0, locals + remotes);
        await composition.Initialize();
        Assert.Equal(0, locals + remotes);
        await composition.ClaudeQuota.GrantConsentAsync(default);
        Assert.Equal(present ? 0 : 1, locals);
        Assert.Equal(present ? 1 : 0, remotes);
        Assert.Equal(present ? 73m : 11m, composition.ClaudeQuota.State.Snapshot!.Primary!.PercentageUsed);
        Assert.Equal(ClaudeQuotaSource.Automatic, composition.ClaudeQuota.Source);
        Assert.False(await Settings.LoadAsync(default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restart_after_detection_changes_clears_the_previous_sources_persisted_cache(bool initiallyRemote)
    {
        var present = initiallyRemote; var store = new Store();
        await using (var first = Runtime(store, _ => new Provider(11), _ => new Provider(73), () => present))
        {
            await first.InitializeAsync(default);
            await first.GrantConsentAsync(default);
            Assert.Equal(initiallyRemote ? 73m : 11m, store.Snapshot!.Primary!.PercentageUsed);
        }
        // Consent saves by either integration must preserve the cache's source identity.
        await Settings.SaveAsync(true, default);
        await Settings.SaveClaudeAsync(true, default);
        present = !present;
        var clears = store.Clears;
        var failure = new FailureProvider();
        await using var restarted = Runtime(store, _ => failure, _ => failure, () => present);
        await restarted.InitializeAsync(default);
        await restarted.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.True(store.Clears > clears);
        Assert.Null(store.Snapshot);
        Assert.Null(restarted.State.Snapshot);
        Assert.Equal(present ? ClaudeQuotaSource.OtherMachine : ClaudeQuotaSource.ThisPc, restarted.ResolvedSource);
        Assert.True(await Settings.LoadAsync(default));
        Assert.True(await Settings.LoadClaudeAsync(default));
    }

    // L33: handlers deliberately block like a synchronous UI subscriber. Both the
    // inner wait and the outer operation are bounded, so the old code fails, not hangs.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Automatic_handoff_notifications_allow_synchronous_refresh(bool initiallyRemote, bool sourceEvent)
    {
        var present = initiallyRemote;
        await using var runtime = Runtime(new Store(), _ => new Provider(11), _ => new Provider(73), () => present);
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        var probe = new ReentryProbe(runtime);
        if (sourceEvent) runtime.SourceChanged += probe.Handle;
        else runtime.StateChanged += probe.Handle;
        present = !present;
        await Task.Run(async () => await runtime.RefreshAsync(RefreshTrigger.Manual, default))
            .WaitAsync(TimeSpan.FromSeconds(15));
        probe.AssertSucceeded();
    }

    [Theory]
    [InlineData("initialize-source")]
    [InlineData("initialize-state")]
    [InlineData("grant-state")]
    [InlineData("grant-source")]
    [InlineData("grant-consent")]
    [InlineData("revoke-state")]
    [InlineData("revoke-consent")]
    [InlineData("clear-state")]
    [InlineData("clear-consent")]
    [InlineData("manual-source")]
    [InlineData("manual-state")]
    public async Task Lifecycle_notifications_allow_synchronous_refresh(string site)
    {
        var present = false;
        await using var runtime = Runtime(new Store(), _ => new Provider(11), _ => new Provider(73), () => present);
        if (site.StartsWith("initialize")) await Settings.SaveClaudeAsync(true, default);
        else
        {
            await runtime.InitializeAsync(default);
            if (!site.StartsWith("grant")) await runtime.GrantConsentAsync(default);
        }
        var probe = new ReentryProbe(runtime);
        if (site.EndsWith("source")) runtime.SourceChanged += probe.Handle;
        else if (site.EndsWith("consent")) runtime.ConsentChanged += probe.Handle;
        else runtime.StateChanged += probe.Handle;
        await Task.Run(async () =>
        {
            if (site.StartsWith("initialize")) await runtime.InitializeAsync(default);
            else if (site.StartsWith("grant")) { present = true; await runtime.GrantConsentAsync(default); }
            else if (site.StartsWith("revoke")) await runtime.RevokeConsentAsync(default);
            else if (site.StartsWith("clear")) { await runtime.CancelAndWaitAsync(default); runtime.ResumeAfterClear(); }
            else await runtime.SetSourceAsync(ClaudeQuotaSource.OtherMachine, default);
        }).WaitAsync(TimeSpan.FromSeconds(15));
        probe.AssertSucceeded();
    }

    [Fact]
    public async Task Freshness_deadline_notification_allows_synchronous_refresh()
    {
        var time = new VirtualTime();
        await using var runtime = Runtime(new Store(), _ => new Provider(11),
            _ => new Provider(73, Now.AddMinutes(-14).AddSeconds(-30)), () => true, time);
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        var probe = new ReentryProbe(runtime);
        runtime.StateChanged += probe.Handle;
        // Isolate deadline reentry from the new one-minute periodic poll.
        await Task.Run(() => time.Advance(TimeSpan.FromSeconds(30))).WaitAsync(TimeSpan.FromSeconds(15));
        probe.AssertSucceeded();
    }

    [Fact]
    public async Task Handoff_publishes_source_before_forwarded_state_without_losing_notifications()
    {
        var present = false;
        await using var runtime = Runtime(new Store(), _ => new Provider(11), _ => new Provider(73), () => present);
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        var events = new List<string>();
        runtime.SourceChanged += () => events.Add("source");
        runtime.StateChanged += () => events.Add("state");
        present = true;
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal("source", events[0]);
        Assert.Single(events.Where(value => value == "source"));
        // teardown clear, initialization, explicit handoff, loading, completed retrieval
        Assert.Equal(5, events.Count(value => value == "state"));
    }

    private sealed class ReentryProbe(ClaudeQuotaRuntime runtime)
    {
        private int _entered;
        private bool _completed;
        private Exception? _failure;
        public void Handle()
        {
            if (Interlocked.Exchange(ref _entered, 1) != 0) return;
            try
            {
                _completed = runtime.RefreshAsync(RefreshTrigger.Manual, default).AsTask()
                    .Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception) { _failure = exception; }
        }
        public void AssertSucceeded()
        {
            Assert.Equal(1, _entered);
            Assert.Null(_failure);
            Assert.True(_completed, "Synchronous subscriber could not re-enter RefreshAsync within five seconds.");
        }
    }

    private ClaudeQuotaRuntime Runtime(Store store, Func<PrivateIntegrationPolicy, IQuotaProvider> local,
        Func<PrivateIntegrationPolicy, IQuotaProvider> remote, Func<bool> available, VirtualTime? time = null) =>
        new(Settings, store, local, new Clock(time), timeProvider: time, remoteProviderFactory: remote, remoteFileAvailable: available);
    private ClaudeRemoteUsageProvider Remote(PrivateIntegrationPolicy policy, RemoteFiles files) =>
        new(policy, RemotePath, files, _ => FileAttributes.Normal, () => Now);
    private static QuotaProviderResult Result(decimal value, DateTimeOffset? generated = null) =>
        new(new(new(value, Now.AddHours(5)), null, generated ?? Now), null);
    private static string CurrentDocument(DateTimeOffset generated) =>
        "{\"schemaVersion\":1,\"generatedAt\":\"" + generated.ToString("yyyy-MM-ddTHH:mm:ss'Z'") + "\",\"state\":\"current\",\"fiveHour\":{\"percentageUsed\":73,\"resetAt\":\"2030-01-01T17:00:00Z\"},\"weekly\":null}";
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class Clock(VirtualTime? time = null) : IClock { public DateTimeOffset UtcNow => time?.GetUtcNow() ?? Now; }
    private sealed class Provider(decimal value, DateTimeOffset? generated = null) : IQuotaProvider
    {
        public int Calls { get; private set; }
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token) { Calls++; return ValueTask.FromResult(Result(value, generated)); }
    }
    private sealed class FailureProvider : IQuotaProvider
    {
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token) =>
            ValueTask.FromResult(new QuotaProviderResult(null, new(QuotaErrorKind.Authentication, "quota_authentication")));
    }
    private sealed class BlockingProvider : IQuotaProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<QuotaProviderResult> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token) { Token = token; Started.TrySetResult(); return new(Release.Task); }
    }
    private sealed class Store : IQuotaSnapshotStore
    {
        public QuotaSnapshot? Snapshot { get; private set; }
        public List<QuotaSnapshot> Saved { get; } = [];
        public List<string> Operations { get; } = [];
        public int Clears { get; private set; }
        public bool BlockFirstSave { get; init; }
        public CancellationToken SaveToken { get; private set; }
        public TaskCompletionSource SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken token) => ValueTask.FromResult(Snapshot);
        public async ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken token)
        {
            if (BlockFirstSave && Saved.Count == 0)
            {
                SaveToken = token; SaveEntered.TrySetResult();
                await ReleaseSave.Task; // intentionally ignore cancellation to model an entered database write
            }
            Snapshot = snapshot; Saved.Add(snapshot);
            Operations.Add($"save:{snapshot.Primary!.PercentageUsed}");
        }
        public ValueTask ClearAsync(CancellationToken token) { Snapshot = null; Clears++; Operations.Add("clear"); return ValueTask.CompletedTask; }
    }
    private sealed class RemoteFiles(string text) : ICredentialFileReader
    {
        public string Text { get; set; } = text;
        public List<string> Paths { get; } = [];
        public ValueTask<Stream> OpenReadAsync(string path, FileShare share, CancellationToken token)
        { Paths.Add(path); return ValueTask.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Text), writable: false)); }
    }
    private sealed class VirtualTime : TimeProvider
    {
        private DateTimeOffset _now = Now;
        private readonly List<Timer> _timers = [];
        public override DateTimeOffset GetUtcNow() => _now;
        public int ActiveTimers => _timers.Count(timer => timer.Due is not null);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state); _timers.Add(timer); timer.Change(dueTime, period); return timer;
        }
        public void Advance(TimeSpan elapsed)
        {
            var target = _now + elapsed;
            while (_timers.Where(t => t.Due <= target).OrderBy(t => t.Due).FirstOrDefault() is { } timer)
            { _now = timer.Due!.Value; timer.Fire(); }
            _now = target;
        }
        private sealed class Timer(VirtualTime owner, TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            private TimeSpan _period;
            public DateTimeOffset? Due { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            { if (_disposed) return false; _period = period; Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime; return true; }
            public void Fire() { Due = _period == Timeout.InfiniteTimeSpan ? null : owner._now + _period; callback(state); }
            public void Dispose() { _disposed = true; Due = null; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
