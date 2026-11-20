namespace MyAntigravity;

internal static partial class Dialogs
{
    internal static T? Search<T>(Form owner, string title, string searchName, string listName, string acceptText,
        IReadOnlyList<T> entries, Func<T, string> searchText, Func<T, string>? describe = null) where T : class
        => Browse(owner, title, searchName, listName, acceptText, entries, searchText, describe).Item;

    internal static (T? Item, bool Remove) Browse<T>(Form owner, string title, string searchName, string listName,
        string acceptText, IReadOnlyList<T> entries, Func<T, string> searchText, Func<T, string>? describe = null,
        string? removeText = null, T? initial = null) where T : class
    {
        using var form = Window(title, new Size(760, 510));
        form.ShowInTaskbar = true;
        form.KeyPreview = true;
        var query = new TextBox
        {
            AccessibleName = searchName, Dock = DockStyle.Top, TabIndex = 0,
            AccessibleDescription = "输入关键词筛选。按向下键或 Tab 到结果列表。"
        };
        var matches = new ListBox
        {
            AccessibleName = listName, Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true,
            TabIndex = 1, AccessibleDescription = "用方向键选择，按回车确认。Ctrl 加 F 筛选。"
        };
        var count = MainForm.Caption("");
        var accept = new Button { Text = acceptText, AutoSize = true, DialogResult = DialogResult.OK };
        var close = new Button { Text = "取消 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new List<Button> { accept };
        Button? remove = null;
        if (removeText is not null)
        {
            remove = new Button { Text = removeText, AutoSize = true, DialogResult = DialogResult.Yes };
            buttons.Add(remove);
            matches.AccessibleDescription += " Delete 打开移除确认。";
        }
        buttons.Add(close);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = describe is null ? 4 : 6 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, describe is null ? 100 : 62));
        panel.Controls.Add(MainForm.Caption(searchName), 0, 0);
        panel.Controls.Add(query, 0, 1);
        panel.Controls.Add(count, 0, 2);
        panel.Controls.Add(matches, 0, 3);
        RichTextBox? details = null;
        if (describe is not null)
        {
            details = MainForm.ReadableText("所选结果详情");
            details.TabIndex = 2;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            panel.Controls.Add(MainForm.Caption("所选结果详情"), 0, 4);
            panel.Controls.Add(details, 0, 5);
        }

        void SelectionChanged()
        {
            var selected = matches.SelectedItem as T;
            accept.Enabled = selected is not null;
            if (remove is not null)
                remove.Enabled = accept.Enabled;
            if (details is not null && describe is not null)
            {
                details.Text = selected is null ? "" : describe(selected);
                details.Select(0, 0);
            }
        }

        void Refresh(object? previous)
        {
            var terms = query.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var results = entries.Where(item => terms.All(term =>
                searchText(item).Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
            matches.BeginUpdate();
            try
            {
                matches.Items.Clear();
                matches.Items.AddRange(results.Cast<object>().ToArray());
                matches.SelectedItem = previous;
                if (matches.SelectedIndex < 0 && results.Length > 0)
                    matches.SelectedIndex = 0;
            }
            finally { matches.EndUpdate(); }

            count.Text = results.Length == 0 ? "没有匹配结果。" : $"共 {results.Length} 项";
            SelectionChanged();
        }

        query.TextChanged += (_, _) => Refresh(matches.SelectedItem);
        query.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Down || matches.Items.Count == 0)
                return;
            e.SuppressKeyPress = true;
            matches.Focus();
        };
        matches.SelectedIndexChanged += (_, _) => SelectionChanged();
        matches.DoubleClick += (_, _) => accept.PerformClick();
        matches.KeyPress += (_, e) =>
        {
            if (char.IsControl(e.KeyChar))
                return;
            e.Handled = true;
            query.Focus();
            query.Select(query.TextLength, 0);
            query.SelectedText = e.KeyChar.ToString();
        };
        form.KeyDown += (_, e) =>
        {
            if (e.KeyData == (Keys.Control | Keys.F))
            {
                e.SuppressKeyPress = true;
                query.Focus();
                query.SelectAll();
            }
            else if (e.KeyData == Keys.Delete && matches.Focused && remove is { Enabled: true })
            {
                e.SuppressKeyPress = true;
                remove.PerformClick();
            }
        };
        form.CancelButton = close;
        form.AcceptButton = accept;
        Vertical(form, panel, MainForm.Buttons(1, buttons.ToArray()));
        Refresh(initial);
        form.ActiveControl = matches.Items.Count > 0 ? matches : query;
        var result = form.ShowDialog(owner);
        return result is DialogResult.OK or DialogResult.Yes
            ? (matches.SelectedItem as T, result == DialogResult.Yes) : (null, false);
    }

    internal static string? SessionName(Form owner, string? original)
    {
        using var form = Window("重命名本地会话", new Size(620, 260));
        form.ShowInTaskbar = true;
        var name = new TextBox
        {
            AccessibleName = "本地会话名称", Dock = DockStyle.Top, Text = original ?? "", MaxLength = 160, TabIndex = 0,
            AccessibleDescription = "名称仅保存在本机。清空后保存可恢复默认名称。"
        };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(MainForm.Caption("本地会话名称。清空后保存可恢复默认名称。"), 0, 0);
        content.Controls.Add(name, 0, 1);
        var save = new Button { Text = "保存 (&S)", AutoSize = true, DialogResult = DialogResult.OK };
        var close = new Button { Text = "取消 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        form.AcceptButton = save;
        form.CancelButton = close;
        Vertical(form, content, MainForm.Buttons(1, save, close));
        form.ActiveControl = name;
        form.Shown += (_, _) => name.SelectAll();
        return form.ShowDialog(owner) == DialogResult.OK ? name.Text : null;
    }

    internal static bool ConfirmCleanup(Form owner, string title, string description, string confirmText)
    {
        using var form = Window(title, new Size(690, 350));
        form.ShowInTaskbar = true;
        var details = MainForm.ReadableText("移除操作与影响");
        details.Text = description;
        details.AccessibleDescription = "按 Alt 加 D 确认。Esc 取消。";
        var cancel = new Button { Text = "取消 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        var confirm = new Button { Text = confirmText, AutoSize = true, DialogResult = DialogResult.OK };
        form.AcceptButton = cancel;
        form.CancelButton = cancel;
        Vertical(form, details, MainForm.Buttons(1, cancel, confirm));
        form.ActiveControl = details;
        details.Select(0, 0);
        return form.ShowDialog(owner) == DialogResult.OK;
    }
}
