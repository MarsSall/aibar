using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class ConfettiPolicyTests
{
    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    public void Accessibility_disables_the_entire_effect(bool enabled, bool highContrast, bool animations, bool visible)
    {
        var plan = ConfettiPolicy.Plan(enabled, highContrast, animations);
        Assert.Equal(visible, plan.Visible); Assert.Equal(TimeSpan.FromSeconds(2.5), plan.Duration);
    }
}
