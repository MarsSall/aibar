using System.Globalization;

namespace AIBar.Domain;

// Components are disjoint: cached input is not included in Input.
public sealed record UsageCostDaily(DateOnly Day, string Model, long Input, long CachedInput, long CacheWrite, long Output);
public sealed record UsageCostPeriod(long? Tokens, decimal? Cost, bool Partial, bool Unpriced = false);
public sealed record UsageCostDay(DateOnly Day, long Tokens, decimal? Cost, bool Partial, bool Unpriced = false);
public sealed record UsageCostSummary(UsageCostPeriod Today, UsageCostPeriod Last30Days)
{
    public IReadOnlyList<UsageCostDay> Days { get; init; } = [];
    public static UsageCostSummary Unavailable { get; } = new(new(null, null, false), new(null, null, false));
    public string TodayLabel => Label("Today", Today);
    public string Last30DaysLabel => Label("Last 30 days", Last30Days);
    private static string Label(string title, UsageCostPeriod value)
    {
        if (value.Tokens is null) return title + ": Unavailable";
        var cost = value.Cost is null ? "Cost unavailable" : "$" + value.Cost.Value.ToString("F2", CultureInfo.InvariantCulture) + (value.Partial ? "+" : "");
        var tokens = value.Tokens.Value;
        var compact = tokens >= 1_000_000_000 ? (tokens / 1_000_000_000m).ToString("0.#", CultureInfo.InvariantCulture) + "B" :
            tokens >= 1_000_000 ? (tokens / 1_000_000m).ToString("0.#", CultureInfo.InvariantCulture) + "M" :
            tokens >= 1_000 ? (tokens / 1_000m).ToString("0.#", CultureInfo.InvariantCulture) + "K" : tokens.ToString(CultureInfo.InvariantCulture);
        return $"{title}: {cost} · {compact} tokens" + (value.Unpriced ? " (some models unpriced)" : value.Partial ? " (incomplete data)" : "");
    }
}

public static class UsageCostPolicy
{
    public static UsageCostSummary Calculate(IEnumerable<UsageCostDaily> days, DateOnly today, bool incomplete = false)
    {
        var rows = days.Where(d => d.Day >= today.AddDays(-29) && d.Day <= today).ToArray();
        var breakdown = Enumerable.Range(0, 30).Select(offset =>
        {
            var day = today.AddDays(offset - 29);
            var value = Summarize(rows.Where(row => row.Day == day), incomplete);
            return new UsageCostDay(day, value.Tokens!.Value, value.Cost, value.Partial, value.Unpriced);
        }).ToArray();
        return new(Summarize(rows.Where(d => d.Day == today), incomplete), Summarize(rows, incomplete)) { Days = breakdown };
    }

    private static UsageCostPeriod Summarize(IEnumerable<UsageCostDaily> rows, bool incomplete)
    {
        long tokens = 0;
        decimal cost = 0;
        var unpriced = false;
        var priced = false;
        foreach (var row in rows)
        {
            if (row.Input < 0 || row.CachedInput < 0 || row.CacheWrite < 0 || row.Output < 0)
                throw new ArgumentOutOfRangeException(nameof(rows));
            tokens = checked(tokens + row.Input + row.CachedInput + row.CacheWrite + row.Output);
            if (!CheckedInPricingCatalog.Current.TryGetRates(row.Model, out var rates) || (row.CacheWrite > 0 && rates.CacheWritePerMillion == 0))
            { unpriced = true; continue; }
            priced = true;
            cost += (row.Input * rates.InputPerMillion + row.CachedInput * rates.CachedInputPerMillion +
                row.CacheWrite * rates.CacheWritePerMillion + row.Output * rates.OutputPerMillion) / 1_000_000m;
        }
        return new(tokens, !priced && (unpriced || incomplete) ? null : cost, incomplete || unpriced, unpriced);
    }
}
