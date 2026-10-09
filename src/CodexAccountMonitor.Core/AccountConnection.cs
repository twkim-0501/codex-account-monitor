using System.Diagnostics;
using System.Text.Json;

namespace CodexAccountMonitor.Core;

public sealed class AccountConnection(AccountSource source, Func<ProcessStartInfo>? launcher = null) : IAsyncDisposable
{
    private RpcClient? rpc;
    private readonly SemaphoreSlim gate = new(1, 1);
    private (bool Exists, long Length, long Written)? authRevision;

    private (bool Exists, long Length, long Written)? LocalAuthRevision()
    {
        if (source.Kind != "local") return null;
        var home = Environment.ExpandEnvironmentVariables(source.CodexHome ?? Environment.GetEnvironmentVariable("CODEX_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
        var file = new FileInfo(Path.Combine(home, "auth.json"));
        try { return file.Exists ? (true, file.Length, file.LastWriteTimeUtc.Ticks) : (false, 0, 0); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private async Task ResetAsync()
    {
        if (rpc is not null) await rpc.DisposeAsync();
        rpc = null; authRevision = null;
    }

    private async Task<RpcClient> EnsureAsync(CancellationToken token)
    {
        var revision = LocalAuthRevision();
        if (rpc is not null && revision != authRevision) await ResetAsync();
        if (rpc is not null) return rpc;
        var candidate = new RpcClient(launcher?.Invoke() ?? SourceLauncher.Create(source));
        try { await candidate.InitializeAsync(token); rpc = candidate; authRevision = revision; return candidate; }
        catch { await candidate.DisposeAsync(); throw; }
    }

    public async Task<AccountSnapshot> ReadAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try { return await ReadCurrentAsync(token); }
                catch (RpcException error) when (attempt == 0 && IsAuthenticationError(error)) { await ResetAsync(); }
                catch (AccountLoginRequiredException) when (attempt == 0) { await ResetAsync(); }
            }
        }
        catch
        {
            await ResetAsync();
            throw;
        }
        finally { gate.Release(); }
    }

    private async Task<AccountSnapshot> ReadCurrentAsync(CancellationToken token)
    {
        var client = await EnsureAsync(token);
        var account = await client.CallAsync("account/read", new { refreshToken = false }, token);
        if (UsageParser.Get(account, "account").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new AccountLoginRequiredException();
        var limitsTask = ReadOptionalAsync(client, "account/rateLimits/read", token);
        var usageTask = ReadOptionalAsync(client, "account/usage/read", token);
        await Task.WhenAll(limitsTask, usageTask);
        // A login can change while the two requests are in flight. Never combine two identities.
        var revision = LocalAuthRevision();
        if (revision != authRevision) { await ResetAsync(); throw new AccountLoginRequiredException(); }
        var snapshot = UsageParser.Parse(source.Id, account, limitsTask.Result.Result, usageTask.Result.Result);
        snapshot.LimitsNote = limitsTask.Result.Note;
        snapshot.UsageNote = usageTask.Result.Note;
        return snapshot;
    }

    public static bool IsAuthenticationError(RpcException error) => error.Code != -32601 && (error.Code is 401 or 403 ||
        new[] { "401", "unauthorized", "token_revoked", "token_expired", "refresh_token_reused", "refresh token", "refresh auth", "access token", "authentication", "not authenticated", "not logged in", "log in", "login required" }
            .Any(marker => error.Message.Contains(marker, StringComparison.OrdinalIgnoreCase)));

    private sealed class AccountLoginRequiredException() : InvalidOperationException("로그인이 필요합니다. 해당 Codex 계정으로 로그인한 뒤 다시 조회하세요.");

    private static async Task<(JsonElement? Result, string? Note)> ReadOptionalAsync(RpcClient client, string method, CancellationToken token)
    {
        try { return (await client.CallAsync(method, null, token), null); }
        catch (RpcException error)
        {
            if (IsAuthenticationError(error)) throw;
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
        try { await ResetAsync(); }
        finally { gate.Release(); }
    }
}
