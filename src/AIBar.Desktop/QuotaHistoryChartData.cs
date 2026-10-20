using AIBar.Domain;

namespace AIBar.Desktop;

public enum QuotaHistoryChartRange { Day, Week, Month }
public sealed record QuotaHistoryChartSeries(string Label, QuotaHistoryProvider Provider, QuotaHistoryWindow Window,
    IReadOnlyList<QuotaHistorySample> Samples);

public sealed record QuotaHistoryChartData(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<QuotaHistoryChartSeries> Series)
{
    public decimal Minimum => 0;
    public decimal Maximum => 100;
    public bool IsEmpty => Series.All(series => series.Samples.Count == 0);

    public static async Task<QuotaHistoryChartData> LoadAsync(IQuotaHistoryStore store, QuotaHistoryChartRange range,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var days = range switch { QuotaHistoryChartRange.Day => 1, QuotaHistoryChartRange.Week => 7,
            QuotaHistoryChartRange.Month => 30, _ => throw new ArgumentOutOfRangeException(nameof(range)) };
        var from = now.AddDays(-days);
        var series = new List<QuotaHistoryChartSeries>();
        foreach (var provider in new[] { QuotaHistoryProvider.Codex, QuotaHistoryProvider.Claude })
        foreach (var window in new[] { QuotaHistoryWindow.FiveHour, QuotaHistoryWindow.Weekly })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var samples = await store.QueryAsync(provider, window, from, now, cancellationToken);
            series.Add(new($"{provider} {(window == QuotaHistoryWindow.FiveHour ? "5-hour" : "weekly")}", provider, window,
                samples.Where(sample => sample.Provider == provider && sample.Window == window && sample.RetrievedAt >= from &&
                    sample.RetrievedAt <= now && sample.Percentage is >= 0 and <= 100).OrderBy(sample => sample.RetrievedAt).ToArray()));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(from, now, series);
    }
}
