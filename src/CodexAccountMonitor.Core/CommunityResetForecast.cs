namespace CodexAccountMonitor.Core;

// A sourced estimate, kept separate from our evidence judgment and from account quotas.
public sealed record CommunityResetForecast(double Within24Hours, double Within48Hours,
    DateTimeOffset UpdatedAt, DateTimeOffset FetchedAt, string Method, string Summary)
{
    public const string SourceUrl = "https://codexreset.org/";
    public bool IsCurrent(DateTimeOffset now) => double.IsFinite(Within24Hours) && double.IsFinite(Within48Hours)
        && Within24Hours >= 0 && Within48Hours <= 100 && Within24Hours <= Within48Hours
        && UpdatedAt <= now.AddMinutes(5) && FetchedAt <= now.AddMinutes(5)
        && now - UpdatedAt <= TimeSpan.FromHours(6) && now - FetchedAt <= TimeSpan.FromMinutes(20);
    public string Reason => Method == "empirical-baseline"
        ? "과거 리셋 간격을 기준으로 계산한 확률입니다. 새로운 예고가 반영된 수치는 아닙니다."
        : "CodexReset이 공개 게시물과 리셋 기록으로 계산한 확률입니다. 앱의 게시물 판단과 다를 수 있습니다.";
}
