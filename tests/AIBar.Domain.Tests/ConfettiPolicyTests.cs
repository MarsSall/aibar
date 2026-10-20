using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class ConfettiPolicyTests
{
    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    // S10 permits system-color pieces in HC; reduced motion still suppresses all particles.
    public void Reduced_motion_disables_effect_while_high_contrast_uses_system_colors(bool enabled, bool highContrast, bool animations, bool visible)
    {
        var plan = ConfettiPolicy.Plan(enabled, highContrast, animations);
        Assert.Equal(visible, plan.Visible); Assert.Equal(TimeSpan.FromSeconds(2.5), plan.Duration);
    }
}
