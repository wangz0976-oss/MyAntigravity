using System.Text.Json;

namespace MyAntigravity;

public static class SessionUsage
{
    public static string? Context(JsonElement update)
    {
        var used = Count(update, "used");
        var size = Count(update, "size");
        return size > 0 && used.HasValue ? $"上下文用量：{used:N0} / {size:N0} token" : null;
    }

    public static string? LastTurn(JsonElement result)
    {
        var usage = result.Field("usage");
        (string Key, string Label)[] fields =
        [
            ("inputTokens", "输入"), ("outputTokens", "输出"), ("thoughtTokens", "推理"),
            ("cachedReadTokens", "读缓存"), ("cachedWriteTokens", "写缓存"), ("totalTokens", "合计")
        ];
        var parts = fields.Select(item => (item.Label, Value: Count(usage, item.Key)))
            .Where(item => item.Value.HasValue).Select(item => $"{item.Label} {item.Value:N0}").ToArray();
        return parts.Length > 0 ? $"最近一轮 token：{string.Join("，", parts)}" : null;
    }

    private static long? Count(JsonElement parent, string key) =>
        parent.Field(key) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out long count) && count >= 0
            ? count : null;
}
