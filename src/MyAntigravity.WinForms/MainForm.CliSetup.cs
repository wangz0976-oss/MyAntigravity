namespace MyAntigravity;

internal sealed partial class MainForm
{
    private sealed class CliSetupException(string message) : IOException(message);

    private string RequireCli()
    {
        try { return Cli.Find(settings.Executable) ?? throw new CliSetupException(Cli.NotInstalled); }
        catch (FileNotFoundException error) { throw new CliSetupException(error.Message); }
    }

    private void OfferCliSetupAtStartup()
    {
        try { RequireCli(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowCliGuide(AgentText.Visible(error.Message), startup: true);
        }
    }

    private bool ShowCliGuide(string reason, Form? owner = null, bool startup = false)
    {
        using var window = Dialogs.Window("安装或选择官方 Antigravity CLI", new Size(830, 475));
        using var installation = new CancellationTokenSource();
        window.FormClosing += (_, _) => installation.Cancel();
        window.ShowInTaskbar = true;
        var details = ReadableText("CLI 安装与检测说明");
        string instructions = $"""
            {reason}

            按 Alt+A 选择“安装官方 CLI”。程序会运行 Google 的安装命令，完成后自动检查 agy.exe 的版本。

            也可以手动安装 Windows CLI（agy.exe）：
            1. 按 Alt+P 复制下面的官方安装命令。按 Win+R，输入 powershell，按回车打开 PowerShell。
            2. 在 PowerShell 按 Ctrl+V 粘贴命令，按回车执行并等待安装完成。
            3. 回到 MyAntigravity，在“账户 → 检测 CLI 版本”重新检测。

            {Cli.InstallCommand}

            默认安装位置会自动识别。自定义位置可在“设置 → 常规设置”选择 agy.exe。
            """;
        details.Text = instructions;
        var automatic = new Button { Text = "安装官方 CLI (&A)", AutoSize = true };
        var copy = new Button { Text = "复制官方 PowerShell 安装命令 (&P)", AutoSize = true };
        var install = new Button { Text = "打开 Google 官方 CLI 安装指南 (&I)", AutoSize = true };
        string closeLabel = startup ? "稍后 (&C)" : "关闭 (&C)";
        var close = new Button { Text = closeLabel, AutoSize = true, DialogResult = DialogResult.Cancel };
        var result = Caption("");
        result.AccessibleName = "CLI 安装状态";
        result.LiveSetting = System.Windows.Forms.Automation.AutomationLiveSetting.Polite;
        bool ready = false;
        automatic.Click += async (_, _) =>
        {
            automatic.Enabled = false;
            details.Text = instructions;
            close.Text = "取消安装 (&C)";
            close.Focus();
            result.Text = "正在联网运行 Google 官方安装命令。安装完成后会检测版本。";
            var cancellation = installation.Token;
            try
            {
                Directory.CreateDirectory(dataDirectory);
                await Cli.InstallWindowsAsync(dataDirectory, cancellation);
                cancellation.ThrowIfCancellationRequested();
                var executable = RequireCli();
                var version = await Cli.VersionAsync(executable, dataDirectory);
                if (version == "版本未报告")
                    throw new IOException("已找到 agy.exe，暂未读到版本。可从账户菜单再次检测。");
                if (!cancellation.IsCancellationRequested)
                {
                    ready = true;
                    result.Text = $"官方 CLI 安装完成。已检测到版本 {AgentText.Visible(version)}。";
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or TimeoutException or
                System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    result.Text = "安装或检测未完成。";
                    details.Text = $"{AgentText.Visible(error.Message)}\n可按 Alt+P 复制命令手动安装。\n\n{instructions}";
                    details.Select(0, 0);
                    details.Focus();
                }
            }
            finally
            {
                if (!cancellation.IsCancellationRequested)
                {
                    automatic.Enabled = !ready;
                    close.Text = ready ? "完成 (&C)" : closeLabel;
                }
            }
        };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(Cli.InstallCommand);
                result.Text = "安装命令已复制。按 Win+R 打开运行。";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                result.Text = "剪贴板暂不可用。可在说明里选中命令后按 Ctrl+C 复制。";
            }
        };
        install.Click += (_, _) => OpenLink(Cli.SetupGuide);
        window.CancelButton = close;
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(details, 0, 0);
        content.Controls.Add(result, 0, 1);
        Dialogs.Vertical(window, content, Buttons(1, automatic, copy, install, close));
        window.ActiveControl = details;
        window.ShowDialog(owner ?? this);
        return ready;
    }
}
