using System.Text.RegularExpressions;

namespace CodexAccountMonitor.Core;

public enum ResetForecastState { Gathering, Announced, Elevated, Watching, Weak, WaitingForCompletion }
public enum ForecastEvidenceRole { Support, Constraint, Context }
public enum ResetCurrentContextKind { Improvement, Completion }
public sealed record ResetCurrentContext(string Id, DateTimeOffset At, ResetCurrentContextKind Kind, string Text);
public sealed record ForecastEvidence(string Id, DateTimeOffset? At, ForecastEvidenceRole Role, string Summary,
    string Text, List<PostContext>? Context = null)
{
    public string RoleLabel => Role switch { ForecastEvidenceRole.Support => "지지 근거", ForecastEvidenceRole.Constraint => "제약", _ => "맥락" };
}
public sealed record ResetForecast(ResetForecastState State, string Headline, string Next24Hours, string Timing,
    string Strength, string Explanation, string WhatChanges, List<ForecastEvidence> Evidence, string? DataWarning,
    ResetSignalKind Kind = ResetSignalKind.UsageReset);

public static class ResetForecasting
{
    private static bool Has(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static List<ResetCurrentContext> CurrentContext(IEnumerable<ResetPost> input, IEnumerable<ResetCompletion> completions, DateTimeOffset now)
    {
        var posts = input.Where(p => p.Author.TrimStart('@').Equals("thsottiaux", StringComparison.OrdinalIgnoreCase)
            && p.CreatedAt <= now && now - p.CreatedAt <= TimeSpan.FromHours(36)).DistinctBy(p => p.Id).ToArray();
        var contexts = new List<ResetCurrentContext>();
        var completed = completions.Where(c => c.Kind == ResetSignalKind.UsageReset && c.At <= now && now - c.At <= TimeSpan.FromHours(36))
            .MaxBy(c => c.At);
        if (completed is not null)
            contexts.Add(new(completed.Id, completed.At, ResetCurrentContextKind.Completion, posts.FirstOrDefault(p => p.Id == completed.Id)?.Text ?? ""));
        var improvement = posts.Where(p => !Has(p.Text, @"\breset") && Has(p.Text, @"roundup|day \d|codex|work")
            && Has(p.Text, @"\bshipped\b|\bnow\b|\blaunched\b|\bincluded\b|\bintegrated\b|\blive\b"))
            .OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        if (improvement is not null) contexts.Add(new(improvement.Id, improvement.CreatedAt, ResetCurrentContextKind.Improvement, improvement.Text));
        return contexts;
    }

    public static ResetForecast Build(ResetOutlook outlook, DateTimeOffset now)
    {
        var evidence = new List<ForecastEvidence>();
        var signals = ResetJudgment.Active(outlook, now);
        var ongoing = signals.FirstOrDefault(s => s.Level == ResetSignalLevel.Conditional && s.Kind == ResetSignalKind.UsageReset);
        var warning = outlook.PartialCoverage ? "수집 불완전 · 이전 근거가 포함될 수 있습니다." : null;
        if (outlook.CheckedAt is { } checkedAt && now - checkedAt > TimeSpan.FromMinutes(20)) warning = "최근 수집값이 아닙니다 · 이전 근거 기준";
        if (outlook.CheckedAt is null)
            return new(ResetForecastState.Gathering, "예측 정보 수집 중", "판단 대기", "확인 중", "미정",
                "공개 게시물과 투표·답글 맥락을 확인하고 있습니다.", "수집이 끝나면 전망과 근거를 표시합니다.", [], warning);

        var announced = signals.Where(s => s.Level == ResetSignalLevel.Announced)
            .OrderBy(s => s.Kind).ThenBy(s => s.DueAt ?? DateTimeOffset.MaxValue).FirstOrDefault();
        ResetForecast forecast;
        if (announced is not null)
        {
            evidence.Add(FromSignal(announced, ForecastEvidenceRole.Support, announced.Reason));
            var passed = announced.DueAt is { } due && due < now;
            var within = announced.DueAt is { } planned && planned >= now && planned - now <= TimeSpan.FromHours(24);
            forecast = new(passed ? ResetForecastState.WaitingForCompletion : ResetForecastState.Announced,
                passed ? "예고 시각 지남 · 실행 확인 대기" : announced.Kind == ResetSignalKind.CreditGrant ? "초기화권 지급 예고 있음" : "리셋 실행 예고 있음",
                passed ? "실행 여부 확인 필요" : within ? "24시간 안에 실행 예고" : announced.NotBefore > now.AddHours(24) ? "24시간 이후의 실행 예고" : "예고 있음 · 정확한 시각 확인 필요",
                announced.Timing, "높음 · 직접 실행 예고", "실행 의사를 밝힌 예고가 가장 강한 근거입니다. 내 계정 반영까지 보장하는 뜻은 아닙니다.",
                "완료 공지 또는 계정의 실제 한도 변화가 확인되면 갱신합니다.", evidence, warning, announced.Kind);
        }
        else
        {
            var hints = signals.Where(s => s.Level == ResetSignalLevel.Hint && s.Kind == ResetSignalKind.UsageReset).OrderByDescending(s => s.PostedAt).ToArray();
            // Poll-majority plus a continuing promise supports an actionable short-term hypothesis, not a calibrated probability.
            var relevantPoll = hints.FirstOrDefault(s => s.Poll is { Choices.Count: > 0 });
            var poll = relevantPoll?.Poll;
            var total = poll?.Choices.Sum(c => Math.Max(0d, c.Votes ?? 0)) ?? 0;
            var resetVotes = poll?.Choices.Where(c => Has(c.Label, @"\breset|리셋|초기화") && !Has(c.Label, @"\b(?:no|not|without|skip|avoid) (?:a |any )?reset|don['’]t (?:need|want) (?:a )?reset"))
                .Sum(c => Math.Max(0d, c.Votes ?? 0)) ?? 0;
            var countsKnown = poll is not null && poll.Choices.All(c => c.Votes is >= 0) && total > 0;
            var resetLeads = countsKnown && resetVotes > total / 2d;
            var opposed = countsKnown && resetVotes < total / 2d;
            var validEnd = relevantPoll is not null && poll?.EndsAt >= relevantPoll.PostedAt && poll.EndsAt <= relevantPoll.PostedAt.AddDays(7) ? poll.EndsAt : null;
            var anchor = validEnd ?? relevantPoll?.PostedAt;
            var watchEnd = anchor?.AddHours(24);
            var inWindow = watchEnd is { } end && end > now;
            if (relevantPoll is not null)
            {
                var share = countsKnown ? $"리셋 선택지 {resetVotes / (double)total:P0} ({resetVotes:N0}/{total:N0}표). 이 비율은 투표 결과이며 리셋 발생 확률이 아닙니다." : "리셋 선택지가 있으나 득표 수를 확인하지 못했습니다.";
                evidence.Add(FromSignal(relevantPoll, opposed ? ForecastEvidenceRole.Constraint : ForecastEvidenceRole.Support,
                    share + (poll!.IsClosed ? " 투표 종료." : " 투표 진행 중으로 결과가 바뀔 수 있습니다.")));
            }
            foreach (var hint in hints.Where(s => s.Id != relevantPoll?.Id).Take(1)) evidence.Add(FromSignal(hint, ForecastEvidenceRole.Support, hint.Reason));
            if (ongoing is not null) evidence.Add(FromSignal(ongoing, ForecastEvidenceRole.Context, ongoing.Reason));
            var pollPromise = ongoing is not null && Has(ongoing.Evidence, @"each day|every day|\bvote\b|\bpoll\b");
            if (resetLeads && pollPromise && validEnd is not null && inWindow && anchor <= now.AddHours(24))
            {
                var start = anchor > now ? anchor.Value : now;
                forecast = new(ResetForecastState.Elevated, "단기 리셋 쪽으로 기운 전망", "리셋 가능성을 우선 관찰",
                    $"{ResetJudgment.KoreanTime(start)} ~ {ResetJudgment.KoreanTime(watchEnd!.Value)} · 추정 관찰 범위",
                    "중간 · 투표와 조건부 약속", "리셋 선택지가 과반이고, 진행 중인 일일 약속과 연결됩니다. 일일 약속에 맞춰 투표 종료 뒤 하루를 추정 범위로 잡았습니다. 명시된 실행 예고는 아직 없습니다.",
                    "직접 예고가 나오면 근거가 강해집니다. 투표가 역전되거나 적용 조건이 달라지면 전망을 낮춥니다.", evidence, warning);
            }
            else if (opposed && ongoing is not null)
                forecast = new(ResetForecastState.Weak, "현재 투표는 리셋을 덜 지지", "단기 리셋 근거 약함", "구체적인 실행 시각 추정 불가", "낮음 · 투표는 반대 방향",
                    "리셋 선택지가 과반에 못 미칩니다. 조건부 약속만으로 가까운 시각의 리셋을 예상하기는 어렵습니다.",
                    "투표 결과가 바뀌거나 새 실행 예고가 나오면 다시 판단합니다.", evidence, warning);
            else if (hints.Length > 0)
                forecast = new(ResetForecastState.Watching, "리셋 단서 있음 · 추가 확인 필요", "단서 관찰 중", "실행 시각 미정", "낮음 · 간접 단서",
                    relevantPoll is not null && !inWindow ? "투표와 연결한 단기 관찰 범위가 지났습니다. 새 예고 없이 같은 투표의 예상 시각을 계속 미루지 않습니다."
                        : "투표·답글의 단서는 있지만, 실행 약속이나 충분한 투표 맥락이 아직 없습니다.",
                    "투표 결과·진행 중인 약속·새 예고가 함께 확인되면 단기 전망을 갱신합니다.", evidence, warning);
            else
                forecast = new(ResetForecastState.Weak, warning is null ? "추가 리셋 단서 대기" : "수집 불완전 · 예측 보류",
                    warning is null ? "가까운 리셋을 뒷받침할 단서 부족" : "현재 전망을 확인할 수 없음", "구체적인 실행 시각 추정 불가",
                    ongoing is not null ? "낮음 · 조건부 약속만 확인" : "낮음 · 미래 예고 없음",
                    ongoing is not null ? "조건부 약속은 유지되지만 다음 리셋을 직접 가리키는 투표나 실행 예고는 없습니다."
                        : "현재 수집 범위에서 다음 리셋의 시점이나 단기 가능성을 추정할 근거가 없습니다.",
                    "리셋을 묻는 새 투표·답글 또는 직접 예고가 나오면 전망을 갱신합니다.", evidence, warning);
        }

        foreach (var context in (outlook.CurrentContext ?? []).Where(c => c.At <= now && now - c.At <= TimeSpan.FromHours(36)).OrderByDescending(c => c.At))
        {
            if (evidence.Any(e => e.Id == context.Id)) continue;
            evidence.Add(new(context.Id, context.At, ForecastEvidenceRole.Context, context.Kind == ResetCurrentContextKind.Completion
                ? "최근 리셋 완료 공지가 있습니다. 이 사건 이전의 일회성 투표·힌트는 다음 리셋의 근거에서 제외했습니다."
                : "최근 개선 출시를 정리한 글이 있습니다. 개선이 있다는 사실만으로 조건 충족이나 리셋 취소를 확정하지는 않습니다.", context.Text));
        }
        return forecast with { Evidence = evidence.Take(5).ToList() };
    }

    private static ForecastEvidence FromSignal(ResetSignal signal, ForecastEvidenceRole role, string summary) => new(signal.Id, signal.PostedAt, role, summary, signal.Evidence, signal.Context);
}
