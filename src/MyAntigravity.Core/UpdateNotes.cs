using System.Net;
using System.Text.RegularExpressions;

namespace MyAntigravity;

public static partial class UpdateNotes
{
    public static string ForVersion(string changelog, string version)
    {
        var lines = changelog.ReplaceLineEndings("\n").Split('\n');
        int heading = Array.FindIndex(lines, line => line.TrimEnd().Equals($"## {version}", StringComparison.Ordinal));
        if (heading < 0)
            return "";
        return string.Join('\n', lines.Skip(heading + 1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))).Trim();
    }

    [GeneratedRegex(@"^\s{0,3}#{1,6}(?:\s+|$)|(?<=\s)#+\s*$", RegexOptions.Multiline)]
    private static partial Regex HeadingMarks();
    [GeneratedRegex(@"^[ \t]{0,3}>(?:[ \t]?>(?:[ \t]?))*[ \t]?", RegexOptions.Multiline)]
    private static partial Regex QuoteMarks();
    [GeneratedRegex(@"^([ \t]*)[-*+][ \t]+", RegexOptions.Multiline)]
    private static partial Regex BulletMarks();
    [GeneratedRegex(@"^[ \t]{0,3}(?:[-=_*][ \t]*){3,}$", RegexOptions.Multiline)]
    private static partial Regex Rule();
    [GeneratedRegex(@"!?\[([^\]\n]+)\]\((?:[^()\n]|\([^()\n]*\))*\)|!?\[([^\]\n]+)\]\[[^\]\n]*\]|<((?:https?://|mailto:)[^>\s]+)>")]
    private static partial Regex Link();
    [GeneratedRegex(@"\*\*\*([^\n*]+)\*\*\*|(\*\*|__|~~)(\S(?:[^\n]*?\S)?)\2|(?<!\w)(\*|_)(\S(?:[^\n]*?\S)?)\4(?!\w)")]
    private static partial Regex Emphasis();
    [GeneratedRegex(@"(\x60+)([^\n]+?)\1(?!\x60)")]
    private static partial Regex InlineCode();
    [GeneratedRegex(@"^ {0,3}(\x60{3,}|~{3,})(.*)$")]
    private static partial Regex Fence();

    public static string PlainText(string notes)
    {
        var lines = notes.ReplaceLineEndings("\n").Split('\n');
        string? fence = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var mark = Fence().Match(lines[i]);
            if (mark.Success && (fence is null ||
                (mark.Groups[1].Value.StartsWith(fence, StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(mark.Groups[2].Value))))
            {
                fence = fence is null ? mark.Groups[1].Value : null;
                lines[i] = "";
                continue;
            }
            if (fence is not null)
                continue;
            var line = QuoteMarks().Replace(lines[i], "");
            line = BulletMarks().Replace(HeadingMarks().Replace(Rule().Replace(line, ""), ""), "$1• ");
            var codes = InlineCode().Matches(line);
            if (codes.Count == 0)
            {
                lines[i] = StripInline(line);
                continue;
            }
            var result = new System.Text.StringBuilder();
            var previous = 0;
            foreach (Match code in codes)
            {
                result.Append(StripInline(line[previous..code.Index])).Append(code.Groups[2].Value);
                previous = code.Index + code.Length;
            }
            lines[i] = result.Append(StripInline(line[previous..])).ToString();
        }
        return AgentText.Visible(string.Join('\n', lines).Trim());
    }

    private static string StripInline(string text)
    {
        text = Link().Replace(text, "$1$2$3");
        return WebUtility.HtmlDecode(Emphasis().Replace(text, "$1$3$5"));
    }
}
