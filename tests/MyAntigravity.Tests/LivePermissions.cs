using System.Diagnostics;
using System.Text.Json;
using MyAntigravity;

internal static class LivePermissions
{
    internal static async Task<int> RunAsync(string executable, string scratch)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("此项使用 Windows 命令测试官方 CLI。");

        string root = Directory.CreateDirectory(Path.Combine(scratch, $"live-permissions-{Guid.NewGuid():N}")).FullName;
        string version = await Cli.VersionAsync(executable, root);
        Console.WriteLine($"官方 Windows CLI {version} ACP 权限测试");
        bool knownAutoRuleDefect = Version.TryParse(version, out var installed) &&
            installed >= new Version(1, 5, 13) && installed <= new Version(1, 5, 18);
        var results = await Task.WhenAll(
            VerifyAsync(executable, root, "auto", "deny", cancel: false, expectedApproval: false, knownAutoRuleDefect),
            VerifyAsync(executable, root, "auto", "ask", cancel: false, expectedApproval: true, knownAutoRuleDefect),
            VerifyAsync(executable, root, "auto", "allow", cancel: true, expectedApproval: false, knownAutoRuleDefect: false));
        return results.All(passed => passed) ? 0 : 1;
    }

    private static async Task<bool> VerifyAsync(string executable, string root, string mode, string rule,
        bool cancel, bool expectedApproval, bool knownAutoRuleDefect)
    {
        var workspace = Directory.CreateDirectory(Path.Combine(root, rule, "workspace")).FullName;
        string marker = Path.Combine(workspace, "marker.txt");
        var home = Directory.CreateDirectory(Path.Combine(root, rule, "profile")).FullName;
        var settingsPath = Directory.CreateDirectory(Path.Combine(home, ".gemini", "antigravity-cli")).FullName;
        var settings = JsonSerializer.Serialize(new
        {
            permissions = new Dictionary<string, string[]> { [rule] = ["command(*)"] }
        });
        await File.WriteAllTextAsync(Path.Combine(settingsPath, "settings.json"), settings);
        var toolRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        try
        {
            if (rule == "deny")
                await CompareTerminalAsync(executable, workspace, home, mode);
            var process = Cli.StartInfo(executable, workspace, true, "acp", "--sandbox", "--disable-telemetry", "--log-file", "NUL");
            UseTestHome(process, home);
            await using var agent = new AcpAgent(process,
                (request, _) =>
                {
                    Interlocked.Increment(ref requests);
                    return Task.FromResult(request.Reject());
                });
            var toolCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            agent.SessionUpdated += (_, update) =>
            {
                if (!update.TryGetProperty("status", out var status))
                    return;
                if (status.GetString() == "in_progress")
                    toolRunning.TrySetResult();
                if (status.GetString() == "completed")
                    toolCompleted.TrySetResult();
            };
            await agent.InitializeAsync("0.1.0");
            var session = await agent.NewSessionAsync();
            string id = session.GetProperty("sessionId").GetString()!;
            var appliedMode = await agent.ApplySettingsAsync(id, SessionOption.Parse(session),
                [new(null, mode, "权限模式", "mode")]);
            if (appliedMode.Problems.Count != 0 || SessionDefaults.ConfirmedValue(appliedMode.Options, "mode") != mode)
                throw new InvalidOperationException("CLI 未确认 Auto 模式。");

            string command = cancel
                ? """powershell.exe -NoProfile -Command "Start-Sleep -Seconds 10; Set-Content -LiteralPath marker.txt -Value permission-probe" """
                : rule == "deny" ? "git --version" : """cmd.exe /d /c "echo permission-probe>marker.txt" """;
            var response = agent.PromptAsync(id,
                $"This is a permission test in an empty workspace. Call the shell tool once for exactly: {command.Trim()}. " +
                "Do not use another tool or command. If permission is denied, stop.");
            if (cancel)
            {
                await Task.WhenAny(toolRunning.Task, response).WaitAsync(TimeSpan.FromSeconds(60));
                if (toolRunning.Task.IsCompleted && !response.IsCompleted)
                    await agent.CancelAsync(id);
            }

            var stop = (await response.WaitAsync(TimeSpan.FromSeconds(60))).GetProperty("stopReason").GetString();
            if (cancel)
                await Task.Delay(TimeSpan.FromSeconds(11));
            bool wrote = File.Exists(marker);
            bool attempted = toolRunning.Task.IsCompleted || requests > 0;
            bool passed = attempted && !wrote && (requests > 0) == expectedApproval &&
                (rule != "deny" || !toolCompleted.Task.IsCompleted) && (!cancel || stop == "cancelled");
            bool observedKnownDefect = !passed && knownAutoRuleDefect && attempted && requests == 0 &&
                (rule == "deny" ? toolCompleted.Task.IsCompleted : wrote) && stop == "end_turn";
            string outcome = passed ? "通过" : observedKnownDefect ? "已知上游 CLI 缺陷" : "失败";
            Console.WriteLine($"{outcome} {mode}/{rule}：工具尝试 {attempted}，审批 {requests} 次，" +
                $"工具完成 {toolCompleted.Task.IsCompleted}，文件写入 {wrote}，结束原因 {stop}。");
            return passed || observedKnownDefect;
        }
        catch (Exception error) when (error is IOException or AcpException or TimeoutException or InvalidOperationException)
        {
            Console.WriteLine($"无法完成 {mode}/{rule}：{error.Message}");
            return false;
        }
    }

    private static async Task CompareTerminalAsync(string executable, string workspace, string home, string mode)
    {
        var start = Cli.StartInfo(executable, workspace, true, "--mode", mode, "--print",
            "Call the shell tool once for git --version. Report the tool result and stop.",
            "--output-format", "json", "--sandbox", "--log-file", "NUL", "--print-timeout", "50s");
        UseTestHome(start, home);
        using var terminal = Process.Start(start) ?? throw new InvalidOperationException("无法启动官方终端对照测试。");
        try
        {
            var output = terminal.StandardOutput.ReadToEndAsync();
            var errors = terminal.StandardError.ReadToEndAsync();
            await terminal.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            string combined = await output + await errors;
            bool denied = combined.Contains("Matches user-configured deny rule", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"普通终端对照：退出代码 {terminal.ExitCode}，报告命中拒绝规则 {denied}。");
        }
        finally
        {
            if (!terminal.HasExited)
                terminal.Kill(entireProcessTree: true);
        }
    }

    private static void UseTestHome(ProcessStartInfo process, string home)
    {
        process.Environment["USERPROFILE"] = home;
        process.Environment["HOME"] = home;
    }
}
