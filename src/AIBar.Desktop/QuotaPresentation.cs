using AIBar.Domain;

namespace AIBar.Desktop;

public sealed record QuotaWindowPresentation(string Label, decimal? PercentageUsed, string? ResetCountdown);

public sealed record QuotaPresentationState(
    QuotaWindowPresentation Primary,
    QuotaWindowPresentation Weekly,
    bool IsCurrent,
    string FreshnessLabel,
    string? ErrorLabel,
    DateTimeOffset? RetrievedAt,
    string QuotaDisclosure,
    string AnalyticsDisclosure,
    string CostDisclosure,
    string PrivateEndpointDisclosure);

public sealed class QuotaPresentationMapper(IClock clock)
{
    public QuotaPresentationState Map(QuotaRefreshState state)
    {
        var snapshot = state.Snapshot;
        var freshness = state.IsLoading
            ? "Loading"
            : snapshot is not null && state.Failure is not null ? "Stale" : state.Freshness.ToString();
        return new(
            Window("5-hour quota", snapshot?.Primary),
            Window("Weekly quota", snapshot?.Weekly),
            snapshot is not null && state.Freshness == FreshnessState.Current && !state.IsLoading && state.Failure is null,
            freshness,
            ErrorLabel(state.Failure),
            snapshot?.RetrievedAt,
            "Private service-reported quota",
            "Locally derived analytics",
            "Estimated cost; not billed cost.",
            "Quota access uses a private, undocumented, unsupported endpoint.");
    }

    private QuotaWindowPresentation Window(string label, QuotaWindow? window) => new(
        label,
        window?.PercentageUsed,
        window is null ? null : FormatCountdown(window.CountdownAt(clock.UtcNow)));

    private static string FormatCountdown(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

    private static string? ErrorLabel(QuotaFailure? failure) => failure?.Kind switch
    {
        QuotaErrorKind.Authentication => "Authentication required",
        QuotaErrorKind.Permission => "Permission denied",
        QuotaErrorKind.MalformedResponse => "Service response unavailable",
        QuotaErrorKind.Network => "Network unavailable",
        QuotaErrorKind.Service => "Service unavailable",
        QuotaErrorKind.Unavailable or QuotaErrorKind.Redirect => "Quota unavailable",
        _ => null
    };
}

public interface IManualRefreshCommand
{
    event EventHandler? CanExecuteChanged;
    bool CanExecute { get; }
    ValueTask ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class ManualRefreshCommand(Func<CancellationToken, ValueTask> refresh, Func<bool> canExecute) : IManualRefreshCommand
{
    private int _isRunning;

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute => Volatile.Read(ref _isRunning) == 0 && canExecute();

    public async ValueTask ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!canExecute() || Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0) return;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await refresh(cancellationToken); }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
