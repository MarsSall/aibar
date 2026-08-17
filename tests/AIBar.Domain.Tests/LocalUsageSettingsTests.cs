using AIBar.Application;
using AIBar.Domain;
using System.Text.Json;

namespace AIBar.Domain.Tests;

public sealed class LocalUsageSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-local-usage-{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(_root, "local-usage.json");

    [Fact]
    public async Task Defaults_off_and_persists_independent_tool_decisions()
    {
        var store = new LocalUsageSettings(SettingsPath);

        AssertDisabled(await store.LoadAsync(default));
        await store.SaveAsync(new(true, false), default);
        var openCodeOnly = await new LocalUsageSettings(SettingsPath).LoadAsync(default);
        Assert.True(openCodeOnly.IsEnabled(UsageTool.OpenCode));
        Assert.False(openCodeOnly.IsEnabled(UsageTool.Pi));

        await store.SaveAsync(new(false, true), default);
        var piOnly = await new LocalUsageSettings(SettingsPath).LoadAsync(default);
        Assert.False(piOnly.IsEnabled(UsageTool.OpenCode));
        Assert.True(piOnly.IsEnabled(UsageTool.Pi));

        await store.SaveAsync(new(false, false), default);
        AssertDisabled(await new LocalUsageSettings(SettingsPath).LoadAsync(default));
    }

    [Fact]
    public async Task Schema_one_loads_without_rewrite_and_schema_two_roundtrips_null_or_normalized_custom_root()
    {
        Directory.CreateDirectory(_root);
        const string schemaOne = "{\"schema\":1,\"openCodeEnabled\":true,\"piEnabled\":false}";
        await File.WriteAllTextAsync(SettingsPath, schemaOne);
        var migrated = await new LocalUsageSettings(SettingsPath).LoadAsync(default);
        Assert.True(migrated.OpenCodeEnabled); Assert.Null(migrated.OpenCodeDataRoot);
        Assert.Equal(schemaOne, await File.ReadAllTextAsync(SettingsPath));

        var custom = Path.Combine(_root, "custom"); Directory.CreateDirectory(custom);
        await new LocalUsageSettings(SettingsPath).SaveAsync(new(true, false, custom), default);

        Assert.Equal(JsonSerializer.Serialize(new { schema = 2, openCodeEnabled = true, piEnabled = false, openCodeDataRoot = custom }), await File.ReadAllTextAsync(SettingsPath));
        var reopened = await new LocalUsageSettings(SettingsPath).LoadAsync(default);
        Assert.Equal(new(true, false, custom), reopened);
        Directory.Delete(custom);
        Assert.Equal(custom, (await new LocalUsageSettings(SettingsPath).LoadAsync(default)).OpenCodeDataRoot);

        await new LocalUsageSettings(SettingsPath).SaveAsync(reopened with { OpenCodeDataRoot = null }, default);
        Assert.Equal(new(true, false), await new LocalUsageSettings(SettingsPath).LoadAsync(default));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schema\":2,\"openCodeEnabled\":true,\"piEnabled\":true}")]
    [InlineData("{\"schema\":1,\"openCodeEnabled\":true}")]
    [InlineData("{\"schema\":\"1\",\"openCodeEnabled\":true,\"piEnabled\":true}")]
    [InlineData("{\"schema\":1.0,\"openCodeEnabled\":true,\"piEnabled\":true}")]
    [InlineData("{\"schema\":1,\"openCodeEnabled\":1,\"piEnabled\":true}")]
    [InlineData("{\"schema\":1,\"openCodeEnabled\":true,\"piEnabled\":\"false\"}")]
    [InlineData("{\"schema\":1,\"schema\":1,\"openCodeEnabled\":true,\"piEnabled\":true}")]
    [InlineData("{\"schema\":1,\"openCodeEnabled\":true,\"openCodeEnabled\":false,\"piEnabled\":true}")]
    [InlineData("{\"schema\":1,\"openCodeEnabled\":true,\"piEnabled\":true,\"sourcePath\":\"private\"}")]
    [InlineData("{\"schema\":2,\"openCodeEnabled\":true,\"piEnabled\":true,\"openCodeDataRoot\":1}")]
    [InlineData("{\"schema\":2,\"openCodeEnabled\":true,\"piEnabled\":true,\"openCodeDataRoot\":\"relative\"}")]
    [InlineData("{\"schema\":1,\"openCodeEnabled\":true,\"piEnabled\":true,\"openCodeDataRoot\":null}")]
    public async Task Ambiguous_or_invalid_documents_fail_closed(string json)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(SettingsPath, json);

        AssertDisabled(await new LocalUsageSettings(SettingsPath).LoadAsync(default));
    }

    [Fact]
    public async Task Existing_schema_one_quota_consent_remains_compatible_and_uncoupled()
    {
        Directory.CreateDirectory(_root);
        var quotaPath = Path.Combine(_root, "quota.json");
        await File.WriteAllTextAsync(quotaPath, "{\"schema\":1,\"privateCodexConsent\":true}");
        var local = new LocalUsageSettings(SettingsPath);

        Assert.True(await new ConsentSettings(quotaPath).LoadAsync(default));
        AssertDisabled(await local.LoadAsync(default));
        await local.SaveAsync(new(true, false), default);
        Assert.True(await new ConsentSettings(quotaPath).LoadAsync(default));

        var policy = new PrivateIntegrationPolicy();
        var coordinator = new QuotaRefreshCoordinator(new EmptyQuotaStore(), new DisabledQuotaProvider(), new FixedClock(DateTimeOffset.UnixEpoch), new FreshnessPolicy(TimeSpan.Zero), TimeSpan.Zero);
        await using var runtime = new BetaRuntime(new ConsentSettings(quotaPath), policy, coordinator, new FixedClock(DateTimeOffset.UnixEpoch));
        await runtime.InitializeAsync(default);
        Assert.True(runtime.IsConsentEnabled);

        await new LocalUsageSettings(Path.Combine(_root, "separate.json")).SaveAsync(new(false, true), default);
        Assert.False(await new ConsentSettings(Path.Combine(_root, "missing-quota.json")).LoadAsync(default));
    }

    [Fact]
    public async Task Failure_and_cancellation_before_replace_preserve_the_previous_file_and_clean_temps()
    {
        var stable = new LocalUsageSettings(SettingsPath);
        await stable.SaveAsync(new(true, false), default);

        var failing = new LocalUsageSettings(SettingsPath, _ => ValueTask.FromException(new IOException("synthetic failure")));
        await Assert.ThrowsAsync<IOException>(() => failing.SaveAsync(new(false, true), default).AsTask());
        AssertOpenCodeOnly(await stable.LoadAsync(default));

        using var cancellation = new CancellationTokenSource();
        var cancelling = new LocalUsageSettings(SettingsPath, token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelling.SaveAsync(new(false, true), cancellation.Token).AsTask());
        AssertOpenCodeOnly(await stable.LoadAsync(default));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task Concurrent_same_instance_saves_serialize_to_complete_last_decision()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async ValueTask BeforeReplace(CancellationToken _)
        {
            if (Interlocked.Increment(ref calls) != 1) return;
            entered.SetResult();
            await release.Task;
        }
        var store = new LocalUsageSettings(SettingsPath, BeforeReplace);

        var first = store.SaveAsync(new(true, false), default).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = store.SaveAsync(new(false, true), default).AsTask();
        Assert.False(second.IsCompleted);
        release.SetResult();
        await Task.WhenAll(first, second);

        var final = await store.LoadAsync(default);
        Assert.False(final.IsEnabled(UsageTool.OpenCode));
        Assert.True(final.IsEnabled(UsageTool.Pi));
        Assert.Equal("{\"schema\":2,\"openCodeEnabled\":false,\"piEnabled\":true,\"openCodeDataRoot\":null}", await File.ReadAllTextAsync(SettingsPath));
        Assert.Equal(2, calls);
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
    }

    [Fact]
    public void Unsupported_tool_never_enables()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalUsagePolicy.Disabled.IsEnabled((UsageTool)999));
    }

    [Fact]
    public async Task Reparse_target_is_rejected_without_mutating_its_target()
    {
        Directory.CreateDirectory(_root);
        var target = Path.Combine(_root, "target.json");
        await File.WriteAllTextAsync(target, "keep");
        try { File.CreateSymbolicLink(SettingsPath, target); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip($"Symbolic-link test unsupported: {error.GetType().Name}");
        }

        var store = new LocalUsageSettings(SettingsPath);
        AssertDisabled(await store.LoadAsync(default));
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(new(true, true), default).AsTask());
        Assert.Equal("keep", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Root_only_target_loads_disabled_without_traversal()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(_root))!;

        AssertDisabled(await new LocalUsageSettings(root).LoadAsync(default));
    }

    [Fact]
    public async Task Root_only_target_save_is_rejected_before_mutation()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(_root))!;
        var beforeReplaceCalled = false;
        var store = new LocalUsageSettings(root, _ => { beforeReplaceCalled = true; return ValueTask.CompletedTask; });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(new(true, true), default).AsTask());

        Assert.Equal("Local usage settings require a non-root file path.", error.Message);
        Assert.False(beforeReplaceCalled);
    }

    [Fact]
    public async Task Relative_target_with_absent_directory_still_roundtrips()
    {
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, Path.Combine(_root, "nested", "settings.json"));
        var store = new LocalUsageSettings(relative);

        AssertDisabled(await store.LoadAsync(default));
        await store.SaveAsync(new(true, false), default);
        AssertOpenCodeOnly(await store.LoadAsync(default));
    }

    private static void AssertDisabled(LocalUsagePolicy policy)
    {
        Assert.False(policy.IsEnabled(UsageTool.OpenCode));
        Assert.False(policy.IsEnabled(UsageTool.Pi));
    }

    private static void AssertOpenCodeOnly(LocalUsagePolicy policy)
    {
        Assert.True(policy.IsEnabled(UsageTool.OpenCode));
        Assert.False(policy.IsEnabled(UsageTool.Pi));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class DisabledQuotaProvider : IQuotaProvider
    {
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken _) => ValueTask.FromResult(new QuotaProviderResult(null, new(QuotaErrorKind.Unavailable, "disabled")));
    }

    private sealed class EmptyQuotaStore : IQuotaSnapshotStore
    {
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken _) => ValueTask.FromResult<QuotaSnapshot?>(null);
        public ValueTask SaveAsync(QuotaSnapshot _, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken _) => ValueTask.CompletedTask;
    }
}
