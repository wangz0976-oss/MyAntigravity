using System.Diagnostics;

namespace MyAntigravity;

internal sealed partial class MainForm
{
    private static readonly HttpClient updateHttp = new() { Timeout = TimeSpan.FromMinutes(3) };

    private async Task EditSettingsAsync()
    {
        using var window = Dialogs.Window("MyAntigravity 设置", new Size(640, 455));
        window.ShowInTaskbar = true;
        var path = new TextBox { Text = settings.Executable ?? "", AccessibleName = "官方 CLI 完整路径", Dock = DockStyle.Top, TabIndex = 0 };
        var browse = new Button { Text = "选择 agy.exe (&B)", AutoSize = true, TabIndex = 1 };
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
            DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "发送消息按键", Width = 275, TabIndex = 5
        };
        sendKey.Items.AddRange(["Ctrl+Enter 发送，Enter 换行", "Enter 发送，Ctrl+Enter 换行"]);
        sendKey.SelectedIndex = settings.EnterToSend ? 1 : 0;
        var autoRead = new CheckBox
        {
            Text = "自动朗读已完成的新回复 (&R)", AutoSize = true, Checked = settings.AutoReadReply,
            AccessibleDescription = "通过争渡接口朗读。历史回放和没有完成的回复不会触发。", TabIndex = 6
        };

        browse.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Title = "选择官方 Antigravity CLI", Filter = "agy.exe|agy.exe|可执行文件|*.exe" };
            if (picker.ShowDialog(window) == DialogResult.OK)
                path.Text = picker.FileName;
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 11, AutoScroll = true };
        for (int i = 0; i < 10; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(Caption("CLI 完整路径（留空时自动检测）"), 0, 0);
        layout.Controls.Add(path, 0, 1);
        layout.Controls.Add(browse, 0, 2);
        layout.Controls.Add(announce, 0, 3);
        layout.Controls.Add(sandbox, 0, 4);
        layout.Controls.Add(Caption("更新渠道"), 0, 5);
        layout.Controls.Add(channel, 0, 6);
        layout.Controls.Add(Caption("发送消息按键"), 0, 7);
        layout.Controls.Add(sendKey, 0, 8);
        layout.Controls.Add(autoRead, 0, 9);
        var save = new Button { Text = "保存 (&S)", AutoSize = true };
        var back = new Button { Text = "取消 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path.Text))
                    Cli.Find(path.Text);
                window.DialogResult = DialogResult.OK;
            }
            catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
            {
                Dialogs.Text(window, "CLI 路径无效", error.Message);
                path.Focus();
            }
        };
        window.CancelButton = back;
        window.AcceptButton = save;
        Dialogs.Vertical(window, layout, Buttons(1, save, back));
        window.ActiveControl = path;
        if (window.ShowDialog(this) != DialogResult.OK)
            return;

        var configured = string.IsNullOrWhiteSpace(path.Text) ? null : Path.GetFullPath(path.Text.Trim());
        var restart = !string.Equals(configured, settings.Executable, StringComparison.OrdinalIgnoreCase)
            || sandbox.Checked != settings.Sandbox;
        settings.Executable = configured;
        settings.Announce = announce.Checked;
        settings.EnterToSend = sendKey.SelectedIndex == 1;
        settings.AutoReadReply = autoRead.Checked;
        settings.Sandbox = sandbox.Checked;
        settings.UpdateChannel = channel.SelectedIndex == 0 ? "stable" : "beta";
        storage.Save(settings);
        UpdateLiveSetting();
        if (restart)
            await ResetAgentAsync();
        SetStatus(restart ? "设置已保存。请重新打开会话以应用 CLI 选项。" : "设置已保存。");
    }

    private Task ShowUpdatesAsync()
    {
        using var window = Dialogs.Window("MyAntigravity 更新", new Size(680, 435));
        using var cancellation = new CancellationTokenSource();
        var updater = new Updates(updateHttp, dataDirectory);
        var details = ReadableText("更新版本与说明");
        details.Text = $"当前版本：{Program.Version}\n渠道：{settings.UpdateChannel}\n来源：GitHub Releases";
        var progress = new ProgressBar { Dock = DockStyle.Fill, AccessibleName = "下载进度", Visible = false };
        var progressLabel = Caption("");
        progressLabel.AccessibleName = "下载状态";
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(details, 0, 0);
        layout.Controls.Add(progressLabel, 0, 1);
        layout.Controls.Add(progress, 0, 2);

        UpdateManifest? manifest = null;
        var check = new Button { Text = "检查更新 (&K)", AutoSize = true };
        var download = new Button { Text = "下载并打开安装包 (&D)", AutoSize = true, Enabled = false };
        var close = new Button { Text = "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        window.FormClosing += (_, _) => cancellation.Cancel();
        window.CancelButton = close;
        Dialogs.Vertical(window, layout, Buttons(1, check, download, close));
        window.ActiveControl = check;

        async Task HandleAsync(Func<Task> operation)
        {
            check.Enabled = download.Enabled = false;
            try { await operation(); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or HttpRequestException or InvalidOperationException
                or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException or
                System.ComponentModel.Win32Exception or OperationCanceledException)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    details.Text = $"更新操作失败。\n{error.Message}";
                    details.Focus();
                }
            }
            finally
            {
                if (!cancellation.IsCancellationRequested)
                {
                    check.Enabled = true;
                    download.Enabled = manifest is not null;
                }
            }
        }

        check.Click += async (_, _) => await HandleAsync(async () =>
        {
            manifest = null;
            details.Text = "正在检查 GitHub Releases。";
            var found = await updater.CheckAsync(settings.UpdateChannel, Program.Version, cancellation.Token);
            if (cancellation.IsCancellationRequested)
                return;
            manifest = found;
            details.Text = manifest is null ? $"当前版本 {Program.Version} 已是此渠道的最新版本。"
                : $"发现 {manifest.Version}（{manifest.Channel}）\n\n{AgentText.Visible(manifest.Notes)}";
            details.Select(0, 0);
            details.Focus();
        });

        download.Click += async (_, _) => await HandleAsync(async () =>
        {
            if (manifest is null)
                return;
            progress.Visible = true;
            progress.Value = 0;
            progressLabel.Text = "正在下载安装包。";
            var report = new Progress<int?>(percent =>
            {
                if (cancellation.IsCancellationRequested)
                    return;
                progress.Style = percent.HasValue ? ProgressBarStyle.Blocks : ProgressBarStyle.Marquee;
                if (percent.HasValue)
                    progress.Value = percent.Value;
                progressLabel.Text = percent.HasValue ? $"下载进度：{percent}%" : "正在下载安装包。";
            });
            var installer = await updater.DownloadAsync(manifest, report, cancellation.Token);
            if (cancellation.IsCancellationRequested)
                return;

            progressLabel.Text = "SHA-256 已校验。";
            var start = new ProcessStartInfo(installer) { UseShellExecute = true };
            var installLocation = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            start.ArgumentList.Add($"/DIR={installLocation}");
            using var process = Process.Start(start) ?? throw new IOException("无法打开安装包。");
            window.Close();
        });

        window.ShowDialog(this);
        return Task.CompletedTask;
    }
}
