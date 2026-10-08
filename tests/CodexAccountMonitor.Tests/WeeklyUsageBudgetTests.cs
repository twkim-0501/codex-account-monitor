using CodexAccountMonitor.Core;

internal static class WeeklyUsageBudgetTests
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-10-08T02:00:00Z");
        AccountSnapshot Snapshot(double used = 60, double days = 4) => new()
        {
            UpdatedAt = now,
            Windows = [new("codex", "Codex", 99, 300, now.AddHours(3)), new("codex", "Codex", used, 10080, now.AddDays(days))]
        };
        WeeklyUsageBudget Budget(AccountSnapshot data, bool current = true, DateTimeOffset? at = null) => WeeklyUsageBudget.Build(data, current, at ?? now).Single();
        var example = Budget(Snapshot());
        check(example.IsAvailable && example.RemainingPercent == 40 && example.RemainingDays == 4 && example.DailyPercent == 10,
            "60 percent used with four days left allows ten percentage points of weekly quota per day");
        check(Math.Abs(example.ElapsedPercent - 300d / 7) < 0.001 && example.Pace == WeeklyPace.Fast,
            "weekly pacing compares used quota with the elapsed fraction of its seven-day period");
        check(example.Window!.DurationMinutes == 10080, "five-hour quota does not enter the weekly daily budget");
        check(Budget(Snapshot(20, 2)).Pace == WeeklyPace.Roomy && Budget(Snapshot(43)).Pace == WeeklyPace.Steady,
            "slower use and near-even use have separate pacing states");
        check(Budget(Snapshot(100)).Pace == WeeklyPace.Exhausted && Budget(Snapshot(100)).DailyPercent == 0,
            "exhausted weekly quota has zero budget even when time remains");
        var lastDay = Budget(Snapshot(80, 0.05));
        check(lastDay.IsLastDay && lastDay.DailyPercent == 20 && lastDay.RemainingPercent == 20,
            "a near reset never suggests spending more than the total remaining quota in a day");
        check(Budget(Snapshot(0, 7)).ElapsedPercent == 0 && Math.Abs(Budget(Snapshot(0, 7)).DailyPercent - 100d / 7) < 0.001,
            "a fresh reset uses the newly returned full seven-day period");
        var changed = Snapshot(10, 7);
        check(Budget(changed).RemainingDays == 7 && Math.Abs(Budget(changed).DailyPercent - 90d / 7) < 0.001,
            "a credit redemption's new quota and reset timestamp immediately replace the previous budget");
        check(!Budget(Snapshot(), false).IsAvailable, "a failed refresh hides the plan even if the cached timestamp is recent");
        var stale = Snapshot(); stale.UpdatedAt = now.AddMinutes(-11);
        check(!Budget(stale).IsAvailable, "stale quota cannot produce a current spending recommendation");
        stale.UpdatedAt = now.AddMinutes(6);
        check(!Budget(stale).IsAvailable, "a future-dated snapshot is not considered fresh");
        var missingTime = Snapshot(); missingTime.Windows[1] = missingTime.Windows[1] with { ResetsAt = null };
        check(!Budget(missingTime).IsAvailable, "missing reset time stays unknown rather than assuming a calendar week");
        check(!Budget(Snapshot(days: 0)).IsAvailable && !Budget(Snapshot(days: -1)).IsAvailable,
            "a passed reset waits for a fresh server read instead of automatically restoring quota");
        check(!Budget(Snapshot(days: 8)).IsAvailable, "a reset outside its seven-day period does not invent an elapsed fraction");
        check(!Budget(Snapshot(double.NaN)).IsAvailable && !Budget(Snapshot(double.PositiveInfinity)).IsAvailable,
            "non-finite percentages cannot create a daily budget");
        check(Budget(Snapshot(110)).RemainingPercent == 0 && Budget(Snapshot(-10)).RemainingPercent == 100,
            "numeric percentages clamp consistently with existing quota displays");
        check(!WeeklyUsageBudget.Build(null, true, now).Single().IsAvailable &&
            !WeeklyUsageBudget.Build(new() { UpdatedAt = now, Windows = [new("codex", "Codex", 20, 300, now.AddHours(3))] }, true, now).Single().IsAvailable,
            "missing weekly data is never replaced by a five-hour window");
        var multiple = Snapshot(); multiple.Windows.Add(new("other", "Other", 80, 10080, now.AddDays(2)));
        var budgets = WeeklyUsageBudget.Build(multiple, true, now);
        check(budgets.Count == 2 && budgets[0].Window!.Bucket == "codex" && budgets[1].Window!.Bucket == "other",
            "each weekly bucket keeps its own remaining time and allowance");
        var beforeReset = Snapshot(90, 0.001);
        check(!Budget(beforeReset, at: now.AddMinutes(2)).IsAvailable && Budget(Snapshot(0, 7)).IsAvailable,
            "elapsed time alone expires a plan; only new server data establishes a new period");
    }
}
