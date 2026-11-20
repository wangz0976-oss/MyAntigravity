using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyAntigravity;

public sealed partial record McpServer(string Name, string Transport, bool Enabled, string? Directory = null)
{
    public bool IsGlobal => Directory is null;
    public bool CanChange => IsGlobal && IsConfigurableName(Name);
    public string Scope => IsGlobal ? "官方 CLI 全局" : $"工作目录配置 {Directory}";

    public override string ToString() =>
        $"{AgentText.Visible(Name)}，{(Enabled ? "配置为启用" : "已停用")}，{AgentText.Visible(Scope)}";

    [GeneratedRegex(@"^(?<name>.+?)\s{2,}(?<type>\S+)\s{2,}(?<status>enabled|disabled)(?:\s{2,}|$)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex McpCliRow();

    public static IReadOnlyList<McpServer> ParseCliList(string output)
    {
        if (string.Equals(output.Trim(), "No MCP servers configured.", StringComparison.OrdinalIgnoreCase))
            return [];
        var servers = new List<McpServer>();
        foreach (var line in output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = McpCliRow().Match(line.Trim());
            if (match.Success)
                servers.Add(new McpServer(match.Groups["name"].Value, match.Groups["type"].Value,
                    match.Groups["status"].Value.Equals("enabled", StringComparison.OrdinalIgnoreCase)));
        }
        if (servers.Count == 0)
            throw new IOException("无法识别官方 CLI 的 MCP 列表。");
        return servers;
    }

    public static IReadOnlyList<McpServer> ReadWorkspace(string directory)
    {
        var path = Path.Combine(directory, ".agents", "mcp_config.json");
        if (!File.Exists(path))
            return [];
        using var file = File.OpenRead(path);
        if (file.Length > 1024 * 1024)
            throw new IOException("工作目录的 MCP 配置超过 1 MiB。");
        using var document = JsonDocument.Parse(file, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        var entries = document.RootElement.Field("mcpServers");
        if (entries.ValueKind != JsonValueKind.Object)
            return [];
        return entries.EnumerateObject().Where(item => item.Value.ValueKind == JsonValueKind.Object)
            .Select(item => new McpServer(item.Name,
                item.Value.Text("serverUrl") is not null || item.Value.Text("url") is not null ? "http"
                    : item.Value.Text("command") is not null ? "stdio" : "未指定",
                !item.Value.Flag("disabled"), directory))
            .ToArray();
    }

    internal static bool IsConfigurableName(string name) =>
        name.Length is > 0 and <= 200 && !name.StartsWith('-') && !name.Any(char.IsControl);
}
