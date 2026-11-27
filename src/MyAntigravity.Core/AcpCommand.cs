using System.Text.Json;

namespace MyAntigravity;

public sealed record AcpCommand(string Name, string Description, string? InputHint)
{
    public override string ToString() => "/" + Name;

    public bool IsCollaboration => Name is "agents" or "teamwork" or "swarm" or "boost" or "run-workflow";

    public static IReadOnlyList<AcpCommand> Parse(JsonElement update)
    {
        var commands = new List<AcpCommand>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in update.Field("availableCommands").Items())
        {
            var name = item.Text("name")?.TrimStart('/');
            if (string.IsNullOrEmpty(name) ||
                name.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) ||
                AgentText.Visible(name) != name || !seen.Add(name))
                continue;

            commands.Add(new AcpCommand(name,
                AgentText.Visible(item.Text("description") ?? ""),
                item.Field("input").Text("hint") is { } hint ? AgentText.Visible(hint) : null));
        }
        return commands;
    }

    public (string Text, int Caret) InsertBefore(string draft)
    {
        var prefix = ToString();
        if (draft.Length > 0 && !char.IsWhiteSpace(draft[0]) ||
            draft.Length == 0 && !string.IsNullOrWhiteSpace(InputHint))
            prefix += " ";
        return (prefix + draft, prefix.Length);
    }
}
