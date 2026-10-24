using System.Text.Json;
using AIBar.Application;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class StartupOffersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-onboarding-{Guid.NewGuid():N}");
    private string OnboardingPath => Path.Combine(_root, OnboardingSettings.FileName);

    [Fact]
    public async Task Onboarding_defaults_fail_closed_roundtrip_and_clear_removes_the_file()
    {
        var settings = new OnboardingSettings(OnboardingPath);
        Assert.Equal(new OnboardingState(), await settings.LoadAsync(default));
        Directory.CreateDirectory(_root);
        foreach (var json in new[] { "{", "{\"schema\":2,\"fontOfferDeclined\":true}", "{\"schema\":1,\"dockOfferAnswered\":\"yes\"}" })
        {
            await File.WriteAllTextAsync(OnboardingPath, json);
            Assert.Equal(new OnboardingState(), await settings.LoadAsync(default));
        }
        var state = new OnboardingState(FontOfferDeclined: true, DockOfferAnswered: true);
        await settings.SaveAsync(state, default);
        Assert.Equal(state, await settings.LoadAsync(default));
        using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(OnboardingPath)))
            Assert.Equal(new[] { "schema", "fontOfferDeclined", "dockOfferAnswered" }, json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        await File.WriteAllTextAsync(Path.Combine(_root, "dock.json"), "{}");
        await new ClearAiBarDataService(_root, [settings], _ => ValueTask.CompletedTask).ClearAsync(default);
        Assert.False(File.Exists(OnboardingPath));
        Assert.False(File.Exists(Path.Combine(_root, "dock.json")));
        Assert.Equal(new OnboardingState(), await settings.LoadAsync(default));
    }

    [Theory]
    // YASB present: only the font offer, and only when it is missing, bundled and not declined.
    [InlineData(true, false, true, false, false, false, StartupOffer.InstallFont)]
    [InlineData(true, true, true, false, false, false, StartupOffer.None)]
    [InlineData(true, false, false, false, false, false, StartupOffer.None)]
    [InlineData(true, false, true, false, true, false, StartupOffer.None)]
    [InlineData(true, false, true, false, false, true, StartupOffer.InstallFont)]
    // YASB absent: only the widget offer, and only when it is off and was never answered.
    [InlineData(false, false, true, false, false, false, StartupOffer.EnableDock)]
    [InlineData(false, false, true, true, false, false, StartupOffer.None)]
    [InlineData(false, false, true, false, false, true, StartupOffer.None)]
    [InlineData(false, false, true, false, true, false, StartupOffer.EnableDock)]
    public void Policy_offers_font_to_yasb_users_and_widget_to_everyone_else(bool yasb, bool fontInstalled, bool bundled, bool dockEnabled, bool fontDeclined, bool dockAnswered, object expected) =>
        Assert.Equal(Assert.IsType<StartupOffer>(expected), StartupOfferPolicy.Decide(yasb, fontInstalled, bundled, dockEnabled, new(fontDeclined, dockAnswered)));

    [Fact]
    public void Font_not_now_asks_again_while_dont_ask_again_and_any_widget_answer_are_remembered()
    {
        var fresh = new OnboardingState();
        Assert.Equal(fresh, StartupOfferPolicy.Record(StartupOffer.InstallFont, StartupOfferAnswer.NotNow, fresh));
        Assert.Equal(fresh, StartupOfferPolicy.Record(StartupOffer.InstallFont, StartupOfferAnswer.Accept, fresh));
        Assert.Equal(new OnboardingState(FontOfferDeclined: true), StartupOfferPolicy.Record(StartupOffer.InstallFont, StartupOfferAnswer.Decline, fresh));
        foreach (var answer in new[] { StartupOfferAnswer.Accept, StartupOfferAnswer.NotNow, StartupOfferAnswer.Decline })
            Assert.Equal(new OnboardingState(DockOfferAnswered: true), StartupOfferPolicy.Record(StartupOffer.EnableDock, answer, fresh));
    }

    [Theory]
    [InlineData(GlyphFontInstallStatus.Installed, null, null)]
    [InlineData(GlyphFontInstallStatus.AlreadyInstalled, null, null)]
    [InlineData(GlyphFontInstallStatus.SourceMissing, null, "The bundled font file is missing from AIBar's fonts folder.")]
    [InlineData(GlyphFontInstallStatus.SourceMismatch, null, "The bundled font file does not match the expected version.")]
    [InlineData(GlyphFontInstallStatus.Failed, "IOException", "Windows refused the installation (IOException).")]
    public void Font_results_have_shared_user_facing_messages(object status, string? exception, string? expected) =>
        Assert.Equal(expected, GlyphFontMessages.For(new(Assert.IsType<GlyphFontInstallStatus>(status), exception)));

    [Fact]
    public void Yasb_is_detected_by_config_folder_or_running_process()
    {
        var profile = Path.Combine("C:", "Users", "someone");
        var expected = Path.Combine(profile, ".config", "yasb");
        Assert.True(YasbPresence.IsDetected(profile, path => path == expected, () => false));
        Assert.True(YasbPresence.IsDetected(profile, _ => false, () => true));
        Assert.False(YasbPresence.IsDetected(profile, _ => false, () => false));
    }

    [Fact]
    public async Task Accepting_the_widget_offer_enables_it_and_is_never_asked_again()
    {
        var prompt = new FakePrompt(StartupOfferAnswer.Accept);
        var enabled = 0;
        var offers = Create(prompt, yasb: false, enableDock: () => enabled++);
        Assert.Equal(StartupOffer.EnableDock, await offers.RunAsync(default));
        Assert.Equal(1, enabled);
        Assert.Equal(new OnboardingState(DockOfferAnswered: true), await new OnboardingSettings(OnboardingPath).LoadAsync(default));
        Assert.Equal(StartupOffer.None, await Create(prompt, yasb: false).RunAsync(default));
        Assert.Equal(new[] { StartupOffer.EnableDock }, prompt.Asked);
    }

    [Fact]
    public async Task Declining_the_widget_offer_leaves_it_off_and_is_remembered()
    {
        var enabled = 0;
        Assert.Equal(StartupOffer.EnableDock, await Create(new FakePrompt(StartupOfferAnswer.NotNow), yasb: false, enableDock: () => enabled++).RunAsync(default));
        Assert.Equal(0, enabled);
        Assert.Equal(new OnboardingState(DockOfferAnswered: true), await new OnboardingSettings(OnboardingPath).LoadAsync(default));
    }

    [Fact]
    public async Task Accepting_the_font_offer_installs_and_reports_success_or_the_failure()
    {
        var installs = 0;
        var prompt = new FakePrompt(StartupOfferAnswer.Accept);
        Assert.Equal(StartupOffer.InstallFont, await Create(prompt, yasb: true, installFont: () => { installs++; return null; }).RunAsync(default));
        Assert.Equal(1, installs);
        Assert.Equal("IosevkaTerm Nerd Font installed", Assert.Single(prompt.Reports).Heading);
        Assert.False(File.Exists(OnboardingPath));

        var failing = new FakePrompt(StartupOfferAnswer.Accept);
        await Create(failing, yasb: true, installFont: () => "The bundled font file is missing.").RunAsync(default);
        Assert.Equal(("The font was not installed", "The bundled font file is missing."), Assert.Single(failing.Reports));
    }

    [Fact]
    public async Task Font_not_now_installs_nothing_and_asks_again_next_start_while_dont_ask_again_stops_it()
    {
        var installs = 0;
        var notNow = new FakePrompt(StartupOfferAnswer.NotNow);
        await Create(notNow, yasb: true, installFont: () => { installs++; return null; }).RunAsync(default);
        await Create(notNow, yasb: true, installFont: () => { installs++; return null; }).RunAsync(default);
        Assert.Equal(2, notNow.Asked.Count);
        Assert.Equal(0, installs);
        Assert.False(File.Exists(OnboardingPath));

        var decline = new FakePrompt(StartupOfferAnswer.Decline);
        await Create(decline, yasb: true).RunAsync(default);
        Assert.Equal(StartupOffer.None, await Create(decline, yasb: true).RunAsync(default));
        Assert.Single(decline.Asked);
        Assert.Empty(decline.Reports);
    }

    [Fact]
    public async Task Failures_are_reported_and_never_escape_startup()
    {
        var errors = new List<Exception>();
        var offers = new StartupOffers(() => throw new InvalidOperationException("probe"), () => false, () => true, _ => ValueTask.FromResult(false),
            new OnboardingSettings(OnboardingPath), new FakePrompt(StartupOfferAnswer.Accept), () => null, _ => ValueTask.CompletedTask, errors.Add);
        Assert.Equal(StartupOffer.None, await offers.RunAsync(default));
        Assert.IsType<InvalidOperationException>(Assert.Single(errors));
    }

    private StartupOffers Create(FakePrompt prompt, bool yasb, Action? enableDock = null, Func<string?>? installFont = null) =>
        new(() => yasb, () => false, () => true, _ => ValueTask.FromResult(false), new OnboardingSettings(OnboardingPath), prompt,
            installFont ?? (() => null), _ => { enableDock?.Invoke(); return ValueTask.CompletedTask; }, error => throw error);

    private sealed class FakePrompt(StartupOfferAnswer answer) : IStartupOfferPrompt
    {
        public List<StartupOffer> Asked { get; } = [];
        public List<(string Heading, string Text)> Reports { get; } = [];
        public StartupOfferAnswer Ask(StartupOffer offer) { Asked.Add(offer); return answer; }
        public void Report(string heading, string text) => Reports.Add((heading, text));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
