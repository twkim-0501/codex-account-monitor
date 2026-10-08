namespace CodexAccountMonitor.Core;

// A sourced estimate, kept separate from our evidence judgment and from account quotas.
public sealed record CommunityResetForecast(double Within24Hours, double Within48Hours,
    DateTimeOffset UpdatedAt, DateTimeOffset FetchedAt, string Method, string Summary)
{
    public const string SourceUrl = "https://codexreset.org/";
    public bool IsCurrent(DateTimeOffset now, DateTimeOffset? lastResetAt = null) => double.IsFinite(Within24Hours) && double.IsFinite(Within48Hours)
        && Within24Hours >= 0 && Within48Hours <= 100 && Within24Hours <= Within48Hours
        && UpdatedAt <= now.AddMinutes(5) && FetchedAt <= now.AddMinutes(5)
        && now - UpdatedAt <= TimeSpan.FromHours(6) && now - FetchedAt <= TimeSpan.FromMinutes(20)
        && (lastResetAt is null || UpdatedAt > lastResetAt);
    public string Reason => Method == "empirical-baseline"
        ? "CodexReset이 과거 리셋 간격을 보고 계산한 예상 확률입니다. 새 리셋 예고는 이 숫자에 포함되지 않았습니다."
        : "CodexReset이 게시물과 과거 리셋 기록으로 계산한 예상 확률입니다. 오른쪽 상태 표시는 앱이 글을 읽고 정하므로 이 숫자와 다를 수 있습니다.";
}
