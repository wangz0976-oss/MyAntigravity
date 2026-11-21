namespace MyAntigravity;

internal sealed partial class MainForm
{
    private sealed record GuideChapter(string Name, string Text)
    {
        public override string ToString() => Name;
    }

    private Task ShowUserGuideAsync()
    {
        var guide = ReadHelpResource("MyAntigravity.UserGuide.txt");
        var chapters = ("\n" + guide.ReplaceLineEndings("\n")).Split("\n## ", StringSplitOptions.RemoveEmptyEntries)
            .Select(section => section.Trim()).Where(section => section.Contains('\n'))
            .Select(section => new GuideChapter(section[..section.IndexOf('\n')], section[(section.IndexOf('\n') + 1)..]))
            .ToArray();
        GuideChapter? selected = null;
        while (!closing)
        {
            (selected, _) = Dialogs.Browse(this, "MyAntigravity 使用说明", "按主题或关键字筛选", "说明书章节",
                "阅读所选章节 (&R)", chapters, chapter => $"{chapter.Name} {chapter.Text}", initial: selected);
            if (selected is null)
                break;
            Dialogs.Text(this, $"使用说明 - {selected.Name}", selected.Text.Trim(), showInTaskbar: true);
        }
        return Task.CompletedTask;
    }

    private Task ShowCurrentChangelogAsync()
    {
        string notes = UpdateNotes.PlainText(UpdateNotes.ForVersion(
            ReadHelpResource("MyAntigravity.Changelog.md"), Program.Version));
        Dialogs.Text(this, "MyAntigravity 更新日志", $"当前版本：{Program.Version}\n\n" +
            (notes.Length > 0 ? notes : "尚未登记这个构建的更新日志。"), showInTaskbar: true);
        return Task.CompletedTask;
    }

    private static string ReadHelpResource(string name)
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("找不到内置的帮助文件。");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
