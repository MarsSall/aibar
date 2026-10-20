using System.Reflection;
using System.Text;
using System.Text.Json;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;
using Forms = System.Windows.Forms;

namespace AIBar.Domain.Tests;

// T2, S1–S2. All inputs are synthetic; no real Claude credentials are opened.
public sealed class ClaudeRemoteUsageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-claude-remote-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Window = "{\"percentageUsed\":32.5,\"resetAt\":\"2030-01-01T17:00:00Z\"}";
    private const string Valid = "{\"schemaVersion\":1,\"generatedAt\":\"2030-01-01T12:00:00Z\",\"state\":\"current\",\"fiveHour\":" + Window + ",\"weekly\":null}";
    private string SettingsPath => Path.Combine(_root, "settings.json");
    private ConsentSettings Settings => new(SettingsPath);
    private string RemotePath => Path.Combine(_root, "claude-remote-usage.json");

    // a: defaults, independent decisions, and exact wire spelling.
    [Fact]
    public async Task Missing_source_defaults_to_automatic_without_writing_or_granting_consent()
    {
        Assert.Equal(ClaudeQuotaSource.Automatic, await Settings.LoadClaudeSourceAsync(default));
        Assert.False(File.Exists(SettingsPath));
        Assert.False(await Settings.LoadAsync(default));
        Assert.False(await Settings.LoadClaudeAsync(default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_Codex_and_Claude_round_trips_do_not_add_a_source(bool withClaude)
    {
        await Settings.SaveAsync(true, default);
        if (withClaude) await Settings.SaveClaudeAsync(true, default);
        var before = await File.ReadAllTextAsync(SettingsPath);
        Assert.Equal(ClaudeQuotaSource.Automatic, await Settings.LoadClaudeSourceAsync(default));
        Assert.Equal(before, await File.ReadAllTextAsync(SettingsPath));
        using var json = JsonDocument.Parse(before);
        Assert.Equal(withClaude ? 3 : 2, json.RootElement.EnumerateObject().Count());
        Assert.True(await Settings.LoadAsync(default));
        Assert.Equal(withClaude, await Settings.LoadClaudeAsync(default));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Source_changes_preserve_both_consents_and_survive_each_consent_save(bool codex, bool claude)
    {
        await Settings.SaveAsync(codex, default);
        await Settings.SaveClaudeAsync(claude, default);
        foreach (var source in new[] { ClaudeQuotaSource.OtherMachine, ClaudeQuotaSource.ThisPc })
        {
            await Settings.SaveClaudeSourceAsync(source, default);
            Assert.Equal(source, await Settings.LoadClaudeSourceAsync(default));
            Assert.Equal(codex, await Settings.LoadAsync(default));
            Assert.Equal(claude, await Settings.LoadClaudeAsync(default));
            using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath)))
                Assert.Equal(source == ClaudeQuotaSource.ThisPc ? "this-pc" : "other-machine", json.RootElement.GetProperty("claudeSource").GetString());
            await Settings.SaveAsync(!codex, default);
            await Settings.SaveClaudeAsync(!claude, default);
            Assert.Equal(source, await Settings.LoadClaudeSourceAsync(default));
            await Settings.SaveAsync(codex, default);
            await Settings.SaveClaudeAsync(claude, default);
        }
    }

    [Theory]
    [InlineData("this-pc", ClaudeQuotaSource.ThisPc)]
    [InlineData("other-machine", ClaudeQuotaSource.OtherMachine)]
    public async Task Source_loads_exact_persisted_wire_values(string wire, ClaudeQuotaSource expected)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(SettingsPath, "{\"schema\":1,\"privateCodexConsent\":true,\"privateClaudeConsent\":false,\"claudeSource\":\"" + wire + "\"}");
        Assert.Equal(expected, await Settings.LoadClaudeSourceAsync(default));
        Assert.True(await Settings.LoadAsync(default));
        Assert.False(await Settings.LoadClaudeAsync(default));
    }

    // b: runtime handoff must cancel/drain the old generation and refresh immediately.
    [Fact]
    public async Task Switching_source_cancels_inflight_local_work_and_publishes_only_remote_result_without_restart()
    {
        await Settings.SaveAsync(true, default);
        await Settings.SaveClaudeAsync(true, default);
        var local = new BlockingProvider(); var remote = new Provider(73); var store = new Store();
        await using var runtime = new ClaudeQuotaRuntime(Settings, store, _ => local, new Clock(), remoteProviderFactory: _ => remote);
        await runtime.InitializeAsync(default);
        await local.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task switching;
        try
        {
            switching = runtime.SetSourceAsync(ClaudeQuotaSource.OtherMachine, default).AsTask();
            await Until(() => local.Token.IsCancellationRequested);
        }
        finally { local.Release.TrySetResult(new(new(new(11, Now.AddHours(5)), null, Now), null)); }
        await switching.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(ClaudeQuotaSource.OtherMachine, runtime.Source);
        Assert.Equal(1, remote.Calls);
        Assert.Equal(73m, runtime.State.Snapshot!.Primary!.PercentageUsed);
        Assert.DoesNotContain(store.Saved, snapshot => snapshot.Primary?.PercentageUsed == 11);
        Assert.True(runtime.IsConsentEnabled);
        Assert.True(await Settings.LoadAsync(default)); Assert.True(await Settings.LoadClaudeAsync(default));
        Assert.Equal(ClaudeQuotaSource.OtherMachine, await Settings.LoadClaudeSourceAsync(default));
    }

    [Fact]
    public async Task Selecting_source_while_disabled_is_lazy_and_does_not_grant_either_consent()
    {
        var factories = 0;
        await using var runtime = new ClaudeQuotaRuntime(Settings, new Store(), _ => { factories++; return new Provider(); }, new Clock(),
            remoteProviderFactory: _ => { factories++; return new Provider(); });
        await runtime.InitializeAsync(default);
        await runtime.SetSourceAsync(ClaudeQuotaSource.OtherMachine, default);
        Assert.Equal(0, factories);
        Assert.Equal(ClaudeQuotaSource.OtherMachine, runtime.Source);
        Assert.False(runtime.IsConsentEnabled);
        Assert.False(await Settings.LoadAsync(default)); Assert.False(await Settings.LoadClaudeAsync(default));
    }

    [Fact]
    public async Task Tray_source_controller_reflects_persisted_selection_and_routes_choice_without_prompt()
    {
        await Settings.SaveClaudeSourceAsync(ClaudeQuotaSource.OtherMachine, default);
        await using var runtime = new ClaudeQuotaRuntime(Settings, new Store(), _ => new Provider(), new Clock());
        await runtime.InitializeAsync(default);
        var tray = new SourceTray(); var prompt = new Prompt();
        using var controller = new ClaudeQuotaTrayController(tray, runtime, prompt);
        Assert.Equal(ClaudeQuotaSource.OtherMachine, tray.Selected);
        await controller.SetSourceAsync(ClaudeQuotaSource.ThisPc, default);
        Assert.Equal(ClaudeQuotaSource.ThisPc, tray.Selected);
        tray.Choose(ClaudeQuotaSource.OtherMachine);
        await Until(() => tray.Selected == ClaudeQuotaSource.OtherMachine);
        Assert.Equal(ClaudeQuotaSource.OtherMachine, await Settings.LoadClaudeSourceAsync(default));
        Assert.Equal(0, prompt.Calls);
    }

    [Fact]
    public async Task Windows_tray_source_choices_are_exclusive_and_leave_Codex_menu_unchanged()
    {
        await using var tray = new WindowsTrayRuntime();
        tray.SetSettingsAvailable(true); tray.SetPrivateIntegrationEnabled(true); tray.SetClaudeQuotaEnabled(false);
        var menu = (Forms.ContextMenuStrip)typeof(WindowsTrayRuntime).GetField("_menu", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tray)!;
        var codexBefore = menu.Items.Cast<Forms.ToolStripItem>().Where(item => item.Text!.Contains("Private Quota Integration")).ToArray();
        ClaudeQuotaSource? requested = null;
        ((IClaudeQuotaSourceTray)tray).ClaudeSourceRequested += source => requested = source;
        foreach (var source in new[] { ClaudeQuotaSource.Automatic, ClaudeQuotaSource.ThisPc, ClaudeQuotaSource.OtherMachine })
        {
            tray.SetClaudeSource(source);
            var parent = Assert.Single(menu.Items.OfType<Forms.ToolStripMenuItem>(), item => item.Text == "Claude source");
            var children = parent.DropDownItems.OfType<Forms.ToolStripMenuItem>().ToArray();
            Assert.Equal(new[] { "Automatic", "This PC", "Other machine" }, children.Select(item => item.Text));
            Assert.Equal(source == ClaudeQuotaSource.Automatic, children[0].Checked);
            Assert.Equal(source == ClaudeQuotaSource.ThisPc, children[1].Checked);
            Assert.Equal(source == ClaudeQuotaSource.OtherMachine, children[2].Checked);
            Assert.Equal(codexBefore, menu.Items.Cast<Forms.ToolStripItem>().Where(item => item.Text!.Contains("Private Quota Integration")).ToArray());
            children[2].PerformClick(); Assert.Equal(ClaudeQuotaSource.OtherMachine, requested);
        }
        Assert.DoesNotContain(typeof(ITrayRuntime).GetMembers(), member => member.Name.Contains("Claude"));
    }

    // c/d: the remote provider can open only the injected usage path, never credentials.
    [Fact]
    public void Default_remote_path_is_the_owned_LocalAppData_usage_file() =>
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIBar", "claude-remote-usage.json"), ClaudeRemoteUsageProvider.DefaultPath());

    [Fact]
    public async Task Current_maps_windows_and_generated_time_and_opens_only_remote_file_read_only()
    {
        var files = new Files(Valid);
        var result = await Remote(files).GetQuotaAsync(default);
        Assert.Null(result.Failure);
        Assert.Equal(Now, result.Snapshot!.RetrievedAt);
        Assert.Equal(32.5m, result.Snapshot.Primary!.PercentageUsed);
        Assert.Equal(Now.AddHours(5), result.Snapshot.Primary.ResetAt);
        Assert.Null(result.Snapshot.Weekly);
        Assert.Null(result.ResetCredits); Assert.Null(result.OptionalFailure);
        Assert.Equal(RemotePath, Assert.Single(files.Paths));
        Assert.Equal(FileShare.ReadWrite | FileShare.Delete, files.Share);
        Assert.False(files.Stream!.CanWrite); Assert.True(files.Disposed);
    }

    [Fact]
    public async Task Production_reader_consumes_only_synthetic_usage_file_and_leaves_bytes_unchanged()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(RemotePath, Valid, new UTF8Encoding(false));
        var before = await File.ReadAllBytesAsync(RemotePath);
        // A simultaneous reader that denies writers protects the read-only contract.
        await using var guard = new FileStream(RemotePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var provider = new ClaudeRemoteUsageProvider(new PrivateIntegrationPolicy(true), RemotePath, utcNow: () => Now);
        var result = await provider.GetQuotaAsync(default);
        Assert.Null(result.Failure);
        Assert.Equal(before, await File.ReadAllBytesAsync(RemotePath));
        Assert.Equal(new[] { RemotePath }, Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData("{\"percentageUsed\":0,\"resetAt\":\"2030-01-01T17:00:00Z\"}", "null")]
    [InlineData("null", "{\"percentageUsed\":100,\"resetAt\":\"2030-01-01T17:00:00Z\"}")]
    [InlineData(Window, Window)]
    public async Task Current_accepts_range_boundaries_required_resets_and_either_or_both_windows(string primary, string weekly)
    {
        var result = await Remote(new Files(Document("current", primary, weekly))).GetQuotaAsync(default);
        Assert.Null(result.Failure);
        Assert.Equal(primary != "null", result.Snapshot!.Primary is not null);
        Assert.Equal(weekly != "null", result.Snapshot.Weekly is not null);
    }

    [Theory]
    [InlineData("{\"percentageUsed\":0,\"resetAt\":null}", "null")]
    [InlineData("null", "{\"percentageUsed\":100,\"resetAt\":null}")]
    [InlineData("{\"percentageUsed\":0,\"resetAt\":null}", Window)]
    [InlineData(Window, "{\"percentageUsed\":100,\"resetAt\":null}")]
    public async Task Current_rejects_null_reset_even_when_the_other_window_is_valid(string primary, string weekly) =>
        Failure(await Remote(new Files(Document("current", primary, weekly))).GetQuotaAsync(default),
            QuotaErrorKind.Unavailable, "quota_unavailable");

    [Theory]
    [InlineData(ClaudeQuotaSource.ThisPc, "Open Claude Code on this PC to renew the session")]
    [InlineData(ClaudeQuotaSource.OtherMachine, "Open Claude Code on the other machine to renew the session")]
    public void Authentication_required_guidance_names_the_selected_source(ClaudeQuotaSource source, string expected)
    {
        var state = new QuotaRefreshState(null, FreshnessState.Unavailable, false,
            new(QuotaErrorKind.Authentication, "quota_authentication"), null);
        var panel = new ClaudeQuotaPresentationMapper(new Clock()).Map(state, true, source);
        Assert.Equal(expected, panel.WarningLabel);
        Assert.Equal(expected, panel.ErrorLabel);
    }

    [Theory]
    [InlineData("authentication-required", QuotaErrorKind.Authentication, "quota_authentication")]
    [InlineData("unavailable", QuotaErrorKind.Unavailable, "quota_unavailable")]
    public async Task Noncurrent_states_use_existing_safe_failures(string state, QuotaErrorKind kind, string code)
    {
        var result = await Remote(new Files(Document(state, "null", "null"))).GetQuotaAsync(default);
        Failure(result, kind, code);
        if (kind == QuotaErrorKind.Authentication)
        {
            var panel = new ClaudeQuotaPresentationMapper(new Clock()).Map(new(null, FreshnessState.Unavailable, false, result.Failure, null), true);
            Assert.Contains("renew", panel.WarningLabel!, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static IEnumerable<object[]> InvalidDocuments()
    {
        foreach (var text in new[] { "", "{bad", "[]", "null", "{}", Valid + "{}", Valid + "," }) yield return new object[] { text };
        foreach (var replacement in new[] { "0", "2", "1.0", "\"1\"", "true", "null" })
            yield return new object[] { Valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":" + replacement) };
        yield return new object[] { Valid.Replace("\"schemaVersion\":1,", "") };
        yield return new object[] { Valid.Replace("\"weekly\":null", "\"weekly\":null,\"token\":\"synthetic-secret\"") };
        yield return new object[] { Valid.Replace("\"weekly\":null", "\"weekly\":null,\"weekly\":null") };
        yield return new object[] { Valid.Replace("\"schemaVersion\":1,\"generatedAt\":\"2030-01-01T12:00:00Z\"", "\"generatedAt\":\"2030-01-01T12:00:00Z\",\"schemaVersion\":1") };
        yield return new object[] { Valid.Replace(Window, "{\"resetAt\":null,\"percentageUsed\":32.5}") };
        foreach (var value in new[] { "-0.1", "100.1", "\"32.5\"", "null", "true", "1e999", "NaN" })
            yield return new object[] { Valid.Replace("32.5", value) };
        foreach (var timestamp in new[] { "2030-01-01T12:00:00+00:00", "2030-01-01T12:00:00", "2030-01-01t12:00:00z", "2030-01-01T12:00Z", "2030-01-01T12:00:00.12345678Z", "2030-02-30T12:00:00Z", "invalid" })
        {
            yield return new object[] { Valid.Replace("2030-01-01T12:00:00Z", timestamp) };
            yield return new object[] { Valid.Replace("2030-01-01T17:00:00Z", timestamp) };
        }
        yield return new object[] { Valid.Replace("\"generatedAt\":\"2030-01-01T12:00:00Z\"", "\"generatedAt\":null") };
        yield return new object[] { Valid.Replace("\"resetAt\":\"2030-01-01T17:00:00Z\"", "\"resetAt\":42") };
        foreach (var window in new[] { "{}", "false", "[]", "{\"percentageUsed\":1}", "{\"resetAt\":null}", "{\"percentageUsed\":1,\"percentageUsed\":1,\"resetAt\":null}", "{\"percentageUsed\":1,\"resetAt\":null,\"account\":\"synthetic\"}" })
            yield return new object[] { Valid.Replace(Window, window) };
        yield return new object[] { Valid.Replace(",\"weekly\":null", "") };
        yield return new object[] { Document("current", "null", "null") };
        yield return new object[] { Document("unknown", "null", "null") };
        yield return new object[] { Document("authentication-required", Window, "null") };
        yield return new object[] { Document("unavailable", "null", Window) };
    }

    [Theory]
    [MemberData(nameof(InvalidDocuments))]
    public async Task Rejects_every_contract_shape_type_order_range_or_timestamp_violation(string json) =>
        Failure(await Remote(new Files(json)).GetQuotaAsync(default), QuotaErrorKind.Unavailable, "quota_unavailable");

    [Theory]
    [InlineData("")]
    [InlineData(".1")]
    [InlineData(".1234567")]
    public async Task Accepts_UTC_seconds_and_one_to_seven_fractional_digits(string fraction)
    {
        var timestamp = "2030-01-01T12:00:00" + fraction + "Z";
        var result = await Remote(new Files(Valid.Replace("2030-01-01T12:00:00Z", timestamp))).GetQuotaAsync(default);
        Assert.Null(result.Failure);
        Assert.Equal(DateTimeOffset.Parse(timestamp, System.Globalization.CultureInfo.InvariantCulture), result.Snapshot!.RetrievedAt);
    }

    [Theory]
    [InlineData(4096, true)]
    [InlineData(4097, false)]
    public async Task Size_cap_is_4096_UTF8_bytes_including_trailing_whitespace(int size, bool accepted)
    {
        var bytes = Encoding.UTF8.GetBytes(Valid.PadRight(size));
        var result = await Remote(new Files(bytes)).GetQuotaAsync(default);
        if (accepted) Assert.Null(result.Failure);
        else Failure(result, QuotaErrorKind.Unavailable, "quota_unavailable");
    }

    [Fact]
    public async Task Invalid_UTF8_is_rejected_instead_of_replaced()
    {
        var bytes = Encoding.UTF8.GetBytes(Valid).Concat(new byte[] { 0xff }).ToArray();
        Failure(await Remote(new Files(bytes)).GetQuotaAsync(default), QuotaErrorKind.Unavailable, "quota_unavailable");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reparse_file_or_parent_is_rejected_before_open(bool parent)
    {
        var files = new Files(Valid);
        var blocked = parent ? _root : RemotePath;
        var provider = Remote(files, attributes: path => path == blocked ? FileAttributes.ReparsePoint : FileAttributes.Normal);
        Failure(await provider.GetQuotaAsync(default), QuotaErrorKind.Unavailable, "quota_unavailable");
        Assert.Empty(files.Paths);
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(DirectoryNotFoundException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task Missing_or_unreadable_file_exposes_only_safe_failure(Type exception)
    {
        var error = (Exception)Activator.CreateInstance(exception, "synthetic-private-path-token")!;
        Failure(await Remote(new Files(Valid) { Error = error }).GetQuotaAsync(default), QuotaErrorKind.Unavailable, "quota_unavailable");
    }

    [Fact]
    public async Task Disabled_remote_provider_never_opens_any_file()
    {
        var files = new Files(Valid);
        Failure(await Remote(files, enabled: false).GetQuotaAsync(default), QuotaErrorKind.Unavailable, "quota_disabled");
        Assert.Empty(files.Paths);
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved_before_file_access()
    {
        var files = new Files(Valid); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Remote(files).GetQuotaAsync(cancellation.Token).AsTask());
        Assert.Empty(files.Paths);
    }

    // e: boundary and cached-fallback behavior use the existing coordinator path.
    [Theory]
    [InlineData(-900, true)]
    [InlineData(-901, false)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public async Task Generated_time_enforces_fifteen_minute_age_and_two_minute_future_tolerance(int seconds, bool accepted)
    {
        var json = Valid.Replace("2030-01-01T12:00:00Z", Now.AddSeconds(seconds).ToString("yyyy-MM-ddTHH:mm:ss'Z'"));
        var result = await Remote(new Files(json)).GetQuotaAsync(default);
        if (accepted) { Assert.Null(result.Failure); Assert.Equal(Now.AddSeconds(seconds), result.Snapshot!.RetrievedAt); }
        else if (seconds < 0) Failure(result, QuotaErrorKind.Service, "quota_service");
        else Failure(result, QuotaErrorKind.Unavailable, "quota_unavailable");
    }

    [Fact]
    public async Task Stopped_push_keeps_cached_snapshot_stale_never_current()
    {
        var cached = new QuotaSnapshot(new(21, Now.AddHours(5)), null, Now.AddMinutes(-20));
        var store = new Store { Snapshot = cached };
        var files = new Files(Valid.Replace("2030-01-01T12:00:00Z", "2030-01-01T11:44:59Z"));
        await using var coordinator = new QuotaRefreshCoordinator(store, Remote(files), new Clock(), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.FromMinutes(5));
        await coordinator.InitializeAsync(default);
        await coordinator.ReevaluateAsync(RefreshTrigger.Manual, default);
        Assert.Equal(cached, coordinator.State.Snapshot);
        Assert.Equal(FreshnessState.Stale, coordinator.State.Freshness);
        Assert.Equal(QuotaErrorKind.Service, coordinator.State.Failure!.Kind);
        Assert.Empty(store.Saved);
    }

    // T3, S2: an open panel expires without waiting for the five-minute poll.
    [Fact]
    public async Task Remote_deadline_marks_cached_values_stale_without_polling_or_changing_guidance()
    {
        var time = new AdvancingTimeProvider();
        var clock = new AdvancingClock(time);
        var files = new Files(GeneratedAt(Now.AddMinutes(-14)));
        await using var runtime = await RemoteRuntime(time, clock, files);
        var mapper = new ClaudeQuotaPresentationMapper(clock);
        var panel = mapper.Map(runtime.State, true, runtime.Source);
        Assert.True(panel.IsCurrent);
        var warning = panel.WarningLabel;
        var error = panel.ErrorLabel;
        var changes = 0;
        runtime.StateChanged += () => { changes++; panel = mapper.Map(runtime.State, true, runtime.Source); };
        var reads = files.Paths.Count;

        time.Advance(TimeSpan.FromSeconds(59));
        Assert.True(panel.IsCurrent);
        Assert.Equal(0, changes);
        time.Advance(TimeSpan.FromSeconds(1)); // generatedAt + 15 minutes, not the next poll
        Assert.False(panel.IsCurrent);
        Assert.Equal(FreshnessState.Stale, runtime.State.Freshness);
        Assert.True(panel.IsCached);
        Assert.Equal("Stale", panel.FreshnessLabel);
        Assert.Equal(32.5m, panel.Primary.PercentageUsed);
        Assert.Equal(warning, panel.WarningLabel);
        Assert.Equal(error, panel.ErrorLabel);
        Assert.Equal(1, changes);
        time.Advance(TimeSpan.FromMinutes(1)); // file is now 16 minutes old
        Assert.False(panel.IsCurrent);
        Assert.Equal(reads, files.Paths.Count);
    }

    [Fact]
    public async Task Fresher_remote_file_replaces_the_previous_deadline()
    {
        var time = new AdvancingTimeProvider();
        var clock = new AdvancingClock(time);
        var files = new Files(GeneratedAt(Now.AddMinutes(-14)));
        await using var runtime = await RemoteRuntime(time, clock, files);
        time.Advance(TimeSpan.FromSeconds(30));
        files.Content = Encoding.UTF8.GetBytes(GeneratedAt(time.GetUtcNow().AddMinutes(-12)));
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        var changes = 0;
        runtime.StateChanged += () => changes++;
        var reads = files.Paths.Count;
        time.Advance(TimeSpan.FromSeconds(30)); // old deadline
        Assert.Equal(FreshnessState.Current, runtime.State.Freshness);
        Assert.Equal(0, changes);
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(FreshnessState.Current, runtime.State.Freshness);
        time.Advance(TimeSpan.FromSeconds(30)); // replacement deadline
        Assert.Equal(FreshnessState.Stale, runtime.State.Freshness);
        Assert.Equal(1, changes);
        Assert.Equal(reads, files.Paths.Count);
    }

    [Theory]
    [InlineData("this-pc")]
    [InlineData("revoke")]
    [InlineData("clear")]
    [InlineData("dispose")]
    public async Task Leaving_remote_mode_cancels_its_deadline(string exit)
    {
        var time = new AdvancingTimeProvider();
        var clock = new AdvancingClock(time);
        var files = new Files(GeneratedAt(Now.AddMinutes(-14)));
        await using var runtime = await RemoteRuntime(time, clock, files);
        if (exit == "this-pc") await runtime.SetSourceAsync(ClaudeQuotaSource.ThisPc, default);
        else if (exit == "revoke") await runtime.RevokeConsentAsync(default);
        else if (exit == "clear") await runtime.CancelAndWaitAsync(default);
        else await runtime.DisposeAsync();
        Assert.Equal(exit == "this-pc" ? 1 : 0, time.ActiveTimers); // only local polling may remain
        var changes = 0;
        runtime.StateChanged += () => changes++;
        var reads = files.Paths.Count;
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(0, changes);
        Assert.Equal(reads, files.Paths.Count);
        if (exit == "this-pc") Assert.Equal(FreshnessState.Current, runtime.State.Freshness);
        else Assert.False(runtime.IsConsentEnabled);
    }

    private static string GeneratedAt(DateTimeOffset generated) =>
        Valid.Replace("2030-01-01T12:00:00Z", generated.ToString("yyyy-MM-ddTHH:mm:ss'Z'"));

    private async Task<ClaudeQuotaRuntime> RemoteRuntime(AdvancingTimeProvider time, IClock clock, Files files)
    {
        await Settings.SaveClaudeSourceAsync(ClaudeQuotaSource.OtherMachine, default);
        var runtime = new ClaudeQuotaRuntime(Settings, new Store(), _ => new Provider(), clock,
            timeProvider: time, remoteProviderFactory: policy => new ClaudeRemoteUsageProvider(policy,
                RemotePath, files, _ => FileAttributes.Normal, time.GetUtcNow));
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        Assert.Equal(FreshnessState.Current, runtime.State.Freshness);
        return runtime;
    }

    // f: keep all existing promises, extend the disclosure to both modes.
    [Fact]
    public void Disclosure_covers_local_read_only_login_and_remote_usage_without_local_credentials()
    {
        var disclosure = ClaudeQuotaRuntime.ConsentDisclosure;
        foreach (var phrase in new[] { "this PC", "Claude Code", "read-only", "never refresh", "other machine", "usage file", "another computer", "no credentials" })
            Assert.Contains(phrase, disclosure, StringComparison.OrdinalIgnoreCase);
    }

    // g: real desktop composition selection, no real user profile or credential input.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Desktop_default_is_lazy_local_and_explicit_remote_never_constructs_local(bool remote)
    {
        if (remote) await Settings.SaveClaudeSourceAsync(ClaudeQuotaSource.OtherMachine, default);
        var locals = 0; var remotes = 0; var provider = new Provider();
        var composition = App.CreateComposition(new App.CompositionSeams(DataDirectory: _root,
            CodexHome: Path.Combine(_root, "synthetic-codex"), UserProfile: Path.Combine(_root, "synthetic-profile"),
            ClaudeProviderFactory: _ => { locals++; return provider; },
            ClaudeRemoteProviderFactory: _ => { remotes++; return provider; }));
        await using var resource = composition.Resource;
        Assert.Equal(0, locals + remotes);
        await composition.Initialize();
        Assert.Equal(0, locals + remotes);
        await composition.ClaudeQuota.GrantConsentAsync(default);
        Assert.Equal(remote ? 0 : 1, locals); Assert.Equal(remote ? 1 : 0, remotes);
        Assert.Equal(1, provider.Calls);
        Assert.False(await Settings.LoadAsync(default));
    }

    private ClaudeRemoteUsageProvider Remote(Files files, bool enabled = true, Func<string, FileAttributes>? attributes = null) =>
        new(new PrivateIntegrationPolicy(enabled), RemotePath, files, attributes ?? (_ => FileAttributes.Normal), () => Now);
    private static string Document(string state, string primary, string weekly) =>
        "{\"schemaVersion\":1,\"generatedAt\":\"2030-01-01T12:00:00Z\",\"state\":\"" + state + "\",\"fiveHour\":" + primary + ",\"weekly\":" + weekly + "}";
    private static void Failure(QuotaProviderResult result, QuotaErrorKind kind, string code)
    {
        Assert.Null(result.Snapshot); Assert.NotNull(result.Failure);
        Assert.Equal(kind, result.Failure!.Kind); Assert.Equal(code, result.Failure.SafeCode);
        Assert.DoesNotContain("synthetic", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
    }
    private static async Task Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++) await Task.Delay(10);
        Assert.True(condition());
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed class AdvancingClock(TimeProvider time) : IClock { public DateTimeOffset UtcNow => time.GetUtcNow(); }
    // Synchronous virtual timers make expiry/event assertions independent of sleeps and thread scheduling.
    private sealed class AdvancingTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = Now;
        private readonly List<VirtualTimer> _timers = [];
        public override DateTimeOffset GetUtcNow() => _now;
        public int ActiveTimers => _timers.Count(timer => timer.Due is not null);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new VirtualTimer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
        public void Advance(TimeSpan elapsed)
        {
            var target = _now + elapsed;
            while (_timers.Where(timer => timer.Due <= target).OrderBy(timer => timer.Due).FirstOrDefault() is { } timer)
            {
                _now = timer.Due!.Value;
                timer.Fire();
            }
            _now = target;
        }
        private sealed class VirtualTimer(AdvancingTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            private TimeSpan _period;
            public DateTimeOffset? Due { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed) return false;
                _period = period;
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                return true;
            }
            public void Fire()
            {
                Due = _period == Timeout.InfiniteTimeSpan ? null : owner._now + _period;
                callback(state);
            }
            public void Dispose() { _disposed = true; Due = null; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class Files(byte[] bytes) : ICredentialFileReader
    {
        public Files(string json) : this(Encoding.UTF8.GetBytes(json)) { }
        public byte[] Content { get; set; } = bytes;
        public Exception? Error { get; init; }
        public List<string> Paths { get; } = [];
        public FileShare Share { get; private set; }
        public TrackingStream? Stream { get; private set; }
        public bool Disposed => Stream?.Disposed == true;
        public ValueTask<Stream> OpenReadAsync(string path, FileShare share, CancellationToken cancellationToken)
        {
            Paths.Add(path); Share = share;
            if (Error is not null) return ValueTask.FromException<Stream>(Error);
            Stream = new TrackingStream(Content);
            return ValueTask.FromResult<Stream>(Stream);
        }
    }
    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class Store : IQuotaSnapshotStore
    {
        public QuotaSnapshot? Snapshot { get; set; }
        public List<QuotaSnapshot> Saved { get; } = [];
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken token) => ValueTask.FromResult(Snapshot);
        public ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken token) { Saved.Add(snapshot); Snapshot = snapshot; return ValueTask.CompletedTask; }
        public ValueTask ClearAsync(CancellationToken token) { Snapshot = null; return ValueTask.CompletedTask; }
    }
    private sealed class Provider(decimal percentage = 42) : IQuotaProvider
    {
        public int Calls { get; private set; }
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token)
        { Calls++; return ValueTask.FromResult(new QuotaProviderResult(new(new(percentage, Now.AddHours(5)), null, Now), null)); }
    }
    private sealed class BlockingProvider : IQuotaProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<QuotaProviderResult> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token) { Token = token; Started.TrySetResult(); return new(Release.Task); }
    }
    private sealed class SourceTray : IClaudeQuotaTray, IClaudeQuotaSourceTray
    {
        public event Action? ClaudeQuotaEnableRequested { add { } remove { } }
        public event Action? ClaudeQuotaDisableRequested { add { } remove { } }
        public event Action<ClaudeQuotaSource>? ClaudeSourceRequested;
        public ClaudeQuotaSource? Selected { get; private set; }
        public void SetClaudeQuotaEnabled(bool enabled) { }
        public void SetClaudeSource(ClaudeQuotaSource source) => Selected = source;
        public void Choose(ClaudeQuotaSource source) => ClaudeSourceRequested?.Invoke(source);
    }
    private sealed class Prompt : IPrivateIntegrationConsentPrompt
    {
        public int Calls { get; private set; }
        public bool Confirm() { Calls++; return true; }
    }
}
