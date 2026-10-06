using System.Text.Json;

namespace CodexAccountMonitor.Core;

public sealed class AccountConnection(AccountSource source) : IAsyncDisposable
{
    private RpcClient? rpc;
    private readonly SemaphoreSlim gate = new(1, 1);

    private async Task<RpcClient> EnsureAsync(CancellationToken token)
    {
        if (rpc is not null) return rpc;
        var candidate = new RpcClient(SourceLauncher.Create(source));
        try { await candidate.InitializeAsync(token); rpc = candidate; return candidate; }
        catch { await candidate.DisposeAsync(); throw; }
    }

    public async Task<AccountSnapshot> ReadAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var client = await EnsureAsync(token);
            var account = await client.CallAsync("account/read", new { refreshToken = false }, token);
            if (UsageParser.Get(account, "account").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                throw new InvalidOperationException("로그인이 필요합니다. 해당 Codex 계정으로 로그인한 뒤 다시 조회하세요.");
            var limitsTask = ReadOptionalAsync(client, "account/rateLimits/read", token);
            var usageTask = ReadOptionalAsync(client, "account/usage/read", token);
            await Task.WhenAll(limitsTask, usageTask);
            var snapshot = UsageParser.Parse(source.Id, account, limitsTask.Result.Result, usageTask.Result.Result);
            snapshot.LimitsNote = limitsTask.Result.Note;
            snapshot.UsageNote = usageTask.Result.Note;
            return snapshot;
        }
        catch
        {
            if (rpc is not null) { await rpc.DisposeAsync(); rpc = null; }
            throw;
        }
        finally { gate.Release(); }
    }

    private static async Task<(JsonElement? Result, string? Note)> ReadOptionalAsync(RpcClient client, string method, CancellationToken token)
    {
        try { return (await client.CallAsync(method, null, token), null); }
        catch (RpcException error)
        {
            return (null, error.Code == -32601 ? "설치된 Codex 버전에서 미지원" : "계정에서 조회되지 않음 · 로그인/구독 확인");
        }
    }

    public async Task LoginAsync(Action<string, string?> onInstruction, CancellationToken token)
    {
        if (source.Kind != "local" || string.IsNullOrWhiteSpace(source.CodexHome))
            throw new InvalidOperationException("앱 안에서 로그인하려면 분리된 로컬 계정 프로필을 사용하세요.");
        await gate.WaitAsync(token);
        try
        {
            var client = await EnsureAsync(token);
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler(string method, JsonElement data)
            {
                if (method != "account/login/completed") return;
                if (UsageParser.Get(data, "success").ValueKind == JsonValueKind.True) completion.TrySetResult(true);
                else completion.TrySetException(new InvalidOperationException("로그인이 완료되지 않았습니다. 다시 시도하세요."));
            }
            client.Notification += Handler;
            string? loginId = null;
            try
            {
                JsonElement result;
                try { result = await client.CallAsync("account/login/start", new { type = "chatgptDeviceCode" }, token); }
                catch (RpcException) { result = await client.CallAsync("account/login/start", new { type = "chatgpt" }, token); }
                loginId = UsageParser.String(result, "loginId");
                var url = UsageParser.String(result, "verificationUrl") ?? UsageParser.String(result, "authUrl");
                if (url is null) throw new InvalidOperationException("로그인 주소를 받지 못했습니다.");
                onInstruction(url, UsageParser.String(result, "userCode"));
                await completion.Task.WaitAsync(token);
            }
            finally
            {
                client.Notification -= Handler;
                if (!completion.Task.IsCompletedSuccessfully && loginId is not null)
                    try { using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await client.CallAsync("account/login/cancel", new { loginId }, cancel.Token); } catch (Exception) { }
            }
        }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try { if (rpc is not null) await rpc.DisposeAsync(); rpc = null; }
        finally { gate.Release(); }
    }
}
