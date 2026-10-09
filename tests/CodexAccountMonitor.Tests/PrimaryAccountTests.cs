using CodexAccountMonitor.Core;

internal static class PrimaryAccountTests
{
    public static void Run(Action<bool, string> check)
    {
        var hrk = new AccountSource { Id = "hrk", Name = "First", Kind = "ssh" };
        var lab = new AccountSource { Id = "lab", Name = "Lab", Kind = "ssh" };
        var twkim = new AccountSource { Id = "twkim", Name = "Third", Kind = "ssh" };
        var current = new AccountSource { Id = "desktop-current", Name = "Current" };
        AccountSnapshot Identity(string email) => new() { Email = email, AuthType = "chatgpt", UpdatedAt = DateTimeOffset.UtcNow };
        AccountSource[] sources = [hrk, lab, twkim];
        var snapshots = new Dictionary<string, AccountSnapshot> { [hrk.Id] = Identity("first@example.com"), [lab.Id] = Identity("lab@example.com"), [twkim.Id] = Identity("third@example.com") };
        AccountDisplayOrder Build(AccountSnapshot? identity, IEnumerable<AccountSource>? configured = null) => PrimaryAccountOrdering.Build(configured ?? sources, snapshots, identity, current);
        var order = Build(Identity("third@example.com"));
        check(order.PrimarySourceId == twkim.Id && order.Sources.Select(s => s.Id).SequenceEqual(new[] { "twkim", "hrk", "lab" }),
            "the desktop account leads the display while other connections retain their order");
        check(sources[0] == hrk && sources[2] == twkim && twkim.Kind == "ssh", "primary display does not rewrite saved connection order or authentication sources");
        order = Build(Identity("first@example.com"));
        check(order.PrimarySourceId == hrk.Id && order.Sources[0] == hrk, "switching the desktop login moves primary to the matching tracked account");
        check(Build(Identity(" THIRD@example.com ")).PrimarySourceId == twkim.Id, "primary matching uses normalized full email rather than display names");
        twkim.Name = "first@example.com";
        check(Build(Identity("first@example.com")).PrimarySourceId == hrk.Id, "a matching display name cannot impersonate another account's identity");
        check(Build(null).PrimarySourceId is null && Build(null).Sources.SequenceEqual(sources), "unknown desktop identity does not keep an old primary designation");
        check(Build(new() { AuthType = "apiKey", Email = "third@example.com" }).PrimarySourceId is null &&
            Build(new() { AuthType = "chatgpt" }).PrimarySourceId is null, "API-key mode and missing email do not invent a primary account");
        var local = new AccountSource { Id = "local", Kind = "local" }; snapshots[local.Id] = Identity("third@example.com");
        order = Build(Identity("third@example.com"), sources.Append(local));
        check(order.PrimarySourceId == local.Id && order.Sources.Count(s => s.Id == local.Id) == 1,
            "a default local connection is preferred among duplicate matching connections without duplicating it");
        twkim.Enabled = false;
        order = Build(Identity("third@example.com"));
        check(order.PrimarySourceId == current.Id && order.Sources[0] == current && !order.Sources.Contains(twkim),
            "disabled connections stay disabled while an untracked desktop account appears automatically");
        twkim.Enabled = true;
        order = Build(Identity("new@example.com"));
        check(order.Sources.Count == 4 && order.Sources[0] == current && order.Sources.Skip(1).SequenceEqual(sources),
            "a new desktop account is shown first without deleting existing account connections");
        order = Build(Identity("third@example.com"));
        check(order.Sources.Count == 3 && !order.Sources.Contains(current), "an existing matching SSH account does not gain a duplicate local row");
    }
}
