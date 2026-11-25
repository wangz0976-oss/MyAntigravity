namespace MyAntigravity;

internal sealed partial class MainForm
{
    private async Task EditSettingsAsync()
    {
        using var window = Dialogs.Window("MyAntigravity 设置", new Size(640, 490));
        window.ShowInTaskbar = true;
        var path = new TextBox { Text = settings.Executable ?? "", AccessibleName = "官方 CLI 完整路径", Dock = DockStyle.Top, TabIndex = 0 };
        var browse = new Button { Text = "选择 agy.exe (&B)", AutoSize = true, TabIndex = 1 };
        var install = new Button { Text = "安装官方 Windows CLI (&I)", AutoSize = true };
        var announce = new CheckBox
        {
            Text = "读屏状态播报 (&N)", AutoSize = true, Checked = settings.Announce, TabIndex = 2
        };
        var sandbox = new CheckBox
        {
            Text = "启用官方 CLI 终端沙箱 (&X)", AutoSize = true, Checked = settings.Sandbox, TabIndex = 3
        };
        var channel = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "更新渠道", Width = 180, TabIndex = 4
        };
        channel.Items.AddRange(["稳定版 (Stable)", "测试版 (Beta)"]);
        channel.SelectedIndex = settings.UpdateChannel == "stable" ? 0 : 1;
        var sendKey = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "发送消息按键", Width = 275, TabIndex = 6
        };
        sendKey.Items.AddRange(["Ctrl+Enter 发送，Enter 换行", "Enter 发送，Ctrl+Enter 换行"]);
        sendKey.SelectedIndex = settings.EnterToSend ? 1 : 0;
        var autoRead = new CheckBox
        {
            Text = "自动朗读已完成的新回复 (&R)", AutoSize = true, Checked = settings.AutoReadReply,
            AccessibleDescription = "通过争渡接口朗读。历史回放和没有完成的回复不会触发。", TabIndex = 7
        };

        browse.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Title = "选择官方 Antigravity CLI 的 agy.exe", Filter = "官方 CLI|agy.exe|可执行文件|*.exe" };
            if (picker.ShowDialog(window) == DialogResult.OK)
            {
                try { path.Text = Cli.ValidatePath(picker.FileName); }
                catch (FileNotFoundException error) { ShowCliGuide(error.Message, window); }
            }
        };
        install.Click += (_, _) => ShowCliGuide("可通过 Google 官方 PowerShell 命令安装 Windows CLI。", window);

        var startupUpdates = new CheckBox
        {
            Text = "启动时检查更新 (&U)", AutoSize = true, Checked = settings.CheckUpdatesOnStartup, TabIndex = 5
        };
        var startupSession = new CheckBox { Text = "启动时恢复上次会话 (&H)", AutoSize = true,
            Checked = settings.RestoreSessionOnStartup, TabIndex = 1 };
        var tabs = new TabControl { Dock = DockStyle.Fill, AccessibleName = "设置分类", TabIndex = 0 };
        tabs.TabPages.AddRange([
            SettingsPage("常规", Caption("发送消息按键"), sendKey, startupSession),
            SettingsPage("CLI 连接", Caption("CLI 完整路径（留空时自动检测）"), path, Buttons(1, browse, install), sandbox),
            SettingsPage("读屏", announce, autoRead),
            SettingsPage("更新", Caption("更新渠道"), channel, startupUpdates)]);
        var save = new Button { Text = "保存 (&S)", AutoSize = true };
        var back = new Button { Text = "取消 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path.Text))
                    Cli.ValidatePath(path.Text);
                window.DialogResult = DialogResult.OK;
            }
            catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
            {
                ShowCliGuide(error.Message, window);
                path.Focus();
            }
        };
        window.CancelButton = back;
        window.AcceptButton = save;
        Dialogs.Vertical(window, tabs, Buttons(1, save, back));
        window.ActiveControl = tabs;
        if (window.ShowDialog(this) != DialogResult.OK)
            return;

        var configured = string.IsNullOrWhiteSpace(path.Text) ? null : Cli.ValidatePath(path.Text);
        var restart = !string.Equals(configured, settings.Executable, StringComparison.OrdinalIgnoreCase)
            || sandbox.Checked != settings.Sandbox;
        settings.Executable = configured;
        settings.Announce = announce.Checked;
        settings.EnterToSend = sendKey.SelectedIndex == 1;
        settings.AutoReadReply = autoRead.Checked;
        settings.Sandbox = sandbox.Checked;
        settings.UpdateChannel = channel.SelectedIndex == 0 ? "stable" : "beta";
        settings.CheckUpdatesOnStartup = startupUpdates.Checked;
        settings.RestoreSessionOnStartup = startupSession.Checked;
        storage.Save(settings);
        UpdateLiveSetting();
        if (restart)
            await ResetAgentAsync();
        SetStatus(restart ? "设置已保存。请重新打开会话以应用 CLI 选项。" : "设置已保存。");
    }

    private static TabPage SettingsPage(string title, params Control[] controls)
    {
        var page = new TabPage(title) { Padding = new Padding(10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = controls.Length + 1, AutoScroll = true };
        for (int i = 0; i < controls.Length; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            controls[i].TabIndex = i;
            layout.Controls.Add(controls[i], 0, i);
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);
        return page;
    }
}
