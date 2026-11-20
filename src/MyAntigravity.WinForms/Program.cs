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
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, @"Local\MyAntigravity-" +
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(DataDirectory).ToUpperInvariant()))), out var first);
        if (!first)
        {
            MessageBox.Show("此目录下的 MyAntigravity 已打开。", "MyAntigravity",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
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
    }
}
