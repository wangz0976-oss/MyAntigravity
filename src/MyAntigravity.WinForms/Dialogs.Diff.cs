namespace MyAntigravity;

internal static partial class Dialogs
{
    private sealed record ReviewFile(string Path, FileChange[] Revisions)
    {
        public override string ToString() => $"{AgentText.Visible(Path)}，{Revisions.Length} 次修改";
    }

    private sealed record ReviewSection(int Revision, FileChange Change, DiffSection? Section)
    {
        public override string ToString() => $"第 {Revision} 次修改 · {Section?.Label ?? "全文比较"}";
    }

    internal static void ReviewChanges(Form owner, IEnumerable<FileChange> changes, CancellationToken cancellation = default)
    {
        var files = changes.GroupBy(change => change.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReviewFile(group.Key, group.ToArray())).ToArray();
        if (cancellation.IsCancellationRequested)
            return;
        if (files.Length == 0)
        {
            Text(owner, "文件差异审查", "当前会话尚未收到文件差异。");
            return;
        }

        using var form = new DiffWindow
        {
            Text = "文件差异审查", ClientSize = new Size(1000, 640), MinimumSize = new Size(590, 400),
            ShowInTaskbar = true, StartPosition = FormStartPosition.CenterParent,
            AutoScaleMode = AutoScaleMode.Dpi, MinimizeBox = false
        };
        using var registration = cancellation.Register(() =>
        {
            try { form.BeginInvoke(form.Close); }
            catch (InvalidOperationException) { }
        });
        var paths = new ListBox
        {
            AccessibleName = "修改文件列表", AccessibleDescription = "用方向键选择文件，回车进入差异段。",
            Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, TabIndex = 0
        };
        var sections = new ListBox
        {
            AccessibleName = "差异段列表", AccessibleDescription = "用方向键选择差异段，回车阅读详情。",
            Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, TabIndex = 1
        };
        var details = MainForm.ReadableText("所选差异段详情");
        details.TabIndex = 2;
        details.AccessibleDescription = "F7 或 F8 跳转到上一段或下一段。Ctrl 加 Page Up 或 Page Down 切换文件。";
        var help = MainForm.Caption("Ctrl+Page Up / Down 切换文件。F7 / F8 切换差异段。回车进入下一栏。");
        var entire = new Button { Text = "所选修改全文 (&W)", AutoSize = true };
        var close = new Button { Text = "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };

        void LoadSections()
        {
            sections.BeginUpdate();
            try
            {
                sections.Items.Clear();
                if (paths.SelectedItem is ReviewFile file)
                {
                    for (int i = 0; i < file.Revisions.Length; i++)
                    {
                        var change = file.Revisions[i];
                        var items = DiffSections.Build(change);
                        if (items.Count == 0)
                            sections.Items.Add(new ReviewSection(i + 1, change, null));
                        else
                            sections.Items.AddRange(items.Select(section => new ReviewSection(i + 1, change, section)).ToArray());
                    }
                }
                if (sections.Items.Count > 0)
                    sections.SelectedIndex = 0;
            }
            finally { sections.EndUpdate(); }
        }

        void ShowSection()
        {
            if (sections.SelectedItem is not ReviewSection selected)
                return;
            details.Text = $"文件：{AgentText.Visible(selected.Change.Path)}\n"
                + $"差异段 {sections.SelectedIndex + 1} / {sections.Items.Count}，第 {selected.Revision} 次修改\n\n"
                + (selected.Section?.Describe() ?? selected.Change.Describe());
            details.Select(0, 0);
            details.ScrollToCaret();
        }

        void Move(ListBox list, int offset)
        {
            if (list.Items.Count == 0)
                return;
            int next = Math.Clamp(list.SelectedIndex + offset, 0, list.Items.Count - 1);
            if (next == list.SelectedIndex && ReferenceEquals(list, sections))
                ShowSection();
            else
                list.SelectedIndex = next;
            list.Focus();
        }

        paths.SelectedIndexChanged += (_, _) => LoadSections();
        sections.SelectedIndexChanged += (_, _) => ShowSection();
        entire.Click += (_, _) =>
        {
            if (sections.SelectedItem is not ReviewSection selected)
                return;
            details.Text = selected.Change.Describe();
            details.Select(0, 0);
            details.Focus();
            details.ScrollToCaret();
        };
        form.Navigate = key =>
        {
            switch (key)
            {
                case Keys.Control | Keys.PageUp: Move(paths, -1); break;
                case Keys.Control | Keys.PageDown: Move(paths, 1); break;
                case Keys.F7: Move(sections, -1); break;
                case Keys.F8: Move(sections, 1); break;
                case Keys.Enter when paths.Focused: sections.Focus(); break;
                case Keys.Enter when sections.Focused: details.Focus(); break;
                default: return false;
            }
            return true;
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 66));
        layout.Controls.Add(MainForm.Caption("修改文件"), 0, 0);
        layout.Controls.Add(MainForm.Caption("差异段"), 1, 0);
        layout.Controls.Add(paths, 0, 1);
        layout.Controls.Add(sections, 1, 1);
        layout.Controls.Add(help, 0, 2);
        layout.SetColumnSpan(help, 2);
        layout.Controls.Add(details, 0, 3);
        layout.SetColumnSpan(details, 2);
        form.CancelButton = close;
        Vertical(form, layout, MainForm.Buttons(1, entire, close));
        paths.Items.AddRange(files);
        paths.SelectedIndex = 0;
        form.ActiveControl = paths;
        form.Shown += (_, _) =>
        {
            if (cancellation.IsCancellationRequested)
                form.Close();
        };
        form.ShowDialog(owner);
    }

    private sealed class DiffWindow : Form
    {
        internal Func<Keys, bool>? Navigate;

        protected override bool ProcessCmdKey(ref Message message, Keys keys) =>
            Navigate?.Invoke(keys) == true || base.ProcessCmdKey(ref message, keys);
    }
}
