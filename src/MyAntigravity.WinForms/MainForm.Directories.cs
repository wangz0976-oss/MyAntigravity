using System.Diagnostics;

namespace MyAntigravity;

internal sealed partial class MainForm
{
    private Task EditAdditionalDirectoriesAsync() => RunAsync(async () =>
    {
        if (project is not { } active)
            return;
        var previous = settings.DirectoriesForProject(active);
        var selection = Dialogs.AdditionalDirectories(this, active, previous);
        if (selection is null || previous.SequenceEqual(selection, StringComparer.OrdinalIgnoreCase))
            return;
        var saved = settings.AdditionalDirectories.ToList();
        settings.SetProjectDirectories(active, selection);
        try { storage.Save(settings); }
        catch
        {
            settings.AdditionalDirectories = saved;
            throw;
        }
        if (sessionId is not null)
            await ResetAgentAsync();
        SetStatus($"已保存 {selection.Count} 个附加工作目录。新建或恢复会话时生效。");
    });

    private Task ShowSelectedFolderAsync() => RunAsync(() =>
    {
        if (projects.SelectedItem is string path)
        {
            string directory = LocalSettings.ProjectPath(path);
            using var opened = Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        return Task.CompletedTask;
    });

    private Task CopySelectedFolderAsync() => RunAsync(() =>
    {
        if (projects.SelectedItem is string path)
        {
            try { Clipboard.SetText(path); }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                throw new IOException("暂时无法写入剪贴板，请重试。");
            }
            SetStatus("项目路径已复制。");
        }
        return Task.CompletedTask;
    });
}
