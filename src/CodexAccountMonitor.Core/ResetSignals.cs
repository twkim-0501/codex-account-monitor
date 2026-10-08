using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexAccountMonitor.Core;

public enum ResetSignalKind { UsageReset, CreditGrant }
public enum ResetSignalLevel { Announced, Conditional, Hint }
public sealed record PostContext(string Id, string Author, string Text);
public sealed record PollChoice(string Label, long? Votes);
public sealed record ResetPoll(List<PollChoice> Choices, DateTimeOffset? EndsAt, bool IsClosed);
public sealed record ResetSchedule(ResetSignalKind Kind, DateTimeOffset? By);
public sealed record ResetPost(string Id, string Author, string Text, DateTimeOffset CreatedAt,
    PostContext? Parent = null, PostContext? Quote = null, ResetPoll? Poll = null, ResetSchedule? Schedule = null);
public sealed record ResetCompletion(string Id, ResetSignalKind Kind, DateTimeOffset At);
public sealed record ResetSignal(string Id, ResetSignalKind Kind, ResetSignalLevel Level, DateTimeOffset PostedAt,
    DateTimeOffset ExpiresAt, DateTimeOffset? DueAt, string Title, string Reason, string Timing,
    string Evidence, string? ContextId = null, string? PollSummary = null, DateTimeOffset? NotBefore = null,
    ResetPoll? Poll = null, List<PostContext>? Context = null)
{
    public string SourceUrl => $"https://x.com/thsottiaux/status/{Id}";
    public string LevelLabel => Level switch { ResetSignalLevel.Announced => "확정 예고", ResetSignalLevel.Conditional => "조건부 약속", _ => "힌트 · 미확정" };
    public string KindLabel => Kind == ResetSignalKind.CreditGrant ? "초기화권 지급" : "사용 한도 리셋";
}
public sealed class ResetOutlook
{
    public DateTimeOffset? CheckedAt { get; set; }
    public DateTimeOffset? SourceUpdatedAt { get; set; }
    public string Note { get; set; } = "공개 소식 확인 중";
    public bool PartialCoverage { get; set; }
    public List<ResetSignal> Signals { get; set; } = [];
    // Only the latest completion boundary per kind is kept; there is no announcement archive.
    public List<ResetCompletion> Completions { get; set; } = [];
    public List<ResetCurrentContext> CurrentContext { get; set; } = [];
    public CommunityResetForecast? CommunityForecast { get; set; }
}

public static class ResetJudgment
{
    private static bool Has(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Pacific Standard Time" : "America/Los_Angeles");
    private static bool IsTibo(string author) => author.TrimStart('@').Equals("thsottiaux", StringComparison.OrdinalIgnoreCase);
    private static bool ResetMention(string text) => Has(text, @"\breset(?:s|ting|ed)?\b|초기화|리셋");
    private static bool Unrelated(string text) => Has(text, @"reset.{0,18}(?:password|router|device|computer|phone)|(?:password|router|factory).{0,12}reset");
    private static bool Negated(string text) => Has(text, @"\b(?:no|not|without) (?:a |any |full )?reset\b|won['’]t (?:do |ship |give |be )?(?:a )?reset|no (?:reset )?plans?\b");
    private static bool Conditional(string text) => Has(text, @"\beither\b|\bif\b|\bunless\b|depending|condition|조건");

    public static ResetCompletion? Completion(ResetPost post)
    {
        if (!IsTibo(post.Author) || Unrelated(post.Text) || Negated(post.Text) || !ResetMention(post.Text)) return null;
        if (Has(post.Text, @"\b(?:will|shall|going to)\b.{0,25}\b(?:load|credit|grant|reset)")) return null;
        if (!Has(post.Text, @"reset(?:s)? (?:all )?(?:has been |have been |is |are )?(?:propagated|processed|completed)|(?:have|has|now) reset|all reset for|reset.{0,25}has been (?:processed|propagated)|(?:loaded|credited|granted) (?:a |one )?banked reset")) return null;
        return new(post.Id, Kind(post.Text), post.CreatedAt);
    }

    private static ResetSignalKind Kind(string text) => Has(text, @"banked|reset credit|earned reset|初期化券|초기화권") ? ResetSignalKind.CreditGrant : ResetSignalKind.UsageReset;

    public static List<ResetSignal> Evaluate(IEnumerable<ResetPost> input, IEnumerable<ResetCompletion> completions, DateTimeOffset now)
    {
        var posts = input.Where(p => IsTibo(p.Author) && p.CreatedAt <= now.AddMinutes(5) && p.CreatedAt >= now.AddDays(-32))
            .DistinctBy(p => p.Id).OrderBy(p => p.CreatedAt).ToArray();
        var completed = completions.Concat(posts.Select(Completion).OfType<ResetCompletion>()).ToArray();
        var signals = new List<ResetSignal>();
        foreach (var post in posts)
        {
            var own = post.Text;
            if (Unrelated(own) || Negated(own) || Completion(post) is not null) continue;
            var context = string.Join("\n", new[] { post.Parent?.Text, post.Quote?.Text }.OfType<string>());
            var challenge = signals.LastOrDefault(s => s.Level == ResetSignalLevel.Conditional && s.ExpiresAt > post.CreatedAt);
            var hasReset = ResetMention(own);
            var pollReset = post.Poll?.Choices.Any(c => ResetMention(c.Label)) == true;
            var voteInChallenge = Has(own, @"^\s*vote[.!]?\s*$") && challenge is not null && post.Parent is { } parent && IsTibo(parent.Author) && Has(context, @"roundup|day \d");
            var contextualHint = ResetMention(context) && !Unrelated(context) && Has(own, @"\bsoon\b|more coming|next week|stay tuned|we['’]ll|we will|working on it");
            var authoredResetContext = new[] { post.Parent, post.Quote }.OfType<PostContext>()
                .Any(c => IsTibo(c.Author) && ResetMention(c.Text) && !Unrelated(c.Text) && !Negated(c.Text));
            var contextualAnnouncement = authoredResetContext && Has(own, @"\bwill be (?:there|available|loaded|credited|granted)\b")
                && Has(own, @"\beod\b|end of day|today|tomorrow|\b(?:in|next)\b.{0,12}(?:hours?|minutes?)");
            var loadingCredit = hasReset && Has(own, @"\b(?:loading|crediting|granting|adding) (?:a |one )?banked reset\b");
            if (!hasReset && !pollReset && !voteInChallenge && !contextualHint && !contextualAnnouncement && post.Schedule is null) continue;
            var kind = post.Schedule?.Kind ?? Kind(own + (pollReset ? " " + string.Join(" ", post.Poll!.Choices.Select(c => c.Label)) : (contextualHint || contextualAnnouncement) && !hasReset ? " " + context : ""));
            var level = ResetSignalLevel.Hint;
            var reason = "Tibo가 리셋을 언급했습니다. 실제로 리셋하겠다고 약속한 글은 아직 없습니다.";
            DateTimeOffset? due = null;
            var expires = post.CreatedAt.AddHours(48);
            var timing = "실행 시각 미정 · 힌트는 게시 후 최대 48시간 표시";
            if (hasReset && Conditional(own) && Has(own, @"\b(?:we|i|will|we['’]ll|each day)\b"))
            {
                level = ResetSignalLevel.Conditional;
                reason = Has(own, @"\beither\b") ? "Tibo가 조건에 따라 Codex를 개선하거나 리셋하겠다고 약속했습니다. 매일 리셋한다는 뜻은 아닙니다."
                    : "Tibo가 특정 조건이 맞으면 리셋하겠다고 약속했습니다. 어떤 조건인지, 내 계정도 대상인지는 원문에서 확인해야 합니다.";
                var days = Regex.Match(own, @"next\s+(\d{1,2})\s+days", RegexOptions.IgnoreCase);
                if (days.Success && int.TryParse(days.Groups[1].Value, out var n) && n is > 0 and <= 31)
                {
                    expires = post.CreatedAt.AddDays(n + 1);
                    timing = $"{n}일 동안의 조건부 약속 · 정확한 일별 시각 미정";
                }
                else timing = "조건 충족 여부와 실행 시각 미정";
            }
            else if ((post.Schedule is not null || contextualAnnouncement || loadingCredit || hasReset && Has(own, @"\b(?:we|i) (?:will|shall|are (?:going to|resetting)|will be)|\bwe['’]ll\b|\bwe['’]re (?:resetting|giving|granting)|\b(?:more )?resets? (?:are )?coming|\blands? (?:today|tomorrow|end)|\bwill (?:give|ship|reset|credit|do)\b|reset.{0,80}(?:will (?:land|arrive|be (?:given|granted))|is coming)")) && !Has(own, @"\b(?:maybe|might|could|hope|wish|please|should we|try|trying|probably|likely)\b"))
            {
                level = ResetSignalLevel.Announced;
                var action = kind == ResetSignalKind.CreditGrant ? "초기화권을 지급" : "한도를 리셋";
                reason = contextualAnnouncement ? $"Tibo가 앞선 공지에 이어 답글로 {action}하겠다고 알렸습니다. 내 계정에도 적용됐는지는 따로 확인합니다."
                    : post.Schedule is not null && !hasReset ? $"공개 사이트가 Tibo의 후속 글을 {action}한다는 예고로 분류했습니다. 원문 내용과 내 계정의 상태는 따로 확인해야 합니다."
                    : loadingCredit ? "Tibo가 초기화권을 지급 중이라고 알렸습니다. 모든 계정에 지급이 끝났다는 뜻은 아닙니다."
                    : $"Tibo가 {action}하겠다고 직접 알렸습니다. 내 계정에도 적용됐는지는 따로 확인합니다.";
                (due, expires, timing) = Timing(post);
            }
            else if (pollReset || voteInChallenge)
            {
                reason = pollReset ? "Tibo의 투표에 ‘리셋’ 항목이 있습니다. 표가 많아도 실제로 리셋할지는 아직 알 수 없습니다."
                    : "Tibo가 하루 동안의 Codex 개선 내용을 정리한 글에 ‘Vote’라는 답글을 달았습니다. 투표 항목은 확인하지 못했습니다.";
                if (challenge is not null) reason += " 앞서 ‘특정 조건이 맞으면 리셋하겠다’고 한 약속도 함께 참고합니다.";
            }
            else if (contextualHint)
            {
                reason = "Tibo가 리셋 관련 글에 앞으로의 계획을 암시하는 답글이나 인용을 남겼습니다. 리셋을 확실히 약속한 글은 아닙니다.";
            }
            else if (!Has(own, @"codex|work|usage|limit|quota|banked|reset.{0,15}(?:today|tomorrow|week|coming)|more resets")) continue;
            if (expires <= now) continue;
            // A continuing conditional promise survives individual completions; one-off plans/hints do not.
            var notBefore = level == ResetSignalLevel.Announced ? EarliestExecution(post, due) : null;
            if (level != ResetSignalLevel.Conditional && completed.Any(c => c.Kind == kind && c.At >= post.CreatedAt && c.Id != post.Id && (notBefore is null || c.At >= notBefore))) continue;
            if (Has(own, @"paid|plus.{0,15}pro")) reason += " 원문에는 유료 플랜 사용자가 대상이라고 적혀 있습니다.";
            var title = level == ResetSignalLevel.Conditional && Has(own, @"\beither\b") ? "개선 출시 또는 리셋" : kind == ResetSignalKind.CreditGrant ? "초기화권 지급 소식" : "리셋 소식";
            string? pollSummary = null;
            if (post.Poll is { } poll)
            {
                pollSummary = string.Join(" · ", poll.Choices.Select(c => c.Votes is { } votes ? $"{c.Label} {votes:N0}표" : c.Label));
                pollSummary += poll.IsClosed ? " · 투표 종료" : " · 진행 중";
                if (poll.EndsAt is { } end) pollSummary += $" ({KoreanTime(end)})";
            }
            // A verified timing reply refines its author's initial announcement; retain the parent as evidence.
            if (level == ResetSignalLevel.Announced && post.Parent is { } announcedParent && IsTibo(announcedParent.Author))
                signals.RemoveAll(s => s.Id == announcedParent.Id && s.Kind == kind && s.Level == ResetSignalLevel.Announced);
            signals.Add(new(post.Id, kind, level, post.CreatedAt, expires, due, title, reason, timing, own,
                post.Parent?.Id ?? post.Quote?.Id ?? (voteInChallenge ? challenge?.Id : null), pollSummary, notBefore, post.Poll,
                new[] { post.Parent, post.Quote }.OfType<PostContext>().Concat(challenge is not null && level == ResetSignalLevel.Hint
                    ? [new PostContext(challenge.Id, "thsottiaux", challenge.Evidence)] : []).DistinctBy(c => c.Id).Take(3).ToList()));
        }
        return signals.OrderBy(s => s.Level).ThenByDescending(s => s.PostedAt).Take(4).ToList();
    }

    private static (DateTimeOffset? Due, DateTimeOffset Expires, string Label) Timing(ResetPost post)
    {
        var text = ExecutionText(post.Text);
        if (post.Schedule?.By is { } scheduled)
        {
            var ambiguous = Has(text, @"\bPST\b") && Pacific.IsDaylightSavingTime(TimeZoneInfo.ConvertTime(post.CreatedAt, Pacific).Date);
            return (scheduled, scheduled.AddHours(24), KoreanTime(scheduled) + "까지 · 공개 사이트가 안내한 시각" +
                (ambiguous ? " · 원문의 미국 서부 시간 표기에 따라 1시간 차이 가능" : "") + " · 완료 별도 확인");
        }
        var relative = Regex.Match(text, @"(?:next|in|~)\s*(one|an?|\d{1,3})\s*(hours?|minutes?)", RegexOptions.IgnoreCase);
        if (relative.Success)
        {
            var n = int.TryParse(relative.Groups[1].Value, out var amount) ? amount : 1;
            var due = post.CreatedAt.AddMinutes(n * (relative.Groups[2].Value.StartsWith("hour", StringComparison.OrdinalIgnoreCase) ? 60 : 1));
            return (due, due.AddHours(24), "대략 " + KoreanTime(due) + " · 예고 시각 이후에는 완료 확인 대기");
        }
        var local = TimeZoneInfo.ConvertTime(post.CreatedAt, Pacific);
        var endOfDay = Regex.Match(text, @"\b(?:eod|end of day)\s*(PT|PDT|PST)\b", RegexOptions.IgnoreCase);
        if (endOfDay.Success)
        {
            var date = local.Date.AddDays(Has(text, @"tomorrow") ? 2 : 1);
            var abbreviation = endOfDay.Groups[1].Value.ToUpperInvariant();
            var zoneOffset = abbreviation == "PST" ? TimeSpan.FromHours(-8) : abbreviation == "PDT" ? TimeSpan.FromHours(-7) : Pacific.GetUtcOffset(date);
            var end = new DateTimeOffset(date, zoneOffset).ToUniversalTime();
            var ambiguous = abbreviation == "PST" && Pacific.IsDaylightSavingTime(local.Date);
            return (end, end.AddHours(24), KoreanTime(end) + "까지" + (ambiguous ? " · 원문의 PST 기준. 미국 서부 현지 시간을 뜻했다면 1시간 빠름" : " · Tibo가 말한 하루의 끝을 한국시간으로 변환") + " · 완료 별도 확인");
        }
        var clock = Regex.Match(text, @"\b(\d{1,2})(?::(\d{2}))?\s*(am|pm)\s*(PT|PDT|PST)\b", RegexOptions.IgnoreCase);
        if (clock.Success && Has(text, @"today|tomorrow"))
        {
            var hour = int.Parse(clock.Groups[1].Value, CultureInfo.InvariantCulture);
            var minute = clock.Groups[2].Success ? int.Parse(clock.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            if (hour is >= 1 and <= 12 && minute < 60)
            {
                hour = hour % 12 + (clock.Groups[3].Value.Equals("pm", StringComparison.OrdinalIgnoreCase) ? 12 : 0);
                var date = local.Date.AddDays(Has(text, @"tomorrow") ? 1 : 0).AddHours(hour).AddMinutes(minute);
                var abbreviation = clock.Groups[4].Value.ToUpperInvariant();
                // Literal PST is UTC-8; authors sometimes mean Pacific local time instead. Never silently choose.
                var zoneOffset = abbreviation == "PST" ? TimeSpan.FromHours(-8) : abbreviation == "PDT" ? TimeSpan.FromHours(-7) : Pacific.GetUtcOffset(date);
                var due = new DateTimeOffset(date, zoneOffset).ToUniversalTime();
                var ambiguous = abbreviation == "PST" && Pacific.IsDaylightSavingTime(date);
                return (due, due.AddHours(24), KoreanTime(due) + (ambiguous ? " · 원문의 PST 기준. 미국 서부 현지 시간을 뜻했다면 1시간 빠름" : " · 예고 시각") + " · 완료 별도 확인");
            }
        }
        if (Has(text, @"next week")) return (null, new DateTimeOffset(local.Date.AddDays(14), Pacific.GetUtcOffset(local.Date.AddDays(14))), "다음 주 · 정확한 일시 미정");
        if (Has(text, @"today|tomorrow|end of day"))
        {
            var endDate = local.Date.AddDays(Has(text, @"tomorrow") ? 2 : 1);
            var end = new DateTimeOffset(endDate, Pacific.GetUtcOffset(endDate));
            return (null, end.AddHours(24), Has(text, @"tomorrow") ? "게시자의 내일 · 정확한 일시 미정" : "게시자의 오늘 · 정확한 일시 미정");
        }
        return (null, post.CreatedAt.AddDays(7), "실행 일시 미정 · 게시 후 최대 7일 확인");
    }

    private static DateTimeOffset? EarliestExecution(ResetPost post, DateTimeOffset? due)
    {
        var text = ExecutionText(post.Text);
        if (Has(text, @"\bby\b.{0,20}\b(?:eod|end of day)\b")) return post.CreatedAt;
        if (due is not null) return due.Value.AddMinutes(-90); // Covers approximate times and PST/PT wording ambiguity.
        var local = TimeZoneInfo.ConvertTime(post.CreatedAt, Pacific);
        DateTime date;
        if (Has(text, @"next week"))
        {
            var untilMonday = ((int)DayOfWeek.Monday - (int)local.DayOfWeek + 7) % 7;
            date = local.Date.AddDays(untilMonday == 0 ? 7 : untilMonday);
        }
        else if (Has(text, @"tomorrow")) date = local.Date.AddDays(1);
        else return null;
        return new DateTimeOffset(date, Pacific.GetUtcOffset(date));
    }

    private static string ExecutionText(string text) => Regex.Replace(text, @"\bsee you(?: again)? tomorrow[.!]?", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string KoreanTime(DateTimeOffset value) => value.ToOffset(TimeSpan.FromHours(9)).ToString("MM/dd HH:mm 'KST'", CultureInfo.InvariantCulture);
    public static List<ResetSignal> Active(ResetOutlook outlook, DateTimeOffset now) => outlook.Signals.Where(s => s.ExpiresAt > now).ToList();
    public static List<ResetSignal> MergePartial(ResetOutlook previous, ResetOutlook current, DateTimeOffset now) => Active(current, now)
        .Concat(Active(previous, now).Where(s => current.Signals.All(c => c.Id != s.Id) &&
            (s.Level == ResetSignalLevel.Conditional || !current.Completions.Any(c => c.Kind == s.Kind && c.At >= s.PostedAt && (s.NotBefore is null || c.At >= s.NotBefore)))))
        .OrderBy(s => s.Level).ThenByDescending(s => s.PostedAt).Take(4).ToList();
}

public sealed record MonitorNotice(string Key, string Title, string Body);
public static class NoticeLedger
{
    public static bool Remember(Dictionary<string, DateTimeOffset> delivered, string key, DateTimeOffset now)
    {
        foreach (var old in delivered.Where(x => now - x.Value >= TimeSpan.FromDays(33)).Select(x => x.Key).ToArray()) delivered.Remove(old);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        if (delivered.ContainsKey(hash)) return false;
        if (delivered.Count >= 256) delivered.Remove(delivered.MinBy(x => x.Value).Key);
        delivered.Add(hash, now);
        return true;
    }
}
public static class ResetNotices
{
    public static IEnumerable<MonitorNotice> AccountChanges(string name, AccountSnapshot? previous, AccountSnapshot current, DateTimeOffset now)
    {
        if (previous?.IdentityKey is null || previous.IdentityKey != current.IdentityKey || current.UpdatedAt <= previous.UpdatedAt || now - previous.UpdatedAt > TimeSpan.FromMinutes(10)) yield break;
        var recovered = current.Windows.Where(w => previous.Windows.Any(p => p.Bucket == w.Bucket && p.DurationMinutes == w.DurationMinutes && p.UsedPercent - w.UsedPercent >= 5)).ToArray();
        if (recovered.Length > 0)
            yield return new($"quota:{current.IdentityKey}:{string.Join(';', recovered.Select(w => $"{w.Bucket}:{w.DurationMinutes}:{w.ResetsAt}:{Math.Round(w.UsedPercent)}"))}", name + " 한도 회복 확인", string.Join(" · ", recovered.Select(w => $"{w.DurationLabel} {w.RemainingPercent:0}% 남음")) + "\n원인은 정기 갱신·초기화권·전체 리셋 중 별도 확인이 필요합니다.");
        else
        {
            // Small server timestamp corrections and the start of a new quota window are routine refreshes.
            var changed = current.Windows.Where(w => w.ResetsAt is { } reset && reset > now && previous.Windows.Any(p =>
                p.Bucket == w.Bucket && p.DurationMinutes == w.DurationMinutes && p.ResetsAt is { } oldReset && oldReset > now &&
                (p.UsedPercent > 0 || w.UsedPercent > 0) && (reset - oldReset).Duration() >= TimeSpan.FromMinutes(5)))
                .OrderBy(w => w.Bucket, StringComparer.Ordinal).ThenBy(w => w.DurationMinutes).ToArray();
            if (changed.Length > 0)
                yield return new($"schedule:{current.IdentityKey}:{string.Join(';', changed.Select(w => $"{w.Bucket}:{w.DurationMinutes}:{w.ResetsAt!.Value.ToUnixTimeSeconds()}"))}", name + " 리셋 시각 변경",
                    string.Join(" · ", changed.Select(w => $"{w.DurationLabel} {ResetJudgment.KoreanTime(w.ResetsAt!.Value)}")) + "\n진행 중인 한도의 리셋 일정이 5분 이상 바뀌었습니다.");
        }
        if (current.ResetCredits is { } count && previous.ResetCredits is { } old && count > old)
            yield return new($"credits:{current.IdentityKey}:{count}:{string.Join(';', current.ResetCreditDetails?.Select(c => c.Id) ?? [])}", name + " 초기화권 증가", $"초기화권 {old}개 → {count}개. 지급 공지와 계정 반영은 별도로 확인합니다.");
    }

    public static IEnumerable<MonitorNotice> CreditExpiry(string name, AccountSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.IdentityKey is null || snapshot.ResetCredits == 0 || now - snapshot.UpdatedAt > TimeSpan.FromMinutes(10)) yield break;
        foreach (var credit in snapshot.ResetCreditDetails ?? [])
        {
            if (!credit.IsAvailable || credit.ExpiresAt is not { } expiry || expiry <= now || expiry - now > TimeSpan.FromDays(3)) continue;
            var stage = expiry - now <= TimeSpan.FromDays(1) ? "24h" : "3d";
            yield return new($"expiry:{snapshot.IdentityKey}:{credit.Id}:{expiry.ToUnixTimeSeconds()}:{stage}", name + " 초기화권 만료 임박", $"{ResetJudgment.KoreanTime(expiry)} 만료 · {stage switch { "24h" => "24시간", _ => "3일" }} 이내\n계정에서 사용 여부를 결정하세요.");
        }
    }
}
