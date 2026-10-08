using CodexAccountMonitor.Core;

internal static class CreditGrantNewsTests
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-10-08T02:00:00Z");
        var promise = new ResetSignal("planned", ResetSignalKind.CreditGrant, ResetSignalLevel.Announced, now.AddHours(-6), now.AddDays(1), now.AddHours(5),
            "Grant", "Grant promised", "Public deadline", "A banked reset will arrive today.");
        var outlook = new ResetOutlook { CheckedAt = now, Signals = [promise] };
        var account = new AccountSnapshot { SourceId = "a", AccountId = "id", Email = "user@example.com", UpdatedAt = now, ResetCredits = 3,
            ResetCreditDetails = [new("old", "codexRateLimits", "available", now.AddDays(-8), now.AddDays(22), null),
                new("new", "codexRateLimits", "available", now.AddHours(-2), now.AddDays(28), null)] };
        CreditGrantNews? Build(ResetOutlook source, params NamedAccountSnapshot[] accounts) => CreditGrantNewsBuilder.Build(source, accounts, now);
        var news = Build(outlook, new NamedAccountSnapshot("Personal", account))!;
        check(news is { State: CreditGrantState.AccountConfirmed, ConfirmedAccounts: 1, CreditsPerAccount: 1 }
            && news.Accounts[0].AvailableCount == 3, "recent server grant is displayed as +1 rather than the total held credits");
        check(news.Timing.Contains("발급 확인") && news.DeadlineEvidence == promise.Timing, "account receipt time replaces the pending deadline while original timing stays in evidence");
        news = Build(outlook, new NamedAccountSnapshot("Local", account), new NamedAccountSnapshot("SSH", account))!;
        check(news.ConfirmedAccounts == 1 && news.Accounts.Single().Name == "Local / SSH", "duplicate connections to one identity cannot inflate received-account counts");
        var stale = new AccountSnapshot { SourceId = account.SourceId, Email = account.Email, AccountId = account.AccountId, UpdatedAt = now.AddHours(-1),
            ResetCredits = account.ResetCredits, ResetCreditDetails = account.ResetCreditDetails };
        news = Build(outlook, new NamedAccountSnapshot("Stale", stale))!;
        check(news.State == CreditGrantState.Announced && news.Accounts.Single().State == CreditGrantAccountState.Unknown, "stale account reads cannot confirm a newly announced credit grant");
        var unknown = new AccountSnapshot { SourceId = "unknown", AccountId = "other", Email = "other@example.com", UpdatedAt = now, ResetCredits = 3 };
        news = Build(outlook, new NamedAccountSnapshot("Unknown", unknown))!;
        check(news.Accounts.Single().State == CreditGrantAccountState.Unknown && news.CreditsPerAccount is null, "a held credit count alone cannot prove receipt of a new grant");
        var laterPromise = promise with { PostedAt = now.AddHours(-1) };
        news = Build(new() { CheckedAt = now, Signals = [laterPromise] }, new NamedAccountSnapshot("Personal", account))!;
        check(news.State == CreditGrantState.Announced && news.ConfirmedAccounts == 0, "an earlier account grant cannot fulfill a newer announcement");
        news = Build(new() { CheckedAt = now, Signals = [promise with { Evidence = "Loading a banked reset for paid accounts." }] }, new NamedAccountSnapshot("Unknown", unknown))!;
        check(news.State == CreditGrantState.Distributing, "an in-progress credit announcement is distinct from account confirmation");
        news = Build(new() { CheckedAt = now, Signals = [promise with { DueAt = now.AddMinutes(-1) }] }, new NamedAccountSnapshot("Unknown", unknown))!;
        check(news.State == CreditGrantState.AwaitingConfirmation, "a passed grant deadline does not automatically become a completed payout");
        var completed = new ResetOutlook { CheckedAt = now, Completions = [new("done", ResetSignalKind.CreditGrant, now.AddHours(-1))] };
        news = Build(completed, new NamedAccountSnapshot("Unknown", unknown))!;
        check(news.State == CreditGrantState.Completed && news.ConfirmedAccounts == 0, "a public completion remains separate from my account's receipt");
        news = Build(new(), new NamedAccountSnapshot("Personal", account))!;
        check(news.State == CreditGrantState.AccountConfirmed && news.SourceUrl is null, "recent server-issued credits remain visible even when there is no collected announcement");
        var oldOnly = new AccountSnapshot { SourceId = "old", AccountId = "old", Email = "old@example.com", UpdatedAt = now, ResetCredits = 1,
            ResetCreditDetails = [account.ResetCreditDetails[0]] };
        check(Build(new(), new NamedAccountSnapshot("Old", oldOnly)) is null, "old held credits do not create a perpetual news card");
        var redeemed = new AccountSnapshot { SourceId = account.SourceId, AccountId = account.AccountId, Email = account.Email, UpdatedAt = now, ResetCredits = 0,
            ResetCreditDetails = [account.ResetCreditDetails[1] with { Status = "redeemed" }] };
        news = Build(outlook, new NamedAccountSnapshot("Used", redeemed))!;
        check(news.State == CreditGrantState.AccountConfirmed && news.Accounts.Single().AvailableCount == 0, "a newly received and already redeemed credit does not disappear from receipt confirmation");
        var noIdentity = new AccountSnapshot { SourceId = "none", UpdatedAt = now, ResetCredits = 3, ResetCreditDetails = account.ResetCreditDetails };
        news = Build(outlook, new NamedAccountSnapshot("No identity", noIdentity))!;
        check(news.Accounts.Single().State == CreditGrantAccountState.Unknown, "an unidentified read cannot certify an account's new grant");
    }
}
