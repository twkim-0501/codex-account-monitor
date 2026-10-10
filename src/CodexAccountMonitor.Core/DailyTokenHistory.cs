using System.Globalization;

namespace CodexAccountMonitor.Core;

public sealed record DailyTokenDay(DateOnly Date, long? Tokens)
{
    public string DateLabel => Date.ToString("MM/dd", CultureInfo.InvariantCulture);
    public string Weekday => "일월화수목금토"[(int)Date.DayOfWeek].ToString();
}

public static class DailyTokenHistory
{
    public static IReadOnlyList<DailyTokenDay> Build(IEnumerable<DailyTokens>? daily)
    {
        if (daily is null) return [];
        var dates = new Dictionary<DateOnly, List<long>>();
        foreach (var point in daily)
        {
            if (!DateOnly.TryParseExact(point.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            if (!dates.TryGetValue(date, out var values)) dates[date] = values = [];
            if (point.Tokens >= 0) values.Add(point.Tokens);
        }
        if (dates.Count == 0) return [];
        var latest = dates.Keys.Max();
        var count = Math.Min(7, latest.DayNumber + 1);
        return Enumerable.Range(0, count).Select(offset =>
        {
            var date = latest.AddDays(offset - count + 1);
            var values = dates.GetValueOrDefault(date)?.Distinct().ToArray();
            // Missing or conflicting buckets are unknown, never inferred to be zero or summed twice.
            return new DailyTokenDay(date, values is { Length: 1 } ? values[0] : null);
        }).ToArray();
    }

    public static string Amount(long? tokens) => tokens switch
    {
        null or < 0 => "—",
        >= 1_000_000_000_000 => (tokens.Value / 1_000_000_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "조",
        >= 100_000_000 => (tokens.Value / 100_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "억",
        >= 10_000_000 => (tokens.Value / 10_000d).ToString("0", CultureInfo.InvariantCulture) + "만",
        >= 10_000 => (tokens.Value / 10_000d).ToString("0.#", CultureInfo.InvariantCulture) + "만",
        _ => tokens.Value.ToString("N0", CultureInfo.InvariantCulture)
    };
}
