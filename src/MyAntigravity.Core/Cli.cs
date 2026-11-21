using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MyAntigravity;

public static class Cli
{
    public static readonly Uri SetupGuide = new("https://antigravity.google/docs/getting-started?tab=cli");
    public const string NotInstalled = "未找到 Google Antigravity CLI（agy.exe）。请打开官方 CLI 安装指南，安装后重新检测。";

    public static string? Find(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            try { return ValidatePath(configured); }
            catch (FileNotFoundException)
            {
                string? installed = FindInstalled();
                if (installed is not null)
                    return installed;
                throw;
            }
        }
        return FindInstalled();
    }

    public static string ValidatePath(string configured)
    {
        string full = Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"'));
        if (string.Equals(Path.GetFileName(full), "Antigravity.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Antigravity.exe 是 Google 的桌面版。请安装官方 Antigravity CLI 并选择 agy.exe。");
        if (!Path.IsPathFullyQualified(full) || !string.Equals(Path.GetFileName(full), "agy.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("请填写官方 Antigravity CLI 的完整路径，并选择 agy.exe。");
        var path = Path.GetFullPath(full);
        if (!File.Exists(path))
            throw new FileNotFoundException("这个路径下找不到 agy.exe。请重新选择，或清空路径后自动检测。", path);
        return path;
    }

    private static string? FindInstalled()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var local = Path.Combine(dir.FullName, "local", "agy", "agy.exe");
            if (File.Exists(local))
                return local;
        }

        var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "agy", "bin", "agy.exe");
        if (File.Exists(standard))
            return standard;

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = directory.Trim().Trim('"');
            if (Path.IsPathFullyQualified(path) && File.Exists(Path.Combine(path, "agy.exe")))
                return Path.Combine(path, "agy.exe");
        }

        return null;
    }

    public static ProcessStartInfo StartInfo(string executable, string cwd, bool redirect, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = cwd,
            UseShellExecute = !redirect,
            CreateNoWindow = redirect,
            RedirectStandardInput = redirect,
            RedirectStandardError = redirect,
            RedirectStandardOutput = redirect
        };
        if (redirect)
        {
            info.StandardInputEncoding = new UTF8Encoding(false);
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
        }

        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        return info;
    }

    public static async Task<string> VersionAsync(string executable, string cwd)
    {
        var (output, error) = await ReadOutputAsync(executable, cwd, "CLI 版本检查", "--version").ConfigureAwait(false);
        return output.Length > 0 ? output : error is { Length: > 0 } ? error : "版本未报告";
    }

    public static bool SupportsQuotaQuery(string? version) =>
        version is not null && Version.TryParse(version.Split('+', 2)[0].Split('-', 2)[0], out var parsed)
            && parsed >= new Version(1, 5, 0);

    public static async Task<IReadOnlyList<ModelQuota>> ReadModelQuotasAsync(string executable, string cwd,
        CancellationToken cancellationToken)
    {
        var (output, _) = await ReadOutputAsync(executable, cwd, "官方额度查询", cancellationToken, TimeSpan.FromSeconds(15),
            "--print", "/usage", "--output-format", "json", "--print-timeout", "12s", "--log-file", "NUL")
            .ConfigureAwait(false);
        return ModelQuota.Parse(output);
    }

    public static async Task<IReadOnlyList<CliPlugin>> ListPluginsAsync(string executable, string cwd)
    {
        var (output, _) = await ReadOutputAsync(executable, cwd, "官方插件查询", "plugin", "list", "--json")
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(output);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new IOException("官方 CLI 未返回插件列表。");
        return document.RootElement.Items().Where(item => item.Text("name") is not null)
            .Select(item => new CliPlugin(item.Text("name")!, item.Text("version"), item.Flag("enabled"))
            {
                Skills = item.Field("skills").Items().Where(skill => skill.ValueKind == JsonValueKind.String)
                    .Select(skill => skill.GetString()!).ToArray()
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static async Task<IReadOnlyList<McpServer>> ListMcpAsync(string executable, string cwd)
    {
        var (output, _) = await ReadOutputAsync(executable, cwd, "官方 MCP 查询", "mcp", "list").ConfigureAwait(false);
        return McpServer.ParseCliList(output);
    }

    public static async Task SetMcpEnabledAsync(string executable, string cwd, string name, bool enabled)
    {
        if (!McpServer.IsConfigurableName(name))
            throw new ArgumentException("此 MCP 名称无法通过官方命令修改。", nameof(name));
        await ReadOutputAsync(executable, cwd, "更新 MCP 状态", "mcp", enabled ? "enable" : "disable", name)
            .ConfigureAwait(false);
    }

    private static Task<(string Output, string Error)> ReadOutputAsync(string executable, string cwd,
        string operation, params string[] arguments) =>
        ReadOutputAsync(executable, cwd, operation, CancellationToken.None, TimeSpan.FromSeconds(12), arguments);

    private static async Task<(string Output, string Error)> ReadOutputAsync(string executable, string cwd,
        string operation, CancellationToken cancellationToken, TimeSpan timeLimit, params string[] arguments)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(StartInfo(executable, cwd, true, arguments))
            ?? throw new IOException("无法启动 Antigravity CLI。");
        process.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeLimit);
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = (await stdout.ConfigureAwait(false)).Trim();
            var error = (await stderr.ConfigureAwait(false)).Trim();
            if (process.ExitCode != 0)
                throw new IOException($"{operation}失败（退出码 {process.ExitCode}）。");
            return (output, error);
        }
        catch (OperationCanceledException)
        {
            Stop(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"{operation}超时。");
        }
    }

    internal static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}

public sealed record CliPlugin(string Name, string? Version, bool Enabled)
{
    public IReadOnlyList<string> Skills { get; init; } = [];

    public override string ToString() =>
        $"{AgentText.Visible(Name)}，版本 {AgentText.Visible(Version ?? "未报告")}，{(Enabled ? "已启用" : "已停用")}";
}
