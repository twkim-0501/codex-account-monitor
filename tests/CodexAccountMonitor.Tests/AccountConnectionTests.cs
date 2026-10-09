using System.Diagnostics;
using System.Text.Json;
using CodexAccountMonitor.Core;

internal static class AccountConnectionTests
{
    private sealed record State(string Email = "before@example.com", int Revision = 1, double UsedPercent = 30,
        bool DenyAuth = false, bool UnsupportedUsage = false, bool ChangeDuringRead = false);

    public static async Task RunServerAsync(string statePath)
    {
        State Read() => File.Exists(statePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(statePath))! : new(Email: "");
        var initial = Read();
        string? line;
        while ((line = await Console.In.ReadLineAsync()) is not null)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var id = UsageParser.Get(root, "id");
            if (id.ValueKind != JsonValueKind.Number) continue;
            var number = id.GetInt64();
            var method = UsageParser.String(root, "method");
            var current = Read();
            object Result(object result) => new { id = number, result };
            object Error(int code, string message) => new { id = number, error = new { code, message } };
            object reply;
            if (method == "account/read") reply = Result(new { account = string.IsNullOrEmpty(initial.Email) ? null : new { type = "chatgpt", email = initial.Email, planType = "pro" } });
            else if (method == "account/usage/read" && current.UnsupportedUsage) reply = Error(-32601, "Unknown authentication method");
            else if (method is "account/rateLimits/read" or "account/usage/read")
            {
                if (current.ChangeDuringRead && method == "account/rateLimits/read")
                {
                    current = current with { Email = "after@example.com", Revision = current.Revision + 1, UsedPercent = 70, ChangeDuringRead = false };
                    File.WriteAllText(statePath, JsonSerializer.Serialize(current));
                    // Mimic a successful response that races with an external login change.
                }
                else if (current.DenyAuth || current.Revision != initial.Revision)
                {
                    Console.WriteLine(JsonSerializer.Serialize(Error(-32000, "401 Unauthorized: token_revoked")));
                    continue;
                }
                reply = method == "account/rateLimits/read"
                    ? Result(new { accountId = current.Email, rateLimits = new { secondary = new { usedPercent = current.UsedPercent, windowDurationMins = 10080, resetsAt = DateTimeOffset.UtcNow.AddDays(4).ToUnixTimeSeconds() } } })
                    : Result(new { summary = new { lifetimeTokens = 1000 }, dailyUsageBuckets = Array.Empty<object>() });
            }
            else reply = Result(new { ready = true });
            Console.WriteLine(JsonSerializer.Serialize(reply));
        }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "CodexMonitor-connection-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var launches = 0;
            ProcessStartInfo Launch(string path)
            {
                launches++;
                var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                info.ArgumentList.Add(typeof(AccountConnectionTests).Assembly.Location);
                info.ArgumentList.Add("--fake-account-server"); info.ArgumentList.Add(path);
                return info;
            }
            async Task Write(string path, State state)
            {
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state));
                // Guarantee a changed timestamp on filesystems with coarse metadata precision.
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(state.Revision));
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var identityState = Path.Combine(directory, "identity.json");
            await Write(identityState, new(DenyAuth: true));
            await using (var connection = new AccountConnection(new() { Kind = "ssh" }, () => Launch(identityState)))
            {
                var identity = await connection.ReadIdentityAsync(timeout.Token);
                check(identity.Email == "before@example.com" && identity.Windows.Count == 0 && identity.LifetimeTokens is null && launches == 1,
                    "desktop identity polling reads only account information and avoids quota and token requests");
            }
            launches = 0;
            var localHome = Path.Combine(directory, "local"); Directory.CreateDirectory(localHome);
            var localState = Path.Combine(localHome, "auth.json");
            await Write(localState, new());
            await using (var connection = new AccountConnection(new() { Id = "local", CodexHome = localHome }, () => Launch(localState)))
            {
                var first = await connection.ReadAsync(timeout.Token);
                var second = await connection.ReadAsync(timeout.Token);
                check(first.Email == "before@example.com" && launches == 1 && second.Windows.Count == 1,
                    "unchanged local credentials reuse the existing connection");
                await Write(localState, new("after@example.com", 2, 65));
                var switched = await connection.ReadAsync(timeout.Token);
                check(switched.Email == "after@example.com" && switched.Windows.Single().RemainingPercent == 35 && launches == 2,
                    "external local login change reloads account and quota in one connection");
                var switchedIdentity = await connection.ReadIdentityAsync(timeout.Token);
                check(switchedIdentity.Email == switched.Email && switchedIdentity.Windows.Count == 0 && launches == 2,
                    "identity polling follows a changed desktop login using the recovered connection");
                await Write(localState, new("after@example.com", 3, 65));
                var renewed = await connection.ReadAsync(timeout.Token);
                check(renewed.Email == switched.Email && launches == 3, "credential renewal for the same account reloads its auth state");
                File.Delete(localState);
                try { await connection.ReadAsync(timeout.Token); throw new Exception("Logout accepted"); }
                catch (InvalidOperationException) { check(launches == 5, "local logout cannot keep showing a healthy previous account"); }
            }
            launches = 0;
            var remoteState = Path.Combine(directory, "remote.json"); await Write(remoteState, new());
            await using (var connection = new AccountConnection(new() { Id = "remote", Kind = "ssh", SshHost = "fake" }, () => Launch(remoteState)))
            {
                await connection.ReadAsync(timeout.Token);
                await Write(remoteState, new("remote-after@example.com", 2, 80));
                var recovered = await connection.ReadAsync(timeout.Token);
                check(launches == 2 && recovered.Email == "remote-after@example.com" && recovered.Windows.Single().RemainingPercent == 20,
                    "stale remote auth reconnects once and returns fresh quota instead of an empty successful snapshot");
                await Write(remoteState, new("remote-after@example.com", 2, 80, UnsupportedUsage: true));
                var limited = await connection.ReadAsync(timeout.Token);
                check(launches == 2 && limited.Windows.Count == 1 && limited.UsageNote == "설치된 Codex 버전에서 미지원",
                    "unsupported optional usage does not reconnect or hide valid quota");
                await Write(remoteState, new("remote-after@example.com", 2, 80, DenyAuth: true));
                try { await connection.ReadAsync(timeout.Token); throw new Exception("Revoked auth accepted"); }
                catch (RpcException error) { check(AccountConnection.IsAuthenticationError(error) && launches == 3,
                    "persistent auth failure retries only once and remains a failed read"); }
                await Write(remoteState, new("remote-after@example.com", 3, 10));
                var relogged = await connection.ReadAsync(timeout.Token);
                check(launches == 4 && relogged.Windows.Single().RemainingPercent == 90,
                    "a later login recovers without restarting the monitor");
            }
            launches = 0;
            await Write(localState, new(ChangeDuringRead: true));
            await using (var connection = new AccountConnection(new() { CodexHome = localHome }, () => Launch(localState)))
            {
                var raced = await connection.ReadAsync(timeout.Token);
                check(launches == 2 && raced.Email == "after@example.com" && raced.Windows.Single().RemainingPercent == 30,
                    "a login change during quota reads cannot mix the old identity with new usage");
            }
            check(AccountConnection.IsAuthenticationError(new(-32000, "Failed to refresh auth")) &&
                !AccountConnection.IsAuthenticationError(new(-32601, "Unknown authentication method")) &&
                !AccountConnection.IsAuthenticationError(new(-32000, "Quota unavailable for this subscription")),
                "authentication recovery stays separate from unsupported methods and unavailable plan metrics");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
