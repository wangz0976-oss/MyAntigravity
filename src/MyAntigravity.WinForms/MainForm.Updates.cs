using System.Diagnostics;
using System.Windows.Forms.Automation;

namespace MyAntigravity;

internal sealed partial class MainForm
{
    private static readonly HttpClient updateHttp = new() { Timeout = TimeSpan.FromMinutes(3) };
    private CancellationTokenSource? startupUpdate;

    private async Task CheckStartupUpdateAsync(Updates updater, string current)
    {
        if (!settings.CheckUpdatesOnStartup)
            return;

        using var pending = new CancellationTokenSource();
        startupUpdate = pending;
        var channel = settings.UpdateChannel;
        UpdateManifest? found;
        try
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(pending.Token);
            request.CancelAfter(TimeSpan.FromSeconds(15));
            found = await updater.CheckAsync(channel, current, request.Token);
            if (found is null)
                return;

            while (busy || ActiveForm != this || WindowState == FormWindowState.Minimized)
                await Task.Delay(500, pending.Token);
            if (!pending.IsCancellationRequested && !closing && settings.CheckUpdatesOnStartup &&
                settings.UpdateChannel == channel)
                ShowUpdateDialog(updater, found);
        }
        catch (Exception error) when (IsUpdateError(error))
        {
            // A background check must let the app open offline. Manual checks report the error.
        }
        finally
        {
            startupUpdate = null;
        }
    }

    private Task ShowUpdatesAsync()
    {
        startupUpdate?.Cancel();
        ShowUpdateDialog(new Updates(updateHttp, dataDirectory), null);
        return Task.CompletedTask;
    }

    private void ShowUpdateDialog(Updates updater, UpdateManifest? manifest)
    {
        var startup = manifest is not null;
        using var window = Dialogs.Window(startup ? "发现 MyAntigravity 新版本" : "MyAntigravity 更新", new Size(680, 435));
        using var cancellation = new CancellationTokenSource();
        var details = ReadableText("更新日志");
        var progress = new ProgressBar { Dock = DockStyle.Fill, AccessibleName = "下载进度", Visible = false };
        var progressLabel = Caption("");
        progressLabel.LiveSetting = settings.Announce ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(details, 0, 0);
        layout.Controls.Add(progressLabel, 0, 1);
        layout.Controls.Add(progress, 0, 2);

        var download = new Button { Text = "更新 (&D)", AutoSize = true, Enabled = startup };
        var check = new Button { Text = "检查更新 (&K)", AutoSize = true, Visible = !startup };
        var close = new Button
        {
            Text = startup ? "稍后 (&C)" : "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel
        };
        window.FormClosing += (_, _) => cancellation.Cancel();
        window.CancelButton = close;
        window.AcceptButton = startup ? download : check;
        Dialogs.Vertical(window, layout, Buttons(1, download, check, close));
        window.ActiveControl = startup ? details : check;
        window.Shown += (_, _) => window.ActiveControl?.Focus();
        DisplayNotes();

        void DisplayNotes()
        {
            var channel = settings.UpdateChannel == "stable" ? "稳定版" : "测试版";
            details.Text = $"当前版本：{Program.Version}\n更新渠道：{channel}";
            if (manifest is not null)
            {
                var notes = UpdateNotes.PlainText(manifest.Notes);
                details.AppendText($"\n新版本：{manifest.Version}\n\n{(notes.Length > 0 ? notes : "这次发布未附更新日志。")}");
            }
            details.Select(0, 0);
        }

        async Task HandleAsync(Func<Task> operation)
        {
            check.Enabled = download.Enabled = false;
            window.AcceptButton = null;
            try { await operation(); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error) when (IsUpdateError(error))
            {
                if (!cancellation.IsCancellationRequested)
                {
                    progress.Style = ProgressBarStyle.Blocks;
                    progress.Visible = false;
                    progressLabel.Text = $"更新操作失败：{AgentText.Visible(error.Message)}";
                    details.Text = progressLabel.Text + "\n\n" + details.Text;
                    details.Select(0, 0);
                    details.Focus();
                }
            }
            finally
            {
                if (!cancellation.IsCancellationRequested)
                {
                    check.Enabled = true;
                    download.Enabled = manifest is not null;
                    window.AcceptButton = download.Enabled ? download : check;
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
            if (manifest is null)
                details.Text = $"当前版本 {Program.Version} 已是此渠道的最新版本。";
            else
                DisplayNotes();
            details.Focus();
        });

        download.Click += async (_, _) => await HandleAsync(async () =>
        {
            if (manifest is null)
                return;
            progress.Visible = true;
            progress.Style = ProgressBarStyle.Marquee;
            progress.Value = 0;
            progressLabel.Text = "正在下载安装包。";
            close.Text = "取消下载 (&C)";
            close.Focus();
            var lastAnnounced = -10;
            var report = new Progress<int?>(percent =>
            {
                if (cancellation.IsCancellationRequested || percent is null)
                    return;
                progress.Style = ProgressBarStyle.Blocks;
                progress.Value = percent.Value;
                progressLabel.LiveSetting = settings.Announce && percent.Value >= lastAnnounced + 10
                    ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off;
                if (progressLabel.LiveSetting == AutomationLiveSetting.Polite)
                    lastAnnounced = percent.Value;
                progressLabel.Text = $"下载进度：{percent}%";
            });
            try
            {
                var installer = await updater.DownloadAsync(manifest, report, cancellation.Token);
                if (cancellation.IsCancellationRequested)
                    return;
                progressLabel.Text = "安装包已通过 SHA-256 校验。";
                var start = new ProcessStartInfo(installer) { UseShellExecute = true };
                start.ArgumentList.Add($"/DIR={Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)}");
                using var process = Process.Start(start) ?? throw new IOException("无法打开安装包。");
                window.Close();
            }
            finally
            {
                if (!cancellation.IsCancellationRequested)
                {
                    progressLabel.LiveSetting = settings.Announce ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off;
                    close.Text = "关闭 (&C)";
                }
            }
        });

        window.ShowDialog(this);
    }

    private static bool IsUpdateError(Exception error) =>
        error is IOException or HttpRequestException or InvalidOperationException or UnauthorizedAccessException
            or FormatException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception
            or System.Xml.XmlException or OperationCanceledException or ArgumentException;
}
