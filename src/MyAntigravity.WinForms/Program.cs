using System.Reflection;

namespace MyAntigravity;

internal static class Program
{
    internal static readonly string Version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    internal static string DataDirectory
    {
        get
        {
            for (var path = new DirectoryInfo(AppContext.BaseDirectory); path is not null; path = path.Parent)
            {
                if (File.Exists(Path.Combine(path.FullName, "src", "MyAntigravity.WinForms",
                    "MyAntigravity.WinForms.csproj")))
                    return Path.Combine(path.FullName, "data");
            }
            return Path.Combine(AppContext.BaseDirectory, "data");
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--setup-cli", var reportPath])
            return SetupCli(reportPath);

        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, @"Local\MyAntigravity-" +
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(DataDirectory).ToUpperInvariant()))), out var first);
        if (!first)
        {
            MessageBox.Show("此目录下的 MyAntigravity 已打开。", "MyAntigravity",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        try
        {
            using var form = new MainForm(DataDirectory);
            Application.Run(form);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
        return 0;
    }

    private static int SetupCli(string reportPath)
    {
        if (!Path.IsPathFullyQualified(reportPath))
            return 2;
        try
        {
            Cli.EnsureWindowsInstalledAsync(AppContext.BaseDirectory, CancellationToken.None).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or TimeoutException or
            System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            try
            {
                using var report = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(report);
                writer.WriteLine(AgentText.Visible(error.Message));
            }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return 2;
            }
            return 1;
        }
    }
}
