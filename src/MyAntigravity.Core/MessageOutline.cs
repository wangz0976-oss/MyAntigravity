using System.Text.RegularExpressions;

namespace MyAntigravity;

public sealed record MessageCodeBlock(int Line, string Language, int Offset, int End, string Code)
{
    public override string ToString() => $"第 {Line} 行 · {(Language.Length == 0 ? "代码" : AgentText.Visible(Language))}";
}

public sealed record MessageLink(int Line, int Offset, string Address)
{
    public override string ToString() => $"第 {Line} 行 · {Address}";
}

public sealed partial class MessageOutline
{
    public IReadOnlyList<MessageCodeBlock> CodeBlocks { get; }
    public IReadOnlyList<MessageLink> Links { get; }

    private MessageOutline(List<MessageCodeBlock> codeBlocks, List<MessageLink> links) =>
        (CodeBlocks, Links) = (codeBlocks, links);

    // Offsets refer to text normalized to LF, as used by the message reader.
    public static MessageOutline Parse(string body)
    {
        var codeBlocks = new List<MessageCodeBlock>();
        var links = new List<MessageLink>();
        int start = 0;
        int lineNumber = 0;
        int fenceLength = 0;
        int codeStart = 0;
        int codeLine = 0;
        char fence = '\0';
        string language = "";
        foreach (var line in body.Split('\n'))
        {
            lineNumber++;
            ReadOnlySpan<char> trimmed = line.AsSpan().TrimStart(' ');
            int run = 0;
            char marker = trimmed.IsEmpty ? '\0' : trimmed[0];
            if (line.Length - trimmed.Length <= 3 && marker is '\x60' or '~')
                while (run < trimmed.Length && trimmed[run] == marker)
                    run++;

            if (fenceLength > 0)
            {
                if (marker == fence && run >= fenceLength && trimmed[run..].Trim().IsEmpty)
                {
                    codeBlocks.Add(new MessageCodeBlock(codeLine, language, codeStart, start, body[codeStart..start]));
                    fenceLength = 0;
                }
            }
            else if (run >= 3 && (marker != '\x60' || !trimmed[run..].Contains('\x60')))
            {
                fence = marker;
                fenceLength = run;
                codeStart = Math.Min(body.Length, start + line.Length + 1);
                codeLine = lineNumber;
                language = trimmed[run..].Trim().ToString().Split((char[]?)null, 2)[0];
            }
            else
            {
                foreach (Match match in LinkPattern().Matches(line))
                {
                    var address = match.Value.TrimEnd('.', ',', ';', '!', '?', '"', '\'');
                    if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Host.Length > 0 &&
                        uri.UserInfo.Length == 0)
                        links.Add(new MessageLink(lineNumber, start + match.Index, address));
                }
            }
            start += line.Length + 1;
        }
        if (fenceLength > 0)
            codeBlocks.Add(new MessageCodeBlock(codeLine, language, codeStart, body.Length, body[codeStart..]));
        return new MessageOutline(codeBlocks, links);
    }

    [GeneratedRegex(@"https?://[^\s<>()\[\]\x00-\x1f]+", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex LinkPattern();
}
