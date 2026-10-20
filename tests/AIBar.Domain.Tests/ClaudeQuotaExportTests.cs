using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class ClaudeQuotaExportTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-claude-export-{Guid.NewGuid():N}");
    private string ClaudePath => Path.Combine(_root, "AIBar", "yasb-claude-quota.json");
    private string CodexPath => WindowsQuotaExportPath.ForRoot(_root);
    private ConsentSettings Settings => new(Path.Combine(_root, "settings.json"));

    // a/b: separate fixed destination; no relaxation of the existing Codex constructor.
    [Fact]
    public void Claude_path_is_fixed_and_Codex_path_validation_is_unchanged()
    {
        Assert.Equal(ClaudePath, WindowsQuotaExportPath.ForClaudeRoot(_root));
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIBar", "yasb-claude-quota.json"), WindowsQuotaExportPath.ForClaudeCurrentUser());
        Assert.Equal(Path.Combine(_root, "AIBar", "yasb-quota.json"), CodexPath);
        Assert.Throws<ArgumentException>(() => WindowsQuotaExportPath.ForClaudeRoot(Path.Combine(_root, "..", "alias")));
        Assert.Throws<ArgumentException>(() => WindowsQuotaExportPath.ForClaudeRoot("relative"));
        Assert.Throws<ArgumentException>(() => new WindowsAtomicQuotaExportWriter(ClaudePath, _root, new()));
    }

    [Fact]
    public async Task Claude_uses_the_same_atomic_writer_exact_v1_bytes_and_verified_minimum_ACL()
    {
        Directory.CreateDirectory(_root);
        var points = new HashSet<QuotaExportWritePoint>();
        var writer = WindowsAtomicQuotaExportWriter.ForClaudeRoot(_root, new(
            Point: (point, path) =>
            {
                points.Add(point);
                if (point == QuotaExportWritePoint.AfterTemporaryAcl)
                {
                    Assert.Equal(Path.GetDirectoryName(ClaudePath), Path.GetDirectoryName(path));
                    Assert.Equal(0, new FileInfo(path).Length);
                    AssertAcl(path);
                }
            }));
        Assert.IsType<WindowsAtomicQuotaExportWriter>(writer);
        await writer.WriteAsync(Document(37), Now, default);
        var first = File.ReadAllBytes(ClaudePath);
        Assert.Equal(Encoding.UTF8.GetBytes(QuotaExportWire.Serialize(Document(37), Now)), first);
        Assert.False(first.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        await writer.WriteAsync(Document(75), Now, default);
        Assert.Equal(Encoding.UTF8.GetBytes(QuotaExportWire.Serialize(Document(75), Now)), File.ReadAllBytes(ClaudePath));
        AssertAcl(ClaudePath);
        Assert.Contains(QuotaExportWritePoint.BeforeCommit, points);
        Assert.Contains(QuotaExportWritePoint.BeforeResultVerification, points);
        Assert.False(File.Exists(CodexPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(ClaudePath)!, "*.tmp"));
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("destination")]
    [InlineData("temporary")]
    [InlineData("commit-race")]
    [InlineData("result-race")]
    public async Task Claude_rejects_reparse_substitution_at_each_boundary(string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ClaudePath)!);
        var before = Encoding.UTF8.GetBytes(QuotaExportWire.Serialize(Document(11), Now));
        File.WriteAllBytes(ClaudePath, before);
        var raced = false;
        var writer = WindowsAtomicQuotaExportWriter.ForClaudeRoot(_root, new(
            Point: (point, _) =>
            {
                if (target == "commit-race" && point == QuotaExportWritePoint.BeforeCommit ||
                    target == "result-race" && point == QuotaExportWritePoint.BeforeResultVerification) raced = true;
            },
            Attributes: path =>
                target == "parent" && path == Path.GetDirectoryName(ClaudePath) ||
                target == "destination" && path == ClaudePath ||
                target == "temporary" && Path.GetExtension(path) == ".tmp" || raced && path == ClaudePath
                    ? FileAttributes.ReparsePoint : File.GetAttributes(path)));
        await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync(Document(37), Now, default).AsTask());
        if (target != "result-race") Assert.Equal(before, File.ReadAllBytes(ClaudePath));
        Assert.False(File.Exists(CodexPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(ClaudePath)!, "*.tmp"));
    }

    [Theory]
    [InlineData((int)QuotaExportWritePoint.AfterDirectoryAcl)]
    [InlineData((int)QuotaExportWritePoint.AfterTemporaryAcl)]
    [InlineData((int)QuotaExportWritePoint.AfterDestinationAcl)]
    [InlineData((int)QuotaExportWritePoint.AfterResultAcl)]
    public async Task Claude_ACL_readback_failure_is_not_silently_accepted(int boundary)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ClaudePath)!);
        File.WriteAllText(ClaudePath, QuotaExportWire.Serialize(Document(11), Now));
        var writer = WindowsAtomicQuotaExportWriter.ForClaudeRoot(_root, new(ReadAcl: (point, path, directory) =>
            point == (QuotaExportWritePoint)boundary ? throw new UnauthorizedAccessException("synthetic") :
            directory ? FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(path)) :
                FileSystemAclExtensions.GetAccessControl(new FileInfo(path))));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.WriteAsync(Document(37), Now, default).AsTask());
        Assert.False(File.Exists(CodexPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(ClaudePath)!, "*.tmp"));
    }

    [Fact]
    public async Task Cached_Claude_values_and_freshness_reevaluation_do_not_unlock_before_post_enable_retrieval()
    {
        var writer = new Writer(); var provider = new Provider { Block = true };
        await using var runtime = Runtime(writer, provider, new Store { Snapshot = Snapshot(11) });
        await runtime.InitializeAsync(default);
        Assert.All(writer.Documents, value => Assert.Equal(QuotaExportState.Disabled, value.State));
        var grant = runtime.GrantConsentAsync(default).AsTask();
        try
        {
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await runtime.RefreshAsync(RefreshTrigger.Sleep, default);
            await runtime.ExportCompletion;
            Assert.NotEmpty(writer.Documents);
            Assert.All(writer.Documents, value => Assert.Equal(QuotaExportState.Disabled, value.State));
        }
        finally { provider.Release.TrySetResult(); }
        await grant;
        await runtime.ExportCompletion;
        Assert.Equal(37m, writer.Documents.Last().Weekly!.PercentageUsed);
        Assert.Equal(QuotaExportState.Current, writer.Documents.Last().State);
    }

    [Fact]
    public async Task Persisted_consent_with_fresh_cache_still_waits_for_a_new_retrieval()
    {
        await Settings.SaveClaudeAsync(true, default);
        var writer = new Writer(); var provider = new Provider { Block = true };
        await using var runtime = Runtime(writer, provider, new Store { Snapshot = Snapshot(11) });
        await runtime.InitializeAsync(default);
        // Startup now refreshes by interval even with a fresh cache (S13); while that retrieval
        // is pending the cached value must still not unlock the export.
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(60));
        await runtime.ExportCompletion;
        Assert.All(writer.Documents, value => Assert.Equal(QuotaExportState.Disabled, value.State));
        provider.Release.TrySetResult();
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        await runtime.ExportCompletion;
        Assert.Equal(37m, writer.Documents.Last().Weekly!.PercentageUsed);
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("clear")]
    [InlineData("dispose")]
    public async Task Privacy_exit_drains_a_cancellation_resistant_write_before_finishing_disabled(string exit)
    {
        var writer = new Writer { BlockCurrent = true };
        var runtime = Runtime(writer, new Provider());
        Task? grant = null; Task? privacy = null;
        try
        {
            grant = runtime.GrantConsentAsync(default).AsTask();
            await writer.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            privacy = exit == "revoke" ? runtime.RevokeConsentAsync(default).AsTask() :
                exit == "clear" ? runtime.CancelAndWaitAsync(default).AsTask() : runtime.DisposeAsync().AsTask();
            await writer.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(privacy.IsCompleted);
            writer.Release.TrySetResult();
            await Task.WhenAll(grant, privacy).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(QuotaExportState.Disabled, writer.Documents.Last().State);
        }
        finally
        {
            writer.Release.TrySetResult();
            if (grant is not null) await grant;
            if (privacy is not null) await privacy;
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task Failed_post_enable_attempt_without_cache_publishes_sanitized_unavailable()
    {
        var writer = new Writer(); var provider = new Provider
        {
            Result = new(null, new(QuotaErrorKind.Authentication, "token-canary C:\\private\\credentials.json plan-canary"))
        };
        await using var runtime = Runtime(writer, provider);
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        await runtime.ExportCompletion;
        var last = writer.Documents.Last();
        Assert.Equal(QuotaExportState.Unavailable, last.State);
        Assert.Equal(QuotaExportWarning.AuthenticationFailed, last.Warning);
        Assert.Null(last.SourceRetrievedAt);
        Assert.Null(last.FiveHour);
        Assert.Null(last.Weekly);
        AssertClosedWire(last);
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("clear")]
    [InlineData("dispose")]
    public async Task Every_privacy_exit_finishes_disabled_and_late_events_cannot_republish(string exit)
    {
        var writer = new Writer(); var runtime = Runtime(writer, new Provider());
        try
        {
            await runtime.InitializeAsync(default);
            await runtime.GrantConsentAsync(default);
            await runtime.ExportCompletion;
            Assert.Equal(QuotaExportState.Current, writer.Documents.Last().State);
            if (exit == "revoke") await runtime.RevokeConsentAsync(default);
            else if (exit == "clear")
                await new ClearAiBarDataService(_root, [runtime], _ => ValueTask.CompletedTask).ClearAsync(default);
            else await runtime.DisposeAsync();
            var last = writer.Documents.Last();
            Assert.Equal(QuotaExportState.Disabled, last.State);
            Assert.Equal(QuotaExportWarning.Disabled, last.Warning);
            Assert.Null(last.SourceRetrievedAt);
            Assert.Null(last.FiveHour);
            Assert.Null(last.Weekly);
            var count = writer.Documents.Count;
            await runtime.RefreshAsync(RefreshTrigger.Manual, default);
            Assert.Equal(count, writer.Documents.Count);
            if (exit != "dispose")
            {
                await runtime.GrantConsentAsync(default);
                await runtime.ExportCompletion;
                Assert.Equal(QuotaExportState.Current, writer.Documents.Last().State);
            }
        }
        finally { await runtime.DisposeAsync(); }
    }

    // b: both directions, including actual file bytes while Claude consent changes.
    [Fact]
    public async Task Provider_writers_cannot_overwrite_each_others_files()
    {
        Directory.CreateDirectory(_root);
        var codexWriter = new WindowsAtomicQuotaExportWriter(_root, new());
        await using var authority = new QuotaRefreshCoordinator(new Store(), new Provider(), new Clock(),
            new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
        await using var codex = new QuotaExportPublisher(authority, codexWriter, new Clock());
        var claude = WindowsAtomicQuotaExportWriter.ForClaudeRoot(_root, new());
        await codex.EnableAsync(default);
        codex.Accept(new(new(Snapshot(11), FreshnessState.Current, false, null, null), 1, 1));
        await codex.PublicationCompletion;
        var codexBytes = File.ReadAllBytes(CodexPath);
        await using var runtime = Runtime(claude, new Provider());
        await runtime.InitializeAsync(default);
        Assert.Equal(codexBytes, File.ReadAllBytes(CodexPath));
        await runtime.GrantConsentAsync(default);
        await runtime.ExportCompletion;
        Assert.Equal(codexBytes, File.ReadAllBytes(CodexPath));
        var claudeBytes = File.ReadAllBytes(ClaudePath);
        codex.Accept(new(new(Snapshot(11), FreshnessState.Current, false, null, null), 2, 2));
        await codex.PublicationCompletion;
        Assert.Equal(codexBytes, File.ReadAllBytes(CodexPath));
        codex.Accept(new(new(Snapshot(81), FreshnessState.Current, false, null, null), 3, 3));
        await codex.PublicationCompletion;
        Assert.Equal(claudeBytes, File.ReadAllBytes(ClaudePath));
        var updatedCodex = File.ReadAllBytes(CodexPath);
        await runtime.RevokeConsentAsync(default);
        Assert.Equal(updatedCodex, File.ReadAllBytes(CodexPath));
    }

    [Fact]
    public async Task Desktop_composition_connects_Claude_writer_only_to_Claude_runtime()
    {
        var codex = new Writer(); var claude = new Writer();
        var at = DateTimeOffset.UtcNow;
        var provider = new Provider { Result = new(new QuotaSnapshot(new(37, at.AddHours(1)), new(37, at.AddDays(1)), at), null) };
        var composition = App.CreateComposition(new(DataDirectory: _root, UserProfile: _root,
            CodexHome: Path.Combine(_root, "synthetic-codex"), ExportWriter: codex,
            ClaudeProviderFactory: _ => provider, ClaudeExportWriter: claude));
        try
        {
            await composition.ClaudeQuota.InitializeAsync(default);
            var before = codex.Documents.Count;
            await composition.ClaudeQuota.GrantConsentAsync(default);
            await composition.ClaudeQuota.ExportCompletion;
            Assert.NotEmpty(claude.Documents);
            Assert.Equal(QuotaExportState.Current, claude.Documents.Last().State);
            Assert.Equal(before, codex.Documents.Count);
            await composition.ClaudeQuota.RevokeConsentAsync(default);
            Assert.Equal(QuotaExportState.Disabled, claude.Documents.Last().State);
            Assert.Equal(before, codex.Documents.Count);
        }
        finally { await composition.Resource.DisposeAsync(); }
    }

    // e: exercise the real runtime projection, not a separate Claude wire format.
    [Fact]
    public async Task Claude_export_has_exact_v1_order_and_never_exports_plan_token_or_path()
    {
        var writer = new Writer();
        await using var runtime = Runtime(writer, new Provider());
        await runtime.GrantConsentAsync(default);
        await runtime.ExportCompletion;
        AssertClosedWire(writer.Documents.Last());
    }

    private static void AssertClosedWire(QuotaExportDocument document)
    {
        var json = QuotaExportWire.Serialize(document, Now);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(new[] { "schemaVersion", "generatedAt", "state", "warning", "sourceRetrievedAt", "fiveHour", "weekly" },
            parsed.RootElement.EnumerateObject().Select(value => value.Name));
        Assert.Equal(1, parsed.RootElement.GetProperty("schemaVersion").GetInt32());
        foreach (var name in new[] { "fiveHour", "weekly" })
            if (parsed.RootElement.GetProperty(name).ValueKind != JsonValueKind.Null)
                Assert.Equal(new[] { "percentageUsed", "resetAt" }, parsed.RootElement.GetProperty(name).EnumerateObject().Select(value => value.Name));
        foreach (var forbidden in new[] { "token", "credential", "plan", "subscription", "C:\\private", "canary" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }
    private static void AssertAcl(string path)
    {
        var acl = FileSystemAclExtensions.GetAccessControl(new FileInfo(path));
        Assert.True(acl.AreAccessRulesProtected);
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        Assert.Equal(2, rules.Length);
        Assert.Equal(new[] { WindowsIdentity.GetCurrent().User!.Value, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value }.Order(),
            rules.Select(value => value.IdentityReference.Value).Order());
        Assert.All(rules, rule =>
        {
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
            Assert.Equal(InheritanceFlags.None, rule.InheritanceFlags);
            Assert.Equal(PropagationFlags.None, rule.PropagationFlags);
            Assert.Equal(FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.Delete |
                FileSystemRights.ChangePermissions | FileSystemRights.Synchronize, rule.FileSystemRights);
        });
    }
    private ClaudeQuotaRuntime Runtime(IQuotaExportWriter writer, Provider provider, Store? store = null) =>
        new(Settings, store ?? new(), _ => provider, new Clock(), exportWriter: writer);
    private static QuotaSnapshot Snapshot(decimal value) => new(new(value, Now.AddHours(1)), new(value, Now.AddDays(1)), Now);
    private static QuotaExportDocument Document(decimal value) => new(1, Now, QuotaExportState.Current, null, Now, new(value, Now.AddHours(1)), new(value, Now.AddDays(1)));
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed class Store : IQuotaSnapshotStore
    {
        public QuotaSnapshot? Snapshot { get; set; }
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Snapshot);
        public ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken) { Snapshot = snapshot; return ValueTask.CompletedTask; }
        public ValueTask ClearAsync(CancellationToken cancellationToken) { Snapshot = null; return ValueTask.CompletedTask; }
    }
    private sealed class Provider : IQuotaProvider
    {
        public bool Block { get; init; }
        public QuotaProviderResult Result { get; init; } = new(Snapshot(37), null);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (Block) await Release.Task.WaitAsync(cancellationToken);
            return Result;
        }
    }
    private sealed class Writer : IQuotaExportWriter
    {
        private readonly ConcurrentQueue<QuotaExportDocument> _documents = new();
        public IReadOnlyList<QuotaExportDocument> Documents => _documents.ToArray();
        public bool BlockCurrent { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask WriteAsync(QuotaExportDocument document, DateTimeOffset generatedAt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var registration = cancellationToken.Register(() => CancellationObserved.TrySetResult());
            if (BlockCurrent && document.State == QuotaExportState.Current)
            {
                Started.TrySetResult();
                // Deliberately ignore cancellation: privacy completion must drain this write.
                await Release.Task;
            }
            _documents.Enqueue(document);
        }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
