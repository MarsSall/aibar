using AIBar.Desktop;
using AIBar.Application;
using System.Windows.Threading;

namespace AIBar.Domain.Tests;

public sealed class HostRuntimeTests
{
    [Fact]
    public async Task Tray_toggle_recreation_activation_and_deactivation_are_deterministic()
    {
        var name = $"AIBar.Tests.{Guid.NewGuid():N}";
        using var instance = new SingleInstanceHost(name);
        var tray = new FakeTray();
        var popover = new FakePopover();
        var taskbar = new FakeRecreationEvents();
        var refreshes = 0;
        var command = new ManualRefreshCommand(_ => { refreshes++; return ValueTask.CompletedTask; }, () => true);
        await using var host = new TrayHostRuntime(instance, tray, popover, taskbar, _ => Task.CompletedTask, new ProbeResource(), () => { }, command);
        host.Start(); Assert.True(tray.RefreshAvailable); tray.Refresh();
        Assert.Equal(1, refreshes);

        tray.Click(); tray.Click();
        Assert.Equal(1, popover.Shows); Assert.Equal(1, popover.Hides);
        popover.IsOwnedDialogActive = true; popover.Deactivate();
        Assert.Equal(1, popover.Hides);
        popover.IsOwnedDialogActive = false; popover.Deactivate();
        Assert.Equal(2, popover.Hides);
        taskbar.Recreate();
        Assert.Equal(2, tray.Shows);
        using var secondary = new SingleInstanceHost(name);
        Assert.True(secondary.RequestActivation());
        PumpUntil(() => popover.Shows == 2);
        Assert.Equal(2, popover.Shows);
        instance.Dispose();
        await host.ExitAsync();
    }

    [Fact]
    public async Task Exit_cancels_waits_then_disposes_every_resource_once_and_blocks_late_activation()
    {
        using var instance = new SingleInstanceHost($"AIBar.Tests.{Guid.NewGuid():N}");
        var tray = new FakeTray(); var popover = new FakePopover(); var resource = new ProbeResource();
        var cancellationObserved = false; var exited = 0;
        var host = new TrayHostRuntime(instance, tray, popover, new FakeRecreationEvents(), token =>
        {
            cancellationObserved = token.IsCancellationRequested; resource.Events.Add("cancel"); return Task.CompletedTask;
        }, resource, () => exited++);

        Assert.False(tray.RefreshAvailable); tray.Refresh();
        tray.Exit(); tray.Exit();
        instance.Dispose();
        await host.ExitAsync();

        Assert.True(cancellationObserved);
        Assert.Equal(new[] { "cancel", "dispose" }, resource.Events);
        Assert.Equal(1, resource.Disposals); Assert.Equal(1, tray.Disposals); Assert.Equal(1, exited);
        tray.Click(); Assert.Equal(0, popover.Shows);
    }

    [Fact]
    public async Task Exit_failure_is_reported_and_retry_only_repeats_process_exit()
    {
        using var instance = new SingleInstanceHost($"AIBar.Tests.{Guid.NewGuid():N}");
        var tray = new FakeTray(); var resource = new ProbeResource(); var attempts = 0;
        await using var host = new TrayHostRuntime(instance, tray, new FakePopover(), new FakeRecreationEvents(), _ => Task.CompletedTask, resource,
            () => { if (++attempts == 1) throw new InvalidOperationException("exit failed"); });

        instance.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(host.ExitAsync);
        await host.ExitAsync();

        Assert.Equal(2, attempts); Assert.Equal(1, resource.Disposals); Assert.Equal(1, tray.Disposals);
    }

    [Fact]
    public void Native_settings_events_read_back_startup_and_execute_clear_without_live_windows_state()
    {
        using var instance = new SingleInstanceHost($"AIBar.Tests.{Guid.NewGuid():N}");
        var tray = new FakeTray(); var startup = new FakeStartupRegistration(); var local = new FakeLocalUsageControl(new(true, false));
        var clear = new FakeClearCommand { OnClear = () => local.Persisted = LocalUsagePolicy.Disabled };
        var settings = new NativeSettingsCommands(startup, clear, new PrivateIntegrationPolicy(),
            loadLocalUsage: local.LoadAsync, saveLocalUsage: local.SaveAsync, applyLocalUsage: local.ApplyAsync);
        var host = new TrayHostRuntime(instance, tray, new FakePopover(), new FakeRecreationEvents(), _ => Task.CompletedTask, new ProbeResource(), () => { }, settings: settings);

        host.Start(); Assert.True(tray.LocalUsageAvailable); Assert.Equal(new(true, false), tray.LocalUsagePolicy); tray.StartupToggle();
        Assert.True(host.StartupEnabled); Assert.True(tray.StartupEnabled); Assert.True(startup.Enabled);
        tray.StartupToggle();
        Assert.False(host.StartupEnabled); Assert.False(tray.StartupEnabled); Assert.False(startup.Enabled);
        tray.TogglePiLocalUsage();
        Assert.Equal(new(true, true), tray.LocalUsagePolicy); Assert.Equal(1, local.PiAccesses);
        tray.ClearAiBarData();
        Assert.Equal(1, clear.Calls); Assert.Equal(LocalUsagePolicy.Disabled, tray.LocalUsagePolicy);
        instance.Dispose(); _ = host.ExitAsync();
    }

    [Fact]
    public async Task Failed_local_usage_apply_keeps_tray_truthful_and_disposal_detaches_toggle_handlers()
    {
        using var instance = new SingleInstanceHost($"AIBar.Tests.{Guid.NewGuid():N}");
        var tray = new FakeTray(); var local = new FakeLocalUsageControl(LocalUsagePolicy.Disabled) { ApplyFailure = new IOException("synthetic") };
        var settings = new NativeSettingsCommands(new FakeStartupRegistration(), new FakeClearCommand(), new(),
            loadLocalUsage: local.LoadAsync, saveLocalUsage: local.SaveAsync, applyLocalUsage: local.ApplyAsync);
        var host = new TrayHostRuntime(instance, tray, new FakePopover(), new FakeRecreationEvents(), _ => Task.CompletedTask,
            new ProbeResource(), () => { }, settings: settings);
        host.Start();

        tray.ToggleOpenCodeLocalUsage();
        Assert.Equal(LocalUsagePolicy.Disabled, tray.LocalUsagePolicy);
        Assert.Equal(new(true, false), local.Persisted);
        instance.Dispose(); await host.ExitAsync();
        var saves = local.Saves;
        tray.TogglePiLocalUsage();
        Assert.Equal(saves, local.Saves);
    }

    [Fact]
    public void Clear_failure_is_published_through_the_host_unavailable_seam()
    {
        using var instance = new SingleInstanceHost($"AIBar.Tests.{Guid.NewGuid():N}");
        var tray = new FakeTray(); var clear = new FakeClearCommand { Failure = new IOException("locked") }; var failures = 0;
        var settings = new NativeSettingsCommands(new FakeStartupRegistration(), clear, new PrivateIntegrationPolicy());
        var host = new TrayHostRuntime(instance, tray, new FakePopover(), new FakeRecreationEvents(), _ => Task.CompletedTask, new ProbeResource(), () => { }, settings: settings, reportSettingsFailure: () => failures++);

        host.Start(); tray.ClearAiBarData(); PumpUntil(() => failures == 1);

        Assert.Equal(1, clear.Calls);
        instance.Dispose(); _ = host.ExitAsync();
    }

    [Fact]
    public async Task Clear_rolls_back_an_earlier_prepared_target_when_a_later_owned_file_is_locked()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-rollback-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        var settings = Path.Combine(root, "settings.json"); var database = Path.Combine(root, "aibar.db");
        await File.WriteAllTextAsync(settings, "settings"); await File.WriteAllTextAsync(database, "database"); var recreated = 0;
        try
        {
            await using var locked = new FileStream(database, FileMode.Open, FileAccess.Read, FileShare.None);
            var clear = new ClearAiBarDataService(root, [], _ => { recreated++; return ValueTask.CompletedTask; }, ["settings.json", "aibar.db"]);
            await Assert.ThrowsAsync<IOException>(() => clear.ClearAsync(default).AsTask());
            Assert.Equal("settings", await File.ReadAllTextAsync(settings)); Assert.True(File.Exists(database)); Assert.Equal(0, recreated);
            Assert.Equal(2, Directory.EnumerateFileSystemEntries(root).Count());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Production_clear_callback_publishes_empty_unavailable_quota_state()
    {
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var store = new FakeQuotaStore(new(new(42, now.AddHours(1)), new(20, now.AddDays(1)), now));
        await using var coordinator = new QuotaRefreshCoordinator(store, new NeverQuotaProvider(), new FixedClock(now), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.FromMinutes(5));
        await using var presentation = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(new FixedClock(now)));
        await coordinator.InitializeAsync(default); Assert.Equal(42, presentation.Primary.PercentageUsed);

        await App.CreateEmptyStateFactory(coordinator)(default);

        Assert.Null(coordinator.State.Snapshot); Assert.Null(presentation.Primary.PercentageUsed);
        Assert.Equal("Unavailable", presentation.FreshnessLabel); Assert.True(store.Cleared);
    }

    [Fact]
    public async Task Fresh_production_composition_identifies_disabled_private_integration_and_exposes_enable_only()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-onboarding-{Guid.NewGuid():N}");
        var composition = App.CreateComposition(new(root, Path.Combine(root, "codex"), UserProfile: Path.Combine(root, "home")));
        var presentation = ((BetaAnalyticsPresentation)composition.Presentation).QuotaPresentation;
        using var instance = new SingleInstanceHost($"AIBar.Tests.{Guid.NewGuid():N}");
        var tray = new FakeTray();
        var host = new TrayHostRuntime(instance, tray, new FakePopover(), new FakeRecreationEvents(), _ => Task.CompletedTask,
            composition.Resource, () => { }, composition.RefreshCommand, composition.Settings, presentation: presentation, consentPrompt: new FakeConsentPrompt(false));
        instance.Dispose();
        try
        {
            await composition.Initialize();

            Assert.Equal("Private quota integration disabled", presentation.FreshnessLabel);
            Assert.True(presentation.State.IsPrivateIntegrationDisabled);
            Assert.True(tray.EnablePrivateVisible); Assert.False(tray.DisablePrivateVisible);
        }
        finally
        {
            await host.ExitAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Declining_private_integration_disclosure_changes_nothing()
    {
        using var instance = new SingleInstanceHost($"AIBar.Tests.{Guid.NewGuid():N}");
        var policy = new PrivateIntegrationPolicy(); var grants = 0; var reevaluations = 0;
        var settings = new NativeSettingsCommands(new FakeStartupRegistration(), new FakeClearCommand(), policy,
            grantPrivate: _ => { grants++; policy.Enable(); return ValueTask.CompletedTask; });
        var tray = new FakeTray(); var prompt = new FakeConsentPrompt(false);
        await using var host = new TrayHostRuntime(instance, tray, new FakePopover(), new FakeRecreationEvents(), _ => Task.CompletedTask,
            new ProbeResource(), () => { }, settings: settings, reevaluate: (_, _) => { reevaluations++; return ValueTask.CompletedTask; }, consentPrompt: prompt);

        tray.EnablePrivate();

        Assert.Equal(1, prompt.Calls); Assert.Equal(0, grants); Assert.Equal(0, reevaluations);
        Assert.False(policy.IsEnabled); Assert.True(tray.EnablePrivateVisible); Assert.False(tray.DisablePrivateVisible);
        instance.Dispose();
    }

    [Fact]
    public async Task Accept_reaches_real_grant_once_updates_state_and_disable_restores_the_menu()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-grant-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        var settingsFile = Path.Combine(root, "settings.json"); var consent = new ConsentSettings(settingsFile); var policy = new PrivateIntegrationPolicy();
        var files = new MissingCredentialFiles(); var credentials = new ConsentCredentialSource(policy, new CodexCredentialReader(files), Path.Combine(root, "codex"));
        var provider = new GatedCredentialProvider(credentials);
        var coordinator = new QuotaRefreshCoordinator(new EmptyQuotaStore(), provider, new FixedClock(DateTimeOffset.UtcNow), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
        var beta = new BetaRuntime(consent, policy, coordinator, new FixedClock(DateTimeOffset.UtcNow), credentials);
        await using var presentation = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(new FixedClock(DateTimeOffset.UtcNow)), () => policy.IsEnabled);
        using var instance = new SingleInstanceHost($"AIBar.Tests.{Guid.NewGuid():N}");
        var tray = new FakeTray(); var prompt = new FakeConsentPrompt(true);
        var settings = new NativeSettingsCommands(new FakeStartupRegistration(), new FakeClearCommand(), policy, beta.RevokeConsentAsync, beta.GrantConsentAsync);
        var host = new TrayHostRuntime(instance, tray, new FakePopover(), new FakeRecreationEvents(), _ => Task.CompletedTask, beta, () => { }, settings: settings, presentation: presentation, consentPrompt: prompt);
        instance.Dispose();
        try
        {
            await beta.InitializeAsync(default); presentation.RefreshAvailabilityChanged();
            tray.EnablePrivate(); tray.EnablePrivate();
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(1, prompt.Calls); Assert.Equal(1, provider.Calls); Assert.Equal(1, files.Calls);
            provider.Release.TrySetResult(); PumpUntil(() => tray.DisablePrivateVisible);
            using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(settingsFile));
            Assert.Equal(2, document.RootElement.EnumerateObject().Count()); Assert.True(document.RootElement.GetProperty("privateCodexConsent").GetBoolean());
            Assert.Equal("quota_credential_missing", beta.State.Failure!.SafeCode);

            tray.DisablePrivate(); PumpUntil(() => tray.EnablePrivateVisible);
            Assert.False(await consent.LoadAsync(default)); Assert.True(presentation.State.IsPrivateIntegrationDisabled);
            Assert.False(presentation.State.IsLoading); Assert.False(presentation.State.IsMissingCredential);
        }
        finally
        {
            provider.Release.TrySetResult(); await host.ExitAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) }; var timeout = DateTime.UtcNow.AddSeconds(2);
        timer.Tick += (_, _) => frame.Continue = !condition() && DateTime.UtcNow < timeout;
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        Assert.True(condition());
    }

    private sealed class FakeTray : ITrayRuntime
    {
        public event Action? Toggled; public event Action? ExitRequested; public event Action? RefreshRequested; public event Action? StartupToggleRequested; public event Action? ClearAiBarDataRequested; public event Action? PrivateIntegrationEnableRequested; public event Action? PrivateIntegrationDisableRequested; public event Action? OpenCodeLocalUsageToggleRequested; public event Action? PiLocalUsageToggleRequested; public event Action? Recreated;
        public int Shows { get; private set; } public int Disposals { get; private set; } public bool RefreshAvailable { get; private set; }
        public void SetRefreshAvailable(bool available) => RefreshAvailable = available;
        public BetaPresentationState? State { get; private set; }
        public void SetPresentation(BetaPresentationState state) => State = state;
        public bool SettingsAvailable { get; private set; }
        public void SetSettingsAvailable(bool available) => SettingsAvailable = available;
        public bool PrivateIntegrationEnabled { get; private set; }
        public void SetPrivateIntegrationEnabled(bool enabled) => PrivateIntegrationEnabled = enabled;
        public bool EnablePrivateVisible => SettingsAvailable && !PrivateIntegrationEnabled;
        public bool DisablePrivateVisible => SettingsAvailable && PrivateIntegrationEnabled;
        public bool StartupEnabled { get; private set; }
        public void SetStartupEnabled(bool enabled) => StartupEnabled = enabled;
        public bool LocalUsageAvailable { get; private set; }
        public LocalUsagePolicy LocalUsagePolicy { get; private set; } = LocalUsagePolicy.Disabled;
        public void SetLocalUsageAvailable(bool available) => LocalUsageAvailable = available;
        public void SetLocalUsagePolicy(LocalUsagePolicy policy) => LocalUsagePolicy = policy;
        public void Show() => Shows++; public void Hide() { } public void Click() => Toggled?.Invoke(); public void Exit() => ExitRequested?.Invoke(); public void Refresh() { if (RefreshAvailable) RefreshRequested?.Invoke(); } public void StartupToggle() => StartupToggleRequested?.Invoke(); public void ClearAiBarData() => ClearAiBarDataRequested?.Invoke(); public void EnablePrivate() => PrivateIntegrationEnableRequested?.Invoke(); public void DisablePrivate() => PrivateIntegrationDisableRequested?.Invoke(); public void ToggleOpenCodeLocalUsage() => OpenCodeLocalUsageToggleRequested?.Invoke(); public void TogglePiLocalUsage() => PiLocalUsageToggleRequested?.Invoke(); public void Recreate() => Recreated?.Invoke();
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class FakePopover : IPopoverRuntime
    {
        public event Action? Deactivated; public bool IsVisible { get; private set; } public bool IsOwnedDialogActive { get; set; }
        public int Shows { get; private set; } public int Hides { get; private set; }
        public void Show() { IsVisible = true; Shows++; } public void Hide() { IsVisible = false; Hides++; } public void Activate() { }
        public void Deactivate() => Deactivated?.Invoke();
    }

    private sealed class FakeRecreationEvents : ITaskbarRecreationEvents
    {
        public event Action? Recreated;
        public void Recreate() => Recreated?.Invoke();
    }
    private sealed class ProbeResource : IAsyncDisposable
    {
        public List<string> Events { get; } = []; public int Disposals { get; private set; }
        public ValueTask DisposeAsync() { Events.Add("dispose"); Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class FakeStartupRegistration : IStartupRegistration
    {
        public bool Enabled { get; private set; }
        public ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Enabled);
        public ValueTask<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken) => ValueTask.FromResult(Enabled = enabled);
    }
    private sealed class FakeClearCommand : IAiBarDataClearCommand
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }
        public Action? OnClear { get; init; }
        public ValueTask ClearAsync(CancellationToken cancellationToken) { Calls++; if (Failure is not null) return ValueTask.FromException(Failure); OnClear?.Invoke(); return ValueTask.CompletedTask; }
    }
    private sealed class FakeLocalUsageControl(LocalUsagePolicy policy)
    {
        public LocalUsagePolicy Persisted { get; set; } = policy;
        public int Saves { get; private set; } public int OpenCodeAccesses { get; private set; } public int PiAccesses { get; private set; }
        public Exception? ApplyFailure { get; init; }
        public ValueTask<LocalUsagePolicy> LoadAsync(CancellationToken token) => ValueTask.FromResult(Persisted);
        public ValueTask SaveAsync(LocalUsagePolicy value, CancellationToken token) { Saves++; Persisted = value; return ValueTask.CompletedTask; }
        public ValueTask ApplyAsync(LocalUsagePolicy value, CancellationToken token)
        {
            if (ApplyFailure is not null) return ValueTask.FromException(ApplyFailure);
            if (value.OpenCodeEnabled) OpenCodeAccesses++; if (value.PiEnabled) PiAccesses++;
            return ValueTask.CompletedTask;
        }
    }
    private sealed class FakeQuotaStore(QuotaSnapshot snapshot) : IQuotaSnapshotStore
    {
        private QuotaSnapshot? _snapshot = snapshot; public bool Cleared { get; private set; }
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_snapshot);
        public ValueTask SaveAsync(QuotaSnapshot value, CancellationToken cancellationToken) { _snapshot = value; return ValueTask.CompletedTask; }
        public ValueTask ClearAsync(CancellationToken cancellationToken) { _snapshot = null; Cleared = true; return ValueTask.CompletedTask; }
    }
    private sealed class NeverQuotaProvider : IQuotaProvider
    {
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new QuotaProviderResult(null, new(QuotaErrorKind.Unavailable, "unused")));
    }
    private sealed class FakeConsentPrompt(bool accepted) : IPrivateIntegrationConsentPrompt
    {
        public int Calls { get; private set; }
        public bool Confirm() { Calls++; return accepted; }
    }
    private sealed class MissingCredentialFiles : ICredentialFileReader
    {
        public int Calls { get; private set; }
        public ValueTask<Stream> OpenReadAsync(string path, FileShare share, CancellationToken cancellationToken) { Calls++; throw new FileNotFoundException(); }
    }
    private sealed class GatedCredentialProvider(ConsentCredentialSource credentials) : IQuotaProvider
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
        {
            Calls++; var availability = await credentials.GetAvailabilityAsync(cancellationToken); Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
            return new(null, new(QuotaErrorKind.Unavailable, availability == CredentialAvailability.Unusable ? "quota_credential_unusable" : "quota_credential_missing"));
        }
    }
    private sealed class EmptyQuotaStore : IQuotaSnapshotStore
    {
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult<QuotaSnapshot?>(null);
        public ValueTask SaveAsync(QuotaSnapshot value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
