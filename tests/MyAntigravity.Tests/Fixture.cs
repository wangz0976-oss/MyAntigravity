using System.Text;
using System.Text.Json;

internal static class Fixture
{
    internal static async Task RunAsync(bool supportsImages, string? account = null, bool rich = false, bool steering = false)
    {
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);
        var cwd = Directory.GetCurrentDirectory();
        JsonElement? prompt = null;
        JsonElement? followUp = null;
        var expectedOption = "deny";
        string? expectedAnswer = null;
        bool cancelled = false;
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var parsed = JsonDocument.Parse(line);
            var message = parsed.RootElement;
            if (!message.TryGetProperty("method", out var method))
            {
                var id = message.GetProperty("id").GetString();
                if (id == "unsupported")
                {
                    if (message.GetProperty("error").GetProperty("code").GetInt32() != -32601)
                        throw new InvalidOperationException("备用终端请求本应被拒绝。");
                    continue;
                }
                if (id == "question" && prompt is { } requestId && expectedAnswer is not null)
                {
                    if (expectedAnswer == "unsupported")
                    {
                        if (message.GetProperty("error").GetProperty("code").GetInt32() != -32602)
                            throw new InvalidOperationException("客户端打开了未声明的 URL 表单。");
                    }
                    else
                    {
                        var response = message.GetProperty("result");
                        if (response.GetProperty("action").GetString() != expectedAnswer)
                            throw new InvalidOperationException("回答动作与预期不符。");
                        if (expectedAnswer == "accept")
                        {
                            var answers = response.GetProperty("content");
                            if (answers.GetProperty("choice").GetString() != "tea" ||
                                answers.GetProperty("confirmed").GetBoolean() ||
                                answers.GetProperty("count").GetInt32() != 2)
                                throw new InvalidOperationException("回答未经正确编码。");
                        }
                        else if (response.TryGetProperty("content", out _))
                            throw new InvalidOperationException("拒绝或取消时不应传递回答。");
                    }
                    Reply(requestId, new { stopReason = cancelled ? "cancelled" : "end_turn" });
                    prompt = null;
                    expectedAnswer = null;
                    continue;
                }
                if (id != "approval" || prompt is null)
                    throw new InvalidOperationException("意外的服务端请求回复。");

                var outcome = message.GetProperty("result").GetProperty("outcome");
                var value = outcome.GetProperty("outcome").GetString();
                if (cancelled ? value != "cancelled" : value != "selected" || outcome.GetProperty("optionId").GetString() != expectedOption)
                    throw new InvalidOperationException("收到错误的审批结果。");
                Reply(prompt.Value, new { stopReason = cancelled ? "cancelled" : "end_turn" });
                prompt = null;
                continue;
            }

            var parameters = message.GetProperty("params");
            switch (method.GetString())
            {
                case "initialize":
                    var sessionCapabilities = new Dictionary<string, object> { ["list"] = new { }, ["close"] = new { } };
                    if (rich)
                    {
                        var elicitation = parameters.GetProperty("clientCapabilities").GetProperty("elicitation");
                        if (elicitation.GetProperty("form").ValueKind != JsonValueKind.Object || elicitation.TryGetProperty("url", out _))
                            throw new InvalidOperationException("客户端应只声明表单模式。");
                        sessionCapabilities["additionalDirectories"] = new { };
                    }
                    Reply(message.GetProperty("id"), new
                    {
                        protocolVersion = 1,
                        agentInfo = new { title = "测试 Agent", name = steering ? "Antigravity ACP Server" : "Fixture", version = "1.5.13" },
                        agentCapabilities = new { loadSession = true, promptCapabilities = new { image = supportsImages, embeddedContext = rich },
                            sessionCapabilities },
                        authMethods = new[] { new { id = "oauth-personal", name = "Google" } }
                    });
                    break;
                case "authenticate":
                    if (parameters.GetProperty("methodId").GetString() != "oauth-personal")
                        throw new InvalidOperationException("错误的认证方法。");
                    Reply(message.GetProperty("id"), new { });
                    break;
                case "session/list":
                    if (parameters.GetProperty("cwd").GetString() != cwd)
                        throw new InvalidOperationException("未传递绝对目录。");
                    if (parameters.TryGetProperty("cursor", out _))
                        Reply(message.GetProperty("id"), new { sessions = new[] { new { sessionId = "second", cwd, updatedAt = "2026-11-19T10:00:00Z" } } });
                    else
                        Reply(message.GetProperty("id"), new
                        {
                            sessions = new[] { new { sessionId = "first", cwd, updatedAt = "2026-11-18T10:00:00Z" },
                                new { sessionId = "foreign", cwd = Path.GetPathRoot(cwd)!, updatedAt = "2026-11-19T10:00:00Z" } },
                            nextCursor = "more"
                        });
                    break;
                case "session/new":
                    if (rich)
                        CheckDirectories(parameters);
                    if (account is not null)
                    {
                        var id = message.GetProperty("id");
                        if (account == "required")
                            Write(new { jsonrpc = "2.0", id, error = new { code = -32000, message = "Authentication required" } });
                        else if (account == "failed")
                            Write(new { jsonrpc = "2.0", id, error = new { code = -32603, message = "Connection unavailable" } });
                        else
                            Reply(id, new { sessionId = "probe", _meta = new { antigravity = new { tier = new { name = "Test tier" } } } });
                        break;
                    }
                    Update("demo", new
                    {
                        sessionUpdate = "available_commands_update",
                        availableCommands = new object[]
                        {
                            new { name = "context", description = "Show context", input = new { hint = "Filter by section" } },
                            new { name = "/context", description = "Duplicate" },
                            new { name = "bad command", description = "Invalid" }
                        }
                    });
                    Reply(message.GetProperty("id"), new { sessionId = "demo", configOptions = ConfigOptions() });
                    break;
                case "session/set_config_option":
                    var configId = parameters.GetProperty("configId").GetString();
                    var configValue = parameters.GetProperty("value").GetString();
                    if ((configId, configValue) is not (("m", "dynamic-model") or ("mode", "accept-edits")))
                        throw new InvalidOperationException("未收到预期的 ACP 配置 ID 或选项值。");
                    Reply(message.GetProperty("id"), new
                    {
                        configOptions = ConfigOptions(configId == "mode" ? "accept-edits" : "default")
                    });
                    break;
                case "session/load":
                    if (rich)
                        CheckDirectories(parameters);
                    string? restored = parameters.GetProperty("sessionId").GetString();
                    Update(restored, new { sessionUpdate = "user_message_chunk", content = new { type = "text", text = "你好" } });
                    Update(restored, new { sessionUpdate = "agent_message_chunk", content = new { type = "text", text = "你好！" } });
                    Reply(message.GetProperty("id"), new { configOptions = ConfigOptions() });
                    break;
                case "session/close":
                    Reply(message.GetProperty("id"), new { });
                    break;
                case "session/prompt":
                    var blocks = parameters.GetProperty("prompt");
                    if (steering)
                    {
                        var messageId = message.GetProperty("id");
                        string? command = blocks[0].GetProperty("text").GetString();
                        switch (command)
                        {
                            case "开始任务":
                                prompt = messageId.Clone();
                                Update("demo", new { sessionUpdate = "fixture_started" });
                                break;
                            case "正常追加":
                                Reply(prompt ?? throw new InvalidOperationException("任务尚未开始。"), new { stopReason = "end_turn" });
                                prompt = null;
                                Update("demo", new { sessionUpdate = "fixture_primary_finished" });
                                await Task.Delay(250);
                                Reply(messageId, new { stopReason = "end_turn", usage = new { outputTokens = 42 } });
                                break;
                            case "被拒绝的追加":
                                Write(new { jsonrpc = "2.0", id = messageId, error = new { code = -32603, message = "waiting for a response" } });
                                break;
                            case "中止前追加":
                                followUp = messageId.Clone();
                                Update("demo", new { sessionUpdate = "fixture_followup_started" });
                                break;
                            default:
                                throw new InvalidOperationException("收到意外的追加测试内容。");
                        }
                        break;
                    }
                    if (rich && blocks.EnumerateArray().Any(block => block.GetProperty("type").GetString() == "resource"))
                    {
                        var file = blocks[blocks.GetArrayLength() - 1].GetProperty("resource");
                        var uri = file.GetProperty("uri").GetString()!;
                        if (!uri.StartsWith("myantigravity://attachment/") || !uri.EndsWith("/probe.py") ||
                            file.GetProperty("mimeType").GetString() != "text/plain" ||
                            file.GetProperty("text").GetString() != "标记 = '测试通过'\r\n")
                            throw new InvalidOperationException("附件未正确嵌入或包含本机路径。");
                        Reply(message.GetProperty("id"), new { stopReason = "end_turn" });
                        break;
                    }
                    if (blocks.EnumerateArray().Any(block => block.GetProperty("type").GetString() == "image"))
                    {
                        int count = blocks.GetArrayLength();
                        var image = blocks[count - 1];
                        if (!supportsImages || count is not (1 or 2) ||
                            count == 2 && blocks[0].GetProperty("text").GetString() != "图片说明" ||
                            image.GetProperty("type").GetString() != "image" ||
                            image.GetProperty("mimeType").GetString() != "image/png" ||
                            !image.GetProperty("data").GetBytesFromBase64().SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47 }) ||
                            image.TryGetProperty("uri", out _))
                            throw new InvalidOperationException("图片 ACP 内容块与协议不符。");
                        Reply(message.GetProperty("id"), new { stopReason = "end_turn" });
                        break;
                    }
                    var text = blocks[0].GetProperty("text").GetString();
                    if (text == "exit")
                        return;
                    expectedOption = text == "请允许" ? "approve" : "deny";
                    prompt = message.GetProperty("id").Clone();
                    cancelled = false;
                    string? sessionId = parameters.GetProperty("sessionId").GetString();
                    if (rich && text is not null && text.StartsWith("question:"))
                    {
                        expectedAnswer = text["question:".Length..];
                        Write(new
                        {
                            jsonrpc = "2.0", id = "question", method = "elicitation/create",
                            @params = Question(sessionId, expectedAnswer == "unsupported" ? "url" : "form")
                        });
                        break;
                    }
                    Write(new { jsonrpc = "2.0", id = "unsupported", method = "terminal/create", @params = new { sessionId } });
                    Write(new
                    {
                        jsonrpc = "2.0", id = "approval", method = "session/request_permission",
                        @params = new
                        {
                            sessionId,
                            toolCall = new { title = "修改", locations = new[] { new { path = "src/测试.cs" } } },
                            options = new[] {
                                new { optionId = "approve", name = "允许", kind = "allow_once" },
                                new { optionId = "allow-all", name = "始终允许", kind = "allow_always" },
                                new { optionId = "deny", name = "拒绝", kind = "reject_once" }
                            }
                        }
                    });
                    break;
                case "session/cancel":
                    if (steering && prompt is { } active)
                    {
                        Reply(active, new { stopReason = "cancelled" });
                        if (followUp is { } pending)
                            Reply(pending, new { stopReason = "cancelled" });
                        prompt = followUp = null;
                        break;
                    }
                    cancelled = true;
                    break;
                default:
                    throw new InvalidOperationException("未知测试请求。");
            }
        }

        void CheckDirectories(JsonElement parameters)
        {
            if (parameters.GetProperty("cwd").GetString() != cwd ||
                !parameters.GetProperty("additionalDirectories").EnumerateArray().Select(item => item.GetString())
                    .SequenceEqual([Path.Combine(cwd, "附加测试目录")]))
                throw new InvalidOperationException("没有收到附加工作目录。");
        }
    }

    internal static object Question(string? sessionId, string mode) => new
    {
        sessionId, mode, message = "请选择并填写测试参数。",
        requestedSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["choice"] = new { type = "string", title = "饮料", oneOf = new[] { new { @const = "tea", title = "茶" } } },
                ["confirmed"] = new { type = "boolean", title = "加糖", @default = false },
                ["count"] = new { type = "integer", title = "数量", minimum = 1, maximum = 3 },
                ["extras"] = new { type = "array", title = "配料", maxItems = 1, items = new { type = "string", @enum = new[] { "lemon", "mint" } } }
            },
            required = new[] { "choice", "count" }
        }
    };

    private static object[] ConfigOptions(string mode = "default") =>
    [
        new
        {
            id = "m", name = "Model", category = "model", type = "select", currentValue = "dynamic-model",
            options = new[] { new { value = "dynamic-model", name = "Dynamic model" } }
        },
        new
        {
            id = "mode", name = "Mode", category = "mode", type = "select", currentValue = mode,
            options = new[] { new { value = "default", name = "Default" }, new { value = "accept-edits", name = "Accept edits" } }
        }
    ];

    private static void Update(string? sessionId, object update) =>
        Write(new { jsonrpc = "2.0", method = "session/update", @params = new { sessionId, update } });

    private static void Reply(JsonElement id, object result) => Write(new { jsonrpc = "2.0", id, result });

    private static void Write(object message) => Console.WriteLine(JsonSerializer.Serialize(message));
}
