using System.Text.Json.Serialization;

namespace CodexAccountMonitor.Core;

public sealed class MonitorSettings
{
    public int RefreshSeconds { get; set; } = 60;
    public bool AlwaysOnTop { get; set; } = true;
    public bool AlertsEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool ShowMiniWidget { get; set; } = true;
    public bool DockMiniWidget { get; set; } = true;
    public List<AccountSource> Sources { get; set; } = [];
}

public sealed class AccountSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Local Codex";
    public string Kind { get; set; } = "local";
    public string? SshHost { get; set; }
    public string? CodexPath { get; set; }
    public string? CodexHome { get; set; }
    public bool Enabled { get; set; } = true;
    [JsonIgnore] public string Location => Kind == "ssh" ? SshHost ?? "SSH" : "이 PC";
}

public sealed record QuotaWindow(string Bucket, string Label, double UsedPercent, long? DurationMinutes, DateTimeOffset? ResetsAt)
{
    public double RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);
    public string DurationLabel => DurationMinutes switch
    {
        null => Label,
        >= 1440 when DurationMinutes % 1440 == 0 => $"{DurationMinutes / 1440}일 한도",
        >= 60 when DurationMinutes % 60 == 0 => $"{DurationMinutes / 60}시간 한도",
        _ => $"{DurationMinutes}분 한도"
    };
}

public sealed record DailyTokens(string Date, long Tokens);

public sealed class AccountSnapshot
{
    public string SourceId { get; set; } = "";
    public string? AccountId { get; set; }
    public string? Email { get; set; }
    public string? Plan { get; set; }
    public string AuthType { get; set; } = "unknown";
    public bool? OrdinaryUsageAllowed { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<QuotaWindow> Windows { get; set; } = [];
    public long? LifetimeTokens { get; set; }
    public List<DailyTokens>? Daily { get; set; }
    public int? ResetCredits { get; set; }
    public int? StreakDays { get; set; }
    public string? UsageNote { get; set; }
    public string? LimitsNote { get; set; }
    [JsonIgnore] public string? IdentityKey => !string.IsNullOrWhiteSpace(AccountId) && !string.IsNullOrWhiteSpace(Email)
        ? $"{Email.ToLowerInvariant()}|{AccountId}" : null;
}

public static class MetricFormatting
{
    public static string Tokens(long? value) => value switch
    {
        null => "—",
        >= 100_000_000 => $"{value / 100_000_000d:0.##}억",
        >= 10_000 => $"{value / 10_000d:0.##}만",
        _ => $"{value:N0}"
    };

    public static string Reset(QuotaWindow window, DateTimeOffset now)
    {
        if (window.ResetsAt is not { } reset) return "초기화 시각 미제공";
        if (reset <= now) return "초기화 시각 지남 · 갱신 대기";
        var span = reset - now;
        var relative = span.TotalDays >= 1 ? $"{(int)span.TotalDays}일 {span.Hours}시간 후"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours}시간 {span.Minutes}분 후" : $"{Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))}분 후";
        return $"{relative} · {reset.ToLocalTime():MM/dd HH:mm}";
    }

    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "계정 정보 미제공";
        var index = email.IndexOf('@');
        return index < 0 ? email : email[..Math.Min(3, index)] + "***" + email[index..];
    }
}
