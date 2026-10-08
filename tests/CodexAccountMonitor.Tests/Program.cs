using System.Diagnostics;
using System.Text.Json;
using CodexAccountMonitor.Core;

if (args.Contains("--fake-server"))
{
    string? input;
    JsonElement? first = null;
    while ((input = Console.ReadLine()) is not null)
    {
        var root = JsonDocument.Parse(input).RootElement.Clone();
        var id = UsageParser.Get(root, "id");
        if (id.ValueKind == JsonValueKind.Undefined) continue;
        var method = UsageParser.String(root, "method");
        if (method == "hold") continue;
        if (method == "malformed") { Console.WriteLine(JsonSerializer.Serialize(new { id = id.GetInt64() })); continue; }
        if (method == "pair")
        {
            if (first is null) { first = root; continue; }
            Console.WriteLine(JsonSerializer.Serialize(new { id = id.GetInt64(), result = new { value = "second" } }));
            Console.WriteLine(JsonSerializer.Serialize(new { id = UsageParser.Get(first.Value, "id").GetInt64(), result = new { value = "first" } }));
            continue;
        }
        Console.WriteLine(method == "missing" ? JsonSerializer.Serialize(new { id = id.GetInt64(), error = new { code = -32601, message = "Unknown method" } })
            : JsonSerializer.Serialize(new { id = id.GetInt64(), result = new { ready = true } }));
    }
    return;
}

if (args.Contains("--feed-live"))
{
    using var feed = new ResetFeed();
    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(55));
    var outlook = await feed.ReadAsync(DateTimeOffset.UtcNow, cancel.Token);
    Console.WriteLine(JsonSerializer.Serialize(new { outlook.CheckedAt, outlook.SourceUpdatedAt, outlook.PartialCoverage, outlook.Note,
        signals = outlook.Signals.Select(s => new { s.Id, s.Level, s.Title, s.Timing, s.Reason, s.PollSummary }), outlook.Completions,
        outlook.CommunityForecast, forecast = ResetForecasting.Build(outlook, DateTimeOffset.UtcNow) }, new JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Contains("--live"))
{
    var sources = args.SkipWhile(x => x != "--live").Skip(1).ToArray();
    foreach (var target in sources.Length == 0 ? new[] { "local" } : sources)
    {
        var source = new AccountSource { Name = target, Kind = target == "local" ? "local" : "ssh", SshHost = target == "local" ? null : target };
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var connection = new AccountConnection(source);
        try
        {
            var data = await connection.ReadAsync(cancel.Token);
            Console.WriteLine(JsonSerializer.Serialize(new { target, email = MetricFormatting.MaskEmail(data.Email), data.AuthType, data.Plan, buckets = data.Windows.Count,
                windows = data.Windows.Select(x => new { x.Bucket, x.DurationMinutes, x.RemainingPercent }), hasLifetime = data.LifetimeTokens.HasValue,
                dailyCount = data.Daily?.Count, data.ResetCredits, creditDetails = data.ResetCreditDetails?.Select(c => new { c.Status, c.ExpiresAt }), data.LimitsNote, data.UsageNote }));
        }
        catch (Exception error) { Console.WriteLine(JsonSerializer.Serialize(new { target, error = error.GetType().Name, message = error is RpcException ? "RPC error" : error.Message })); Environment.ExitCode = 1; }
    }
    return;
}

var passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); passed++; }
JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
ResetTests.Run(Check);
ForecastTests.Run(Check);
CommunityForecastTests.Run(Check);
CreditGrantNewsTests.Run(Check);
await ResetFeedTests.RunAsync(Check);
var account = Json("""{"account":{"type":"chatgpt","email":"test@example.com","planType":"pro"}}""");
var snapshot = UsageParser.Parse("a", account,
    Json("""{"accountId":"workspace-one","ordinaryUsageAllowed":false,"rateLimits":{"primary":{"usedPercent":99}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":25,"windowDurationMins":300,"resetsAt":1800000000},"secondary":{"usedPercent":80,"windowDurationMins":10080}},"other":{"primary":{"usedPercent":110,"windowDurationMins":60}}},"rateLimitResetCredits":{"availableCount":2}}"""),
    Json("""{"summary":{"lifetimeTokens":100000000,"currentStreakDays":3},"dailyUsageBuckets":[{"startDate":"2026-10-05","tokens":10},{"startDate":"2026-10-04","tokens":8}]}"""));
Check(snapshot.Windows.Count == 3, "multiple quota buckets replace legacy bucket");
Check(snapshot.Windows[0].RemainingPercent == 75 && snapshot.Windows[2].RemainingPercent == 0, "remaining percentage clamps correctly");
Check(snapshot.OrdinaryUsageAllowed == false, "backend blocked state remains blocked despite remaining percentage");
Check(snapshot.Daily![0].Date == "2026-10-04" && snapshot.LifetimeTokens == 100000000, "daily buckets sort without inventing local today");
Check(snapshot.ResetCredits == 2 && snapshot.IdentityKey == "test@example.com|workspace-one", "identity includes workspace and reset credits are retained");
var missing = UsageParser.Parse("b", account, Json("""{"rateLimitsByLimitId":{},"rateLimits":{"primary":{"usedPercent":5}},"ordinaryUsageAllowed":null}"""), Json("""{"summary":{"lifetimeTokens":null},"dailyUsageBuckets":null}"""));
Check(missing.Windows.Count == 1 && missing.LifetimeTokens is null && missing.Daily is null && missing.IdentityKey is null, "missing data stays unknown and empty map falls back");
Check(missing.Windows[0].DurationLabel == "codex", "unknown quota duration is not hardcoded");
Check(UsageParser.Parse("c", account, Json("""{"rateLimits":{"primary":{"usedPercent":20,"resetsAt":9223372036854775807}}}"""), null).Windows[0].ResetsAt is null, "invalid reset timestamp is handled");
Check(MetricFormatting.Reset(snapshot.Windows[0], DateTimeOffset.FromUnixTimeSeconds(1900000000)).Contains("갱신 대기"), "past reset does not imply recovered quota");
Check(SourceLauncher.QuotePosix("a'b;$(touch x)") == "'a'\"'\"'b;$(touch x)'", "remote shell arguments are quoted");
try { SourceLauncher.Create(new() { Kind = "ssh", SshHost = "-oProxyCommand=bad" }); throw new Exception("Accepted SSH option injection"); }
catch (ArgumentException) { Check(true, "SSH option injection rejected"); }
var launch = SourceLauncher.Create(new() { Kind = "ssh", SshHost = "research-host", CodexPath = "/path with spaces/codex", CodexHome = "/home/test/codex'home" });
Check(launch.ArgumentList.Last().Contains("'\"'\"'"), "remote profile and binary paths are safely quoted");
Check(MetricFormatting.Tokens(null) == "—" && MetricFormatting.Tokens(150000000) == "1.5억", "unknown and Korean token formatting");

var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
info.ArgumentList.Add(typeof(AccountSource).Assembly.Location.Replace("CodexAccountMonitor.Core.dll", "CodexAccountMonitor.Tests.dll", StringComparison.Ordinal));
info.ArgumentList.Add("--fake-server");
await using (var rpc = new RpcClient(info))
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await rpc.InitializeAsync(timeout.Token);
    var first = rpc.CallAsync("pair", null, timeout.Token);
    var second = rpc.CallAsync("pair", null, timeout.Token);
    await Task.WhenAll(first, second);
    Check(UsageParser.String(first.Result, "value") == "first" && UsageParser.String(second.Result, "value") == "second", "JSON-RPC matches out-of-order responses");
    try { await rpc.CallAsync("missing", null, timeout.Token); throw new Exception("No RPC error"); }
    catch (RpcException error) { Check(error.Code == -32601, "RPC unsupported-method error reaches caller"); }
    try { await rpc.CallAsync("malformed", null, timeout.Token); throw new Exception("Malformed response accepted"); }
    catch (IOException) { Check(true, "malformed RPC response fails immediately without hanging"); }
    using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
    try { await rpc.CallAsync("hold", null, cancel.Token); throw new Exception("No cancellation"); }
    catch (OperationCanceledException) { Check(true, "unanswered RPC is cancellable"); }
}
Console.WriteLine($"{passed} checks passed.");
