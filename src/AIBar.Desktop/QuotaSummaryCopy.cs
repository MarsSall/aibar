using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace AIBar.Desktop;

public sealed class QuotaSummaryCopyCommand : ICommand, INotifyPropertyChanged
{
    private readonly Func<BetaPresentationState?> _state;
    private readonly Action<string> _writeText;
    private string _feedback = string.Empty;

    public QuotaSummaryCopyCommand(Func<BetaPresentationState?> state, Action<string> writeText)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(writeText);
        _state = state;
        _writeText = writeText;
    }

    public event EventHandler? CanExecuteChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Feedback => _feedback;
    public bool CanExecute(object? parameter) => CanCopy(_state());

    public void Execute(object? parameter)
    {
        // Capture once: availability and copied values must describe the same snapshot.
        var state = _state();
        if (!CanCopy(state)) return;
        try
        {
            _writeText(Format(state!));
            SetFeedback("Copied");
        }
        catch (ExternalException)
        {
            // Another application may own the clipboard. Never retry or show raw errors.
            SetFeedback("Clipboard unavailable");
        }
    }

    public void RefreshState()
    {
        SetFeedback(string.Empty);
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool CanCopy(BetaPresentationState? state) => state is not null
        && !state.IsPrivateIntegrationDisabled && (state.IsWeeklyAvailable || state.IsPrimaryAvailable);

    private static string Format(BetaPresentationState state)
    {
        var lines = new List<string>
        {
            "AIBar quota",
            Window("Weekly quota", state.Weekly, state.IsWeeklyAvailable),
            Window("5-hour quota", state.Primary, state.IsPrimaryAvailable),
            $"Status: {state.FreshnessLabel}"
        };
        if (!string.IsNullOrEmpty(state.CachedAgeLabel)) lines.Add(state.CachedAgeLabel);
        if (!string.IsNullOrEmpty(state.WarningLabel)) lines.Add(state.WarningLabel);
        return string.Join(Environment.NewLine, lines);
    }

    private static string Window(string label, QuotaWindowPresentation window, bool available) =>
        available && window.PercentageUsed is { } used
            ? $"{label}: {used.ToString("0.##", CultureInfo.InvariantCulture)}% used; reset in {window.ResetCountdown ?? "unavailable"}"
            : $"{label}: unavailable";

    private void SetFeedback(string value)
    {
        if (_feedback == value) return;
        _feedback = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Feedback)));
    }
}
