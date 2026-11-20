namespace MyAntigravity;

internal static partial class Dialogs
{
    internal static IReadOnlyList<string>? AdditionalDirectories(Form owner, string primary, IReadOnlyList<string> saved)
    {
        using var form = Window("附加工作目录", new Size(760, 470));
        form.ShowInTaskbar = true;
        form.KeyPreview = true;
        var list = new ListBox
        {
            AccessibleName = "当前项目的附加工作目录", AccessibleDescription = "Ctrl 加 O 添加，Delete 从列表移除。",
            Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true
        };
        list.Items.AddRange(saved.Cast<object>().ToArray());
        var description = MainForm.Caption("附加目录会加入会话的工作范围。保存后重新打开会话生效。");
        var main = MainForm.Caption($"主目录：{primary}");
        var add = new Button { Text = "添加目录 (&A)", AutoSize = true };
        var remove = new Button { Text = "移除所选 (&D)", AutoSize = true };
        var save = new Button { Text = "保存 (&S)", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };

        list.SelectedIndexChanged += (_, _) => remove.Enabled = list.SelectedIndex >= 0;
        add.Click += (_, _) =>
        {
            string? path = ProjectDirectory(form, null);
            if (path is null)
                return;
            if (LocalSettings.PathEquals(primary, path) || list.Items.Cast<string>().Any(item => LocalSettings.PathEquals(item, path)))
            {
                Text(form, "目录已在工作范围内", "此目录已经是主目录或已添加到列表。");
                return;
            }
            list.SelectedIndex = list.Items.Add(path);
            list.Focus();
        };
        remove.Click += (_, _) =>
        {
            int index = list.SelectedIndex;
            if (index < 0)
                return;
            list.Items.RemoveAt(index);
            if (list.Items.Count > 0)
            {
                list.SelectedIndex = Math.Min(index, list.Items.Count - 1);
                list.Focus();
            }
            else
                add.Focus();
        };
        form.KeyDown += (_, e) =>
        {
            if (e.KeyData == (Keys.Control | Keys.O))
            {
                e.SuppressKeyPress = true;
                add.PerformClick();
            }
            else if (e.KeyData == Keys.Delete && list.Focused)
            {
                e.SuppressKeyPress = true;
                remove.PerformClick();
            }
        };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(description, 0, 0);
        panel.Controls.Add(main, 0, 1);
        panel.Controls.Add(list, 0, 2);
        remove.Enabled = list.Items.Count > 0;
        if (remove.Enabled)
            list.SelectedIndex = 0;
        form.CancelButton = cancel;
        Vertical(form, panel, MainForm.Buttons(1, add, remove, save, cancel));
        form.ActiveControl = list.Items.Count > 0 ? list : add;
        return form.ShowDialog(owner) == DialogResult.OK ? list.Items.Cast<string>().ToArray() : null;
    }
}
