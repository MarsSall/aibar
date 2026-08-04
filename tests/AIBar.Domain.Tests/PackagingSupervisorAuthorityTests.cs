using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using AIBar.Packaging.Supervisor.Authority.Testing;

namespace AIBar.Domain.Tests;

public sealed class PackagingSupervisorAuthorityTests
{
    private const string PublicKey = "0024000004800000140100000602000000240000525341310008000001000100F51E383EAE0372C9E1223F4CD3158495C4A38F12837D75E43E85D18752622529FFB59BFC44F6D708BD71D2EF555B885765D5D6A996B9BCF73F1C3A0DF3302ED983F3F9C4C6D5F0EFF6A9A8E32524D16B862985772000E8DADF4D4E508B040D3F8C6897B81290D64795BB0E5C47760AEB41849E02B796B15989881FB9046D1175DC92E5AB21B7314D60F0AFF3072523489FDC9742C07E688F69DDB1840899567DD860BAA527DDD6F68C0A3B0AE84254D348880A2B742BA05F4773F7F6966CC30B4BBF3145F0FB6F92A8703767D10B8CC3A004248343DB28C1B9D69EEFCD286EC6A5C819CA458459066ABFAC09BE5BFFD0D88B35B98651B96957CD6ECAC06022DE";

    [Fact]
    public void Final_signed_friendship_sets_are_exact_and_temporary_b2_friends_are_absent()
    {
        var core = Assembly.Load("AIBar.Packaging.Supervisor.Core");
        var authority = Assembly.Load("AIBar.Packaging.Supervisor.C2Authority");
        var testing = Assembly.Load("AIBar.Packaging.Supervisor.Authority.Testing");
        var friends = core.GetCustomAttributes<InternalsVisibleToAttribute>().Select(attribute => attribute.AssemblyName).ToArray();
        var authorityFriends = authority.GetCustomAttributes<InternalsVisibleToAttribute>().Select(attribute => attribute.AssemblyName).ToArray();

        AssertSignedIdentity(core, "AIBar.Packaging.Supervisor.Core");
        AssertSignedIdentity(authority, "AIBar.Packaging.Supervisor.C2Authority");
        AssertSignedIdentity(testing, "AIBar.Packaging.Supervisor.Authority.Testing");
        AssertSignedIdentity(typeof(PackagingSupervisorAuthorityTests).Assembly, "AIBar.Domain.Tests");
        Assert.Equal(2, friends.Length);
        Assert.True(HasExactlyExpectedFriends(friends, "AIBar.Packaging.Supervisor.C2Authority", "AIBar.Packaging.Supervisor.Authority.Testing"));
        Assert.True(HasExactlyExpectedFriends(authorityFriends, "AIBar.Packaging.Supervisor.Authority.Testing"));
        Assert.DoesNotContain(friends, friend => friend.StartsWith("AIBar.Packaging.Supervisor,", StringComparison.Ordinal) || friend.StartsWith("AIBar.Domain.Tests,", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(InvalidFriendMetadata))]
    public void Core_friend_metadata_fails_closed_for_unsigned_wrong_or_extra_friends(string[] friends)
        => Assert.False(HasExactlyExpectedFriends(friends, "AIBar.Packaging.Supervisor.C2Authority", "AIBar.Packaging.Supervisor.Authority.Testing"));

    [Fact]
    public void Core_preserves_internal_c1c_c2a_seams_without_public_authority_or_test_hooks()
    {
        var core = Assembly.Load("AIBar.Packaging.Supervisor.Core");

        Assert.False(core.GetType("AIBar.Packaging.Supervisor.CapabilityFacetFailurePoint", throwOnError: true)!.IsPublic);
        Assert.False(core.GetType("AIBar.Packaging.Supervisor.ICommittedEvidenceCapabilityFacet", throwOnError: true)!.IsPublic);
        Assert.False(core.GetType("AIBar.Packaging.Supervisor.RetainedTreeReadOnly", throwOnError: true)!.IsPublic);
        Assert.DoesNotContain(core.GetExportedTypes(), type => type.Name.Contains("Test", StringComparison.Ordinal) || type.Name.Contains("Issuer", StringComparison.Ordinal) || type.Name.Contains("Token", StringComparison.Ordinal) || type.Name.Contains("Session", StringComparison.Ordinal) || type.Name.Contains("Lease", StringComparison.Ordinal) || type.Name.Contains("Sandbox", StringComparison.Ordinal));
        Assert.DoesNotContain(core.GetReferencedAssemblies(), reference => reference.Name is "AIBar.Packaging.Supervisor" or "AIBar.Domain.Tests");
    }

    [Fact]
    public void NativeDelete_is_private_and_authority_exports_no_raw_authority()
    {
        var authority = Assembly.Load("AIBar.Packaging.Supervisor.C2Authority");
        var nativeImports = authority.GetTypes().SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)).Where(method => method.GetCustomAttribute<System.Runtime.InteropServices.DllImportAttribute>() is not null);

        Assert.DoesNotContain(authority.GetExportedTypes(), type => type.Name.Contains("Issuer", StringComparison.Ordinal) || type.Name.Contains("Token", StringComparison.Ordinal) || type.Name.Contains("Session", StringComparison.Ordinal) || type.Name.Contains("Lease", StringComparison.Ordinal) || type.Name.Contains("Sandbox", StringComparison.Ordinal));
        var nativeDelete = Assert.Single(nativeImports.Where(method => method.Name == "NtSetInformationFile"));
        Assert.Equal("NtSetInformationFile", nativeDelete.Name);
        Assert.False(nativeDelete.IsPublic);
        Assert.All(nativeImports.Where(method => method.Name is "CryptProtectData" or "CryptUnprotectData" or "LocalFree"), method => Assert.False(method.IsPublic));
        Assert.DoesNotContain(typeof(AuthorityTestingBridge).GetMethods(), method => method.ReturnType.Namespace == "AIBar.Packaging.Supervisor");

        var root = FindRepositoryRoot();
        Assert.DoesNotContain("FileDispositionInformation", File.ReadAllText(Path.Combine(root, "tools", "AIBar.Packaging.Supervisor.Core", "NativeRenameReadiness.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("FileDispositionInformation", File.ReadAllText(Path.Combine(root, "tools", "AIBar.Packaging.Supervisor", "DirectoryCapability.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void NativeDelete_rejects_arbitrary_mismatched_reused_and_invalidated_authorization_without_exporting_authority()
    {
        var rejections = AuthorityTestingBridge.VerifyNativeDeleteRejections();
        var runtime = AuthorityTestingBridge.VerifyNativeDeleteRuntime();

        Assert.True(rejections.Succeeded);
        Assert.Equal("native-delete-rejections-verified", rejections.Code);
        Assert.True(runtime.Succeeded);
        Assert.Equal("native-delete-gated-runtime-verified", runtime.Code);
    }

    [Fact]
    public void NativeDelete_testing_support_remains_excluded_from_production_closure()
    {
        var root = FindRepositoryRoot();
        var testingProject = File.ReadAllText(Path.Combine(root, "tests", "AIBar.Packaging.Supervisor.Authority.Testing", "AIBar.Packaging.Supervisor.Authority.Testing.csproj"));
        var supervisorProject = File.ReadAllText(Path.Combine(root, "tools", "AIBar.Packaging.Supervisor", "AIBar.Packaging.Supervisor.csproj"));
        var authority = Assembly.Load("AIBar.Packaging.Supervisor.C2Authority");

        Assert.Contains("<IsPackable>false</IsPackable>", testingProject, StringComparison.Ordinal);
        Assert.Contains("<IsPublishable>false</IsPublishable>", testingProject, StringComparison.Ordinal);
        Assert.Contains("RefuseTestDistribution", testingProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Authority.Testing", supervisorProject, StringComparison.Ordinal);
        Assert.DoesNotContain(authority.GetReferencedAssemblies(), reference => reference.Name == "AIBar.Packaging.Supervisor.Authority.Testing");
    }

    [Fact]
    public void Guarded_cleanup_exposes_only_the_facade_to_the_host_and_preserves_the_acyclic_graph()
    {
        var outcome = AuthorityTestingBridge.VerifyGuardedCleanupBoundary();
        var root = FindRepositoryRoot();
        var authority = Assembly.Load("AIBar.Packaging.Supervisor.C2Authority");
        var coreProject = File.ReadAllText(Path.Combine(root, "tools", "AIBar.Packaging.Supervisor.Core", "AIBar.Packaging.Supervisor.Core.csproj"));
        var authorityProject = File.ReadAllText(Path.Combine(root, "tools", "AIBar.Packaging.Supervisor.C2Authority", "AIBar.Packaging.Supervisor.C2Authority.csproj"));
        var testingProject = File.ReadAllText(Path.Combine(root, "tests", "AIBar.Packaging.Supervisor.Authority.Testing", "AIBar.Packaging.Supervisor.Authority.Testing.csproj"));
        var domainTestsProject = File.ReadAllText(Path.Combine(root, "tests", "AIBar.Domain.Tests", "AIBar.Domain.Tests.csproj"));
        var supervisorProject = File.ReadAllText(Path.Combine(root, "tools", "AIBar.Packaging.Supervisor", "AIBar.Packaging.Supervisor.csproj"));
        var hostSource = File.ReadAllText(Path.Combine(root, "tools", "AIBar.Packaging.Supervisor", "ProcessSupervisor.cs"));

        Assert.True(outcome.Succeeded);
        Assert.Equal("guarded-cleanup-boundary-verified", outcome.Code);
        Assert.Equal(["GuardedCleanupFacade"], authority.GetExportedTypes().Select(type => type.Name));
        Assert.DoesNotContain("C2Authority", coreProject, StringComparison.Ordinal);
        Assert.Contains("Supervisor.Core", authorityProject, StringComparison.Ordinal);
        Assert.Contains("C2Authority", testingProject, StringComparison.Ordinal);
        Assert.Contains("Authority.Testing", domainTestsProject, StringComparison.Ordinal);
        Assert.DoesNotContain("C2Authority.csproj", domainTestsProject, StringComparison.Ordinal);
        Assert.Contains("AIBar.Packaging.Supervisor.C2Authority.csproj", supervisorProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Authority.Testing", supervisorProject, StringComparison.Ordinal);
        Assert.Contains("GuardedCleanupFacade", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RetainedTreeSession", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RetainedTreeAuthorization", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_descendants_delete_in_post_order_and_faults_retain_the_remaining_quarantine_truthfully()
    {
        var outcome = AuthorityTestingBridge.VerifyGuardedCleanupFaultMatrix();

        Assert.True(outcome.Succeeded, outcome.Code);
        Assert.Equal("guarded-cleanup-fault-matrix-verified", outcome.Code);
    }

    [Fact]
    public void Cleanup_descendant_entry_depth_and_cancellation_bounds_fail_closed_before_deletion()
    {
        var outcome = AuthorityTestingBridge.VerifyGuardedCleanupBounds();

        Assert.True(outcome.Succeeded, outcome.Code);
        Assert.Equal("guarded-cleanup-bounds-verified", outcome.Code);
    }

    [Fact]
    public void Cleanup_metadata_binds_phase_retry_count_and_next_eligible_time_fail_closed()
    {
        var outcome = AuthorityTestingBridge.VerifyGuardedCleanupMetadataBinding();

        Assert.True(outcome.Succeeded, outcome.Code);
        Assert.Equal("guarded-cleanup-metadata-binding-verified", outcome.Code);
    }

    [Fact]
    public void Cleanup_descendant_identity_mismatch_fails_closed_before_deletion()
        => Assert.Equal("guarded-cleanup-descendant-identity-mismatch-verified", AuthorityTestingBridge.VerifyGuardedCleanupDescendantIdentityMismatch().Code);

    [Fact]
    public void Cleanup_descendant_kind_mismatch_fails_closed_before_deletion()
        => Assert.Equal("guarded-cleanup-descendant-kind-mismatch-verified", AuthorityTestingBridge.VerifyGuardedCleanupDescendantKindMismatch().Code);

    [Fact]
    public void Cleanup_descendant_volume_mismatch_fails_closed_before_deletion()
        => Assert.Equal("guarded-cleanup-descendant-volume-mismatch-verified", AuthorityTestingBridge.VerifyGuardedCleanupDescendantVolumeMismatch().Code);

    [Fact]
    public void Cleanup_descendant_reparse_fails_closed_before_deletion()
        => Assert.Equal("guarded-cleanup-descendant-reparse-verified", AuthorityTestingBridge.VerifyGuardedCleanupDescendantReparse().Code);

    [Fact]
    public void Cleanup_descendant_reopen_failure_fails_closed_before_deletion()
        => Assert.Equal("guarded-cleanup-descendant-reopen-failure-verified", AuthorityTestingBridge.VerifyGuardedCleanupDescendantReopenFailure().Code);

    [Fact]
    public void Cleanup_deadline_exhaustion_fails_closed_before_deletion()
        => Assert.Equal("guarded-cleanup-deadline-exhaustion-verified", AuthorityTestingBridge.VerifyGuardedCleanupDeadlineExhaustion().Code);

    [Fact]
    public void Cleanup_native_delete_failure_fails_closed_before_deletion()
        => Assert.Equal("guarded-cleanup-native-delete-failure-verified", AuthorityTestingBridge.VerifyGuardedCleanupNativeDeleteFailure().Code);

    [Fact]
    public void Cleanup_unknown_fault_maps_to_partial_and_retains_quarantine()
        => Assert.Equal("guarded-cleanup-unknown-fault-mapping-verified", AuthorityTestingBridge.VerifyGuardedCleanupUnknownFaultMapping().Code);

    [Fact]
    public void B2_Core_relocation_preserves_the_five_materialized_source_bytes()
    {
        var root = FindRepositoryRoot();
        var expectedHashes = new Dictionary<string, string>
        {
            ["CommittedChildEvidence.cs"] = "eda7c48d12e294296b3f66c7f44388163b7fa4f95df406fdbc1bb12900f6424c",
            ["DirectoryCapability.cs"] = "303e19f37b0f631160f6310a02611805205f7b36d7256c920a35f99cabaa070e",
            ["NativeRenameReadiness.cs"] = "a80699140ee0852586b636091e238b473e37c38c473aa3ce857a6a7c80029446",
            ["WindowsDirectoryCapabilityFileSystem.cs"] = "7e4b7da8a77feaf8c4cce3f6be3ebe9fa5a0cd0c8948f5b984464983fe38bf17",
            ["RetainedTreeReadOnly.cs"] = "8fee7b6444b00c01d66e853cdce731dd65de3bfde681e90ce99623dfca498bda"
        };

        foreach (var (file, expectedHash) in expectedHashes)
        {
            var source = Path.Combine(root, "tools", "AIBar.Packaging.Supervisor", "Core", file);
            var relocated = Path.Combine(root, "tools", "AIBar.Packaging.Supervisor.Core", file);
            Assert.False(File.Exists(source));
            Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(relocated))).ToLowerInvariant());
        }
    }

    [Fact]
    public void Test_project_is_non_packable_non_publishable_and_absent_from_production_references()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "tests", "AIBar.Domain.Tests", "AIBar.Domain.Tests.csproj"));
        var supervisorProject = File.ReadAllText(Path.Combine(root, "tools", "AIBar.Packaging.Supervisor", "AIBar.Packaging.Supervisor.csproj"));
        var coreReferences = Assembly.Load("AIBar.Packaging.Supervisor.Core").GetReferencedAssemblies();
        var authorityReferences = Assembly.Load("AIBar.Packaging.Supervisor.C2Authority").GetReferencedAssemblies();

        Assert.Contains("<IsPackable>false</IsPackable>", project, StringComparison.Ordinal);
        Assert.Contains("<IsPublishable>false</IsPublishable>", project, StringComparison.Ordinal);
        Assert.Contains("RefuseTestDistribution", project, StringComparison.Ordinal);
        Assert.Contains("AIBar.Domain.Tests is test-only and cannot be packaged or published.", project, StringComparison.Ordinal);
        Assert.Contains("AIBar.Packaging.Supervisor.Authority.Testing.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Supervisor.Core", project, StringComparison.Ordinal);
        Assert.Contains("<Compile Remove=\"PackagingSupervisorTests.cs\" />", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Authority.Testing", supervisorProject, StringComparison.Ordinal);
        Assert.DoesNotContain(coreReferences, reference => reference.Name == "AIBar.Domain.Tests");
        Assert.DoesNotContain(authorityReferences, reference => reference.Name == "AIBar.Domain.Tests");
    }

    public static IEnumerable<object[]> InvalidFriendMetadata()
    {
        var authority = $"AIBar.Packaging.Supervisor.C2Authority, PublicKey={PublicKey}";
        var testing = $"AIBar.Packaging.Supervisor.Authority.Testing, PublicKey={PublicKey}";
        yield return [new[] { authority, "AIBar.Packaging.Supervisor.Authority.Testing" }];
        yield return [new[] { authority, "AIBar.Packaging.Supervisor.Authority.Testing, PublicKeyToken=0540e36870da7bad" }];
        yield return [new[] { authority, $"AIBar.Packaging.Supervisor.Authority.Testing, PublicKey={PublicKey[..^1]}F" }];
        yield return [new[] { authority, testing, testing }];
        yield return [new[] { authority, testing, $"AIBar.Unapproved, PublicKey={PublicKey}" }];
        yield return [new[] { authority, $"AIBar.Packaging.Supervisor, PublicKey={PublicKey}" }];
        yield return [new[] { authority, $"AIBar.Domain.Tests, PublicKey={PublicKey}" }];
        yield return [new[] { $"AIBar.Packaging.Supervisor.C2Authority.Renamed, PublicKey={PublicKey}", testing }];
    }

    [Fact]
    public void Domain_tests_consume_only_bounded_authority_testing_outcomes()
    {
        Assert.True(AuthorityTestingBridge.VerifyC1cOwnership().Succeeded);
        Assert.True(AuthorityTestingBridge.VerifyC2aAuthorization().Succeeded);
        Assert.DoesNotContain(typeof(AuthorityTestingBridge).GetMethods(), method => method.ReturnType.Namespace == "AIBar.Packaging.Supervisor");
    }

    private static bool HasExactlyExpectedFriends(IEnumerable<string> friends, params string[] expectedNames)
    {
        var parsed = friends.Select(friend => new AssemblyName(friend)).ToArray();
        var expected = new HashSet<string>(expectedNames, StringComparer.Ordinal);
        return parsed.Length == expected.Count
            && parsed.All(friend => friend.GetPublicKey() is { Length: > 0 } key && Convert.ToHexString(key) == PublicKey)
            && parsed.Select(friend => friend.Name).All(name => name is not null && expected.Contains(name))
            && parsed.Select(friend => friend.Name).Distinct(StringComparer.Ordinal).Count() == expected.Count;
    }

    private static void AssertSignedIdentity(Assembly assembly, string expectedName)
    {
        Assert.Equal(expectedName, assembly.GetName().Name);
        Assert.Equal(PublicKey, Convert.ToHexString(assembly.GetName().GetPublicKey()!));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AIBar.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
