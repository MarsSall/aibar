using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class EdgeGlowPolicyTests
{
    [Theory]
    [InlineData(true, true, true, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, false, true, true, true)]
    public void Accessibility_controls_visibility_and_animation(bool enabled, bool highContrast, bool animations, bool visible, bool animate)
    {
        var plan = EdgeGlowPolicy.Plan(enabled, highContrast, animations);
        Assert.Equal(visible, plan.Visible);
        Assert.Equal(animate, plan.Animate);
        Assert.Equal(TimeSpan.FromSeconds(3), plan.Duration);
    }
}
