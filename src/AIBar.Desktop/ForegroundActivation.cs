using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AIBar.Desktop;

internal readonly record struct ForegroundTarget(nint Window, int ProcessId, int SessionId, string ExecutablePath);

internal static class ForegroundActivation
{
    internal const string WindowTitle = "AIBar quota";

    internal static bool Request(Func<bool> grant, Func<bool> signal)
    {
        try { Trace.TraceInformation("AIBar foreground grant: {0}", grant()); }
        catch (Exception exception) { Trace.TraceWarning("AIBar foreground grant unavailable: {0}", exception.GetType().Name); }
        // Grant failure must not suppress the existing show request or change election.
        return signal();
    }

    internal static bool TryGrant(IEnumerable<ForegroundTarget> candidates, int callerProcessId,
        int sessionId, string executablePath, Func<ForegroundTarget, bool> grant)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        var matches = candidates.Where(target => target.Window != 0 && target.ProcessId > 0 &&
            target.ProcessId != callerProcessId && target.SessionId == sessionId &&
            string.Equals(target.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length == 1 && grant(matches[0]);
    }

    internal static bool TryGrantToPrimary()
    {
        using var caller = Process.GetCurrentProcess();
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return false;
        var targets = new List<ForegroundTarget>();
        var discoveryFailed = false;
        // EnumWindows includes hidden windows; Process.MainWindowHandle does not reliably do so.
        var enumerated = EnumWindows((window, _) =>
        {
            try
            {
                if (!HasExpectedTitle(window)) return true;
                if (GetWindowThreadProcessId(window, out var pid) == 0 || pid == 0 || pid > int.MaxValue)
                { discoveryFailed = true; return false; }
                using var process = Process.GetProcessById((int)pid);
                targets.Add(new(window, process.Id, process.SessionId, process.MainModule?.FileName ?? ""));
                return true;
            }
            catch { discoveryFailed = true; return false; }
        }, 0);
        if (!enumerated || discoveryFailed) return false;
        return TryGrant(targets, caller.Id, caller.SessionId, executable, target =>
        {
            using var process = Process.GetProcessById(target.ProcessId);
            // Retain a process handle and revalidate the HWND-derived identity before granting.
            _ = process.Handle;
            if (process.HasExited || process.SessionId != target.SessionId ||
                !string.Equals(process.MainModule?.FileName, target.ExecutablePath, StringComparison.OrdinalIgnoreCase) ||
                !HasExpectedTitle(target.Window) || GetWindowThreadProcessId(target.Window, out var currentPid) == 0 ||
                currentPid != (uint)process.Id) return false;
            var allowed = AllowSetForegroundWindow((uint)process.Id);
            Trace.TraceInformation("AIBar foreground target: {0}; granted: {1}", process.Id, allowed);
            return allowed;
        });
    }

    internal static uint ForegroundProcessId()
    {
        _ = GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
        return processId;
    }

    private static bool HasExpectedTitle(nint window)
    {
        var title = new StringBuilder(WindowTitle.Length + 2);
        return GetWindowText(window, title, title.Capacity) == WindowTitle.Length && title.ToString() == WindowTitle;
    }

    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int maximum);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
