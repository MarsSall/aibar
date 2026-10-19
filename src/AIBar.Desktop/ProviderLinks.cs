using System.ComponentModel;
using System.Diagnostics;
using AIBar.Domain;

namespace AIBar.Desktop;

/// <summary>No caller-supplied URLs: only these official destinations can reach the shell.</summary>
public static class ProviderLinks
{
    public static void OpenUsage(StatusProvider provider, Action<ProcessStartInfo>? launch = null) => Open(provider switch
    {
        StatusProvider.Codex => "https://chatgpt.com/codex/settings/usage",
        StatusProvider.Claude => "https://claude.ai/settings/usage",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    }, launch);
    public static void OpenStatus(StatusProvider provider, Action<ProcessStartInfo>? launch = null) => Open(provider switch
    {
        StatusProvider.Codex => "https://status.openai.com",
        StatusProvider.Claude => "https://status.claude.com",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    }, launch);
    private static void Open(string url, Action<ProcessStartInfo>? launch)
    {
        try
        {
            var start = new ProcessStartInfo(url) { UseShellExecute = true };
            if (launch is null) Process.Start(start); else launch(start);
        }
        catch (Win32Exception) { }
        catch (InvalidOperationException) { }
    }
}
