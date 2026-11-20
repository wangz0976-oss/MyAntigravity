namespace MyAntigravity;

internal sealed partial class MainForm
{
    private sealed record ExtensionEntry(string Label, string Description, Func<Task> Open)
    {
        public override string ToString() => Label;
    }

    private Task ShowExtensionHubAsync() => RunAsync(async () =>
    {
        ExtensionEntry[] entries =
        [
            new("技能", "浏览工作目录、本机和插件里的技能，查看说明并从当前会话使用可用命令。", ShowSkillsAsync),
            new("MCP 服务", "查看全局与工作目录配置，启用或停用官方 CLI 的全局服务。", ShowMcpAsync),
            new("已安装插件", "查看官方 CLI 返回的插件名称、版本和启用状态。", ShowPluginsAsync)
        ];
        var selected = Dialogs.Search(this, "技能、MCP 与插件", "按名称筛选", "扩展管理入口",
            "打开所选工具 (&O)", entries, entry => entry.Label, entry => entry.Description);
        if (selected is not null)
            await selected.Open();
    });

    private string[] ExtensionWorkspaces() => project is { } active
        ? [active, .. settings.DirectoriesForProject(active)] : [];

    private async Task ShowSkillsAsync()
    {
        IReadOnlyList<CliPlugin> plugins = [];
        string? note = null;
        try
        {
            if (Cli.Find(settings.Executable) is { } executable)
                plugins = await Cli.ListPluginsAsync(executable, WorkingDirectory());
            else
                note = "找不到官方 CLI。当前列表显示本机可读的技能文件。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or TimeoutException
            or System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {
            note = "暂时无法查询官方插件。当前列表显示本机可读的技能文件。";
        }
        if (closing)
            return;
        string[] workspaces = ExtensionWorkspaces();
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var skills = await Task.Run(() => SkillCatalog.Read(workspaces, profile, plugins));
        if (closing)
            return;
        if (skills.Count == 0)
        {
            Dialogs.Text(this, "技能", note ?? "当前工作目录、本机和官方插件没有发现技能。", showInTaskbar: true);
            return;
        }
        var selected = Dialogs.Search(this, "技能", "按名称、来源或说明筛选", "发现的技能",
            "查看或使用所选技能 (&V)", skills, skill => $"{skill.Name} {skill.Source} {skill.Description}", Describe);
        if (selected is null)
            return;
        var skillAgent = agent;
        var skillSession = sessionId;
        var available = selected.Enabled && agent is { Connected: true } && sessionId is not null
            ? availableCommands.FirstOrDefault(command => command.Name.Equals(selected.Name, StringComparison.OrdinalIgnoreCase))
            : null;
        string details = Describe(selected);
        if (selected.FilePath is { } path)
        {
            try { details += $"\n\n{await Task.Run(() => SkillCatalog.Preview(path))}"; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                details += "\n\n无法读取技能文件。";
            }
        }
        details += available is not null ? "\n\n按 Alt+I 把技能命令填入消息框。"
            : "\n\n技能命令由当前会话报告。打开支持此技能的会话后即可使用。";
        if (!closing && Dialogs.SkillDetails(this, selected.Name, details, available is not null) && available is not null &&
            ReferenceEquals(agent, skillAgent) && skillAgent is { Connected: true } && sessionId == skillSession &&
            availableCommands.Any(command => command.Name == available.Name))
        {
            var draft = available.InsertBefore(input.Text);
            input.Text = draft.Text;
            input.Select(draft.Caret, 0);
            input.Focus();
            SetStatus($"已填入 {available}。核对后按 Ctrl+Enter 发送。");
        }

        string Describe(AgentSkill skill) => string.Join("\n", new[]
        {
            skill.ToString(), AgentText.Visible(skill.Description), skill.FilePath is { } path ? AgentText.Visible(path) : "",
            note ?? ""
        }.Where(value => value.Length != 0));
    }

    private Task ShowMcpAsync()
    {
        using var window = Dialogs.Window("MCP 服务", new Size(820, 490));
        window.ShowInTaskbar = true;
        var list = new ListBox
        {
            AccessibleName = "MCP 服务列表", AccessibleDescription = "方向键选择，Tab 读取详细状态。Alt 加 R 刷新。",
            Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false
        };
        var detail = ReadableText("所选 MCP 服务配置");
        var statusText = Caption("正在读取 MCP 配置。");
        statusText.LiveSetting = System.Windows.Forms.Automation.AutomationLiveSetting.Polite;
        var refresh = new Button { Text = "刷新 (&R)", AutoSize = true };
        var toggle = new Button { Text = "启用或停用 (&T)", AutoSize = true, Enabled = false };
        var close = new Button { Text = "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        string? executable = null;
        string cwd = WorkingDirectory();
        string[] workspaces = ExtensionWorkspaces();
        bool loading = false;
        bool closed = false;

        void UpdateSelection()
        {
            var selected = list.SelectedItem as McpServer;
            toggle.Enabled = !loading && executable is not null && selected?.CanChange == true;
            toggle.Text = selected is { CanChange: true } ? selected.Enabled ? "停用所选 (&T)" : "启用所选 (&T)" : "启用或停用 (&T)";
            detail.Text = selected is null ? "选择服务后查看其配置。" :
                $"{selected}\n传输方式：{AgentText.Visible(selected.Transport)}\n"
                + (selected.IsGlobal ? "Alt+T 修改官方 CLI 全局开关。重新打开会话后应用。"
                    : "这一项来自工作目录的 .agents/mcp_config.json。");
            detail.Select(0, 0);
        }

        async Task UpdateAsync(McpServer? change = null)
        {
            if (loading)
                return;
            var previous = list.SelectedItem as McpServer;
            loading = true;
            refresh.Enabled = false;
            UpdateSelection();
            statusText.Text = change is null ? "正在读取 MCP 配置。" : $"正在更新 {AgentText.Visible(change.Name)}。";
            var items = new List<McpServer>();
            var notices = new List<string>();
            try
            {
                executable = Cli.Find(settings.Executable);
                if (executable is null)
                    notices.Add("找不到官方 CLI。");
                else
                {
                    var global = await Cli.ListMcpAsync(executable, cwd);
                    if (change is not null)
                    {
                        if (global.Any(server => server.Name == change.Name && server.Enabled == change.Enabled))
                        {
                            await Cli.SetMcpEnabledAsync(executable, cwd, change.Name, !change.Enabled);
                            global = await Cli.ListMcpAsync(executable, cwd);
                        }
                        else
                            notices.Add("全局配置已变化。请重新选择服务。");
                    }
                    items.AddRange(global);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or TimeoutException
                or System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
            {
                notices.Add(change is null ? "无法读取官方 CLI 全局列表。" : "无法确认全局开关更新，请刷新核对。");
            }
            foreach (var path in workspaces)
            {
                try { items.AddRange(await Task.Run(() => McpServer.ReadWorkspace(path))); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    notices.Add($"无法读取工作目录的 MCP 配置。{AgentText.Visible(path)}");
                }
            }
            if (closed)
                return;
            loading = false;
            refresh.Enabled = true;
            var results = items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Directory).ToArray();
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                list.Items.AddRange(results);
                if (list.Items.Count > 0)
                {
                    int index = Array.FindIndex(results, item => previous is not null && item.Name == previous.Name &&
                        item.Directory == previous.Directory);
                    list.SelectedIndex = Math.Max(0, index);
                }
            }
            finally { list.EndUpdate(); }
            var summary = results.Length == 0 ? "列表为空。" : $"共有 {results.Length} 项配置。";
            statusText.Text = string.Join(" ", new[] { summary }.Concat(notices));
            UpdateSelection();
            if (list.Items.Count > 0)
                list.Focus();
            else
                refresh.Focus();
        }

        list.SelectedIndexChanged += (_, _) => UpdateSelection();
        refresh.Click += async (_, _) => await UpdateAsync();
        toggle.Click += async (_, _) =>
        {
            if (list.SelectedItem is not McpServer { CanChange: true } selected)
                return;
            var action = selected.Enabled ? "停用" : "启用";
            if (MessageBox.Show(window, $"{action} MCP 服务 {AgentText.Visible(selected.Name)} 的全局开关？\n重新打开会话后应用。",
                "更改全局 MCP 配置", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                await UpdateAsync(selected);
        };
        window.FormClosing += (_, _) => closed = true;
        window.Shown += async (_, _) => await UpdateAsync();
        window.CancelButton = close;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        panel.Controls.Add(statusText, 0, 0);
        panel.Controls.Add(list, 0, 1);
        panel.Controls.Add(detail, 0, 2);
        Dialogs.Vertical(window, panel, Buttons(1, refresh, toggle, close));
        window.ActiveControl = list;
        window.ShowDialog(this);
        return Task.CompletedTask;
    }
}
