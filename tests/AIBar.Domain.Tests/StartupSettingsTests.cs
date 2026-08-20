using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class StartupSettingsTests
{
    [Fact]
    public async Task Startup_prefers_available_packaged_task_and_reads_back_effective_state()
    {
        var task = new FakeStartupTask { IsAvailable = true };
        var run = new FakeRunStore();
        var registration = new PerUserStartupRegistration(task, run, "AIBar", @"C:\Program Files\AIBar\AIBar.exe");

        Assert.True(await registration.SetEnabledAsync(true, default));
        Assert.True(await registration.IsEnabledAsync(default));
        Assert.True(task.Enabled);
        Assert.Empty(run.Values);
    }

    [Fact]
    public async Task Startup_uses_only_current_user_run_value_with_quoted_executable_and_startup_argument()
    {
        var run = new FakeRunStore();
        var registration = new PerUserStartupRegistration(new FakeStartupTask(), run, "AIBar", @"C:\Program Files\AIBar\AIBar.exe");

        var enabled = await registration.SetEnabledAsync(true, default);

        Assert.Equal("\"C:\\Program Files\\AIBar\\AIBar.exe\" --startup", Assert.Single(run.Values).Value);
        Assert.True(enabled);
        Assert.Equal(WindowsCurrentUserRunStore.KeyPath, run.KeyPaths.Single());
        Assert.False(await registration.SetEnabledAsync(false, default));
        Assert.Empty(run.Values);
    }

    [Fact]
    public async Task Startup_reconciles_fallback_when_packaged_task_becomes_available_and_disables_both_backends()
    {
        var task = new FakeStartupTask(); var run = new FakeRunStore();
        var registration = new PerUserStartupRegistration(task, run, "AIBar", @"C:\Program Files\AIBar\AIBar.exe");
        Assert.True(await registration.SetEnabledAsync(true, default));

        task.IsAvailable = true;
        Assert.True(await registration.SetEnabledAsync(true, default));
        Assert.True(task.Enabled); Assert.Empty(run.Values);
        task.Enabled = false; run.Values["AIBar"] = "\"C:\\Program Files\\AIBar\\AIBar.exe\" --startup";
        Assert.True(await registration.IsEnabledAsync(default));

        Assert.False(await registration.SetEnabledAsync(false, default));
        Assert.False(task.Enabled); Assert.Empty(run.Values);
    }

    [Fact]
    public async Task Startup_fails_closed_for_malformed_or_denied_run_values_and_repeated_toggles_read_back_state()
    {
        var run = new FakeRunStore { Values = { ["AIBar"] = "AIBar.exe --startup" } };
        var registration = new PerUserStartupRegistration(new FakeStartupTask(), run, "AIBar", "C:\\AIBar.exe");

        Assert.False(await registration.IsEnabledAsync(default));
        run.DenyWrites = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registration.SetEnabledAsync(true, default).AsTask());
        run.DenyWrites = false;
        Assert.True(await registration.SetEnabledAsync(true, default));
        Assert.True(await registration.SetEnabledAsync(true, default));
        Assert.False(await registration.SetEnabledAsync(false, default));
    }

    [Fact]
    public async Task Settings_commands_toggle_startup_clear_owned_data_and_keep_private_telemetry_and_crash_reporting_disabled()
    {
        var startup = new FakeRegistration();
        var clear = new FakeClearService();
        var commands = new NativeSettingsCommands(startup, clear, new PrivateIntegrationPolicy());

        Assert.False(commands.Privacy.TelemetryEnabled);
        Assert.False(commands.Privacy.RemoteCrashReportingEnabled);
        Assert.False(commands.PrivateIntegrationEnabled);
        Assert.True(await commands.ToggleStartupAsync(default));
        await commands.ClearAiBarDataAsync(default);

        Assert.True(startup.Enabled);
        Assert.Equal(1, clear.Calls);
    }

    [Fact]
    public async Task Local_usage_commands_serialize_complete_independent_transactions_without_lost_updates()
    {
        var persisted = LocalUsagePolicy.Disabled;
        var events = new List<(string Stage, LocalUsagePolicy Policy)>();
        var firstSaveEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCalls = 0;
        ValueTask<LocalUsagePolicy> Load(CancellationToken _) => ValueTask.FromResult(persisted);
        async ValueTask Save(LocalUsagePolicy policy, CancellationToken token)
        {
            if (Interlocked.Increment(ref saveCalls) == 1)
            {
                firstSaveEntered.SetResult();
                await releaseFirstSave.Task.WaitAsync(token);
            }
            persisted = policy; events.Add(("save", policy));
        }
        ValueTask Apply(LocalUsagePolicy policy, CancellationToken _)
        {
            Assert.Equal(persisted, policy);
            events.Add(("apply", policy));
            return ValueTask.CompletedTask;
        }
        var commands = new NativeSettingsCommands(new FakeRegistration(), new FakeClearService(), new(),
            loadLocalUsage: Load, saveLocalUsage: Save, applyLocalUsage: Apply);

        var openCode = commands.ToggleOpenCodeLocalUsageAsync(default).AsTask();
        await firstSaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var pi = commands.TogglePiLocalUsageAsync(default).AsTask();
        Assert.False(pi.IsCompleted);
        releaseFirstSave.SetResult();
        await Task.WhenAll(openCode, pi);

        Assert.Equal(new(true, true), persisted);
        Assert.Equal(new[]
        {
            ("save", new LocalUsagePolicy(true, false)), ("apply", new LocalUsagePolicy(true, false)),
            ("save", new LocalUsagePolicy(true, true)), ("apply", new LocalUsagePolicy(true, true)),
        }, events);
        Assert.Equal(persisted, commands.LocalUsagePolicy);
    }

    [Fact]
    public async Task Local_usage_failure_and_cancellation_do_not_publish_unapplied_state()
    {
        var persisted = LocalUsagePolicy.Disabled;
        var commands = new NativeSettingsCommands(new FakeRegistration(), new FakeClearService(), new(),
            loadLocalUsage: _ => ValueTask.FromResult(persisted),
            saveLocalUsage: (policy, _) => { persisted = policy; return ValueTask.CompletedTask; },
            applyLocalUsage: (_, _) => ValueTask.FromException(new IOException("synthetic apply failure")));

        await Assert.ThrowsAsync<IOException>(() => commands.ToggleOpenCodeLocalUsageAsync(default).AsTask());

        Assert.Equal(new(true, false), persisted);
        Assert.Equal(LocalUsagePolicy.Disabled, commands.LocalUsagePolicy);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => commands.TogglePiLocalUsageAsync(cancellation.Token).AsTask());
        Assert.Equal(new(true, false), persisted);
        Assert.Equal(LocalUsagePolicy.Disabled, commands.LocalUsagePolicy);
    }

    [Fact]
    public async Task OpenCode_root_choose_and_reset_share_save_before_apply_gate_and_invalid_choice_preserves_state()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-command-root-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        try
        {
            var persisted = new LocalUsagePolicy(true, false); var events = new List<(string, string?)>();
            var applyEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseApply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ValueTask Save(LocalUsagePolicy policy, CancellationToken _) { persisted = policy; events.Add(("save", policy.OpenCodeDataRoot)); return ValueTask.CompletedTask; }
            async ValueTask Apply(LocalUsagePolicy policy, CancellationToken token)
            { events.Add(("apply", policy.OpenCodeDataRoot)); if (policy.OpenCodeDataRoot is not null) { applyEntered.SetResult(); await releaseApply.Task.WaitAsync(token); } }
            var commands = new NativeSettingsCommands(new FakeRegistration(), new FakeClearService(), new(),
                loadLocalUsage: _ => ValueTask.FromResult(persisted), saveLocalUsage: Save, applyLocalUsage: Apply);

            var choose = commands.ChooseOpenCodeDataRootAsync(root, default).AsTask(); await applyEntered.Task;
            var reset = commands.ResetOpenCodeDataRootAsync(default).AsTask(); Assert.False(reset.IsCompleted);
            releaseApply.SetResult(); await Task.WhenAll(choose, reset);

            Assert.Equal(new[] { ("save", root), ("apply", root), ("save", (string?)null), ("apply", (string?)null) }, events);
            Assert.Equal(new(true, false), persisted); Assert.Equal(persisted, commands.LocalUsagePolicy);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ChooseOpenCodeDataRootAsync(Path.Combine(root, "missing"), default).AsTask());
            Assert.Equal(LocalUsageSourceRootResolver.InvalidOpenCodeDataRootMessage, error.Message);
            Assert.Equal(4, events.Count); Assert.Equal(new(true, false), persisted);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Private_kill_switch_remains_disabled_and_preserves_local_analytics()
    {
        var policy = new PrivateIntegrationPolicy(true);
        var commands = new NativeSettingsCommands(new FakeRegistration(), new FakeClearService(), policy);

        await commands.DisablePrivateIntegrationAsync(default);

        Assert.False(commands.PrivateIntegrationEnabled);
        Assert.True(commands.LocalAnalyticsEnabled);
    }

    [Fact]
    public async Task Clear_removes_live_quota_database_without_touching_a_separate_codex_root()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-startup-{Guid.NewGuid():N}");
        var codex = Path.Combine(Path.GetTempPath(), $"codex-startup-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root); Directory.CreateDirectory(codex);
            await File.WriteAllTextAsync(Path.Combine(root, "quota.db"), "owned");
            await File.WriteAllTextAsync(Path.Combine(codex, "session.jsonl"), "source");
            await new ClearAiBarDataService(root, [], _ => ValueTask.CompletedTask).ClearAsync(default);
            Assert.False(File.Exists(Path.Combine(root, "quota.db")));
            Assert.Equal("source", await File.ReadAllTextAsync(Path.Combine(codex, "session.jsonl")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); if (Directory.Exists(codex)) Directory.Delete(codex, true); }
    }

    [Theory]
    [InlineData("Completed", false)]
    [InlineData("Error", true)]
    [InlineData("Canceled", true)]
    [InlineData("Unexpected", true)]
    public async Task Packaged_startup_reflection_operation_states_fail_closed_without_real_windows_calls(string status, bool throws)
    {
        var operation = new SyntheticOperation(status);
        if (throws) await Assert.ThrowsAsync<InvalidOperationException>(() => WindowsPackagedStartupTaskRegistration.AwaitResultAsync(operation, default));
        else Assert.Equal("result", await WindowsPackagedStartupTaskRegistration.AwaitResultAsync(operation, default));
    }

    private sealed class FakeStartupTask : IStartupTaskRegistration
    {
        public bool IsAvailable { get; set; }
        public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) => ValueTask.FromResult(IsAvailable);
        public bool Enabled { get; set; }
        public ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Enabled);
        public ValueTask SetEnabledAsync(bool enabled, CancellationToken cancellationToken) { Enabled = enabled; return ValueTask.CompletedTask; }
    }

    private sealed class SyntheticOperation(string status)
    {
        public string Status { get; } = status;
        public string GetResults() => "result";
    }

    private sealed class FakeRunStore : ICurrentUserRunStore
    {
        public Dictionary<string, string> Values { get; } = [];
        public List<string> KeyPaths { get; } = [];
        public bool DenyWrites { get; set; }
        public ValueTask<string?> GetAsync(string keyPath, string name, CancellationToken cancellationToken) => ValueTask.FromResult(Values.GetValueOrDefault(name));
        public ValueTask SetAsync(string keyPath, string name, string value, CancellationToken cancellationToken)
        {
            if (DenyWrites) throw new UnauthorizedAccessException();
            KeyPaths.Add(keyPath); Values[name] = value; return ValueTask.CompletedTask;
        }
        public ValueTask DeleteAsync(string keyPath, string name, CancellationToken cancellationToken)
        {
            if (DenyWrites) throw new UnauthorizedAccessException();
            KeyPaths.Add(keyPath); Values.Remove(name); return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeRegistration : IStartupRegistration
    {
        public bool Enabled { get; private set; }
        public ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Enabled);
        public ValueTask<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken) => ValueTask.FromResult(Enabled = enabled);
    }

    private sealed class FakeClearService : IAiBarDataClearCommand
    {
        public int Calls { get; private set; }
        public ValueTask ClearAsync(CancellationToken cancellationToken) { Calls++; return ValueTask.CompletedTask; }
    }
}
