using System.Text.Json;
using CodexAccountMonitor.Core;

internal static class ForecastTests
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-10-07T02:00:00Z");
        var promise = new ResetSignal("2100000000000000001", ResetSignalKind.UsageReset, ResetSignalLevel.Conditional, now.AddDays(-2), now.AddDays(26), null,
            "개선 또는 리셋", "매일 개선 또는 리셋을 조건부로 약속했습니다.", "일별 시각 미정", "Each day, either improve Codex or reset usage.");
        var poll = new ResetSignal("2100000000000000002", ResetSignalKind.UsageReset, ResetSignalLevel.Hint, now.AddHours(-4), now.AddHours(44), null,
            "투표", "선택지에 리셋", "시각 미정", "Vote", Poll: new([new("good day", 24), new("needs a reset", 76)], now.AddHours(-1), true),
            Context: [new("2100000000000000003", "thsottiaux", "Roundup of Day 2: improvements shipped.")]);
        var outlook = new ResetOutlook { CheckedAt = now, Signals = [promise, poll] };
        var forecast = ResetForecasting.Build(outlook, now);
        check(forecast.State == ResetForecastState.Elevated && forecast.Strength.Contains("중간") && forecast.Timing.Contains("추정"), "reset-leading poll plus a daily conditional promise yields an explicitly inferred short-term forecast");
        check(forecast.Evidence.Any(e => e.Summary.Contains("76") && e.Summary.Contains("발생 확률이 아닙니다")), "poll vote share is shown as evidence, never as a reset probability");
        check(forecast.Evidence.First().Context?.Single().Text.Contains("Day 2") == true, "full parent text is available inside the forecast without visiting X");
        var reversed = poll with { Poll = new([new("good day", 76), new("needs a reset", 24)], now.AddHours(-1), true) };
        outlook.Signals = [promise, reversed];
        check(ResetForecasting.Build(outlook, now) is { State: ResetForecastState.Weak } f && f.Evidence.Any(e => e.Role == ForecastEvidenceRole.Constraint), "opposing poll votes weaken the forecast and appear as a constraint");
        outlook.Signals = [promise, poll with { Poll = new([new("good day", null), new("needs a reset", null)], null, false) }];
        check(ResetForecasting.Build(outlook, now).State == ResetForecastState.Watching, "missing votes cannot create a strong forecast");
        outlook.Signals = [promise, poll with { Poll = null }];
        check(ResetForecasting.Build(outlook, now).State == ResetForecastState.Watching, "old cached hints without structured poll details remain conservative");
        outlook.Signals = [poll];
        check(ResetForecasting.Build(outlook, now).State == ResetForecastState.Watching, "poll majority alone does not imply a near-term reset promise");
        outlook.Signals = [promise, poll with { Poll = new([new("no reset", 90), new("needs a reset", 10)], now.AddHours(-1), true) }];
        check(ResetForecasting.Build(outlook, now).State == ResetForecastState.Weak, "negative reset options do not count toward pro-reset votes");
        outlook.Signals = [promise, poll with { PostedAt = now.AddHours(-30), Poll = new([new("good day", 24), new("needs a reset", 76)], now.AddHours(-25), true) }];
        check(ResetForecasting.Build(outlook, now) is { State: ResetForecastState.Watching, Timing: "실행 시각 미정" }, "the inferred poll window cannot drift forward after it expires");
        outlook.Signals = [promise, poll with { Poll = new([new("good day", 24), new("needs a reset", 76)], now.AddDays(3), false) }];
        check(ResetForecasting.Build(outlook, now).State == ResetForecastState.Watching, "a poll closing days later is not labeled a 24-hour forecast");
        outlook.Signals = [promise];
        check(ResetForecasting.Build(outlook, now) is { State: ResetForecastState.Weak } conditional && conditional.Timing.Contains("추정 불가"), "conditional promise alone cannot invent the next execution time");

        var announced = poll with { Level = ResetSignalLevel.Announced, DueAt = now.AddHours(2), Timing = "10/07 13:00 KST · 예고 시각", Poll = null };
        outlook.Signals = [promise, reversed, announced];
        check(ResetForecasting.Build(outlook, now) is { State: ResetForecastState.Announced, Next24Hours: "24시간 안에 실행 예고" }, "an explicit execution promise outranks an opposing poll");
        outlook.Signals = [announced with { DueAt = now.AddHours(-1) }];
        check(ResetForecasting.Build(outlook, now).State == ResetForecastState.WaitingForCompletion, "a passed announcement remains awaiting confirmation");
        outlook.Signals = [announced with { DueAt = now.AddDays(3), NotBefore = now.AddDays(3).AddMinutes(-90) }];
        check(ResetForecasting.Build(outlook, now).Next24Hours.Contains("24시간 이후"), "a distant announcement does not imply execution within 24 hours");
        outlook.Signals = [announced with { Kind = ResetSignalKind.CreditGrant }];
        check(ResetForecasting.Build(outlook, now) is { Kind: ResetSignalKind.CreditGrant } grant && grant.Headline.Contains("초기화권 지급"), "credit-grant forecasts are distinguished from quota reset forecasts");

        outlook.Signals = [promise]; outlook.PartialCoverage = true;
        check(ResetForecasting.Build(outlook, now).DataWarning is not null, "incomplete collection is visible alongside the forecast");
        outlook.CheckedAt = now.AddMinutes(-25); outlook.PartialCoverage = false;
        check(ResetForecasting.Build(outlook, now).DataWarning!.Contains("이전 근거"), "stale collection is not displayed as a fresh forecast");
        check(ResetForecasting.Build(new(), now).State == ResetForecastState.Gathering, "initial collection does not fabricate a forecast");
        outlook.CheckedAt = now;
        var completed = new ResetCompletion("2100000000000000004", ResetSignalKind.UsageReset, now.AddHours(-1));
        var posts = new[] { new ResetPost(completed.Id, "thsottiaux", "Reset all propagated.", completed.At),
            new ResetPost("2100000000000000005", "thsottiaux", "Roundup of Day 2: Codex features shipped and are now live.", now.AddHours(-2)),
            new ResetPost("2100000000000000006", "community", "Codex improvement shipped.", now) };
        outlook.CurrentContext = ResetForecasting.CurrentContext(posts, [completed], now);
        forecast = ResetForecasting.Build(outlook, now);
        check(outlook.CurrentContext.Count == 2 && forecast.Evidence.Any(e => e.Summary.Contains("완료 공지")), "current completion and improvement context explain why old signals were retired");
        check(ResetForecasting.CurrentContext(posts, [completed], now.AddDays(2)).Count == 0, "context ages out without becoming an announcement archive");
        outlook = JsonSerializer.Deserialize<ResetOutlook>(JsonSerializer.Serialize(outlook))!;
        check(ResetForecasting.Build(outlook, now).Evidence.Any(e => e.Text.Contains("propagated")), "in-app original evidence survives cache serialization");
        outlook.Signals = [promise, poll]; outlook.Completions = [completed];
        check(ResetForecasting.Build(outlook, now).State == ResetForecastState.Weak && ResetJudgment.Active(outlook, now).Single().Id == promise.Id,
            "a cached completed reset expires its old poll forecast while retaining the ongoing program");
        outlook.Signals = [announced with { PostedAt = now.AddHours(-2), DueAt = now.AddHours(3), NotBefore = now.AddHours(2) }];
        check(ResetForecasting.Build(outlook, now).State == ResetForecastState.Announced,
            "an earlier reset cannot expire a separately scheduled future announcement");
        outlook.Completions = [completed with { Kind = ResetSignalKind.CreditGrant }];
        outlook.Signals = [announced with { PostedAt = now.AddHours(-2), NotBefore = null }];
        check(ResetForecasting.Build(outlook, now).State == ResetForecastState.Announced,
            "a credit payout cannot expire a quota-reset forecast");
        outlook.Completions = [completed with { At = now.AddHours(1) }];
        check(ResetForecasting.Build(outlook, now).State == ResetForecastState.Announced,
            "a future-dated completion cannot expire a current forecast");
        outlook.Completions = [completed with { Id = announced.Id }];
        check(ResetForecasting.Build(outlook, now).State == ResetForecastState.Weak,
            "completion of the original announcement expires its cached prediction even with the same post ID");
    }
}
