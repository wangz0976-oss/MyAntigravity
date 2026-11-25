namespace MyAntigravity;

internal sealed partial class MainForm
{
    private sealed record MenuCommand(ToolStripMenuItem Item, string Label, string MenuPath, string Shortcut)
    {
        public string SearchText => $"{Label} {MenuPath} {Shortcut} {Shortcut.Replace('+', ' ')}";
        public override string ToString() => Shortcut.Length == 0 ? $"{Label} · {MenuPath}" : $"{Label} · {MenuPath} · {Shortcut}";
    }

    private Task FindApplicationCommandAsync()
    {
        if (MainMenuStrip is not { } menu)
            return Task.CompletedTask;
        var entries = FindMenuCommands(menu.Items, "").ToArray();
        var chosen = Dialogs.Browse(this, "查找软件功能", "输入功能、菜单名或快捷键", "可用功能",
            "执行所选功能 (&O)", entries, entry => entry.SearchText, focusSearch: true).Item;
        if (chosen is not null && IsAvailable(chosen.Item))
            chosen.Item.PerformClick();
        return Task.CompletedTask;
    }

    private IEnumerable<MenuCommand> FindMenuCommands(ToolStripItemCollection items, string parent)
    {
        foreach (var item in items.OfType<ToolStripMenuItem>())
        {
            if (item == commandFinder || !IsAvailable(item))
                continue;
            string label = MenuLabel(item.Text ?? "");
            if (item.HasDropDownItems)
            {
                string path = parent.Length == 0 ? label : $"{parent} → {label}";
                foreach (var command in FindMenuCommands(item.DropDownItems, path))
                    yield return command;
                continue;
            }
            string shortcut = item.ShortcutKeys == Keys.None ? "" : item.ShortcutKeyDisplayString ?? "";
            if (item.ShortcutKeys != Keys.None && shortcut.Length == 0)
                shortcut = new KeysConverter().ConvertToString(item.ShortcutKeys) ?? "";
            yield return new MenuCommand(item, label, parent, shortcut);
        }
    }

    private static bool IsAvailable(ToolStripMenuItem item) => item.Available && item.Enabled &&
        (item.OwnerItem is not ToolStripMenuItem parent || IsAvailable(parent));

    private static string MenuLabel(string text) =>
        text.Length >= 5 && text[^5..^2] == " (&" && text[^1] == ')' ? text[..^5] : text;
}
