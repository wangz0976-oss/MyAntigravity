namespace MyAntigravity;

internal sealed partial class MainForm
{
    private readonly TurnResults turnResults = new();

    private Task ShowTurnResultsAsync()
    {
        string? activeSession = sessionId;
        string? selectedId = null;
        var failures = new Dialogs.BrowseFilter<TurnToolResult>("只看失败工具 (&F)", result => result.IsFailure);
        while (!closing && sessionId == activeSession)
        {
            var entries = turnResults.Snapshot();
            if (entries.Count == 0)
            {
                Dialogs.Text(this, "本轮结果", "当前会话的本轮任务还没有收到工具结果。", showInTaskbar: true);
                break;
            }
            var selected = Dialogs.Browse(this, "本轮结果", "按工具名称、状态或文件路径筛选", "本轮工具结果",
                "查看结果详情 (&V)", entries, entry => $"{entry} {string.Join(" ", entry.Locations)} " +
                    string.Join(" ", entry.Changes.Select(change => change.Path)), entry => entry.ToString(),
                initial: entries.FirstOrDefault(entry => entry.Id == selectedId), filter: failures).Item;
            if (selected is null)
                break;
            selectedId = selected.Id;
            Dialogs.ReadMessage(this, $"工具结果 - {AgentText.Visible(selected.Title)}", selected.Details(),
                readLatest: null, toolResult: selected, workspaces: ExtensionWorkspaces());
        }
        return Task.CompletedTask;
    }

    private Task ShowProjectChangesAsync() => RunAsync(async () =>
    {
        if (project is null)
            return;
        var review = await GitReview.OpenAsync(project);
        if (review is null)
        {
            Dialogs.Text(this, "Git 项目改动", "当前项目不在 Git 工作区中。", showInTaskbar: true);
            return;
        }
        var files = await review.ListAsync();
        if (files.Count == 0)
        {
            Dialogs.Text(this, "Git 项目改动", "当前 Git 工作区没有可审查的改动。", showInTaskbar: true);
            return;
        }

        GitChange? selected = null;
        while (!closing)
        {
            (selected, _) = Dialogs.Browse(this, "Git 项目改动", "按文件路径或状态筛选",
                "Git 修改文件列表", "审查所选改动 (&R)", files, entry => $"{entry.Path} {entry.ScopeLabel}", initial: selected);
            if (selected is null)
                return;
            try
            {
                string patch = await review.ReadPatchAsync(selected);
                Dialogs.ReviewGitPatch(this, selected, patch);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or TimeoutException)
            {
                Dialogs.Text(this, "无法读取 Git 文件差异", AgentText.Visible(error.Message), showInTaskbar: true);
            }
        }
    });

    private Task ShowPlanAsync()
    {
        using var window = Dialogs.Window("当前会话任务计划", new Size(760, 475));
        window.ShowInTaskbar = true;
        var details = ReadableText("任务计划步骤");
        var close = new Button { Text = "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };

        void Refresh()
        {
            var caret = details.SelectionStart;
            details.Text = plan.Count == 0 ? "当前会话还没有收到任务计划。"
                : string.Join("\n\n", plan.Select((step, index) => $"步骤 {index + 1}。{step}"));
            details.Select(Math.Min(caret, details.TextLength), 0);
        }

        Refresh();
        window.CancelButton = close;
        Dialogs.Vertical(window, details, Buttons(1, close));
        window.ActiveControl = details;
        PlanChanged += Refresh;
        try { window.ShowDialog(this); }
        finally { PlanChanged -= Refresh; }
        return Task.CompletedTask;
    }

    private Task ShowPluginsAsync()
    {
        using var window = Dialogs.Window("官方插件列表", new Size(735, 465));
        window.ShowInTaskbar = true;
        var details = ReadableText("已安装插件及状态");
        var refresh = new Button { Text = "刷新 (&R)", AutoSize = true };
        var close = new Button { Text = "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        bool closed = false;

        async Task LoadAsync()
        {
            refresh.Enabled = false;
            details.Text = "正在查询官方 CLI 的插件列表。";
            string result;
            try
            {
                var executable = Cli.Find(settings.Executable);
                if (executable is null)
                    result = "未找到官方 CLI。请从账户菜单查看 CLI 安装指南。";
                else
                {
                    var installed = await Cli.ListPluginsAsync(executable, WorkingDirectory());
                    result = installed.Count == 0 ? "官方 CLI 未列出已安装的插件。"
                        : $"共有 {installed.Count} 个插件。\n\n{string.Join("\n", installed)}";
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                or InvalidOperationException or TimeoutException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
            {
                result = "无法读取官方插件列表。请检查 CLI 版本是否支持 plugin list --json。";
            }
            if (closed)
                return;
            refresh.Enabled = true;
            details.Text = result;
            details.Select(0, 0);
            details.Focus();
        }

        refresh.Click += async (_, _) => await LoadAsync();
        window.Shown += async (_, _) => await LoadAsync();
        window.FormClosing += (_, _) => closed = true;
        window.CancelButton = close;
        Dialogs.Vertical(window, details, Buttons(1, refresh, close));
        window.ActiveControl = details;
        window.ShowDialog(this);
        return Task.CompletedTask;
    }
}
