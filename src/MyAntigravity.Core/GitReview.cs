using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MyAntigravity;

public enum GitChangeScope { Staged, Worktree, Untracked, Conflict }

public sealed record GitChange(string Path, GitChangeScope Scope, char Status)
{
    public string ScopeLabel => Scope switch
    {
        GitChangeScope.Staged => "已暂存",
        GitChangeScope.Worktree => "未暂存",
        GitChangeScope.Untracked => "未跟踪",
        _ => "冲突"
    };

    public override string ToString() => $"{ScopeLabel} · {AgentText.Visible(Path)}";
}

public sealed partial class GitReview
{
    private const int OutputLimit = 2_000_000;
    private readonly string executable;
    private readonly string root;

    private GitReview(string executable, string root) => (this.executable, this.root) = (executable, root);

    public static async Task<GitReview?> OpenAsync(string cwd)
    {
        string? git = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(item => item.Trim().Trim('"')).Where(Path.IsPathFullyQualified)
            .Select(directory => Path.Combine(directory, OperatingSystem.IsWindows() ? "git.exe" : "git"))
            .FirstOrDefault(File.Exists);
        if (git is null)
            throw new IOException("未找到 Git。请安装 Git 并让系统能找到 git.exe。");
        var probe = new GitReview(git, cwd);
        var result = await probe.RunAsync(["rev-parse", "--show-toplevel"], expectedExit: 128).ConfigureAwait(false);
        if (result.ExitCode == 128 && result.Error.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
            return null;
        var root = result.Output.TrimEnd('\r', '\n');
        if (result.ExitCode != 0 || !Path.IsPathFullyQualified(root) || !Directory.Exists(root))
            throw new IOException("Git 无法读取该项目的工作区根目录。");
        return new GitReview(git, Path.GetFullPath(root));
    }

    public async Task<IReadOnlyList<GitChange>> ListAsync()
    {
        var result = await RunAsync(["status", "--porcelain=v1", "-z", "--untracked-files=all", "--no-renames",
            "--ignore-submodules=dirty"]).ConfigureAwait(false);
        return ParseStatus(result.Output);
    }

    public async Task<string> ReadPatchAsync(GitChange change)
    {
        var arguments = new List<string> { "diff", "--no-ext-diff", "--no-textconv", "--no-color", "--no-renames" };
        if (change.Scope == GitChangeScope.Untracked)
        {
            ValidateUntracked(change.Path);
            arguments.AddRange(["--no-index", "--", "/dev/null", change.Path]);
        }
        else
        {
            if (change.Scope == GitChangeScope.Staged)
                arguments.Add("--cached");
            arguments.AddRange(["--", change.Path]);
        }
        var result = await RunAsync(arguments, change.Scope == GitChangeScope.Untracked ? 1 : 0).ConfigureAwait(false);
        return result.Output;
    }

    private void ValidateUntracked(string relativePath)
    {
        var parts = relativePath.Split('/');
        if (Path.IsPathRooted(relativePath) || parts.Any(part => part is "" or "." or ".." ||
            part.Contains('\\') || part.Contains(':')))
            throw new IOException("Git 返回的未跟踪文件路径无效。");
        string current = root;
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("未跟踪文件路径包含链接，无法在此读取差异。");
        }
        if (!File.Exists(current))
            throw new IOException("该未跟踪文件已移动或不存在。");
    }

    public static IReadOnlyList<GitChange> ParseStatus(string output)
    {
        var changes = new List<GitChange>();
        foreach (var line in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4 || line[2] != ' ')
                throw new IOException("Git 返回了无法识别的文件状态。");
            string flags = line[..2];
            string path = line[3..];
            if (flags == "??")
                changes.Add(new GitChange(path, GitChangeScope.Untracked, '?'));
            else if (flags is "DD" or "AU" or "UD" or "UA" or "DU" or "AA" or "UU")
                changes.Add(new GitChange(path, GitChangeScope.Conflict, 'U'));
            else
            {
                if (flags[0] != ' ')
                    changes.Add(new GitChange(path, GitChangeScope.Staged, flags[0]));
                if (flags[1] != ' ')
                    changes.Add(new GitChange(path, GitChangeScope.Worktree, flags[1]));
            }
        }
        return changes.OrderBy(change => change.Scope).ThenBy(change => change.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<DiffSection> ReadSections(string patch)
    {
        var sections = new List<DiffSection>();
        List<string> removed = [], added = [];
        int oldLine = 0, newLine = 0, firstOld = 0, firstNew = 0;
        bool inHunk = false;
        foreach (var line in patch.Split('\n'))
        {
            var header = HunkHeader().Match(line);
            if (header.Success && int.TryParse(header.Groups[1].ValueSpan, out int oldStart) &&
                int.TryParse(header.Groups[2].ValueSpan, out int newStart))
            {
                Flush();
                oldLine = oldStart;
                newLine = newStart;
                inHunk = true;
            }
            else if (inHunk && line.Length > 0)
            {
                switch (line[0])
                {
                    case '-':
                    case '+':
                        if (removed.Count == 0 && added.Count == 0)
                            (firstOld, firstNew) = (oldLine, newLine);
                        if (line[0] == '-')
                        {
                            removed.Add(line[1..]);
                            oldLine++;
                        }
                        else
                        {
                            added.Add(line[1..]);
                            newLine++;
                        }
                        break;
                    case ' ':
                        Flush();
                        oldLine++;
                        newLine++;
                        break;
                    case '\\': break;
                    default:
                        Flush();
                        inHunk = false;
                        break;
                }
            }
        }
        Flush();
        return sections;

        void Flush()
        {
            if (removed.Count == 0 && added.Count == 0)
                return;
            sections.Add(new DiffSection(firstOld, firstNew, removed.ToArray(), added.ToArray()));
            removed.Clear();
            added.Clear();
        }
    }

    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(IEnumerable<string> arguments,
        int expectedExit = 0, TimeSpan? limit = null)
    {
        var start = Cli.StartInfo(executable, root, true, "--literal-pathspecs", "-c", "core.fsmonitor=false");
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["LC_ALL"] = "C";
        foreach (var key in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR" })
            start.Environment.Remove(key);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("无法启动 Git。");
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(limit ?? TimeSpan.FromSeconds(15));
        var stdout = ReadAsync(process.StandardOutput, timeout.Token);
        var stderr = ReadAsync(process.StandardError, timeout.Token);
        try
        {
            var output = await stdout.ConfigureAwait(false);
            var error = await stderr.ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (process.ExitCode != 0 && process.ExitCode != expectedExit)
                throw new IOException($"Git 操作失败（退出码 {process.ExitCode}）。\n{AgentText.Visible(error.Trim())}");
            return (process.ExitCode, output, error);
        }
        catch (OperationCanceledException) { throw new TimeoutException("Git 操作超时。"); }
        finally { Cli.Stop(process); }
    }

    private static async Task<string> ReadAsync(StreamReader reader, CancellationToken cancellation)
    {
        var text = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) != 0)
        {
            if (text.Length + read > OutputLimit)
                throw new IOException("Git 输出过大。可缩小改动范围后重试。");
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }
}
