using AIBar.Application;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

/// <summary>The Settings colour choice drives the panel bar colour mode.</summary>
public sealed class AppearanceBindingTests
{
    [Theory]
    [InlineData("usage", PanelBarColorMode.Usage)]
    [InlineData("brand", PanelBarColorMode.Brand)]
    [InlineData("unexpected", PanelBarColorMode.Brand)]
    public void Bar_colour_setting_maps_to_the_panel_mode(string barColors, PanelBarColorMode expected) =>
        Assert.Equal(expected, App.ToBarColorMode(new AppearanceOptions(BarColors: barColors)));
}
