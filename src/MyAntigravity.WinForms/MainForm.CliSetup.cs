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
        using var window = Dialogs.Window("安装或选择官方 Antigravity CLI", new Size(700, 335));
        window.ShowInTaskbar = true;
        var details = ReadableText("CLI 安装与检测说明");
        details.Text = reason + "\n\n安装完后可在“账户 → 检测 CLI 版本”重新检测。默认路径会自动识别。" +
            "\n如使用自定义安装目录，请到“设置 → 常规设置”选择 agy.exe。";
        var install = new Button { Text = "打开 Google 官方 CLI 安装指南 (&I)", AutoSize = true };
        var close = new Button { Text = "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        install.Click += (_, _) => OpenLink(Cli.SetupGuide);
        window.CancelButton = close;
        Dialogs.Vertical(window, details, Buttons(1, install, close));
        window.ActiveControl = details;
        window.ShowDialog(owner ?? this);
    }
}
