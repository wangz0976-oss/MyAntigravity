namespace MyAntigravity;

internal sealed partial class MainForm
{
    private readonly List<ActivityRecord> activity = [];
    private event Action<ActivityRecord>? ActivityRecorded;
    private event Action<ActivityRecord>? ActivityUpdated;

    private ActivityRecord Log(string text, string? summary = null, TaskActivityKind kind = TaskActivityKind.General)
    {
        var firstLine = (summary ?? text).AsSpan().TrimStart();
        int newline = firstLine.IndexOfAny('\r', '\n');
        if (newline >= 0)
            firstLine = firstLine[..newline];
        const int maximum = 110;
        string caption = firstLine.Length <= maximum ? firstLine.ToString() : firstLine[..maximum].ToString() + "…";
        var entry = new ActivityRecord(sessionId, DateTimeOffset.Now, caption, text, kind);
        activity.Add(entry);
        ActivityRecorded?.Invoke(entry);
        return entry;
    }

    private Task ShowActivityAsync()
    {
        ShowActivity("任务活动", sessionId);
        return Task.CompletedTask;
    }

    private void ShowLog() => ShowActivity("任务日志", null);

    private void ShowActivity(string title, string? session)
    {
        using var window = Dialogs.Window(title, new Size(800, 560));
        window.ShowInTaskbar = true;
        window.KeyPreview = true;
        var query = new TextBox
        {
            AccessibleName = "搜索任务记录", AccessibleDescription = "输入关键词筛选，按向下键到结果列表。",
            Width = 250, TabIndex = 0
        };
        var kinds = new ComboBox
        {
            AccessibleName = "记录类型", Width = 155, DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = 1
        };
        kinds.Items.AddRange(["全部记录", "失败和错误", "审批和提问", "推理记录"]);
        kinds.SelectedIndex = 0;
        var entries = new ListBox
        {
            Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "任务活动列表",
            AccessibleDescription = "上下选择，回车进入详情。Ctrl 加 C 复制此条。Ctrl 加 F 搜索。", TabIndex = 1
        };
        var details = ReadableText("所选活动详情");
        details.TabIndex = 2;
        details.AccessibleDescription = "Shift 加 Tab 回到活动列表。";
        var matches = Caption("");
        var feedback = Caption("");
        feedback.LiveSetting = System.Windows.Forms.Automation.AutomationLiveSetting.Polite;
        var copy = new Button { Text = "复制所选记录 (&Y)", AutoSize = true };
        var full = new Button { Text = "查看完整日志 (&W)", AutoSize = true };
        var close = new Button { Text = "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        string[] terms = [];
        string shownDetail = "";
        TaskActivityKind? kind = null;
        bool Relevant(ActivityRecord entry) => entry.Matches(session, kind, terms);

        void UpdateCount() => matches.Text = $"共 {entries.Items.Count} 条匹配记录。";

        void ShowDetails()
        {
            var entry = entries.SelectedItem as ActivityRecord;
            shownDetail = entry is null ? "没有符合条件的记录。" : FormatDetail(entry);
            details.Text = shownDetail;
            details.Select(0, 0);
            copy.Enabled = entry is not null;
        }

        static string FormatDetail(ActivityRecord entry) => $"{entry.Timestamp}  {entry.Summary}\n\n{entry.Detail}".ReplaceLineEndings("\n");

        void RefreshMatches()
        {
            var previous = entries.SelectedItem;
            terms = query.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            kind = kinds.SelectedIndex switch
            {
                1 => TaskActivityKind.Failure, 2 => TaskActivityKind.Interaction, 3 => TaskActivityKind.Reasoning, _ => null
            };
            entries.BeginUpdate();
            try
            {
                entries.Items.Clear();
                entries.Items.AddRange(activity.Where(Relevant).Cast<object>().ToArray());
                if (previous is not null)
                    entries.SelectedItem = previous;
                if (entries.SelectedIndex < 0 && entries.Items.Count > 0)
                    entries.SelectedIndex = entries.Items.Count - 1;
            }
            finally { entries.EndUpdate(); }
            ShowDetails();
            UpdateCount();
        }

        void Record(ActivityRecord entry)
        {
            if (session is null || entry.Session == session)
                full.Enabled = true;
            if (!Relevant(entry))
                return;
            entries.Items.Add(entry);
            if (entries.SelectedIndex < 0)
                entries.SelectedIndex = 0;
            UpdateCount();
        }

        void UpdateRecord(ActivityRecord entry)
        {
            if (!entries.Items.Contains(entry))
            {
                Record(entry);
                return;
            }
            if (!ReferenceEquals(entries.SelectedItem, entry))
                return;
            var latest = FormatDetail(entry);
            if (latest.Length > shownDetail.Length)
                Dialogs.AppendReadingText(details, latest[shownDetail.Length..]);
            shownDetail = latest;
        }

        query.TextChanged += (_, _) => RefreshMatches();
        kinds.SelectedIndexChanged += (_, _) => RefreshMatches();
        entries.SelectedIndexChanged += (_, _) => ShowDetails();
        query.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && entries.Items.Count > 0)
            {
                entries.Focus();
                e.SuppressKeyPress = true;
            }
        };
        entries.PreviewKeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
                e.IsInputKey = true;
        };
        entries.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || entries.SelectedIndex < 0)
                return;
            e.SuppressKeyPress = true;
            details.Focus();
        };
        copy.Click += (_, _) =>
        {
            if (entries.SelectedItem is not ActivityRecord entry)
                return;
            try
            {
                Clipboard.SetText(entry.LogText);
                feedback.Text = "已复制所选记录。";
            }
            catch (System.Runtime.InteropServices.ExternalException) { feedback.Text = "剪贴板暂不可用，请重试。"; }
        };
        full.Click += (_, _) => Dialogs.Text(window, "完整任务日志", string.Join("\n\n", activity
            .Where(entry => session is null || entry.Session == session).Select(entry => entry.LogText)));
        window.KeyDown += (_, e) =>
        {
            if (e.KeyData == (Keys.Control | Keys.F))
            {
                query.Focus();
                query.SelectAll();
            }
            else if (e.KeyData == (Keys.Control | Keys.C) && entries.Focused)
                copy.PerformClick();
            else
                return;
            e.SuppressKeyPress = true;
        };

        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, TabIndex = 0 };
        filters.Controls.AddRange([Caption("关键词"), query, Caption("类型"), kinds]);
        var area = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 43));
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 57));
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.Controls.Add(filters, 0, 0);
        area.Controls.Add(matches, 0, 1);
        area.Controls.Add(entries, 0, 2);
        area.Controls.Add(Caption("所选活动详情"), 0, 3);
        area.Controls.Add(details, 0, 4);
        area.Controls.Add(feedback, 0, 5);
        window.CancelButton = close;
        Dialogs.Vertical(window, area, Buttons(1, copy, full, close));
        full.Enabled = activity.Any(entry => session is null || entry.Session == session);
        RefreshMatches();
        window.ActiveControl = entries.Items.Count == 0 ? query : entries;
        ActivityRecorded += Record;
        ActivityUpdated += UpdateRecord;
        try { window.ShowDialog(this); }
        finally
        {
            ActivityRecorded -= Record;
            ActivityUpdated -= UpdateRecord;
        }
    }
}
