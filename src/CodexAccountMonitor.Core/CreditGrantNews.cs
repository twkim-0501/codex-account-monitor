namespace CodexAccountMonitor.Core;

public enum CreditGrantState { Announced, Distributing, AccountConfirmed, Completed, AwaitingConfirmation, Hint }
public enum CreditGrantAccountState { Confirmed, Awaiting, Unknown }
public sealed record NamedAccountSnapshot(string Name, AccountSnapshot? Snapshot);
public sealed record CreditGrantAccount(string Name, CreditGrantAccountState State, int? AvailableCount, int NewCredits, DateTimeOffset? GrantedAt);
public sealed record CreditGrantNews(CreditGrantState State, string Headline, string Badge, string Timing, string Explanation,
    string? SourceId, DateTimeOffset? PostedAt, int? CreditsPerAccount, List<CreditGrantAccount> Accounts, string? DeadlineEvidence)
{
    public int ConfirmedAccounts => Accounts.Count(a => a.State == CreditGrantAccountState.Confirmed);
    public string? SourceUrl => SourceId is null ? null : $"https://x.com/thsottiaux/status/{SourceId}";
}

public static class CreditGrantNewsBuilder
{
    public static CreditGrantNews? Build(ResetOutlook outlook, IEnumerable<NamedAccountSnapshot> input, DateTimeOffset now)
    {
        // This is the latest relevant item, not a news archive or a rollout completion percentage.
        var sources = input.ToArray();
        var signal = ResetJudgment.Active(outlook, now).Where(s => s.Kind == ResetSignalKind.CreditGrant && s.PostedAt <= now)
            .OrderByDescending(s => s.PostedAt).FirstOrDefault();
        var completion = outlook.Completions.Where(c => c.Kind == ResetSignalKind.CreditGrant && c.At <= now && now - c.At <= TimeSpan.FromHours(48))
            .MaxBy(c => c.At);
        if (signal is not null && completion is not null && completion.At >= signal.PostedAt && (signal.NotBefore is null || completion.At >= signal.NotBefore)) signal = null;
        else if (signal is not null) completion = null;
        var recentStart = now.AddHours(-48);
        var start = signal?.PostedAt.AddMinutes(-5) ?? completion?.At.AddHours(-24) ?? recentStart;
        if (start < recentStart) start = recentStart;
        var accounts = new List<CreditGrantAccount>();
        foreach (var group in sources.GroupBy(s => s.Snapshot?.IdentityKey ?? "source:" + s.Snapshot?.SourceId + ":" + s.Name))
        {
            var snapshot = group.Where(s => s.Snapshot is not null).MaxBy(s => s.Snapshot!.UpdatedAt)?.Snapshot;
            var name = string.Join(" / ", group.Select(s => s.Name).Distinct(StringComparer.Ordinal));
            var fresh = snapshot?.IdentityKey is not null && snapshot.UpdatedAt <= now.AddMinutes(5) && now - snapshot.UpdatedAt <= TimeSpan.FromMinutes(10);
            var details = snapshot?.ResetCreditDetails;
            var received = fresh ? (details ?? []).Where(c => (c.IsAvailable || c.Status.Equals("redeemed", StringComparison.OrdinalIgnoreCase))
                && c.GrantedAt is { } at && at >= start && at <= now).DistinctBy(c => c.Id).ToArray() : [];
            var knownEmpty = snapshot?.ResetCredits == 0 || details is { Count: > 0 } && details.All(c => c.GrantedAt is not null);
            var state = received.Length > 0 ? CreditGrantAccountState.Confirmed : fresh && knownEmpty ? CreditGrantAccountState.Awaiting : CreditGrantAccountState.Unknown;
            accounts.Add(new(name, state, fresh ? snapshot?.ResetCredits : null, received.Length, received.MaxBy(c => c.GrantedAt)?.GrantedAt));
        }
        var confirmed = accounts.Where(a => a.State == CreditGrantAccountState.Confirmed).ToArray();
        if (signal is null && completion is null && confirmed.Length == 0) return null;
        var stateOfNews = confirmed.Length > 0 ? CreditGrantState.AccountConfirmed : completion is not null ? CreditGrantState.Completed
            : signal!.Level != ResetSignalLevel.Announced ? CreditGrantState.Hint : signal.DueAt < now ? CreditGrantState.AwaitingConfirmation
            : signal.Evidence.Contains("loading", StringComparison.OrdinalIgnoreCase) || signal.Context?.Any(c => c.Author == "thsottiaux" && c.Text.Contains("loading", StringComparison.OrdinalIgnoreCase)) == true
                ? CreditGrantState.Distributing : CreditGrantState.Announced;
        var (headline, badge) = stateOfNews switch
        {
            CreditGrantState.AccountConfirmed => ("새 초기화권 도착", "내 계정 확인"),
            CreditGrantState.Completed => ("초기화권 지급 완료 공지", "완료 공지"),
            CreditGrantState.Distributing => ("초기화권 지급 진행 중", "지급 진행 중"),
            CreditGrantState.AwaitingConfirmation => ("지급 시각 경과 · 확인 대기", "확인 대기"),
            CreditGrantState.Hint => ("초기화권 지급 단서", "미확정"),
            _ => ("초기화권 지급 예정", "지급 예고")
        };
        var timing = confirmed.Length > 0 ? "발급 확인 · " + ResetJudgment.KoreanTime(confirmed.Max(a => a.GrantedAt)!.Value)
            : signal?.DueAt is { } due ? ResetJudgment.KoreanTime(due) + "까지 예상"
            : completion is not null ? "완료 공지 · " + ResetJudgment.KoreanTime(completion.At) : "지급 시각 미정";
        var quantities = confirmed.Select(a => a.NewCredits).Distinct().ToArray();
        var explanation = confirmed.Length > 0 ? "Codex 서버의 발급 기록에서 새 초기화권이 들어온 것을 확인했습니다. +1은 보유 개수가 아니라 새로 받은 개수입니다."
            : completion is not null ? "Tibo가 초기화권 지급을 마쳤다고 알렸습니다. 내 계정에 들어왔는지는 아래에서 확인합니다."
            : signal!.Reason;
        return new(stateOfNews, headline, badge, timing, explanation, signal?.Id ?? completion?.Id, signal?.PostedAt ?? completion?.At,
            quantities.Length == 1 ? quantities[0] : null, accounts, signal?.Timing);
    }
}
