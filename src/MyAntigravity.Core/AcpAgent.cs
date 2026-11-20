using System.Diagnostics;
using System.Text.Json;

namespace MyAntigravity;

public sealed class AcpAgent : IAsyncDisposable
{
    private readonly AcpConnection connection;
    private readonly Func<PermissionRequest, CancellationToken, Task<string?>> approve;
    private readonly Func<ElicitationRequest, CancellationToken, Task<ElicitationResponse>>? ask;
    private readonly object turnGate = new();
    private sealed class ActiveTurn(string session, CancellationTokenSource interactions)
    {
        internal string Session { get; } = session;
        internal CancellationTokenSource Interactions { get; } = interactions;
        internal Task<JsonElement>? FollowUp;
        internal bool PrimaryFinished;
        internal bool Stopping;
    }

    private ActiveTurn? turn;
    private JsonElement capabilities;
    private JsonElement authMethods;
    private string agentName = "Antigravity CLI";
    private readonly string cwd;

    public event Action<string, JsonElement>? SessionUpdated;
    public event Action? Disconnected;
    public bool Connected => connection.Connected;
    public bool CanList => HasCapability("list");
    public bool CanRestore => capabilities.Flag("loadSession") || HasCapability("resume");
    public bool CanReplay => capabilities.Flag("loadSession");
    public bool CanSendImages => capabilities.Field("promptCapabilities").Flag("image");
    public bool CanSendTextFiles => capabilities.Field("promptCapabilities").Flag("embeddedContext");
    public bool CanAddDirectories => HasCapability("additionalDirectories");
    public bool CanSteer { get; private set; }

    public AcpAgent(ProcessStartInfo startInfo, Func<PermissionRequest, CancellationToken, Task<string?>> approve,
        Func<ElicitationRequest, CancellationToken, Task<ElicitationResponse>>? ask = null)
    {
        this.approve = approve;
        this.ask = ask;
        cwd = startInfo.WorkingDirectory;
        connection = new AcpConnection(startInfo, HandleAgentRequestAsync);
        connection.Disconnected += () => Disconnected?.Invoke();
        connection.Notification += (method, parameters) =>
        {
            if (method == "session/update" && parameters.Text("sessionId") is { } id)
                SessionUpdated?.Invoke(id, parameters.Field("update"));
        };
    }

    public async Task InitializeAsync(string version, TimeSpan? timeout = null)
    {
        var clientCapabilities = new Dictionary<string, object> { ["sessionConfig"] = new { boolean = true } };
        if (ask is not null)
            clientCapabilities["elicitation"] = new { form = new { } };
        var result = await connection.RequestAsync("initialize", new
        {
            protocolVersion = 1,
            clientInfo = new { name = "myantigravity", title = "MyAntigravity", version },
            clientCapabilities
        }, timeout).ConfigureAwait(false);
        if (result.Int("protocolVersion") != 1)
            throw new IOException("CLI 未协商 ACP 版本 1。");
        capabilities = result.Field("agentCapabilities");
        authMethods = result.Field("authMethods");
        var info = result.Field("agentInfo");
        agentName = info.Text("title") ?? info.Text("name") ?? agentName;
        CanSteer = string.Equals(info.Text("name"), "Antigravity ACP Server", StringComparison.OrdinalIgnoreCase) &&
            Version.TryParse(info.Text("version"), out var reported) && reported >= new Version(1, 5, 13);
    }

    public async Task AuthenticateGoogleAsync()
    {
        if (!authMethods.Items().Any(method => method.Text("id") == "oauth-personal" &&
            method.Text("type") is (null or "agent")))
            throw new InvalidOperationException("CLI 未提供浏览器登录。请从账户菜单打开官方终端登录。");

        await connection.RequestAsync("authenticate", new { methodId = "oauth-personal" },
            TimeSpan.FromMinutes(8)).ConfigureAwait(false);
    }

    public Task<JsonElement> NewSessionAsync(TimeSpan? timeout = null, IReadOnlyList<string>? additionalDirectories = null) =>
        connection.RequestAsync("session/new", SessionParameters(null, additionalDirectories), timeout);

    public Task<JsonElement> RestoreAsync(string sessionId, IReadOnlyList<string>? additionalDirectories = null)
    {
        if (!CanRestore)
            throw new InvalidOperationException("当前 CLI 未声明会话恢复能力。");
        return connection.RequestAsync(CanReplay ? "session/load" : "session/resume",
            SessionParameters(sessionId, additionalDirectories));
    }

    private Dictionary<string, object> SessionParameters(string? sessionId, IReadOnlyList<string>? directories)
    {
        var parameters = new Dictionary<string, object> { ["cwd"] = cwd, ["mcpServers"] = Array.Empty<object>() };
        if (sessionId is not null)
            parameters["sessionId"] = sessionId;
        if (directories is { Count: > 0 })
        {
            if (!CanAddDirectories)
                throw new InvalidOperationException("当前官方 CLI 未声明附加工作目录能力。");
            if (directories.Any(path => !Path.IsPathFullyQualified(path)))
                throw new ArgumentException("附加目录须为绝对路径。", nameof(directories));
            parameters["additionalDirectories"] = directories;
        }
        return parameters;
    }

    public Task CloseAsync(string sessionId) =>
        HasCapability("close") ? connection.RequestAsync("session/close", new { sessionId }) : Task.CompletedTask;

    public async Task<IReadOnlyList<LocalSession>> ListAsync()
    {
        var sessions = new Dictionary<string, LocalSession>(StringComparer.Ordinal);
        if (!CanList)
            return [];

        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var parameters = new Dictionary<string, string> { ["cwd"] = cwd };
        while (true)
        {
            var page = await connection.RequestAsync("session/list", parameters).ConfigureAwait(false);
            foreach (var item in page.Field("sessions").Items())
            {
                if (item.Text("sessionId") is not { } id || item.Text("cwd") is not { } directory ||
                    !LocalSettings.PathEquals(directory, cwd))
                    continue;

                DateTimeOffset.TryParse(item.Text("updatedAt"), out var updated);
                sessions[id] = new LocalSession(id, directory, item.Text("title") is { } title
                    ? AgentText.Visible(title) : null, updated);
            }

            if (page.Text("nextCursor") is not { Length: > 0 } cursor || !cursors.Add(cursor))
                return sessions.Values.OrderByDescending(session => session.Updated).ToArray();

            parameters["cursor"] = cursor;
        }
    }

    public Task<JsonElement> PromptAsync(string sessionId, string text) => PromptAsync(sessionId, text, []);

    public Task<JsonElement> PromptAsync(string sessionId, string text, IReadOnlyList<PromptImage> images) =>
        PromptAsync(sessionId, text, images, []);

    public async Task<JsonElement> PromptAsync(string sessionId, string text, IReadOnlyList<PromptImage> images,
        IReadOnlyList<PromptTextFile> textFiles)
    {
        if (images.Count != 0 && !CanSendImages)
            throw new InvalidOperationException("当前官方 CLI 未声明支持图片附件。");
        if (images.Count > PromptImage.MaxCount || images.Sum(image => (long)image.Length) > PromptImage.MaxPromptBytes)
            throw new ArgumentException("一条消息最多附 8 张图片，总大小不超过 12 MiB。", nameof(images));
        if (textFiles.Count != 0 && !CanSendTextFiles)
            throw new InvalidOperationException("当前官方 CLI 未声明支持文本附件。");
        if (textFiles.Count > PromptTextFile.MaxCount || textFiles.Sum(file => (long)file.Length) > PromptTextFile.MaxPromptBytes)
            throw new ArgumentException("一条消息最多附 8 个文本文件，总大小不超过 1 MiB。", nameof(textFiles));
        var prompt = new List<object>();
        if (!string.IsNullOrWhiteSpace(text))
            prompt.Add(new { type = "text", text });
        foreach (var image in images)
            prompt.Add(new { type = "image", mimeType = image.MimeType, data = image.Data });
        foreach (var file in textFiles)
            prompt.Add(new { type = "resource", resource = new { uri = file.Uri, mimeType = "text/plain", text = file.Text } });
        if (prompt.Count == 0)
            throw new ArgumentException("请先输入消息或添加附件。", nameof(text));

        using var interactions = new CancellationTokenSource();
        var current = new ActiveTurn(sessionId, interactions);
        lock (turnGate)
        {
            if (turn is not null)
                throw new InvalidOperationException("当前已有任务。");
            turn = current;
        }
        JsonElement result = default;
        try
        {
            result = await connection.RequestAsync("session/prompt",
                new { sessionId, prompt },
                Timeout.InfiniteTimeSpan).ConfigureAwait(false);
        }
        finally
        {
            Task<JsonElement>? followUp;
            lock (turnGate)
            {
                current.PrimaryFinished = true;
                followUp = current.FollowUp;
            }
            try
            {
                if (followUp is not null)
                {
                    var completion = await followUp.ConfigureAwait(false);
                    if (result.Text("stopReason") != "cancelled")
                        result = completion;
                }
            }
            catch (Exception error) when (error is AcpException or IOException or OperationCanceledException) { }
            finally
            {
                lock (turnGate)
                    turn = null;
                interactions.Cancel();
            }
        }
        return result;
    }

    public Task<JsonElement> SteerAsync(string sessionId, string text)
    {
        if (!CanSteer || string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("当前连接无法发送这条追加指令。");
        lock (turnGate)
        {
            if (turn is not { PrimaryFinished: false, Stopping: false } current || current.Session != sessionId)
                throw new InvalidOperationException("当前任务已结束或正在中止。");
            if (current.FollowUp is { IsCompleted: false })
                throw new InvalidOperationException("上一条追加指令仍在处理中。");
            return current.FollowUp = connection.RequestAsync("session/prompt",
                new { sessionId, prompt = new[] { new { type = "text", text } } }, Timeout.InfiniteTimeSpan);
        }
    }

    public async Task CancelAsync(string sessionId)
    {
        ActiveTurn current;
        lock (turnGate)
        {
            if (turn is null || turn.Session != sessionId || turn.Stopping)
                return;
            current = turn;
            current.Stopping = true;
        }

        try { await connection.NotifyAsync("session/cancel", new { sessionId }).ConfigureAwait(false); }
        finally
        {
            lock (turnGate)
            {
                if (ReferenceEquals(turn, current))
                    current.Interactions.Cancel();
            }
        }
    }

    public Task<JsonElement> SetOptionAsync(string sessionId, SessionOption option, Choice choice)
    {
        if (!option.Choices.Contains(choice))
            throw new ArgumentException("此值不在 Agent 提供的选项中。", nameof(choice));

        if (option.IsBoolean)
            return connection.RequestAsync(option.Method,
                new { sessionId, configId = option.Id, type = "boolean", value = choice.Id == "true" });

        return option.Method == "session/set_mode"
            ? connection.RequestAsync(option.Method, new { sessionId, modeId = choice.Id })
            : connection.RequestAsync(option.Method, new { sessionId, configId = option.Id, value = choice.Id });
    }

    private bool HasCapability(string key) =>
        capabilities.Field("sessionCapabilities").Field(key).ValueKind == JsonValueKind.Object;

    private async Task<object> HandleAgentRequestAsync(string method, JsonElement parameters, CancellationToken lifetime)
    {
        if (method == "elicitation/create" && ask is not null)
            return await HandleElicitationAsync(parameters, lifetime).ConfigureAwait(false);
        if (method != "session/request_permission")
            throw new AcpException(-32601, "此客户端未声明支持该方法。");

        var request = PermissionRequest.Parse(parameters);
        CancellationToken turnToken;
        lock (turnGate)
        {
            if (turn is null || request.SessionId != turn.Session)
                return request.Answer(null);
            turnToken = turn.Interactions.Token;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(turnToken, lifetime);
        if (linked.IsCancellationRequested)
            return request.Answer(null);

        try
        {
            var decision = request.Options.Count == 0 ? null : await approve(request, linked.Token).ConfigureAwait(false);
            return request.Answer(linked.IsCancellationRequested ? null : decision);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return request.Answer(null);
        }
    }

    private async Task<object> HandleElicitationAsync(JsonElement parameters, CancellationToken lifetime)
    {
        if (parameters.Text("mode") != "form")
            throw new AcpException(-32602, "此客户端只声明支持会话提问表单。");
        var request = ElicitationRequest.Parse(parameters, agentName);
        CancellationToken turnToken;
        lock (turnGate)
        {
            if (turn is null || request.SessionId != turn.Session)
                return ElicitationResponse.Cancelled.ToWire();
            turnToken = turn.Interactions.Token;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(turnToken, lifetime);
        if (linked.IsCancellationRequested)
            return ElicitationResponse.Cancelled.ToWire();
        try
        {
            var result = await ask!(request, linked.Token).ConfigureAwait(false);
            return (linked.IsCancellationRequested ? ElicitationResponse.Cancelled : result).ToWire();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return ElicitationResponse.Cancelled.ToWire();
        }
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
