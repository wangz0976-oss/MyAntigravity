using System.Text.Json;

namespace MyAntigravity;

public sealed record ToolLocation(string Path, uint? Line = null)
{
    public override string ToString() => AgentText.Visible(Line is { } line ? $"{Path}，第 {line} 行" : Path);
}

public sealed record TurnToolResult(string Id, string Title, string Kind, string Status, string? Output,
    IReadOnlyList<string> Content, IReadOnlyList<ToolLocation> Locations, IReadOnlyList<FileChange> Changes,
    IReadOnlyList<PromptImage> Images)
{
    public bool IsFailure => Status == "failed";
    public string Summary => $"{AcpDisplayText.ToolStatus(Status)} · {AcpDisplayText.ToolKind(Kind)}";

    public override string ToString() => $"{AgentText.Visible(Title)} · {AgentText.Visible(Summary)}";

    public IReadOnlyList<ToolLocation> ProjectLocations() => Locations.Concat(Changes
        .Where(change => !Locations.Any(location => LocalSettings.PathEquals(location.Path, change.Path)))
        .Select(change => new ToolLocation(change.Path))).Distinct().ToArray();

    public string Details()
    {
        var sections = new List<string> { ToString() };
        if (Locations.Count != 0)
            sections.Add("关联位置\n" + string.Join("\n", Locations));
        if (Changes.Count != 0)
            sections.Add("文件差异\n" + string.Join("\n", Changes.Select(change => change.Path).Distinct()));
        sections.AddRange(Content);
        if (Output is { Length: > 0 } output && !Content.Contains(output, StringComparer.Ordinal))
            sections.Add($"工具输出\n{output}");
        if (sections.Count == 1)
            sections.Add("此工具暂未报告可显示的结果。");
        return AgentText.Visible(string.Join("\n\n", sections));
    }
}

public sealed class TurnResults
{
    private readonly Dictionary<string, TurnToolResult> tools = new(StringComparer.Ordinal);
    private int imageBytes;

    public void Clear()
    {
        tools.Clear();
        imageBytes = 0;
    }

    public IReadOnlyList<TurnToolResult> Snapshot() => tools.Values.ToArray();

    public void Apply(JsonElement update)
    {
        if (update.Text("toolCallId") is not { Length: > 0 } id)
            return;
        tools.TryGetValue(id, out var previous);
        var progress = update.Field("content");
        var locations = update.Field("locations");
        var output = update.Field("rawOutput");
        var images = previous?.Images ?? [];
        if (progress.ValueKind == JsonValueKind.Array)
        {
            imageBytes -= images.Sum(image => image.Length);
            images = ReadImages(progress, PromptImage.MaxPromptBytes - imageBytes);
            imageBytes += images.Sum(image => image.Length);
        }
        tools[id] = new TurnToolResult(id, update.Text("title") ?? previous?.Title ?? "工具操作",
            update.Text("kind") ?? previous?.Kind ?? "other", update.Text("status") ?? previous?.Status ?? "pending",
            output.ValueKind switch
            {
                JsonValueKind.Undefined or JsonValueKind.Null => previous?.Output,
                JsonValueKind.String => output.GetString(),
                _ => output.GetRawText()
            },
            progress.ValueKind == JsonValueKind.Array ? ReadContent(progress).ToArray() : previous?.Content ?? [],
            locations.ValueKind == JsonValueKind.Array ? ReadLocations(locations).ToArray() : previous?.Locations ?? [],
            progress.ValueKind == JsonValueKind.Array ? AgentText.Changes(update).ToArray() : previous?.Changes ?? [],
            images);
    }

    private static IEnumerable<string> ReadContent(JsonElement progress)
    {
        foreach (var entry in progress.Items())
        {
            if (entry.Text("type") == "terminal" && entry.Text("terminalId") is { Length: > 0 } terminalId)
            {
                yield return $"终端引用：{terminalId}";
                continue;
            }
            var content = entry.Field("content");
            string? description = content.Text("type") switch
            {
                "text" => content.Text("text"),
                "resource_link" => Join(content.Text("title") ?? content.Text("name"),
                    content.Text("uri"), content.Text("description")),
                "resource" => Join(content.Field("resource").Text("uri"), content.Field("resource").Text("text")),
                "image" => $"工具返回了图片（{content.Text("mimeType") ?? "格式未知"}）。",
                "audio" => $"工具返回了音频（{content.Text("mimeType") ?? "格式未知"}）。",
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(description))
                yield return description;
        }
    }

    private static IReadOnlyList<PromptImage> ReadImages(JsonElement progress, int remaining)
    {
        var images = new List<PromptImage>();
        foreach (var entry in progress.Items())
        {
            if (images.Count >= PromptImage.MaxCount || remaining <= 0)
                break;
            var content = entry.Field("content");
            var data = content.Field("data");
            int limit = Math.Min(remaining, PromptImage.MaxFileBytes);
            if (content.Text("type") != "image" || content.Text("mimeType") is not { } mime ||
                !PromptImage.IsSupportedMimeType(mime) || data.ValueKind != JsonValueKind.String ||
                (data.GetString()?.Length ?? 0) > ((limit + 2) / 3) * 4 ||
                !data.TryGetBytesFromBase64(out var bytes) || bytes.Length == 0 || bytes.Length > limit)
                continue;
            images.Add(new PromptImage(mime, bytes));
            remaining -= bytes.Length;
        }
        return images;
    }

    private static IEnumerable<ToolLocation> ReadLocations(JsonElement locations)
    {
        foreach (var item in locations.Items())
        {
            if (item.Text("path") is { Length: > 0 } path)
            {
                var reported = item.Field("line");
                yield return new ToolLocation(path,
                    reported.ValueKind == JsonValueKind.Number && reported.TryGetUInt32(out var line) && line > 0
                        ? line : null);
            }
        }
    }

    private static string Join(params string?[] parts) =>
        string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
}
