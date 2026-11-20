namespace MyAntigravity;

public sealed record DiffSection(int OldLine, int NewLine, string[] Removed, string[] Added)
{
    public string Label => $"修改前第 {OldLine} 行，修改后第 {NewLine} 行，删除 {Removed.Length} 行，新增 {Added.Length} 行";

    public string Describe() => $"{Label}\n\n修改前：\n{DescribeLines(Removed, OldLine)}\n\n修改后：\n{DescribeLines(Added, NewLine)}";

    private static string DescribeLines(string[] lines, int start) => lines.Length == 0 ? "（无）"
        : string.Join("\n", lines.Select((line, index) =>
            $"{start + index}  {(line.Length == 0 ? "（空行）" : AgentText.Visible(line))}"));
}

public static class DiffSections
{
    private const int MaxMatrixCells = 1_000_000;

    public static IReadOnlyList<DiffSection> Build(FileChange change)
    {
        var before = Lines(change.Before);
        var after = Lines(change.After);
        var sections = new List<DiffSection>();
        AddRange(0, before.Length, 0, after.Length);
        return sections;

        void AddRange(int oldStart, int oldEnd, int newStart, int newEnd)
        {
            while (oldStart < oldEnd && newStart < newEnd && before[oldStart] == after[newStart])
            {
                oldStart++;
                newStart++;
            }
            while (oldStart < oldEnd && newStart < newEnd && before[oldEnd - 1] == after[newEnd - 1])
            {
                oldEnd--;
                newEnd--;
            }

            int oldCount = oldEnd - oldStart;
            int newCount = newEnd - newStart;
            if (oldCount == 0 || newCount == 0)
            {
                AddSection(oldStart, oldEnd, newStart, newEnd);
                return;
            }
            if (((long)oldCount + 1) * (newCount + 1) <= MaxMatrixCells)
            {
                var matches = MatchLines(before.AsSpan(oldStart, oldCount), after.AsSpan(newStart, newCount));
                int oldGap = oldStart;
                int newGap = newStart;
                foreach (var (oldLine, newLine) in matches)
                {
                    AddSection(oldGap, oldStart + oldLine, newGap, newStart + newLine);
                    oldGap = oldStart + oldLine + 1;
                    newGap = newStart + newLine + 1;
                }
                AddSection(oldGap, oldEnd, newGap, newEnd);
                return;
            }

            // Keep well-separated edits navigable even when a full matrix would be too large.
            var oldPositions = UniquePositions(before, oldStart, oldEnd);
            var newPositions = UniquePositions(after, newStart, newEnd);
            int previousOld = oldStart;
            int previousNew = newStart;
            for (int line = oldStart; line < oldEnd; line++)
            {
                string value = before[line];
                if (oldPositions[value] != line || !newPositions.TryGetValue(value, out int next) || next < previousNew)
                    continue;
                AddSmallGap(previousOld, line, previousNew, next);
                previousOld = line + 1;
                previousNew = next + 1;
            }
            AddSmallGap(previousOld, oldEnd, previousNew, newEnd);
        }

        void AddSmallGap(int oldStart, int oldEnd, int newStart, int newEnd)
        {
            if (((long)oldEnd - oldStart + 1) * (newEnd - newStart + 1) <= MaxMatrixCells)
                AddRange(oldStart, oldEnd, newStart, newEnd);
            else
                AddSection(oldStart, oldEnd, newStart, newEnd);
        }

        void AddSection(int oldStart, int oldEnd, int newStart, int newEnd)
        {
            if (oldStart != oldEnd || newStart != newEnd)
                sections.Add(new DiffSection(oldStart + 1, newStart + 1, before[oldStart..oldEnd], after[newStart..newEnd]));
        }
    }

    private static string[] Lines(string? text) => string.IsNullOrEmpty(text) ? []
        : text.ReplaceLineEndings("\n").Split('\n');

    private static Dictionary<string, int> UniquePositions(string[] lines, int start, int end)
    {
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int line = start; line < end; line++)
        {
            if (!positions.TryAdd(lines[line], line))
                positions[lines[line]] = -1;
        }
        return positions;
    }

    private static List<(int Old, int New)> MatchLines(ReadOnlySpan<string> before, ReadOnlySpan<string> after)
    {
        int width = after.Length + 1;
        var lengths = new int[(before.Length + 1) * width];
        for (int oldLine = before.Length - 1; oldLine >= 0; oldLine--)
        {
            for (int newLine = after.Length - 1; newLine >= 0; newLine--)
            {
                int index = oldLine * width + newLine;
                lengths[index] = before[oldLine] == after[newLine] ? 1 + lengths[index + width + 1]
                    : Math.Max(lengths[index + width], lengths[index + 1]);
            }
        }

        var matches = new List<(int, int)>();
        int previous = 0;
        int next = 0;
        while (previous < before.Length && next < after.Length)
        {
            if (before[previous] == after[next])
                matches.Add((previous++, next++));
            else if (lengths[(previous + 1) * width + next] >= lengths[previous * width + next + 1])
                previous++;
            else
                next++;
        }
        return matches;
    }
}
