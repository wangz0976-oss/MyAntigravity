namespace MyAntigravity;

internal sealed partial class MainForm
{
    private sealed record ActivityRecord(string? Session, DateTimeOffset At, string Summary, string Detail)
    {
        public string Timestamp => At.ToLocalTime().ToString("HH:mm:ss");
        public override string ToString() => $"{Timestamp} · {Summary}";
    }

    private readonly List<ActivityRecord> activity = [];
    private event Action<ActivityRecord>? ActivityRecorded;

    private void Log(string text, string? summary = null)
    {
        var firstLine = (summary ?? text).AsSpan().TrimStart();
        int newline = firstLine.IndexOfAny('\r', '\n');
        if (newline >= 0)
            firstLine = firstLine[..newline];
        const int maximum = 110;
        string caption = firstLine.Length <= maximum ? firstLine.ToString() : firstLine[..maximum].ToString() + "…";
        var entry = new ActivityRecord(sessionId, DateTimeOffset.Now, caption, text);
        activity.Add(entry);
        ActivityRecorded?.Invoke(entry);
    }

    private Task ShowActivityAsync()
    {
        using var window = Dialogs.Window("任务活动", new Size(800, 540));
        window.ShowInTaskbar = true;
        var entries = new ListBox
        {
            Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "任务活动列表",
            AccessibleDescription = "上下选择，回车进入详情，Shift 加 Tab 回到活动列表。", TabIndex = 0
        };
        var details = ReadableText("所选活动详情");
        details.TabIndex = 1;
        var close = new Button { Text = "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        var session = sessionId;
        bool Relevant(ActivityRecord entry) => session is null || entry.Session == session;

        void ShowDetails()
        {
            details.Text = entries.SelectedItem is ActivityRecord entry
                ? $"{entry.Timestamp}  {entry.Summary}\n\n{entry.Detail}"
                : "当前还没有活动记录。";
            details.Select(0, 0);
        }

        void Record(ActivityRecord entry)
        {
            if (!Relevant(entry))
                return;
            entries.Items.Add(entry);
            if (entries.SelectedIndex < 0)
                entries.SelectedIndex = 0;
        }

        entries.SelectedIndexChanged += (_, _) => ShowDetails();
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
        entries.Items.AddRange(activity.Where(Relevant).Cast<object>().ToArray());
        if (entries.Items.Count > 0)
            entries.SelectedIndex = entries.Items.Count - 1;
        else
            ShowDetails();
        var area = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 43));
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 57));
        area.Controls.Add(Caption("任务活动列表"), 0, 0);
        area.Controls.Add(entries, 0, 1);
        area.Controls.Add(Caption("所选活动详情"), 0, 2);
        area.Controls.Add(details, 0, 3);
        window.CancelButton = close;
        Dialogs.Vertical(window, area, Buttons(1, close));
        window.ActiveControl = entries;
        ActivityRecorded += Record;
        try { window.ShowDialog(this); }
        finally { ActivityRecorded -= Record; }
        return Task.CompletedTask;
    }

    private void ShowLog() => Dialogs.Text(this, "任务日志", activity.Count == 0 ? "当前没有任务记录。"
        : string.Join("\n\n", activity.Select(entry => $"[{entry.Timestamp}] {entry.Detail}")));
}
