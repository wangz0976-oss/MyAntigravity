using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace MyAntigravity;

public sealed class AcpException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
    public bool RequiresAuthentication => Code == -32000 && Message.Contains("auth", StringComparison.OrdinalIgnoreCase);
}

public sealed class AcpConnection : IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);
    private readonly Process process;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly Task reader;
    private readonly Task stderr;
    private long nextId;
    private int disposed;

    public bool Connected => !lifetime.IsCancellationRequested;
    public event Action<string, JsonElement>? Notification;
    public event Action? Disconnected;

    public AcpConnection(ProcessStartInfo startInfo,
        Func<string, JsonElement, CancellationToken, Task<object>> agentRequest)
    {
        process = Process.Start(startInfo) ?? throw new IOException("无法启动 ACP 子进程。");
        reader = Task.Run(() => ReadAsync(agentRequest));
        stderr = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync(lifetime.Token).ConfigureAwait(false) is not null) { }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        });
    }

    public async Task<JsonElement> RequestAsync(string method, object parameters, TimeSpan? timeout = null)
    {
        var id = Interlocked.Increment(ref nextId);
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = reply;
        try
        {
            await WriteAsync(new { jsonrpc = "2.0", id, method, @params = parameters }).ConfigureAwait(false);
            return await reply.Task.WaitAsync(timeout ?? RequestTimeout, lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            throw new IOException("ACP 连接已关闭。请重新连接并打开会话。");
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"等待 ACP 方法 {method} 响应超时。");
        }
        finally
        {
            pending.TryRemove(id, out _);
        }
    }

    public Task NotifyAsync(string method, object parameters) =>
        WriteAsync(new { jsonrpc = "2.0", method, @params = parameters });

    private async Task WriteAsync(object message)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var json = JsonSerializer.Serialize(message);
        await writeLock.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteLineAsync(json.AsMemory(), timeout.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (IOException)
        {
            Cli.Stop(process);
            throw;
        }
        catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
        {
            Cli.Stop(process);
            throw new IOException("写入 ACP 子进程超时。");
        }
        finally
        {
            writeLock.Release();
        }
    }

    private async Task ReadAsync(Func<string, JsonElement, CancellationToken, Task<object>> agentRequest)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync(lifetime.Token).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                using var document = JsonDocument.Parse(line);
                var message = document.RootElement;
                if (message.Text("jsonrpc") != "2.0")
                    throw new IOException("CLI 输出不符合 JSON-RPC 2.0。");

                var id = message.Field("id");
                if (message.Text("method") is { } method)
                {
                    var parameters = message.Field("params").Clone();
                    if (id.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                        _ = RespondAsync(id.Clone(), method, parameters, agentRequest);
                    else
                        Notification?.Invoke(method, parameters);
                }
                else if (id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number) &&
                         pending.TryRemove(number, out var response))
                {
                    var error = message.Field("error");
                    if (error.ValueKind == JsonValueKind.Object)
                        response.TrySetException(new AcpException(error.Int("code"), error.Text("message") ?? "ACP 请求失败。"));
                    else
                        response.TrySetResult(message.Field("result").Clone());
                }
            }
        }
        catch (Exception error) when (error is IOException or JsonException or OperationCanceledException) { }
        finally
        {
            lifetime.Cancel();
            Cli.Stop(process);
            Disconnected?.Invoke();
        }
    }

    private async Task RespondAsync(JsonElement id, string method, JsonElement parameters,
        Func<string, JsonElement, CancellationToken, Task<object>> agentRequest)
    {
        object message;
        try
        {
            var result = await agentRequest(method, parameters, lifetime.Token).ConfigureAwait(false);
            message = new { jsonrpc = "2.0", id, result };
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
        catch (AcpException error)
        {
            message = new { jsonrpc = "2.0", id, error = new { code = error.Code, message = error.Message } };
        }
        catch (Exception)
        {
            message = new { jsonrpc = "2.0", id, error = new { code = -32603, message = "客户端无法处理此请求。" } };
        }

        try { await WriteAsync(message).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        lifetime.Cancel();
        Cli.Stop(process);
        await Task.WhenAll(reader, stderr).ConfigureAwait(false);
        await writeLock.WaitAsync().ConfigureAwait(false);
        try { process.Dispose(); }
        finally { writeLock.Release(); }
    }
}
