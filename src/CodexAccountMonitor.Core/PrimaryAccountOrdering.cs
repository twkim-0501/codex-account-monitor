namespace CodexAccountMonitor.Core;

public sealed record AccountDisplayOrder(IReadOnlyList<AccountSource> Sources, string? PrimarySourceId);

public static class PrimaryAccountOrdering
{
    public static AccountDisplayOrder Build(IEnumerable<AccountSource> sources, IReadOnlyDictionary<string, AccountSnapshot> snapshots,
        AccountSnapshot? desktopIdentity, AccountSource currentDesktop)
    {
        var enabled = sources.Where(s => s.Enabled).ToArray();
        if (desktopIdentity?.AuthType != "chatgpt" || string.IsNullOrWhiteSpace(desktopIdentity.Email)) return new(enabled, null);
        var matches = enabled.Where(source => snapshots.TryGetValue(source.Id, out var snapshot) &&
            snapshot.AuthType == "chatgpt" && string.Equals(snapshot.Email?.Trim(), desktopIdentity.Email.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        var primary = matches.FirstOrDefault(s => s.Kind == "local" && string.IsNullOrWhiteSpace(s.CodexHome))
            ?? matches.FirstOrDefault() ?? currentDesktop;
        return new(new[] { primary }.Concat(enabled.Where(s => s.Id != primary.Id)).ToArray(), primary.Id);
    }
}
