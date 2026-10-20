using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class EdgeGlowPolicyTests
{
    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, false, true, false, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, false, true, true, true)]
    public void Accessibility_controls_visibility_and_animation(bool enabled, bool highContrast, bool animations, bool visible, bool animate)
    {
        var plan = EdgeGlowPolicy.Plan(enabled, highContrast, animations);
        Assert.Equal(visible, plan.Visible);
        Assert.Equal(animate, plan.Animate);
        // S10 replaces HC suppression with system Highlight and reduced-motion static + fade.
        Assert.Equal(TimeSpan.FromMilliseconds(animations ? 3000 : 1750), plan.Duration);
    }
}
