using System.Diagnostics;
using System.Reflection;
using AIBar.Application;
using Forms = System.Windows.Forms;

namespace AIBar.Desktop;

/// <summary>Uses the same native task-dialog surface as startup offers. Metadata only.</summary>
internal sealed class UpdateNotice(ConsentSettings settings) : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly UpdateCheckClient _client = new();
    private Task? _loop;
    public void Start() => _loop ??= RunAsync();
    private async Task RunAsync()
    {
        var token = _stop.Token;
        var version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            while (!token.IsCancellationRequested)
            {
                var release = await _client.CheckAsync(settings, version, token);
                // Re-read before showing: a setting changed during an in-flight request wins.
                if (release is not null && !token.IsCancellationRequested && await settings.LoadUpdateCheckAsync(token)) Show(release);
                await Task.Delay(TimeSpan.FromDays(1), token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Update checks never interrupt the app or report user data. */ }
    }
    internal static void Show(UpdateRelease release)
    {
        if (!UpdateCheckClient.IsAllowedUrl(release.Url)) return;
        var open = new Forms.TaskDialogButton("Open download page");
        var close = new Forms.TaskDialogButton("Close");
        var page = new Forms.TaskDialogPage
        {
            Caption = "AIBar", Heading = $"AIBar {release.Tag} is available",
            Text = "View the release on GitHub to download it yourself. AIBar never downloads or installs updates.",
            Icon = Forms.TaskDialogIcon.Information, Buttons = { open, close }, DefaultButton = close,
            AllowCancel = true
        };
        if (Forms.TaskDialog.ShowDialog(page, Forms.TaskDialogStartupLocation.CenterScreen) != open) return;
        try { Process.Start(new ProcessStartInfo(release.Url) { UseShellExecute = true }); }
        catch (Exception) { /* A browser launch failure is silent as well. */ }
    }
    public void Dispose() { _stop.Cancel(); _client.Dispose(); }
}
