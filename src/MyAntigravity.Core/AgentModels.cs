using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyAntigravity;

public sealed record Choice(string Id, string Name, string? Description)
{
    public override string ToString() => Name;
}

public sealed record SessionOption(string Id, string Label, string? Description,
    string? Category, string? Current, IReadOnlyList<Choice> Choices, string Method, bool IsBoolean = false)
{
    public override string ToString() => Label;

    public static IReadOnlyList<SessionOption> Parse(JsonElement data)
    {
        var options = new List<SessionOption>();
        foreach (var option in data.Field("configOptions").Items())
        {
            if (option.Text("id") is not { } id)
                continue;

            var boolean = option.Text("type") == "boolean";
            if (!boolean && option.Text("type") is not (null or "select"))
                continue;
            var category = option.Text("category") ?? AcpDisplayText.OptionCategory(id);

            Choice[] values = boolean
                ? [new("true", "开启", null), new("false", "关闭", null)]
                : option.Field("options").Items().SelectMany(group => group.Field("options").ValueKind == JsonValueKind.Array
                    ? group.Field("options").Items() : [group]).Where(item => item.Text("value") is not null)
                    .Select(item => AcpDisplayText.LocalizeChoice(category, item.Text("value")!,
                        item.Text("name") ?? item.Text("value")!, item.Text("description"))).ToArray();
            if (values.Length > 0)
                options.Add(new SessionOption(id, AcpDisplayText.OptionLabel(category, option.Text("name") ?? id),
                    AcpDisplayText.OptionDescription(category, option.Text("description")), category,
                    boolean ? option.Flag("currentValue").ToString().ToLowerInvariant()
                        : option.Text("currentValue"), values, "session/set_config_option", boolean));
        }
        if (!options.Any(option => option.Category == "mode"))
        {
            var modes = data.Field("modes");
            var values = modes.Field("availableModes").Items().Where(item => item.Text("id") is not null)
                .Select(item => AcpDisplayText.LocalizeChoice("mode", item.Text("id")!,
                    item.Text("name") ?? item.Text("id")!, item.Text("description"))).ToArray();
            if (values.Length > 0)
                options.Add(new SessionOption("mode", AcpDisplayText.OptionLabel("mode", "模式"),
                    AcpDisplayText.OptionDescription("mode", null), "mode", modes.Text("currentModeId"),
                    values, "session/set_mode"));
        }
        return options;
    }
}

public sealed record FileChange(string Path, string? Before, string After)
{
    public string Describe() => $"文件：{AgentText.Visible(Path)}\n\n修改前：\n{AgentText.Visible(Before ?? "（无）")}\n\n修改后：\n{AgentText.Visible(After)}";
}

public sealed record PermissionOffer(string Id, string Name, string Kind)
{
    public override string ToString()
    {
        var scope = Kind switch
        {
            "allow_once" => "仅本次允许",
            "allow_always" => "记住此类允许",
            "reject_once" => "仅本次拒绝",
            "reject_always" => "始终拒绝",
            _ => null
        };
        if (scope is null)
            return AgentText.Visible(Name);
        if (Name is "Allow" or "Reject" or "Deny" or "Allow once" or "Always allow" or "Allow always" or "Reject once" or "Always reject")
            return scope;
        return $"{AgentText.Visible(Name)}（{scope}）";
    }
}

public sealed record PermissionRequest(string SessionId, string Description, IReadOnlyList<PermissionOffer> Options)
{
    public IReadOnlyList<FileChange> Changes { get; init; } = [];

    public static PermissionRequest Parse(JsonElement parameters) => new(parameters.Text("sessionId") ?? "",
        AgentText.ToolDetails(parameters.Field("toolCall")),
        parameters.Field("options").Items().Where(value =>
            value.Text("optionId") is not null && value.Text("kind") is "allow_once" or "allow_always" or "reject_once" or "reject_always")
            .Select(value => new PermissionOffer(value.Text("optionId")!, value.Text("name") ?? "审批选项",
                value.Text("kind")!)).ToArray())
        { Changes = AgentText.Changes(parameters.Field("toolCall")).ToArray() };

    public string? Reject() => Options.FirstOrDefault(option => option.Kind == "reject_once")?.Id;

    public object Answer(string? selected) =>
        selected is not null && Options.Any(option => option.Id == selected)
            ? new { outcome = new { outcome = "selected", optionId = selected } }
            : new { outcome = new { outcome = "cancelled" } };
}

public static partial class AgentText
{
    [GeneratedRegex(@"[\p{Cf}\p{Cc}-[\r\n\t]]")]
    private static partial Regex HiddenCharacters();

    public static string Visible(string text) => HiddenCharacters().Replace(text,
        match => $"\\u{(int)match.Value[0]:X4}");

    public static string ToolDetails(JsonElement update)
    {
        var details = new StringBuilder(update.Text("title") ?? "工具操作");
        foreach (var name in new[] { "kind", "status", "rawInput", "rawOutput" })
        {
            var item = update.Field(name);
            if (item.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            var text = item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText();
            text = name switch
            {
                "kind" => AcpDisplayText.ToolKind(text),
                "status" => AcpDisplayText.ToolStatus(text),
                _ => text
            };
            var label = name switch { "kind" => "类型", "status" => "状态", "rawInput" => "输入", _ => "输出" };
            details.AppendLine().Append(label).Append("：").Append(text);
        }
        foreach (var location in update.Field("locations").Items())
            details.AppendLine().Append("位置：").Append(location.Text("path"));
        foreach (var item in update.Field("content").Items())
        {
            if (item.Field("content").Text("text") is { } text)
                details.AppendLine().Append(text);
        }
        var result = new StringBuilder(Visible(details.ToString()));
        foreach (var diff in Changes(update))
            result.AppendLine().AppendLine().Append(diff.Describe());
        return result.ToString();
    }

    public static IEnumerable<FileChange> Changes(JsonElement update) =>
        update.Field("content").Items().Where(item => item.Text("type") == "diff" &&
            item.Text("path") is not null && item.Text("newText") is not null)
        .Select(item => new FileChange(item.Text("path")!, item.Text("oldText"), item.Text("newText")!));
}
