using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class ForegroundActivationTests
{
    private const string Executable = @"C:\AIBar\AIBar.Desktop.exe";
    private static readonly ForegroundTarget Primary = new(42, 200, 1, Executable);

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Secondary_grants_once_before_signalling_and_preserves_signal_result(bool granted, bool signalled)
    {
        var calls = new List<string>();
        var result = ForegroundActivation.Request(
            () => { calls.Add("grant"); return granted; },
            () => { calls.Add("signal"); return signalled; });
        Assert.Equal(signalled, result);
        Assert.Equal(new[] { "grant", "signal" }, calls);
    }

    [Fact]
    public void Discovery_failure_still_signals_once()
    {
        var grants = 0;
        var signals = 0;
        Assert.True(ForegroundActivation.Request(
            () => { grants++; throw new InvalidOperationException("discovery unavailable"); },
            () => { signals++; return true; }));
        Assert.Equal(1, grants);
        Assert.Equal(1, signals);
    }

    [Fact]
    public void Signal_failure_is_not_retried_or_hidden()
    {
        var signals = 0;
        var error = new InvalidOperationException("signal failed");
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => ForegroundActivation.Request(
            () => true, () => { signals++; throw error; })));
        Assert.Equal(1, signals);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Only_unique_same_executable_same_session_target_is_granted(bool allowed)
    {
        var targets = new List<ForegroundTarget>();
        Assert.Equal(allowed, ForegroundActivation.TryGrant(
            [Primary with { ExecutablePath = Executable.ToUpperInvariant() }], 100, 1, Executable,
            target => { targets.Add(target); return allowed; }));
        Assert.Single(targets);
        Assert.Equal(Primary.Window, targets[0].Window);
        Assert.Equal(Primary.ProcessId, targets[0].ProcessId);
    }

    [Fact]
    public void Missing_mismatched_self_and_ambiguous_targets_never_receive_permission()
    {
        ForegroundTarget[][] rejected =
        [
            [],
            [Primary with { Window = 0 }],
            [Primary with { ProcessId = 0 }],
            [Primary with { ProcessId = 100 }],
            [Primary with { SessionId = 2 }],
            [Primary with { ExecutablePath = @"C:\Other\AIBar.Desktop.exe" }],
            [Primary, Primary with { Window = 43, ProcessId = 201 }]
        ];
        var grants = 0;
        foreach (var candidates in rejected)
            Assert.False(ForegroundActivation.TryGrant(candidates, 100, 1, Executable,
                _ => { grants++; return true; }));
        Assert.Equal(0, grants);
    }
}
