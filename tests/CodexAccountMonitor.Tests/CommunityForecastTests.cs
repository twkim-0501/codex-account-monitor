using System.Text.Json;
using CodexAccountMonitor.Core;

internal static class CommunityForecastTests
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-10-07T07:00:00Z");
        const string html = """<span>0%</span><script>snapshot:$R[14]={status:"degraded",updatedAt:"2026-10-07T06:01:05Z",forecastStatus:"current",forecast:$R[262]={activeFeatures:$R[263]=[],calculationBreakdown:$R[264]={method:"empirical-baseline"},calibrationState:"experimental",score24h:30,score48h:52,semanticSummary:"No predictive signal."},forecastHistory:[{score24h:99,score48h:100}]}</script>""";
        var parsed = ResetFeedParser.CommunityForecast(html, now);
        check(parsed is { Within24Hours: 30, Within48Hours: 52, Method: "empirical-baseline" }, "community forecast uses current source values rather than animation zeros or archived history");
        check(parsed?.Reason.Contains("과거 리셋 간격") == true && parsed.IsCurrent(now), "baseline explanation is distinct from a new execution promise");
        check(ResetFeedParser.CommunityForecast(html.Replace("score48h:52", "score48h:20"), now) is null, "inconsistent 24/48 hour cumulative probabilities are rejected");
        check(ResetFeedParser.CommunityForecast(html.Replace("score24h:30", "score24h:-1"), now) is null
            && ResetFeedParser.CommunityForecast(html.Replace("score48h:52", "score48h:101"), now) is null, "out-of-range forecast values are rejected");
        check(ResetFeedParser.CommunityForecast(html.Replace("score24h:30", "score24h:\"30\""), now) is null, "changed numeric schema cannot silently invent probabilities");
        check(ResetFeedParser.CommunityForecast(html.Replace("forecastStatus:\"current\"", "forecastStatus:\"stale\""), now) is null, "provider stale status suppresses the percentage display");
        check(ResetFeedParser.CommunityForecast(html, now.AddHours(7)) is null, "source older than six hours is unavailable rather than current");
        check(parsed is not null && !parsed.IsCurrent(now.AddMinutes(21)), "an old cached fetch cannot appear as a live probability");
        check(ResetFeedParser.CommunityForecast(html.Replace("06:01:05Z", "08:01:05Z"), now) is null, "future-dated source timestamp is rejected");
        check(ResetFeedParser.CommunityForecast("forecastHistory:[{score24h:99,score48h:100}]", now) is null, "missing current snapshot never falls back to history");
        var restored = JsonSerializer.Deserialize<ResetOutlook>(JsonSerializer.Serialize(new ResetOutlook { CommunityForecast = parsed }));
        check(restored?.CommunityForecast == parsed, "sourced probabilities and freshness survive state persistence");
    }
}
