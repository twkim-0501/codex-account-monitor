using CodexAccountMonitor.Core;

internal static class DailyTokenHistoryTests
{
    public static void Run(Action<bool, string> check)
    {
        var days = DailyTokenHistory.Build([new("2026-06-24", 500_000_000), new("2026-10-06", 898_845), new("2026-10-09", 16_502_782)]);
        check(days.Count == 7 && days[0].Date == new DateOnly(2026, 10, 3) && days[^1].Date == new DateOnly(2026, 10, 9),
            "sparse history displays seven calendar days instead of joining months-old records");
        check(days.Count(d => d.Tokens is null) == 5 && days[3].Tokens == 898_845 && days[^1].Tokens == 16_502_782,
            "gaps remain unknown and are placed under their real server dates");
        days = DailyTokenHistory.Build([new("2026-10-09", 0), new("2026-10-07", 30), new("2026-10-08", 20)]);
        check(days[^1].Tokens == 0 && days[0].Tokens is null && days[^3].Tokens == 30,
            "a server-provided zero stays different from a missing date regardless of response order");
        check(days[^1].DateLabel == "10/09" && days[^1].Weekday == "금", "date and weekday labels describe the server bucket without calling it local today");
        check(DailyTokenHistory.Build(null).Count == 0 && DailyTokenHistory.Build([]).Count == 0 &&
            DailyTokenHistory.Build([new("unknown", 20), new("2026-02-30", 20)]).Count == 0, "missing and malformed dates cannot fabricate a week of usage");
        days = DailyTokenHistory.Build([new("2026-10-08", 20), new("2026-10-09", -1)]);
        check(days[^1].DateLabel == "10/09" && days[^1].Tokens is null, "invalid negative usage leaves the last provided date unknown");
        days = DailyTokenHistory.Build([new("2026-10-09", 30), new("2026-10-09", 30)]);
        check(days[^1].Tokens == 30, "duplicate identical day buckets do not double counted tokens");
        days = DailyTokenHistory.Build([new("2026-10-09", 30), new("2026-10-09", 40)]);
        check(days[^1].Tokens is null, "conflicting duplicate days remain unknown instead of inventing a total");
        check(DailyTokenHistory.Build([new("0001-01-01", 1)]).Single().Tokens == 1, "the earliest valid date cannot underflow the seven-day window");
        check(DailyTokenHistory.Amount(314_689_978) == "3.1억" && DailyTokenHistory.Amount(16_502_782) == "1650만" &&
            DailyTokenHistory.Amount(898_845) == "89.9만", "chart values use short Korean units while retaining meaningful precision");
        check(DailyTokenHistory.Amount(0) == "0" && DailyTokenHistory.Amount(null) == "—" && DailyTokenHistory.Amount(-1) == "—" &&
            DailyTokenHistory.Amount(1_500_000_000_000) == "1.5조", "zero, unavailable usage, and large token counts have unambiguous labels");
    }
}
