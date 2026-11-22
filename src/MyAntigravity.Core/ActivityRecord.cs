using System.Text;

namespace MyAntigravity;

public enum TaskActivityKind { General, Failure, Interaction, Reasoning }

public sealed class ActivityRecord(string? session, DateTimeOffset at, string summary, string detail, TaskActivityKind kind)
{
    private StringBuilder? streamed;
    public string? Session => session;
    public string Summary => summary;
    public TaskActivityKind Kind => kind;
    public string Detail => streamed?.ToString() ?? detail;
    public string Timestamp => at.ToLocalTime().ToString("HH:mm:ss");
    public string LogText => $"[{Timestamp}] {Detail}";
    public override string ToString() => $"{Timestamp} · {Summary}";

    public void AppendDetail(string text) => (streamed ??= new StringBuilder(detail)).Append(text);

    public bool Matches(string? requestedSession, TaskActivityKind? requestedKind, IReadOnlyList<string> terms)
    {
        if ((requestedSession is not null && Session != requestedSession) || (requestedKind is not null && Kind != requestedKind))
            return false;
        if (terms.Count == 0)
            return true;
        var text = Detail;
        return terms.All(term => Summary.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
