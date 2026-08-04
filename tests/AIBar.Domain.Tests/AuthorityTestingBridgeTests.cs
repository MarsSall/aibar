using AIBar.Packaging.Supervisor.Authority.Testing;

namespace AIBar.Domain.Tests;

public sealed class AuthorityTestingBridgeTests
{
    [Fact]
    public void C1c_and_C2a_internal_seams_are_available_only_as_bounded_outcomes()
    {
        Assert.True(AuthorityTestingBridge.VerifyC1cOwnership().Succeeded);
        Assert.True(AuthorityTestingBridge.VerifyC2aAuthorization().Succeeded);
        Assert.True(AuthorityTestingBridge.VerifyBoundedRuntime().Succeeded);
    }
}
