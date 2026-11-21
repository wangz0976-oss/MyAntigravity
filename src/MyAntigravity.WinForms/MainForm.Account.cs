using System.Diagnostics;

namespace MyAntigravity;

internal sealed partial class MainForm
{
    private string? connectedExecutable;
    private bool connectionReady;

    private void RecordAccountVerification(string? executable, bool requiresLogin, string? tier = null)
    {
        if (executable is not null)
            RecordAccountVerification(new AccountVerification(executable, DateTimeOffset.Now, requiresLogin, tier));
    }

    private void RecordAccountVerification(AccountVerification verification)
    {
        if (closing)
            return;
        settings.LastAccountVerification = verification;
        try { storage.Save(settings); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log("账户核验结果暂时无法保存，下次启动后可以重新核验。");
        }
    }

    private async Task<AccountVerification> VerifyAccountAsync(string executable)
    {
        Directory.CreateDirectory(dataDirectory);
        await using var probe = new AcpAgent(AcpStartInfo(executable, dataDirectory),
            (_, _) => Task.FromResult<string?>(null));
        return await AccountVerification.CheckAsync(probe, executable, Program.Version);
    }

    private ProcessStartInfo AcpStartInfo(string executable, string directory) => settings.Sandbox
        ? Cli.StartInfo(executable, directory, true, "acp", "--disable-telemetry", "--log-file", "NUL", "--sandbox")
        : Cli.StartInfo(executable, directory, true, "acp", "--disable-telemetry", "--log-file", "NUL");

    private Task ShowAccountAsync()
    {
        using var window = Dialogs.Window("账户与连接", new Size(790, 415));
        window.ShowInTaskbar = true;
        var details = ReadableText("账户与连接详情");
        details.AccessibleDescription = "Alt 加 R 重新检测并核验，Alt 加 I 打开官方安装指南，Alt 加 U 刷新额度，Esc 关闭。";
        var verify = new Button { Text = "重新核验 (&R)", AutoSize = true };
        var quota = new Button { Text = "刷新模型额度 (&U)", AutoSize = true };
        var quotaTerminal = new Button { Text = "在官方终端查询 (&T)", AutoSize = true };
        var quotaHelp = new Button { Text = "官方额度说明 (&H)", AutoSize = true };
        var install = new Button { Text = "官方 CLI 安装指南 (&I)", AutoSize = true };
        var copyDiagnostic = new Button { Text = "复制连接诊断 (&D)", AutoSize = true,
            AccessibleDescription = "复制版本、CLI 文件名和连接状态。省略完整路径、账号和消息内容。" };
        var close = new Button { Text = "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        using var watcher = new System.Windows.Forms.Timer { Interval = 400 };
        using var dialogLifetime = new CancellationTokenSource();
        string? executable = null;
        string? cliVersion = null;
        string? checkStatus = null;
        string? quotaStatus = null;
        string? quotaReport = null;
        bool checking = false;
        bool queryingQuota = false;
        bool closed = false;
        int quotaSectionStart = 0;

        void Render(bool focus = false)
        {
            var saved = settings.LastAccountVerification;
            var last = saved is not null && executable is not null && LocalSettings.PathEquals(saved.Executable, executable)
                ? saved : null;
            string login = last is not null
                ? $"{(last.RequiresLogin ? "需要登录" : "上次核验通过")}（{last.CheckedAt.ToLocalTime():g}）"
                : "尚未核验。按 Alt+R 检查官方登录。";
            string connection = agent is { Connected: true }
                ? !connectionReady ? "正在建立 ACP 连接"
                    : sessionId is null ? "ACP 已连接，尚未打开会话"
                    : prompting ? "ACP 已连接，当前任务进行中" : "ACP 已连接，当前会话可用"
                : "ACP 未连接";
            string document = $"官方登录：{login}\n当前连接：{connection}\n{context.Text}\n"
                + $"官方 CLI：{(executable is null ? "未找到" : AgentText.Visible(cliVersion ?? "已找到"))}\n"
                + (executable is null ? "" : $"CLI 路径：{executable}\n")
                + (last?.Tier is { } tier ? $"账户套餐：{tier}\n" : "")
                + (checkStatus is null ? "" : $"核验进度：{checkStatus}\n");
            quotaSectionStart = document.Length;
            string quotaHint = executable is null ? "按 Alt+I 打开安装指南，安装后按 Alt+R 重新检测。"
                : Cli.SupportsQuotaQuery(cliVersion) ? "按 Alt+U 查询。"
                : "应用内查询需要官方 CLI 1.5.0 或更新版本。按 Alt+T 可打开官方终端。";
            document += $"模型额度：{quotaStatus ?? quotaReport ?? quotaHint}";
            if (quotaStatus is not null && quotaReport is not null)
                document += $"\n上次查询结果：{quotaReport}";
            if (details.Text != document)
            {
                var caret = focus ? 0 : details.SelectionStart;
                details.Text = document;
                details.Select(Math.Min(caret, details.TextLength), 0);
            }
            verify.Enabled = !checking && !queryingQuota;
            quotaTerminal.Enabled = verify.Enabled && executable is not null && cliVersion is not null;
            quota.Enabled = quotaTerminal.Enabled && Cli.SupportsQuotaQuery(cliVersion);
            if (focus)
            {
                details.Select(0, 0);
                details.Focus();
            }
        }

        async Task CheckAsync(bool verifyLogin)
        {
            if (checking)
                return;
            checking = true;
            checkStatus = verifyLogin ? "正在核验官方登录。不会发送消息。" : null;
            try
            {
                var detected = Cli.Find(settings.Executable);
                if (!string.Equals(executable, detected, StringComparison.OrdinalIgnoreCase))
                {
                    cliVersion = quotaStatus = quotaReport = null;
                    executable = detected;
                }
                Render();
                if (executable is null)
                {
                    checkStatus = Cli.NotInstalled;
                    return;
                }
                Directory.CreateDirectory(dataDirectory);
                cliVersion = await Cli.VersionAsync(executable, dataDirectory);
                if (verifyLogin && !closed)
                {
                    var verification = await VerifyAccountAsync(executable);
                    RecordAccountVerification(verification);
                    checkStatus = verification.RequiresLogin ? "需要登录。请从账户菜单完成 Google 登录。" : "官方 ACP 已接受凭据。";
                }
            }
            catch (FileNotFoundException error)
            {
                executable = cliVersion = quotaStatus = quotaReport = null;
                checkStatus = error.Message;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or TimeoutException
                or AcpException or ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                checkStatus = verifyLogin ? "本次无法核验。请检查网络或 CLI 后重试。" : "无法读取 CLI 版本，请检查设置。";
            }
            finally
            {
                checking = false;
                if (!closed)
                    Render(verifyLogin);
            }
        }

        verify.Click += async (_, _) => await CheckAsync(true);
        quota.Click += async (_, _) =>
        {
            if (executable is null || checking || queryingQuota || !Cli.SupportsQuotaQuery(cliVersion))
                return;

            var cancellation = dialogLifetime.Token;
            queryingQuota = true;
            quotaStatus = "正在查询官方模型额度。";
            details.Focus();
            Render();
            details.Select(quotaSectionStart, 0);
            try
            {
                var result = await Cli.ReadModelQuotasAsync(executable, dataDirectory, cancellation);
                quotaReport = result.Count == 0 ? "官方 CLI 暂未提供额度。"
                    : $"更新于 {DateTimeOffset.Now:g}\n{string.Join("\n", result)}";
                quotaStatus = null;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or TimeoutException
                or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                quotaStatus = "无法读取额度。请检查登录和网络，或按 Alt+T 在官方终端查询。";
            }
            finally
            {
                queryingQuota = false;
                if (!closed)
                {
                    Render();
                    if (details.Focused)
                    {
                        details.Select(quotaSectionStart, 0);
                        details.ScrollToCaret();
                    }
                    if (settings.Announce)
                        ScreenReaderSpeech.TrySpeak(quotaStatus ?? quotaReport ?? "", interrupt: false);
                }
            }
        };
        quotaTerminal.Click += (_, _) =>
        {
            try
            {
                if (executable is null)
                    return;
                using var process = Process.Start(Cli.StartInfo(executable, WorkingDirectory(), false,
                    "--log-file", "NUL"))
                    ?? throw new IOException("未能打开官方 CLI。");
                try
                {
                    Clipboard.SetText("/usage");
                    checkStatus = "已打开官方 CLI，并复制 /usage。请在官方终端按 Ctrl+V，再按回车。";
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    checkStatus = "已打开官方 CLI，剪贴板暂不可用。可在官方终端输入 /usage。";
                }
                Render();
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                checkStatus = "无法打开官方额度。可在官方 CLI 输入 /usage。";
                Render(focus: true);
            }
        };
        quotaHelp.Click += (_, _) => OpenLink(new Uri("https://antigravity.google/docs/cli/commands/usage"));
        install.Click += async (_, _) =>
        {
            if (ShowCliGuide(executable is null ? checkStatus ?? Cli.NotInstalled :
                "可通过 Google 官方 PowerShell 命令安装 Windows CLI。", window))
                await CheckAsync(false);
        };
        copyDiagnostic.Click += (_, _) =>
        {
            string loginStatus = executable is not null && settings.LastAccountVerification is { } verification &&
                LocalSettings.PathEquals(verification.Executable, executable)
                ? verification.RequiresLogin ? "需要登录" : "已核验"
                : "尚未核验";
            string versionText = Version.TryParse(cliVersion?.Split('-', '+')[0], out var version)
                ? version.ToString() : "未检测到版本号";
            string diagnostic = $"MyAntigravity {Program.Version}\n"
                + $"CLI：{(executable is null ? "未找到" : Path.GetFileName(executable))}\n"
                + $"CLI 版本：{versionText}\n登录：{loginStatus}\n"
                + $"ACP：{(agent is { Connected: true } && connectionReady ? "已连接" : "未连接")}\n"
                + $"项目：{(project is null ? "未选择" : "已选择")}";
            try
            {
                Clipboard.SetText(diagnostic);
                checkStatus = "连接诊断已复制。";
            }
            catch (System.Runtime.InteropServices.ExternalException) { checkStatus = "剪贴板暂不可用。"; }
            Render(focus: true);
        };
        watcher.Tick += (_, _) => Render();
        window.FormClosing += (_, _) => { closed = true; watcher.Stop(); dialogLifetime.Cancel(); };
        window.Shown += async (_, _) =>
        {
            watcher.Start();
            await CheckAsync(false);
        };
        window.CancelButton = close;
        Dialogs.Vertical(window, details, Buttons(1, verify, quota, quotaTerminal, install, quotaHelp, copyDiagnostic, close));
        Render();
        window.ActiveControl = details;
        window.ShowDialog(this);
        return Task.CompletedTask;
    }
}
