using System.Diagnostics;
using System.IO;
using AIBar.Application;
using Forms = System.Windows.Forms;

namespace AIBar.Desktop;

/// <summary>Small replaceable font facade shared by Settings and startup offers.</summary>
public sealed record SettingsGlyphFont(Func<bool> IsInstalled, Func<bool> IsBundledFontAvailable, Func<string?> Install)
{
    internal static SettingsGlyphFont Create()
    {
        var installer = new GlyphFontInstaller();
        return new(installer.IsInstalled, () => installer.IsBundledFontAvailable(AppContext.BaseDirectory),
            () => GlyphFontMessages.For(installer.Install(GlyphFontInstaller.BundledFontPath(AppContext.BaseDirectory))));
    }
}

internal static class GlyphFontMessages
{
    internal static string? For(GlyphFontInstallResult result) => result.Status switch
    {
        GlyphFontInstallStatus.Installed or GlyphFontInstallStatus.AlreadyInstalled => null,
        GlyphFontInstallStatus.SourceMissing => "The bundled font file is missing from AIBar's fonts folder.",
        GlyphFontInstallStatus.SourceMismatch => "The bundled font file does not match the expected version.",
        _ => $"Windows refused the installation ({result.ExceptionType ?? "Unknown"})."
    };
}

internal enum StartupOffer { None, InstallFont, EnableDock }

internal enum StartupOfferAnswer { Accept, NotNow, Decline }

/// <summary>Shows a one-time offer and returns the user's answer. Tests replace the native dialog.</summary>
internal interface IStartupOfferPrompt
{
    StartupOfferAnswer Ask(StartupOffer offer);
    void Report(string heading, string text);
}

/// <summary>YASB counts as present when its per-user configuration folder exists or a yasb process is running.</summary>
internal static class YasbPresence
{
    internal static string ConfigDirectory(string userProfile) => Path.Combine(userProfile, ".config", "yasb");

    internal static bool IsDetected(string userProfile, Func<string, bool>? directoryExists = null, Func<bool>? processRunning = null) =>
        (directoryExists ?? Directory.Exists)(ConfigDirectory(userProfile)) || (processRunning ?? IsProcessRunning)();

    private static bool IsProcessRunning()
    {
        var processes = Process.GetProcessesByName("yasb");
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }
}

/// <summary>Picks at most one startup offer. YASB users may get the glyph font; everyone else may get the floating widget.</summary>
internal static class StartupOfferPolicy
{
    internal static StartupOffer Decide(bool yasbDetected, bool fontInstalled, bool bundledFontAvailable, bool dockEnabled, OnboardingState state) =>
        yasbDetected
            ? !fontInstalled && bundledFontAvailable && !state.FontOfferDeclined ? StartupOffer.InstallFont : StartupOffer.None
            : !dockEnabled && !state.DockOfferAnswered ? StartupOffer.EnableDock : StartupOffer.None;

    /// <summary>"Not now" on the font asks again next start; the widget offer is asked once whatever the answer.</summary>
    internal static OnboardingState Record(StartupOffer offer, StartupOfferAnswer answer, OnboardingState state) => offer switch
    {
        StartupOffer.InstallFont when answer == StartupOfferAnswer.Decline => state with { FontOfferDeclined = true },
        StartupOffer.EnableDock => state with { DockOfferAnswered = true },
        _ => state
    };
}

/// <summary>Runs the startup offer once per launch. Failures are reported, never thrown into startup.</summary>
internal sealed class StartupOffers(
    Func<bool> yasbDetected,
    Func<bool> fontInstalled,
    Func<bool> bundledFontAvailable,
    Func<CancellationToken, ValueTask<bool>> dockEnabled,
    OnboardingSettings onboarding,
    IStartupOfferPrompt prompt,
    Func<string?> installFont,
    Func<CancellationToken, ValueTask> enableDock,
    Action<Exception> report)
{
    internal async Task<StartupOffer> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = await onboarding.LoadAsync(cancellationToken);
            var yasb = yasbDetected();
            var offer = StartupOfferPolicy.Decide(yasb, yasb && fontInstalled(), yasb && bundledFontAvailable(),
                !yasb && await dockEnabled(cancellationToken), state);
            if (offer == StartupOffer.None) return offer;
            var answer = prompt.Ask(offer);
            var recorded = StartupOfferPolicy.Record(offer, answer, state);
            if (recorded != state) await onboarding.SaveAsync(recorded, cancellationToken);
            if (answer != StartupOfferAnswer.Accept) return offer;
            if (offer == StartupOffer.EnableDock) await enableDock(cancellationToken);
            else if (installFont() is { } failure) prompt.Report("The font was not installed", failure);
            else prompt.Report("IosevkaTerm Nerd Font installed", "Restart YASB so its widgets pick up the new font.");
            return offer;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            report(error);
            return StartupOffer.None;
        }
    }
}

/// <summary>Native Windows task dialogs for the startup offers.</summary>
internal sealed class WindowsStartupOfferPrompt : IStartupOfferPrompt
{
    public StartupOfferAnswer Ask(StartupOffer offer)
    {
        var accept = new Forms.TaskDialogButton(offer == StartupOffer.InstallFont ? "Install font" : "Show widget");
        var notNow = new Forms.TaskDialogButton(offer == StartupOffer.InstallFont ? "Not now" : "No thanks");
        var page = offer == StartupOffer.InstallFont
            ? new Forms.TaskDialogPage
            {
                Caption = "AIBar", Icon = Forms.TaskDialogIcon.Information,
                Heading = "Install the font for the YASB widget?",
                Text = "AIBar's YASB widget draws its progress ring with IosevkaTerm Nerd Font, which is not installed. AIBar can install the bundled copy for your user only (no administrator rights, no download). License: fonts\\IosevkaTerm-LICENSE.md.",
                Buttons = { accept, notNow },
                Verification = new Forms.TaskDialogVerificationCheckBox("Don't ask again")
            }
            : new Forms.TaskDialogPage
            {
                Caption = "AIBar", Icon = Forms.TaskDialogIcon.Information,
                Heading = "Show the floating quota widget?",
                Text = "YASB was not found. AIBar can show a small always-on-top strip with your Codex and Claude quota rings. Drag it to any screen edge. You can turn it on or off later in Settings.",
                Buttons = { accept, notNow }
            };
        var chosen = Forms.TaskDialog.ShowDialog(page, Forms.TaskDialogStartupLocation.CenterScreen);
        if (chosen == accept) return StartupOfferAnswer.Accept;
        return page.Verification is { Checked: true } ? StartupOfferAnswer.Decline : StartupOfferAnswer.NotNow;
    }

    public void Report(string heading, string text) =>
        Forms.TaskDialog.ShowDialog(new Forms.TaskDialogPage { Caption = "AIBar", Heading = heading, Text = text, Icon = Forms.TaskDialogIcon.Information }, Forms.TaskDialogStartupLocation.CenterScreen);
}
