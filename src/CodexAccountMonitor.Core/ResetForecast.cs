using System.Text.RegularExpressions;

namespace CodexAccountMonitor.Core;

public enum ResetForecastState { Gathering, Announced, Elevated, Watching, Weak, WaitingForCompletion }
public enum ForecastEvidenceRole { Support, Constraint, Context }
public enum ResetCurrentContextKind { Improvement, Completion }
public sealed record ResetCurrentContext(string Id, DateTimeOffset At, ResetCurrentContextKind Kind, string Text);
public sealed record ForecastEvidence(string Id, DateTimeOffset? At, ForecastEvidenceRole Role, string Summary,
    string Text, List<PostContext>? Context = null)
{
    public string RoleLabel => Role switch { ForecastEvidenceRole.Support => "리셋을 기대하는 이유", ForecastEvidenceRole.Constraint => "아직 확실하지 않은 점", _ => "함께 볼 소식" };
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
        var warning = outlook.PartialCoverage ? "일부 소식을 확인하지 못했습니다. 이전에 확인한 글이 포함될 수 있습니다." : null;
        if (outlook.CheckedAt is { } checkedAt && now - checkedAt > TimeSpan.FromMinutes(20)) warning = "최신 소식을 확인하지 못해 이전 근거로 표시합니다.";
        if (outlook.CheckedAt is null)
            return new(ResetForecastState.Gathering, "예측 정보 수집 중", "판단 대기", "확인 중", "미정",
                "Tibo의 게시물과 투표, 답글을 확인하고 있습니다.", "확인이 끝나면 예상과 이유를 표시합니다.", [], warning);

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
                announced.Timing, "높음 · 직접 실행 예고", "Tibo가 실제로 진행하겠다고 알렸습니다. 내 계정에도 적용됐는지는 따로 확인해야 합니다.",
                "Tibo의 완료 공지나 내 계정의 한도 변화를 확인하면 표시를 바꿉니다.", evidence, warning, announced.Kind);
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
                var share = countsKnown ? $"투표 참여자의 {resetVotes / (double)total:P0}가 리셋을 골랐습니다 ({resetVotes:N0}/{total:N0}표). 이 비율은 리셋 발생 확률이 아닙니다." : "투표에 ‘리셋’ 항목이 있지만 몇 표를 받았는지는 확인하지 못했습니다.";
                evidence.Add(FromSignal(relevantPoll, opposed ? ForecastEvidenceRole.Constraint : ForecastEvidenceRole.Support,
                    share + (poll!.IsClosed ? " 투표는 끝났습니다." : " 투표 중이라 결과가 바뀔 수 있습니다.")));
            }
            foreach (var hint in hints.Where(s => s.Id != relevantPoll?.Id).Take(1)) evidence.Add(FromSignal(hint, ForecastEvidenceRole.Support, hint.Reason));
            if (ongoing is not null) evidence.Add(FromSignal(ongoing, ForecastEvidenceRole.Context, ongoing.Reason));
            var pollPromise = ongoing is not null && Has(ongoing.Evidence, @"each day|every day|\bvote\b|\bpoll\b");
            if (resetLeads && pollPromise && validEnd is not null && inWindow && anchor <= now.AddHours(24))
            {
                var start = anchor > now ? anchor.Value : now;
                forecast = new(ResetForecastState.Elevated, "단기 리셋 쪽으로 기운 전망", "리셋 가능성을 우선 관찰",
                    $"{ResetJudgment.KoreanTime(start)} ~ {ResetJudgment.KoreanTime(watchEnd!.Value)} · 앱의 추정",
                    "중간 · 투표와 Tibo의 약속", "투표에서 절반 이상이 리셋을 골랐습니다. Tibo의 일일 약속을 참고해 투표 종료 뒤 24시간을 예상 범위로 잡았습니다. Tibo가 이 시간에 리셋하겠다고 알린 것은 아닙니다.",
                    "Tibo가 직접 예고하면 리셋을 더 기대할 수 있습니다. 투표 결과나 약속의 조건이 바뀌면 예상을 다시 검토합니다.", evidence, warning);
            }
            else if (opposed && ongoing is not null)
                forecast = new(ResetForecastState.Weak, "현재 투표는 리셋을 덜 지지", "단기 리셋 근거 약함", "구체적인 실행 시각 추정 불가", "낮음 · 투표는 반대 방향",
                    "투표에서 리셋을 고른 사람이 절반보다 적습니다. Tibo의 약속만으로 곧 리셋할 것이라고 예상하기는 어렵습니다.",
                    "투표 결과가 바뀌거나 새 리셋 예고가 나오면 다시 확인합니다.", evidence, warning);
            else if (hints.Length > 0)
                forecast = new(ResetForecastState.Watching, "리셋 단서 있음 · 추가 확인 필요", "단서 관찰 중", "실행 시각 미정", "낮음 · 간접 단서",
                    relevantPoll is not null && !inWindow ? "이 투표를 보고 예상했던 시간은 이미 지났습니다. 다음 리셋을 예상하려면 새 소식이 필요합니다."
                        : "리셋을 암시하는 투표나 답글은 있습니다. 실제로 진행하겠다는 약속이나 자세한 투표 결과는 아직 확인하지 못했습니다.",
                    "투표 결과나 Tibo의 새 예고가 확인되면 예상을 다시 검토합니다.", evidence, warning);
            else
                forecast = new(ResetForecastState.Weak, warning is null ? "추가 리셋 단서 대기" : "수집 불완전 · 예측 보류",
                    warning is null ? "가까운 리셋을 뒷받침할 단서 부족" : "현재 전망을 확인할 수 없음", "구체적인 실행 시각 추정 불가",
                    ongoing is not null ? "낮음 · 조건부 약속만 확인" : "낮음 · 미래 예고 없음",
                    ongoing is not null ? "Tibo의 약속은 아직 유효합니다. 다만 다음 리셋의 시점을 알려주는 투표나 예고는 없습니다."
                        : "지금 확인한 글만으로는 다음 리셋이 언제 일어날지 예상하기 어렵습니다.",
                    "새 리셋 투표나 답글, 직접 예고가 나오면 다시 확인합니다.", evidence, warning);
        }

        foreach (var context in (outlook.CurrentContext ?? []).Where(c => c.At <= now && now - c.At <= TimeSpan.FromHours(36)).OrderByDescending(c => c.At))
        {
            if (evidence.Any(e => e.Id == context.Id)) continue;
            evidence.Add(new(context.Id, context.At, ForecastEvidenceRole.Context, context.Kind == ResetCurrentContextKind.Completion
                ? "Tibo가 최근 리셋을 마쳤다는 완료 공지를 올렸습니다. 그 전에 나온 투표와 힌트는 다음 리셋을 예상할 때 쓰지 않습니다."
                : "Tibo가 최근 Codex 개선 내용을 정리했습니다. 이 글만으로 리셋 약속의 조건이 맞았는지, 리셋이 취소됐는지는 알 수 없습니다.", context.Text));
        }
        return forecast with { Evidence = evidence.Take(5).ToList() };
    }

    private static ForecastEvidence FromSignal(ResetSignal signal, ForecastEvidenceRole role, string summary) => new(signal.Id, signal.PostedAt, role, summary, signal.Evidence, signal.Context);
}
