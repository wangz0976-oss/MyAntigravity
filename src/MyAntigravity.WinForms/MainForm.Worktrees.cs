namespace MyAntigravity;

internal sealed partial class MainForm
{
    private Task BrowseWorktreesAsync() => RunAsync(async () =>
    {
        if (await OpenWorktreeRepositoryAsync() is not { } repository || closing)
            return;
        var worktrees = await repository.ListWorktreesAsync();
        if (closing)
            return;

        GitWorktree? selected = worktrees.FirstOrDefault(tree => LocalSettings.PathEquals(tree.Path, repository.Root));
        while (!closing)
        {
            var selection = Dialogs.Browse(this, "Git 工作树", "按路径、分支或状态筛选", "Git 工作树列表",
                "打开为项目 (&O)", worktrees, tree => $"{tree.Path} {tree.State}",
                tree => $"目录：{AgentText.Visible(tree.Path)}\n状态：{AgentText.Visible(tree.State)}" +
                    $"\n提交：{AgentText.Visible(tree.Commit ?? "未报告")}", removeText: "检查并移除 (&D)", initial: selected);
            selected = selection.Item;
            if (selected is null)
                return;
            if (selection.Remove)
            {
                if (await InspectAndRemoveWorktreeAsync(repository, selected))
                    selected = null;
                worktrees = await repository.ListWorktreesAsync();
                selected ??= worktrees.FirstOrDefault(tree => LocalSettings.PathEquals(tree.Path, repository.Root));
                continue;
            }
            if (selected.Bare || selected.Prunable || !Directory.Exists(selected.Path))
            {
                Dialogs.Text(this, "无法打开工作树", "所选记录没有可打开的工作目录。", showInTaskbar: true);
                continue;
            }
            await SelectProjectAsync(selected.Path);
            return;
        }
    });

    private async Task<bool> InspectAndRemoveWorktreeAsync(GitReview repository, GitWorktree selected)
    {
        string[] inUse = ExtensionWorkspaces();
        var inspection = await repository.InspectWorktreeAsync(selected.Path, inUse);
        if (closing)
            return false;
        string details = $"目录：{AgentText.Visible(inspection.Worktree.Path)}\n状态：{AgentText.Visible(inspection.Worktree.State)}";
        if (!inspection.CanRemove)
        {
            if (inspection.BlockedReason is { } reason)
                details += $"\n\n{reason}";
            if (inspection.Changes.Count > 0)
                details += "\n\n有未提交改动：\n" + string.Join("\n", inspection.Changes);
            if (inspection.IgnoredPaths.Count > 0)
                details += "\n\n有 Git 忽略的文件或目录：\n" +
                    string.Join("\n", inspection.IgnoredPaths.Select(AgentText.Visible));
            Dialogs.Text(this, "工作树暂不可移除", details, showInTaskbar: true);
            return false;
        }
        if (!Dialogs.ConfirmCleanup(this, "移除干净的 Git 工作树",
            $"{details}\n\nGit 将移除这个工作树目录。分支、已提交的内容和本机会话历史会保留。",
            "移除工作树 (&D)"))
            return false;

        await repository.RemoveWorktreeAsync(inspection, inUse);
        var previous = settings.Projects.ToList();
        if (settings.RemoveProject(selected.Path))
        {
            try { storage.Save(settings); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                settings.Projects = previous;
                throw new IOException($"Git 工作树已移除。项目列表尚未保存：{error.Message}", error);
            }
            ReloadProjects();
        }
        SetStatus("Git 工作树已移除。分支和本机会话历史已保留。", speak: true);
        return true;
    }

    private Task CreateWorktreeAsync() => RunAsync(async () =>
    {
        if (await OpenWorktreeRepositoryAsync() is not { } repository || closing ||
            Dialogs.CreateWorktree(this, repository) is not { } created)
            return;
        try { await SelectProjectAsync(created); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Git 工作树已经创建，可以用“添加本地项目”打开。\n目录：{created}\n{error.Message}", error);
        }
        SetStatus("已创建 Git 工作树并打开为项目。可以新建会话。", speak: true);
    });

    private async Task<GitReview?> OpenWorktreeRepositoryAsync()
    {
        if (project is null)
            return null;
        var repository = await GitReview.OpenAsync(project);
        if (repository is null && !closing)
            Dialogs.Text(this, "Git 工作树", "当前项目不在 Git 工作区中。", showInTaskbar: true);
        return repository;
    }
}
