using System.Text.Json;

namespace CodexAccountMonitor.Core;

public static class UsageParser
{
    public static AccountSnapshot Parse(string sourceId, JsonElement accountResult, JsonElement? limitsResult, JsonElement? usageResult)
    {
        var result = new AccountSnapshot { SourceId = sourceId, UpdatedAt = DateTimeOffset.UtcNow };
        var account = Get(accountResult, "account");
        result.Email = String(account, "email");
        result.Plan = String(account, "planType");
        result.AuthType = String(account, "type") ?? "unknown";
        if (limitsResult is { } limits)
        {
            result.AccountId = String(limits, "accountId");
            var allowed = Get(limits, "ordinaryUsageAllowed");
            result.OrdinaryUsageAllowed = allowed.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
            var buckets = Get(limits, "rateLimitsByLimitId");
            if (buckets.ValueKind == JsonValueKind.Object && buckets.EnumerateObject().Any())
                foreach (var bucket in buckets.EnumerateObject()) AddBucket(result, bucket.Name, bucket.Value);
            else
                AddBucket(result, String(Get(limits, "rateLimits"), "limitId") ?? "codex", Get(limits, "rateLimits"));
            var resetCredits = Get(limits, "rateLimitResetCredits");
            result.ResetCredits = Int(resetCredits, "availableCount") is >= 0 and var count ? count : null;
            var details = Get(resetCredits, "credits");
            if (details.ValueKind == JsonValueKind.Array)
                result.ResetCreditDetails = details.EnumerateArray().Where(x => !string.IsNullOrWhiteSpace(String(x, "id")))
                    .Select(x => new ResetCredit(String(x, "id")!, String(x, "resetType") ?? "unknown", String(x, "status") ?? "unknown",
                        Timestamp(x, "grantedAt"), Timestamp(x, "expiresAt"), String(x, "title"))).DistinctBy(x => x.Id).ToList();
        }
        if (usageResult is { } usage)
        {
            var summary = Get(usage, "summary");
            result.LifetimeTokens = Long(summary, "lifetimeTokens");
            result.StreakDays = Int(summary, "currentStreakDays");
            var daily = Get(usage, "dailyUsageBuckets");
            if (daily.ValueKind == JsonValueKind.Array)
                result.Daily = daily.EnumerateArray().Where(x => String(x, "startDate") is not null && Long(x, "tokens") is not null)
                    .Select(x => new DailyTokens(String(x, "startDate")!, Long(x, "tokens")!.Value)).OrderBy(x => x.Date, StringComparer.Ordinal).ToList();
        }
        return result;
    }

    private static void AddBucket(AccountSnapshot snapshot, string bucket, JsonElement data)
    {
        var label = String(data, "limitName") ?? bucket;
        foreach (var key in new[] { "primary", "secondary" })
        {
            var window = Get(data, key);
            var percent = Get(window, "usedPercent");
            if (percent.ValueKind != JsonValueKind.Number || !percent.TryGetDouble(out var value) || !double.IsFinite(value)) continue;
            DateTimeOffset? reset = null;
            if (Long(window, "resetsAt") is { } seconds)
                try { reset = DateTimeOffset.FromUnixTimeSeconds(seconds); } catch (ArgumentOutOfRangeException) { }
            snapshot.Windows.Add(new(bucket, label, value, Long(window, "windowDurationMins"), reset));
        }
    }

    public static JsonElement Get(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child) ? child : default;
    public static string? String(JsonElement element, string name) => Get(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
    public static long? Long(JsonElement element, string name) => Get(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number) ? number : null;
    public static DateTimeOffset? Timestamp(JsonElement element, string name)
    {
        if (Long(element, name) is { } seconds)
            try { return DateTimeOffset.FromUnixTimeSeconds(seconds); } catch (ArgumentOutOfRangeException) { }
        return null;
    }
    private static int? Int(JsonElement element, string name) => Long(element, name) is { } number && number is >= int.MinValue and <= int.MaxValue ? (int)number : null;
}
