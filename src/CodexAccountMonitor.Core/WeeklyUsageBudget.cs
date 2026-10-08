namespace CodexAccountMonitor.Core;

public enum WeeklyPace { Unknown, Steady, Fast, Roomy, Exhausted }

public sealed record WeeklyUsageBudget(QuotaWindow? Window, WeeklyPace Pace, string? UnavailableReason,
    double RemainingDays = 0, double UsedPercent = 0, double ElapsedPercent = 0, double DailyPercent = 0)
{
    public bool IsAvailable => Pace != WeeklyPace.Unknown;
    public double RemainingPercent => 100 - UsedPercent;
    public double AheadPercentPoints => UsedPercent - ElapsedPercent;
    public bool IsLastDay => IsAvailable && RemainingDays < 1;

    // This is an evenly distributed reference, not a server limit or a token estimate.
    public static IReadOnlyList<WeeklyUsageBudget> Build(AccountSnapshot? snapshot, bool current, DateTimeOffset now)
    {
        var windows = snapshot?.Windows.Where(w => w.DurationMinutes == 7 * 24 * 60).ToArray() ?? [];
        if (windows.Length == 0) return [new(null, WeeklyPace.Unknown, "주간 한도 미제공")];
        return windows.Select(window => Calculate(window, snapshot!.UpdatedAt, current, now)).ToArray();
    }

    private static WeeklyUsageBudget Calculate(QuotaWindow window, DateTimeOffset updatedAt, bool current, DateTimeOffset now)
    {
        WeeklyUsageBudget Unknown(string reason) => new(window, WeeklyPace.Unknown, reason);
        if (!current || now - updatedAt > TimeSpan.FromMinutes(10) || updatedAt - now > TimeSpan.FromMinutes(5))
            return Unknown("최신 사용량 확인 필요");
        if (window.ResetsAt is not { } resetsAt) return Unknown("정기 리셋 시각 미제공");
        var remainingDays = (resetsAt - now).TotalDays;
        if (remainingDays <= 0) return Unknown("리셋 시각 지남 · 갱신 대기");
        if (remainingDays > 7 || !double.IsFinite(window.UsedPercent)) return Unknown("주간 한도 정보 확인 필요");
        var used = Math.Clamp(window.UsedPercent, 0, 100);
        var elapsed = (1 - remainingDays / 7) * 100;
        var pace = used >= 100 ? WeeklyPace.Exhausted
            : used - elapsed > 5 ? WeeklyPace.Fast
            : used - elapsed < -5 ? WeeklyPace.Roomy : WeeklyPace.Steady;
        // Under 24 hours, show the total remaining allowance rather than extrapolating a full day.
        return new(window, pace, null, remainingDays, used, elapsed, (100 - used) / Math.Max(1, remainingDays));
    }
}
