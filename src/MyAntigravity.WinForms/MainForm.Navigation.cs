using System.Text.Json;

namespace MyAntigravity;

internal sealed partial class MainForm
{
    private readonly Dictionary<string, string?> activeTitles = new(StringComparer.Ordinal);
    private IReadOnlyList<AcpCommand> availableCommands = [];
    private List<(string SessionId, JsonElement Data)>? pendingSessionMetadata;

    private void WireHistoryContextMenu()
    {
        var menu = new ContextMenuStrip { AccessibleName = "历史会话操作" };
        var reopen = Item("恢复所选会话 (&H)", action: RestoreSelectedAsync);
        var rename = Item("重命名本地会话 (&E)", action: RenameSessionAsync);
        var archive = Item("移入本地归档 (&A)", action: ArchiveSelectedAsync);
        var openArchive = Item("打开本地归档 (&B)", action: ShowArchivedAsync);
        var remove = Item("从本机历史删除会话 (&D)", action: RemoveSelectedHistoryAsync);
        menu.Items.AddRange([reopen, rename, archive, openArchive, new ToolStripSeparator(), remove]);
        menu.Opening += (_, _) =>
        {
            reopen.Enabled = restore.Enabled;
            rename.Enabled = renameSession.Enabled;
            archive.Enabled = archiveSession.Enabled;
            openArchive.Enabled = archivedHistory.Enabled;
            remove.Enabled = removeSession.Enabled;
        };
        AssignListMenu(sessions, menu);
    }

    private void WireProjectContextMenu()
    {
        var menu = new ContextMenuStrip { AccessibleName = "项目列表操作" };
        var open = Item("打开所选项目 (&P)", action: OpenSelectedProjectAsync);
        var explorer = Item("用文件资源管理器打开 (&F)", action: ShowSelectedFolderAsync);
        var copy = Item("复制项目路径 (&C)", action: CopySelectedFolderAsync);
        var remove = Item("从项目列表移除 (&D)", action: RemoveSelectedProjectAsync);
        menu.Items.AddRange([open, explorer, copy, new ToolStripSeparator(), remove]);
        menu.Opening += (_, _) =>
        {
            open.Enabled = explorer.Enabled = copy.Enabled = openProject.Enabled;
            remove.Enabled = removeProject.Enabled;
        };
        AssignListMenu(projects, menu);
    }

    private static void AssignListMenu(ListBox list, ContextMenuStrip menu)
    {
        menu.Opened += (_, _) => menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(item => item.Enabled)?.Select();
        list.ContextMenuStrip = menu;
        list.Disposed += (_, _) => menu.Dispose();
    }

    private Task ArchiveSelectedAsync() => RunAsync(() =>
    {
        if (project is null || sessions.SelectedItem is not LocalSession selected ||
            settings.FindSession(selected.Id, project) is not { Archived: false } local)
            return Task.CompletedTask;
        SetArchived(local, true);
        PopulateSessions();
        sessions.Focus();
        SetStatus("会话已移入本地归档。按 Ctrl+Shift+H 可以恢复到历史列表。");
        return Task.CompletedTask;
    });

    private Task ShowArchivedAsync() => RunAsync(async () =>
    {
        if (project is null)
            return;
        var entries = settings.ProjectSessions(project, showOfficialHistory ? officialSessions : [], activeTitles, archived: true);
        var (selected, remove) = Dialogs.Browse(this, "当前项目的本地归档", "搜索名称、日期或会话 ID", "已归档会话",
            "恢复到历史 (&R)", entries, entry => $"{entry} {entry.Id}", entry => $"{entry}\n会话 ID：{entry.Id}",
            removeText: "删除本机记录 (&D)");
        if (selected is not null)
        {
            if (remove)
            {
                await RemoveHistoryAsync(selected);
                return;
            }
            SetArchived(selected, false);
            PopulateSessions(selected.Id);
            sessions.Focus();
            SetStatus("会话已恢复到历史列表。按回车打开。");
        }
    });

    private void SetArchived(LocalSession record, bool value)
    {
        if (!settings.SetSessionArchived(record.Id, record.Directory, value))
            return;
        try { storage.Save(settings); }
        catch
        {
            settings.SetSessionArchived(record.Id, record.Directory, !value);
            throw;
        }
    }

    private Task FindProjectAsync() => RunAsync(async () =>
    {
        var (selected, remove) = Dialogs.Browse(this, "快速打开项目", "搜索项目名称或路径", "已添加项目", "打开项目 (&O)",
            settings.Projects.ToArray(), path => path, removeText: "从项目列表移除 (&D)",
            initial: projects.SelectedItem as string);
        if (selected is null)
            return;
        if (remove)
            await RemoveProjectAsync(selected);
        else
            await SelectProjectAsync(selected);
    });

    private async Task FindSessionAsync()
    {
        if (busy || closing || project is null)
            return;

        var entries = sessions.Items.Cast<LocalSession>().ToArray();
        var (selected, remove) = Dialogs.Browse(this, "查找当前项目的会话", "搜索名称、日期或会话 ID", "当前项目的会话", "打开会话 (&O)",
            entries, entry => $"{entry} {entry.Id}", entry => $"{entry}\n会话 ID：{entry.Id}",
            removeText: "删除本机记录 (&D)", initial: sessions.SelectedItem as LocalSession);
        if (selected is null)
            return;
        if (remove)
            await RunAsync(() => RemoveHistoryAsync(selected));
        else
            await StartSessionAsync(selected.Id);
    }

    private Task RemoveSelectedProjectAsync() => RunAsync(() => projects.SelectedItem is string directory
        ? RemoveProjectAsync(directory) : Task.CompletedTask);

    private async Task RemoveProjectAsync(string directory)
    {
        int index = settings.Projects.FindIndex(path => LocalSettings.PathEquals(path, directory));
        if (index < 0 || !Dialogs.ConfirmCleanup(this, "从项目列表移除",
            $"移除项目 {AgentText.Visible(directory)}？\n\n磁盘文件和会话历史会保留。以后可以重新添加。",
            "移除项目 (&D)"))
            return;

        var original = settings.Projects.ToList();
        settings.RemoveProject(directory);
        try { storage.Save(settings); }
        catch
        {
            settings.Projects = original;
            throw;
        }

        if (project is not null && LocalSettings.PathEquals(project, directory))
        {
            await ResetAgentAsync();
            project = null;
            showOfficialHistory = false;
            officialSessions = [];
            activeTitles.Clear();
            ClearConversation();
            activity.Clear();
            context.Text = "当前项目：未选择";
            PopulateSessions();
        }
        ReloadProjects();
        if (projects.Items.Count == 0)
            addProject.Focus();
        else
        {
            projects.SelectedIndex = Math.Min(index, projects.Items.Count - 1);
            projects.Focus();
        }
        SetStatus("已从项目列表移除。重新添加目录可以查看原有会话。", speak: true);
    }

    private Task RemoveSelectedHistoryAsync() => RunAsync(() => sessions.SelectedItem is LocalSession session
        ? RemoveHistoryAsync(session) : Task.CompletedTask);

    private async Task RemoveHistoryAsync(LocalSession selected)
    {
        if (project is not { } directory || !LocalSettings.PathEquals(selected.Directory, directory) ||
            settings.IsSessionRemoved(selected.Id, directory))
            return;
        if (!Dialogs.ConfirmCleanup(this, "删除本机会话记录",
            $"从 MyAntigravity 的本机历史删除以下会话？\n\n{selected}\n\n这条会话的未发送草稿也会清除。官方 Antigravity 仍保留原会话。",
            "删除本机记录 (&D)"))
            return;

        int index = sessions.SelectedIndex;
        var history = settings.Sessions.ToList();
        var removed = settings.RemovedSessions.ToList();
        var workspaces = settings.SessionWorkspaces.ToList();
        settings.RemoveSession(selected.Id, directory);
        try { storage.Save(settings); }
        catch
        {
            settings.Sessions = history;
            settings.RemovedSessions = removed;
            settings.SessionWorkspaces = workspaces;
            throw;
        }
        if (sessionId == selected.Id)
        {
            await ResetAgentAsync();
            ClearConversation();
        }
        savedDrafts.Remove((directory, selected.Id));
        activeTitles.Remove(selected.Id);
        PopulateSessions();
        if (sessions.Items.Count > 0 && index >= 0)
            sessions.SelectedIndex = Math.Min(index, sessions.Items.Count - 1);
        sessions.Focus();
        SetStatus("已从本机历史删除这条会话。", speak: true);
    }

    private Task RenameSessionAsync() => RunAsync(() =>
    {
        if (project is null || sessions.SelectedItem is not LocalSession selected ||
            settings.FindSession(selected.Id, project) is not { } local ||
            Dialogs.SessionName(this, local.Title) is not { } alias)
            return Task.CompletedTask;

        settings.RenameSession(local.Id, local.Directory, alias);
        try
        {
            storage.Save(settings);
        }
        catch
        {
            settings.RenameSession(local.Id, local.Directory, local.Title);
            throw;
        }

        PopulateSessions(local.Id);
        sessions.Focus();
        SetStatus("已保存本地会话名称。");
        return Task.CompletedTask;
    });

    private Task InsertOfficialCommandAsync() => RunAsync(() =>
    {
        if (sessionId is not { } active || agent is not { Connected: true })
            return Task.CompletedTask;
        if (availableCommands.Count == 0)
        {
            Dialogs.Text(this, "官方命令", "当前会话没有收到可用命令列表。");
            return Task.CompletedTask;
        }

        var selected = Dialogs.Search(this, "官方命令", "搜索命令或说明", "匹配命令", "填入消息框 (&I)",
            availableCommands, command => $"{command} {CommandDetails(command)}", CommandDetails);
        if (selected is null)
            return Task.CompletedTask;
        if (sessionId != active || !availableCommands.Any(command => command.Name == selected.Name))
        {
            SetStatus("官方命令列表已变化。请重新选择命令。");
            return Task.CompletedTask;
        }

        var draft = selected.InsertBefore(input.Text);
        input.Text = draft.Text;
        input.Select(draft.Caret, 0);
        input.Focus();
        SetStatus($"已填入 {selected}。核对内容后按 Ctrl+Enter 发送。");
        return Task.CompletedTask;
    });

    private static string CommandDetails(AcpCommand command)
    {
        var summary = command.Name switch
        {
            "plan" => "先规划，再执行任务。",
            "diff" => "查看本轮文件改动。",
            "compact" => "压缩会话上下文。",
            "context" => "查看会话上下文。",
            "permissions" => "查看官方 CLI 的权限规则。",
            "effort" => "查看或调整模型的推理等级。",
            _ => ""
        };
        return string.Join("\n\n", new[] { summary, command.Description,
            command.InputHint is { Length: > 0 } hint ? $"参数提示：{hint}" : "" }.Where(text => text.Length > 0));
    }
}
