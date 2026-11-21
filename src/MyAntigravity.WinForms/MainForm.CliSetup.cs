namespace MyAntigravity;

internal sealed partial class MainForm
{
    private sealed class CliSetupException(string message) : IOException(message);

    private string RequireCli()
    {
        try { return Cli.Find(settings.Executable) ?? throw new CliSetupException(Cli.NotInstalled); }
        catch (FileNotFoundException error) { throw new CliSetupException(error.Message); }
    }

    private void ShowCliGuide(string reason, Form? owner = null)
    {
        using var window = Dialogs.Window("安装或选择官方 Antigravity CLI", new Size(760, 445));
        window.ShowInTaskbar = true;
        var details = ReadableText("CLI 安装与检测说明");
        details.Text = $"""
            {reason}

            安装 Windows CLI（agy.exe）可按这三步操作：
            1. 按 Alt+P 复制下面的官方安装命令。按 Win+R，输入 powershell，按回车打开 PowerShell。
            2. 在 PowerShell 按 Ctrl+V 粘贴命令，按回车执行并等待安装完成。
            3. 回到 MyAntigravity，在“账户 → 检测 CLI 版本”重新检测。

            {Cli.InstallCommand}

            默认安装位置会自动识别。自定义位置可在“设置 → 常规设置”选择 agy.exe。
            """;
        var copy = new Button { Text = "复制官方 PowerShell 安装命令 (&P)", AutoSize = true };
        var install = new Button { Text = "打开 Google 官方 CLI 安装指南 (&I)", AutoSize = true };
        var close = new Button { Text = "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        var result = Caption("");
        result.LiveSetting = System.Windows.Forms.Automation.AutomationLiveSetting.Polite;
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
        Dialogs.Vertical(window, content, Buttons(1, copy, install, close));
        window.ActiveControl = details;
        window.ShowDialog(owner ?? this);
    }
}
