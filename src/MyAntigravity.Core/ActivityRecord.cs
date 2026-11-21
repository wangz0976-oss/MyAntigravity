namespace MyAntigravity;

public enum TaskActivityKind { General, Failure, Interaction }

public sealed record ActivityRecord(string? Session, DateTimeOffset At, string Summary, string Detail, TaskActivityKind Kind)
{
    public string Timestamp => At.ToLocalTime().ToString("HH:mm:ss");
    public string LogText => $"[{Timestamp}] {Detail}";
    public override string ToString() => $"{Timestamp} · {Summary}";

    public bool Matches(string? session, TaskActivityKind? kind, IReadOnlyList<string> terms) =>
        (session is null || Session == session) && (kind is null || Kind == kind) &&
        terms.All(term => Summary.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            Detail.Contains(term, StringComparison.OrdinalIgnoreCase));
}
