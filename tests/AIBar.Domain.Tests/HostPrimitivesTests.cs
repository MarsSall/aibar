using System.Diagnostics;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class HostPrimitivesTests
{
    [Fact]
    public void Named_mutex_elects_one_owner_and_secondary_handoff_is_idempotent()
    {
        var name = $"AIBar.Tests.{Guid.NewGuid():N}";
        using var primary = new SingleInstanceHost(name);
        using var secondary = new SingleInstanceHost(name);
        var activations = 0;
        primary.ActivationRequested += () => activations++;

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        Assert.True(secondary.RequestActivation());
        Assert.True(secondary.RequestActivation());

        primary.DispatchPendingActivation();
        primary.DispatchPendingActivation();

        Assert.Equal(1, activations);

        primary.Dispose();
        primary.Dispose();
        using var takeover = new SingleInstanceHost(name);
        Assert.True(takeover.IsPrimary);
    }

    [Fact]
    public async Task Named_mutex_supports_cross_process_handoff_and_abandoned_owner_takeover()
    {
        var name = $"AIBar.Tests.{Guid.NewGuid():N}";
        using var ready = OpenEvent(name, "ready"); using var dispatch = OpenEvent(name, "dispatch");
        using var activated = OpenEvent(name, "activated"); using var release = OpenEvent(name, "release");
        using var owner = StartChild(name);
        Assert.True(ready.WaitOne(TimeSpan.FromSeconds(10)));
        using var secondary = new SingleInstanceHost(name);
        Assert.False(secondary.IsPrimary);
        Assert.True(secondary.RequestActivation());
        dispatch.Set();
        Assert.True(activated.WaitOne(TimeSpan.FromSeconds(5)));
        release.Set();
        await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, owner.ExitCode);
        using var takeover = new SingleInstanceHost(name);
        Assert.True(takeover.IsPrimary);

        var abandonedName = $"AIBar.Tests.{Guid.NewGuid():N}";
        using var abandonedReady = OpenEvent(abandonedName, "ready"); using var abandoned = StartChild(abandonedName);
        Assert.True(abandonedReady.WaitOne(TimeSpan.FromSeconds(10)));
        abandoned.Kill(true);
        await abandoned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var recovered = new SingleInstanceHost(abandonedName);
        Assert.True(recovered.IsPrimary);
    }

    [Fact]
    public void CrossProcessMutexOwner()
    {
        var name = Environment.GetEnvironmentVariable("AIBAR_MUTEX_TEST_CHILD");
        if (name is null) return;
        using var host = new SingleInstanceHost(name);
        var wasActivated = false;
        host.ActivationRequested += () => wasActivated = true;
        using var ready = OpenEvent(name, "ready"); using var dispatch = OpenEvent(name, "dispatch");
        using var activated = OpenEvent(name, "activated"); using var release = OpenEvent(name, "release");
        ready.Set();
        Assert.True(dispatch.WaitOne(TimeSpan.FromSeconds(10)));
        host.DispatchPendingActivation();
        Assert.True(wasActivated);
        activated.Set();
        Assert.True(release.WaitOne(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Show_activation_parser_accepts_only_the_exact_payload_free_token()
    {
        Assert.Equal(StartupIntent.Ordinary, StartupIntentParser.Parse([]));
        Assert.Equal(StartupIntent.Show, StartupIntentParser.Parse(["--show"]));

        foreach (var arguments in new[]
        {
            new[] { "--SHOW" }, new[] { "--show=value" }, new[] { "--sho" },
            new[] { "--show", "payload" }, new[] { "https://example.test" },
            new[] { "C:\\payload.json" }, new[] { "--unknown" }
        })
            Assert.Equal(StartupIntent.Ordinary, StartupIntentParser.Parse(arguments));
    }

    [Fact]
    public void Show_activation_preserves_the_fixed_auto_reset_single_instance_handoff()
    {
        var name = $"AIBar.Tests.{Guid.NewGuid():N}";
        using var primary = new SingleInstanceHost(name);
        using var secondary = new SingleInstanceHost(name);
        var activations = 0;
        primary.ActivationRequested += () => activations++;

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        Assert.True(secondary.RequestActivation());
        Assert.True(secondary.RequestActivation());
        primary.DispatchPendingActivation();
        primary.DispatchPendingActivation();

        Assert.Equal(1, activations);
    }

    [Fact]
    public void Placement_uses_taskbar_work_area_and_clamps_to_monitor_edges()
    {
        var result = PopoverPlacement.Place(new(
            new ScreenRect(0, 40, 1920, 1040), new ScreenRect(0, 0, 1920, 1080), 400, 500, 1));

        Assert.Equal(new PopoverBounds(1520, 580, 400, 500), result);

        var clipped = PopoverPlacement.Place(new(
            new ScreenRect(100, 0, 300, 200), new ScreenRect(0, 0, 400, 300), 500, 300, 1));

        Assert.Equal(new PopoverBounds(100, 0, 300, 200), clipped);
    }

    [Theory]
    [InlineData(1.25, 880, 500)]
    [InlineData(2, 400, 200)]
    public void Placement_scales_logical_size_and_rejects_invalid_inputs(double scale, double expectedX, double expectedY)
    {
        var result = PopoverPlacement.Place(new(
            new ScreenRect(0, 0, 1600, 1000), new ScreenRect(0, 0, 1600, 1080), 400, 300, scale));

        Assert.Equal(expectedX, result.Left);
        Assert.Equal(expectedY, result.Top);
        Assert.Throws<ArgumentOutOfRangeException>(() => PopoverPlacement.Place(new(
            new ScreenRect(0, 0, 1, 1), new ScreenRect(0, 0, 1, 1), 1, 1, 0)));
    }

    [Fact]
    public void Placement_rejects_non_finite_non_positive_and_overflowing_inputs()
    {
        var valid = new PopoverPlacementInput(new(0, 0, 1600, 1000), new(0, 0, 1600, 1080), 400, 300, 1);
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            PopoverPlacementInput[] malformed =
            [
                valid with { WorkArea = valid.WorkArea with { Left = bad } }, valid with { WorkArea = valid.WorkArea with { Top = bad } },
                valid with { WorkArea = valid.WorkArea with { Width = bad } }, valid with { WorkArea = valid.WorkArea with { Height = bad } },
                valid with { MonitorBounds = valid.MonitorBounds with { Left = bad } }, valid with { MonitorBounds = valid.MonitorBounds with { Top = bad } },
                valid with { MonitorBounds = valid.MonitorBounds with { Width = bad } }, valid with { MonitorBounds = valid.MonitorBounds with { Height = bad } },
                valid with { LogicalWidth = bad }, valid with { LogicalHeight = bad }, valid with { DpiScale = bad }
            ];
            Assert.All(malformed, input => Assert.Throws<ArgumentOutOfRangeException>(() => PopoverPlacement.Place(input)));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => PopoverPlacement.Place(valid with { MonitorBounds = new(0, 0, 0, 1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PopoverPlacement.Place(valid with { LogicalWidth = double.MaxValue, DpiScale = 2 }));
    }

    private static Process StartChild(string name)
    {
        var project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../..", "AIBar.Domain.Tests.csproj"));
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "test", project, "--no-build", "--filter", "FullyQualifiedName~CrossProcessMutexOwner" })
            start.ArgumentList.Add(argument);
        start.Environment["AIBAR_MUTEX_TEST_CHILD"] = name;
        return Process.Start(start)!;
    }

    private static EventWaitHandle OpenEvent(string name, string suffix) =>
        new(false, EventResetMode.ManualReset, $"{name}.{suffix}");
}
