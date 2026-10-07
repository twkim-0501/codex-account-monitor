using System.Text.Json;
using CodexAccountMonitor.Core;

internal static class ResetTests
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-10-07T02:00:00Z");
        ResetPost Post(string id, string text, double hours = -1, string author = "thsottiaux") => new(id, author, text, now.AddHours(hours));
        List<ResetSignal> Judge(params ResetPost[] posts) => ResetJudgment.Evaluate(posts, [], now);
        var planned = Post("2100000000000000001", "We will reset usage for all paid Codex users tomorrow at 10am PT.");
        var plannedSignal = Judge(planned).Single();
        check(plannedSignal.Level == ResetSignalLevel.Announced && plannedSignal.DueAt == DateTimeOffset.Parse("2026-10-07T17:00:00Z"), "explicit PT timing uses Pacific daylight time and the author's posting date");
        var pst = Judge(planned with { Text = planned.Text.Replace("PT", "PST") }).Single();
        check(pst.DueAt == DateTimeOffset.Parse("2026-10-07T18:00:00Z") && pst.Timing.Contains("1시간"), "literal PST ambiguity is disclosed instead of silently choosing PT");
        var noZone = Judge(planned with { Text = "We will reset Codex usage tomorrow at 10am." }).Single();
        check(noZone.DueAt is null && noZone.Timing.Contains("정확한"), "missing timezone does not invent an exact time");
        var relative = Judge(Post("2100000000000000002", "We will reset Codex limits in ~ 3 hours.")).Single();
        check(relative.DueAt == now.AddHours(2), "relative promise anchors to post time");
        var expiredPlan = Post("2100000000000000003", "We will reset Codex limits in one hour.", -30);
        check(Judge(expiredPlan).Count == 0, "old plans leave the future-only outlook");
        var late = Judge(Post("2100000000000000004", "We will reset Codex in one hour.", -2)).Single();
        check(late.DueAt < now && late.ExpiresAt > now, "passed promise remains awaiting confirmation, not automatically completed");
        var challenge = new ResetPost("2106845241357824205", "thsottiaux", "Over the next 28 days, each day we'll either improve Codex/Work or ship a full reset.", now.AddDays(-2));
        var vote = new ResetPost("2107576143285219799", "thsottiaux", "Vote", now.AddHours(-5),
            new("2107575657014468879", "thsottiaux", "Roundup of Day 2. Codex and Work improvements."), null,
            new([new("good day", 24), new("needs a reset", 76)], now.AddHours(-1), true));
        var signals = Judge(challenge, vote);
        check(signals.Any(s => s.Id == challenge.Id && s.Level == ResetSignalLevel.Conditional) && signals.Any(s => s.Id == vote.Id && s.Level == ResetSignalLevel.Hint && s.PollSummary!.Contains("76")), "short Vote reply plus poll and ongoing promise becomes a hint, not a firm reset");
        check(Judge(challenge, vote with { Poll = null }).Any(s => s.Id == vote.Id && s.Reason.Contains("미수집") == false && s.Reason.Contains("수집되지")), "missing poll choices are disclosed while daily-roundup context still supplies a weak hint");
        check(Judge(vote with { Poll = null }).Count == 0, "Vote alone without reset context is not enough");
        check(Judge(challenge, vote with { Poll = null, Parent = vote.Parent! with { Author = "someoneelse" } }).Count == 1, "community parent cannot supply a promise on Tibo's behalf");
        var completed = Post("2107676072871600470", "Reset all propagated. Enjoy.", -.5);
        signals = Judge(challenge, vote, completed);
        check(signals.Count == 1 && signals[0].Id == challenge.Id, "completion retires the old poll while an ongoing 28-day conditional promise survives");
        check(Judge(Post("2100000000000000005", "We will give all Pro users a banked reset tomorrow."), completed).Single().Kind == ResetSignalKind.CreditGrant, "usage reset completion does not retire an unrelated credit grant");
        check(Judge(planned, completed).Single().Id == planned.Id, "an earlier completion cannot retire a separately scheduled future reset");
        check(Judge(Post("2100000000000000006", "No reset today. We shipped Codex improvements."), Post("2100000000000000007", "We won't reset Codex today.")).Count == 0, "negated resets do not alert");
        check(Judge(Post("2100000000000000008", "We will reset your password tomorrow.")).Count == 0, "password resets are irrelevant");
        check(Judge(Post("2100000000000000009", "Please reset Codex!", author: "community")).Count == 0, "community requests are not monitored-author promises");
        check(Judge(Post("2100000000000000010", "Yes") with { Parent = new("other", "community", "Will you reset Codex tomorrow?") }).Count == 0, "ambiguous Yes reply does not inherit a community promise");
        check(Judge(Post("2100000000000000011", "Maybe we will reset Codex tomorrow.")).Single().Level == ResetSignalLevel.Hint, "hedged statements remain hints");
        check(Judge(Post("2100000000000000013", "More coming next week.") with { Parent = new("2100000000000000014", "community", "Any more Codex resets?") }).Single().Level == ResetSignalLevel.Hint, "future reply without the reset keyword is understood through parent context but stays a hint");
        check(Judge(Post("2100000000000000013", "Soon.") with { Quote = new("2100000000000000014", "community", "Will Codex limits reset?") }).Single().Level == ResetSignalLevel.Hint, "quoted reset context can supply a conservative hint");
        check(Judge(Post("2100000000000000015", "We're resetting Codex usage tomorrow.")).Single().Level == ResetSignalLevel.Announced, "contracted explicit announcements are recognized");
        check(Judge(Post("2100000000000000016", "A banked reset will land tomorrow.")).Single() is { Level: ResetSignalLevel.Announced, Kind: ResetSignalKind.CreditGrant }, "passive credit grant announcements are recognized");
        check(Judge(Post("2100000000000000013", "Soon.") with { Quote = new("2100000000000000014", "community", "Any more banked resets?") }).Single().Kind == ResetSignalKind.CreditGrant, "a contextual hint keeps the credit-grant kind of its parent");
        check(Judge(Post("2100000000000000012", "More resets coming next week.")).Single().DueAt is null, "next week is a range, not an invented timestamp");
        check(ResetJudgment.Evaluate([challenge], [], now.AddDays(30)).Count == 0, "expired conditional program is removed");
        var oldOutlook = new ResetOutlook { Signals = Judge(challenge, vote) };
        var failedSource = new ResetOutlook { PartialCoverage = true };
        check(ResetJudgment.MergePartial(oldOutlook, failedSource, now).Count == 2, "partial source failure preserves unexpired unresolved evidence");
        failedSource.Completions = [new(completed.Id, ResetSignalKind.UsageReset, completed.CreatedAt)];
        check(ResetJudgment.MergePartial(oldOutlook, failedSource, now).Single().Level == ResetSignalLevel.Conditional, "fresh completion can retire old hints even when another source fails");
        check(Judge(vote, vote).Count == 1, "same post is deduplicated");

        JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
        var account = Json("""{"account":{"type":"chatgpt","email":"test@example.com","planType":"pro"}}""");
        var details = UsageParser.Parse("one", account, Json("""{"accountId":"id","rateLimitResetCredits":{"availableCount":5,"credits":[{"id":"a","resetType":"codexRateLimits","status":"available","grantedAt":1791000000,"expiresAt":1792000000},{"id":"b","status":"available","expiresAt":null},{"id":"b","status":"available"},{"status":"available"}]}}"""), null);
        check(details.ResetCredits == 5 && details.ResetCreditDetails?.Count == 2 && details.ResetCreditDetails[1].ExpiresAt is null, "credit count is authoritative, duplicates ignored, unknown expiry stays unknown");
        check(UsageParser.Parse("one", account, Json("""{"rateLimitResetCredits":{"availableCount":2,"credits":null}}"""), null).ResetCreditDetails is null, "null credit details mean unknown");
        check(UsageParser.Parse("one", account, Json("""{"rateLimitResetCredits":{"availableCount":0,"credits":[]}}"""), null).ResetCreditDetails?.Count == 0, "empty credit details remain a known empty list");
        check(UsageParser.Parse("one", account, Json("""{"rateLimitResetCredits":{"availableCount":-1,"credits":[{"id":"x","expiresAt":9223372036854775807}]}}"""), null) is { ResetCredits: null, ResetCreditDetails: [{ ExpiresAt: null }] }, "invalid count and timestamp do not invent credits or expiration");
        var previous = new AccountSnapshot { SourceId = "one", Email = "test@example.com", AccountId = "id", UpdatedAt = now.AddMinutes(-1), Windows = [new("codex", "Codex", 90, 10080, now.AddDays(3))], ResetCredits = 1 };
        var current = new AccountSnapshot { SourceId = "one", Email = previous.Email, AccountId = "id", UpdatedAt = now,
            Windows = [new("codex", "Codex", 5, 10080, now.AddDays(7))], ResetCredits = 2,
            ResetCreditDetails = [new("soon", "codexRateLimits", "available", now, now.AddHours(20), null), new("old", "codexRateLimits", "redeemed", now, now.AddHours(1), null)] };
        check(ResetNotices.AccountChanges("name", previous, current, now).Count() == 2, "fresh account quota recovery and credit increase are separate observations");
        check(!ResetNotices.AccountChanges("name", null, current, now).Any() && !ResetNotices.AccountChanges("name", previous, withIdentity("other"), now).Any(), "first read and switched identity do not emit recovery alerts");
        AccountSnapshot withIdentity(string id) => new() { SourceId = current.SourceId, Email = current.Email, AccountId = id, UpdatedAt = current.UpdatedAt, Windows = current.Windows };
        check(ResetNotices.CreditExpiry("name", current, now).Single().Key.EndsWith("24h"), "only available credits nearing expiry emit 24h notices");
        check(!ResetNotices.CreditExpiry("name", current, now.AddDays(1)).Any(), "stale snapshots cannot trigger expiry alerts");
        var schedule = withIdentity("id"); schedule.Windows = [new("codex", "Codex", 90, 10080, now.AddDays(7))];
        check(ResetNotices.AccountChanges("name", previous, schedule, now).Single().Title.Contains("시각 변경"), "changed server reset time is detected without inventing quota recovery");
        var jitter = withIdentity("id"); jitter.Windows = [previous.Windows[0] with { ResetsAt = previous.Windows[0].ResetsAt!.Value.AddSeconds(-1) }];
        check(!ResetNotices.AccountChanges("hrk", previous, jitter, now).Any(), "one-second server reset correction does not trigger the observed hrk notification spam");
        jitter.Windows = [previous.Windows[0] with { ResetsAt = previous.Windows[0].ResetsAt!.Value.AddMinutes(5).AddSeconds(-1) }];
        check(!ResetNotices.AccountChanges("name", previous, jitter, now).Any(), "reset corrections under five minutes stay silent");
        jitter.Windows = [previous.Windows[0] with { ResetsAt = previous.Windows[0].ResetsAt!.Value.AddMinutes(-5) }];
        check(ResetNotices.AccountChanges("name", previous, jitter, now).Single().Body.Contains("5분"), "a five-minute change to an active future reset still alerts with its new schedule");
        var rollover = withIdentity("id"); rollover.Windows = [new("codex", "Codex", 0, 300, now.AddMinutes(-1))];
        var renewed = withIdentity("id"); renewed.UpdatedAt = now.AddMinutes(1); renewed.Windows = [new("codex", "Codex", 1, 300, now.AddHours(5))];
        check(!ResetNotices.AccountChanges("name", rollover, renewed, renewed.UpdatedAt).Any(), "normal quota window rollover does not announce a changed reset schedule");
        rollover.Windows = [new("codex", "Codex", 0, 300, now.AddHours(1))]; renewed.Windows = [new("codex", "Codex", 0, 300, now.AddHours(5))];
        check(!ResetNotices.AccountChanges("name", rollover, renewed, renewed.UpdatedAt).Any(), "unused quota window timestamps can advance without schedule notifications");
        var extraWindow = new QuotaWindow("codex", "Codex", 10, 300, now.AddHours(1));
        previous.Windows.Add(extraWindow); schedule.Windows.Add(extraWindow);
        var beforeNoise = ResetNotices.AccountChanges("name", previous, schedule, now).Single();
        schedule.Windows[1] = extraWindow with { ResetsAt = extraWindow.ResetsAt!.Value.AddSeconds(1) };
        var afterNoise = ResetNotices.AccountChanges("name", previous, schedule, now).Single();
        check(beforeNoise.Key == afterNoise.Key, "unrelated quota timestamp jitter cannot change a material reset notice fingerprint");

        var syndicated = ResetFeedParser.SyndicatedPost(Json("""{"id_str":"2107576143285219799","created_at":"2026-10-06T20:58:04Z","text":"Vote","user":{"screen_name":"thsottiaux"},"parent":{"id_str":"2107575657014468879","text":"Day 2 roundup","user":{"screen_name":"thsottiaux"}},"card":{"binding_values":{"choice1_label":{"string_value":"good day"},"choice2_label":{"string_value":"needs a reset"},"choice2_count":{"string_value":"76"},"end_datetime_utc":{"string_value":"2026-10-07T00:58:03Z"},"counts_are_final":{"boolean_value":true}}}}"""));
        check(syndicated?.Poll is { IsClosed: true, Choices.Count: 2 } && syndicated.Parent?.Author == "thsottiaux", "syndication adapter reads the real poll-card shape and parent author");
        var html = """<script>$R[2]={assessmentConfidence:99,author:"Tibo",createdAt:"2026-10-06T20:58:04Z",handle:"@thsottiaux",id:"2107576143285219799",forecastImpact:$R[3]={direction:"neutral",state:!0},replyTo:$R[4]={id:"2107575657014468879",handle:"@thsottiaux",text:"Day 2 with {braces} and $R[1]= text"},text:"Vote with \"quotes\""};</script>""";
        var parsed = ResetFeedParser.CommunityPosts(html);
        check(parsed.Count == 1 && parsed[0].Parent!.Text.Contains("$R[1]=") && parsed[0].Text.Contains('"'), "public SSR adapter parses literal data without modifying or executing strings");
        check(ResetFeedParser.CommunityPosts("<script>unrecognized()</script>").Count == 0, "changed page schema yields no fabricated posts");
        try { ResetFeedParser.Status(Json("""{"data":{},"meta":{"generated_at":"2026-10-07T02:00:00Z"}}""")); check(false, "schema accepted"); }
        catch (InvalidDataException) { check(true, "unknown status schema is reported as a failure"); }
        var ledger = new Dictionary<string, DateTimeOffset>();
        check(NoticeLedger.Remember(ledger, "public:poll:Hint", now) && !NoticeLedger.Remember(ledger, "public:poll:Hint", now.AddMinutes(10)), "same poll does not alert again when counts change");
        ledger = JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(JsonSerializer.Serialize(ledger))!;
        check(!NoticeLedger.Remember(ledger, "public:poll:Hint", now.AddMinutes(20)) && NoticeLedger.Remember(ledger, "public:poll:Announced", now.AddMinutes(20)), "deduplication survives restart while upgraded confirmation can alert");
        check(ledger.Keys.All(k => !k.Contains("poll")) && NoticeLedger.Remember(ledger, "public:poll:Hint", now.AddDays(34)), "bounded notification fingerprints contain no raw identity or announcement text");
    }
}
