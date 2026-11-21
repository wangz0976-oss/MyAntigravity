namespace MyAntigravity;

public sealed record GitWorktree(string Path, string? Branch, string? Commit, bool Bare, bool Locked, bool Prunable)
{
    public bool IsPrimary { get; init; }

    public string State => string.Join("，", new[]
    {
        Bare ? "裸仓库" : Branch ?? "分离的 HEAD",
        IsPrimary ? "主工作树" : "",
        Locked ? "已锁定" : "",
        Prunable ? "记录失效" : ""
    }.Where(value => value.Length > 0));

    public override string ToString() => AgentText.Visible($"{State} · {Path}");
}

public sealed record WorktreeInspection(GitWorktree Worktree, IReadOnlyList<GitChange> Changes,
    IReadOnlyList<string> IgnoredPaths, string? BlockedReason)
{
    public bool CanRemove => BlockedReason is null && Changes.Count == 0 && IgnoredPaths.Count == 0;
}

public sealed partial class GitReview
{
    public string Root => root;

    public async Task<IReadOnlyList<GitWorktree>> ListWorktreesAsync()
    {
        var result = await RunAsync(["worktree", "list", "--porcelain", "-z"]).ConfigureAwait(false);
        var worktrees = new List<GitWorktree>();
        foreach (var record in result.Output.Split("\0\0", StringSplitOptions.RemoveEmptyEntries))
        {
            string? path = null, branch = null, commit = null;
            bool bare = false, locked = false, prunable = false;
            foreach (var field in record.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var split = field.IndexOf(' ');
                var key = split >= 0 ? field[..split] : field;
                var value = split >= 0 ? field[(split + 1)..] : "";
                switch (key)
                {
                    case "worktree": path = value; break;
                    case "branch":
                        const string heads = "refs/heads/";
                        branch = value.StartsWith(heads, StringComparison.Ordinal) ? value[heads.Length..] : value;
                        break;
                    case "HEAD": commit = value; break;
                    case "bare": bare = true; break;
                    case "locked": locked = true; break;
                    case "prunable": prunable = true; break;
                }
            }
            if (path is null || !Path.IsPathFullyQualified(path))
                throw new IOException("Git 返回了无法识别的工作树路径。");
            worktrees.Add(new GitWorktree(Path.GetFullPath(path), branch, commit, bare, locked, prunable)
                { IsPrimary = worktrees.Count == 0 });
        }
        return worktrees;
    }

    public async Task<string> CreateWorktreeAsync(string destination, string branch)
    {
        if (!Path.IsPathFullyQualified(destination))
            throw new ArgumentException("请输入新工作树的完整目录路径。", nameof(destination));
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        if (Path.GetDirectoryName(directory) is not { } parent || !Directory.Exists(parent))
            throw new DirectoryNotFoundException("请选择一个已存在的父目录。");
        try
        {
            _ = File.GetAttributes(directory);
            throw new IOException("工作树目标路径已经存在。请输入一个新的目录名。");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }

        if (string.IsNullOrWhiteSpace(branch) || branch.StartsWith('-') ||
            (await RunAsync(["check-ref-format", $"refs/heads/{branch}"], expectedExit: 1)
                .ConfigureAwait(false)).ExitCode != 0)
            throw new ArgumentException("请输入有效的新分支名，例如 work/feature。", nameof(branch));

        var worktrees = await ListWorktreesAsync().ConfigureAwait(false);
        if (worktrees.Any(tree => !tree.Bare && ContainsDirectory(tree.Path, directory)))
            throw new IOException("请选择现有工作树之外的目录。可以在当前仓库的旁边创建。");

        await RunAsync(["-c", "core.hooksPath=/dev/null", "worktree", "add", "-b", branch, "--", directory, "HEAD"],
            limit: TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        return directory;
    }

    public async Task<WorktreeInspection> InspectWorktreeAsync(string path, IReadOnlyList<string> protectedDirectories)
    {
        var worktrees = await ListWorktreesAsync().ConfigureAwait(false);
        var worktree = worktrees.FirstOrDefault(tree => LocalSettings.PathEquals(tree.Path, path))
            ?? throw new IOException("这个工作树已不在 Git 的登记列表中。请刷新列表。");
        var reason = worktree switch
        {
            { IsPrimary: true } or { Bare: true } => "主工作树或裸仓库不能在此移除。",
            { Locked: true } => "工作树已被 Git 锁定。",
            { Prunable: true } => "工作树登记已经失效。",
            { Branch: null } => "工作树处于分离 HEAD。请先把当前提交保存在分支上。",
            _ => null
        };
        if (reason is not null)
            return new WorktreeInspection(worktree, [], [], reason);
        if (LocalSettings.PathEquals(root, worktree.Path) || protectedDirectories.Any(directory => ContainsDirectory(worktree.Path, directory)))
            return new WorktreeInspection(worktree, [], [], "正在使用的项目或附加目录不能移除。");
        if (!Directory.Exists(worktree.Path))
            return new WorktreeInspection(worktree, [], [], "工作树目录已不存在。");
        for (DirectoryInfo? directory = new(worktree.Path); directory is not null; directory = directory.Parent)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                return new WorktreeInspection(worktree, [], [], "工作树路径包含链接。");
        }

        var candidate = await OpenAsync(worktree.Path).ConfigureAwait(false);
        if (candidate is null || !LocalSettings.PathEquals(candidate.Root, worktree.Path))
            return new WorktreeInspection(worktree, [], [], "当前目录与 Git 登记的工作树不一致。");
        var common = await RunAsync(["rev-parse", "--path-format=absolute", "--git-common-dir"]).ConfigureAwait(false);
        var checkoutCommon = await candidate.RunAsync(["rev-parse", "--path-format=absolute", "--git-common-dir"])
            .ConfigureAwait(false);
        if (!LocalSettings.PathEquals(Path.GetFullPath(common.Output.TrimEnd('\r', '\n')),
            Path.GetFullPath(checkoutCommon.Output.TrimEnd('\r', '\n'))))
            return new WorktreeInspection(worktree, [], [], "这个目录现在属于其他 Git 仓库。");

        var status = await candidate.RunAsync(["status", "--porcelain=v1", "-z", "--untracked-files=all",
            "--ignored=matching", "--no-renames", "--ignore-submodules=none"]).ConfigureAwait(false);
        var entries = status.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var ignored = entries.Where(entry => entry.StartsWith("!! ", StringComparison.Ordinal))
            .Select(entry => entry[3..]).ToArray();
        var changes = ParseStatus(string.Join('\0', entries.Where(entry => !entry.StartsWith("!! ", StringComparison.Ordinal))));
        return new WorktreeInspection(worktree, changes, ignored, null);
    }

    public async Task RemoveWorktreeAsync(WorktreeInspection confirmed, IReadOnlyList<string> protectedDirectories)
    {
        if (!confirmed.CanRemove)
            throw new IOException("只能移除已确认且干净的工作树。");
        var current = await InspectWorktreeAsync(confirmed.Worktree.Path, protectedDirectories).ConfigureAwait(false);
        if (!current.CanRemove || current.Worktree != confirmed.Worktree)
            throw new IOException("工作树状态已变化。请重新检查后再确认。");
        await RunAsync(["worktree", "remove", "--", current.Worktree.Path], limit: TimeSpan.FromMinutes(2)).ConfigureAwait(false);
    }

    private static bool ContainsDirectory(string parent, string path)
    {
        var relative = Path.GetRelativePath(parent, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
