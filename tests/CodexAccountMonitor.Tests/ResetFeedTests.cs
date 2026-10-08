using System.Net;
using System.Text;
using CodexAccountMonitor.Core;

internal static class ResetFeedTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-10-08T01:00:00Z");
        using var handler = new PublicSources();
        using var feed = new ResetFeed(handler);
        var outlook = await feed.ReadAsync(now, CancellationToken.None);
        var signal = outlook.Signals.Single();
        check(signal.Kind == ResetSignalKind.CreditGrant && signal.Level == ResetSignalLevel.Announced && signal.DueAt == now.AddHours(6),
            "status schedule survives a duplicate community post and direct X enrichment");
        check(handler.ReplyRequested && signal.Context?.Single().Id == "2100000000000000100",
            "a scheduled reply without the reset keyword still fetches and retains its verified parent");
        check(!outlook.Completions.Any(c => c.Kind == ResetSignalKind.CreditGrant),
            "in-progress credit loading cannot fabricate a completed grant boundary in the full feed");
        var forecast = ResetForecasting.Build(outlook, now);
        check(forecast is { State: ResetForecastState.Announced, Kind: ResetSignalKind.CreditGrant } && forecast.Timing.Contains("10/08 16:00"),
            "collected credit deadline reaches the in-app forecast in Korean time");
        check(outlook.CommunityForecast?.Within24Hours == 30,
            "a banked credit announcement does not replace the separate regular reset probability with certainty");
        check(!outlook.PartialCoverage, "all simulated public sources and original context are accounted for");
    }

    private sealed class PublicSources : HttpMessageHandler
    {
        public bool ReplyRequested { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string text;
            if (request.RequestUri!.Host == "codex-resets.com")
                text = """{"meta":{"generated_at":"2026-10-08T01:00:00Z"},"data":{"latest_reset":null,"active_watch":null,"scheduled_reset":{"id":"2100000000000000101","status":"scheduled","reset_type":"banked","announced_at":"2026-10-07T19:19:33Z","scheduled_for":"2026-10-08T07:00:00Z","text":"Will be there by EOD PST.","source":{"type":"x_post","author":"thsottiaux"}}}}""";
            else if (request.RequestUri.Host == "codexreset.org")
                text = """<script>{assessmentConfidence:90,handle:"@thsottiaux",id:"2100000000000000101",createdAt:"2026-10-07T19:19:33Z",text:"Will be there by EOD PST."};snapshot:{status:"ready",updatedAt:"2026-10-08T01:00:00Z",forecastStatus:"current"};forecast:{score24h:30,score48h:52,calculationBreakdown:{method:"empirical-baseline"},semanticSummary:"Regular reset baseline"}</script>""";
            else if (request.RequestUri.Host == "cdn.syndication.twimg.com" && request.RequestUri.Query.Contains("2100000000000000101"))
            {
                ReplyRequested = true;
                text = """{"id_str":"2100000000000000101","created_at":"2026-10-07T19:19:33Z","text":"Will be there by EOD PST.","user":{"screen_name":"thsottiaux"},"parent":{"id_str":"2100000000000000100","text":"Loading a banked reset for paid accounts.","user":{"screen_name":"thsottiaux"}}}""";
            }
            else throw new InvalidOperationException("Unexpected public source request");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") });
        }
    }
}
