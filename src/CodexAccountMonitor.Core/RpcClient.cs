using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace CodexAccountMonitor.Core;

public sealed class RpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

public sealed class RpcClient : IAsyncDisposable
{
    private readonly Process process;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task reader;
    private readonly Task errorReader;
    private long nextId;
    private bool disposed;
    public event Action<string, JsonElement>? Notification;

    public RpcClient(ProcessStartInfo start)
    {
        process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("Codex 프로세스를 시작하지 못했습니다.");
        reader = ReadAsync();
        // Never persist stderr: it can contain authentication-related diagnostics.
        errorReader = DrainErrorsAsync();
    }

    public async Task InitializeAsync(CancellationToken token)
    {
        await CallAsync("initialize", new { clientInfo = new { name = "codex_account_monitor", title = "Codex Account Monitor", version = "1.0.0" },
            capabilities = new { experimentalApi = true } }, token);
        await SendAsync(new { method = "initialized" }, token);
    }

    public async Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        try
        {
            await SendAsync(new { id, method, @params = parameters }, token);
            return await completion.Task.ConfigureAwait(false);
        }
        finally { pending.TryRemove(id, out _); }
    }

    private async Task SendAsync(object message, CancellationToken token)
    {
        await writer.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(token).ConfigureAwait(false);
        }
        finally { writer.Release(); }
    }

    private async Task ReadAsync()
    {
        Exception? failure = null;
        try
        {
            while (await process.StandardOutput.ReadLineAsync(lifetime.Token).ConfigureAwait(false) is { } line)
            {
                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); } catch (JsonException) { continue; }
                using (doc)
                {
                    var root = doc.RootElement;
                    var id = UsageParser.Get(root, "id");
                    var method = UsageParser.String(root, "method");
                    if (method is not null && id.ValueKind != JsonValueKind.Undefined)
                    {
                        // This monitor never approves tool requests or provides auth tokens.
                        await SendAsync(new { id = id.Clone(), error = new { code = -32601, message = "Read-only monitor does not handle server requests" } }, lifetime.Token);
                    }
                    else if (id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number) && pending.TryRemove(number, out var completion))
                    {
                        var error = UsageParser.Get(root, "error");
                        if (error.ValueKind == JsonValueKind.Object)
                            completion.TrySetException(new RpcException((int)(UsageParser.Long(error, "code") ?? -1), UsageParser.String(error, "message") ?? "Codex 조회 오류"));
                        else if (root.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
                        else completion.TrySetException(new IOException("Codex가 잘못된 응답을 반환했습니다."));
                    }
                    else if (method is not null)
                    {
                        var parameters = UsageParser.Get(root, "params");
                        Notification?.Invoke(method, parameters.ValueKind == JsonValueKind.Undefined ? default : parameters.Clone());
                    }
                }
            }
            failure = new IOException("Codex 연결이 종료되었습니다. SSH 연결 또는 실행파일 경로를 확인하세요.");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { failure = error; }
        finally
        {
            foreach (var completion in pending.Values) completion.TrySetException(failure ?? new IOException("연결이 닫혔습니다."));
        }
    }

    private async Task DrainErrorsAsync()
    {
        try { while (await process.StandardError.ReadLineAsync(lifetime.Token).ConfigureAwait(false) is not null) { } }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        foreach (var completion in pending.Values) completion.TrySetException(new IOException("연결이 닫혔습니다."));
        try
        {
            process.StandardInput.Close();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        try { await Task.WhenAll(reader, errorReader).WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception) { }
        process.Dispose();
        lifetime.Dispose();
        writer.Dispose();
    }
}
