namespace MyAntigravity;

public sealed record ProjectFilePreview(string FullPath, string Text, uint RequestedLine, uint ActualLine, int Start, int Length)
{
    public const int MaxBytes = 1024 * 1024;

    public static async Task<ProjectFilePreview> ReadAsync(ToolLocation location, IReadOnlyList<string> workspaces,
        CancellationToken cancellation = default)
    {
        string path = ResolvePath(location.Path, workspaces);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length > MaxBytes)
            throw new IOException("项目文本预览支持最多 1 MiB 的文件。");
        var bytes = new byte[MaxBytes + 1];
        int count = await file.ReadAtLeastAsync(bytes, bytes.Length, false, cancellation)
            .ConfigureAwait(false);
        if (count > MaxBytes)
            throw new IOException("项目文本预览支持最多 1 MiB 的文件。");
        string decoded = PromptTextFile.Decode(bytes.AsSpan(0, count));
        if (decoded.Any(ch => char.IsControl(ch) && ch is not ('\r' or '\n' or '\t' or '\f')))
            throw new IOException("这个文件包含二进制内容或不支持的控制字符。");
        string text = AgentText.Visible(decoded.ReplaceLineEndings("\n"));

        uint requested = Math.Max(1u, location.Line ?? 1u);
        uint actual = 1;
        int start = 0;
        int next = text.IndexOf('\n');
        while (actual < requested && next >= 0)
        {
            start = next + 1;
            actual++;
            next = text.IndexOf('\n', start);
        }
        return new ProjectFilePreview(path, text, requested, actual, start, (next >= 0 ? next : text.Length) - start);
    }

    private static string ResolvePath(string location, IReadOnlyList<string> workspaces)
    {
        if (workspaces.Count == 0 || string.IsNullOrWhiteSpace(location) ||
            location.Contains("://", StringComparison.Ordinal) ||
            (Path.IsPathRooted(location) && !Path.IsPathFullyQualified(location)))
            throw new IOException("这个结果没有可在当前项目预览的文件路径。");

        string primary = Path.GetFullPath(workspaces[0]);
        string path = Path.GetFullPath(location, primary);
        bool absolute = Path.IsPathFullyQualified(location);
        var candidates = absolute ? workspaces : [primary];
        foreach (string entry in candidates)
        {
            string root = Path.GetFullPath(entry);
            string relative = Path.GetRelativePath(root, path);
            if (relative is "." or ".." || Path.IsPathRooted(relative) || relative.Contains(':') ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
                continue;
            if (!File.Exists(path))
                throw new FileNotFoundException("项目文件已移动或不存在。");
            for (string? part = path; part is not null; part = Path.GetDirectoryName(part))
            {
                if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("该路径经过文件系统链接，无法在项目内预览。");
            }
            return path;
        }
        throw new IOException("只可预览当前项目及附加工作目录里的文件。");
    }
}
