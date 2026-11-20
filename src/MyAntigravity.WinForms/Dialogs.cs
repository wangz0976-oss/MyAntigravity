namespace MyAntigravity;

internal static partial class Dialogs
{
    internal static bool SkillDetails(Form owner, string name, string details, bool available)
    {
        using var window = Window($"技能说明 - {AgentText.Visible(name)}", new Size(780, 540));
        window.ShowInTaskbar = true;
        var text = MainForm.ReadableText("技能说明");
        text.Text = details;
        text.Select(0, 0);
        var insert = new Button { Text = "填入技能命令 (&I)", AutoSize = true, Enabled = available, DialogResult = DialogResult.OK };
        var close = new Button { Text = "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        window.CancelButton = close;
        Vertical(window, text, MainForm.Buttons(1, insert, close));
        window.ActiveControl = text;
        return window.ShowDialog(owner) == DialogResult.OK;
    }

    internal static Form Window(string title, Size size) => new()
    {
        Text = title, ClientSize = size, MinimumSize = new Size(410, 280), ShowInTaskbar = false,
        StartPosition = FormStartPosition.CenterParent, AutoScaleMode = AutoScaleMode.Dpi,
        MinimizeBox = false, MaximizeBox = false
    };

    internal static void Text(Form owner, string title, string text, bool showInTaskbar = false)
    {
        using var form = Window(title, new Size(735, 500));
        form.ShowInTaskbar = showInTaskbar;
        var contents = MainForm.ReadableText(title);
        contents.Text = text;
        contents.SelectionStart = 0;
        var close = new Button { Text = "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel, TabIndex = 1 };
        form.CancelButton = close;
        Vertical(form, contents, MainForm.Buttons(1, close));
        form.ActiveControl = contents;
        form.ShowDialog(owner);
    }

    internal static void Vertical(Form form, Control content, Control footer)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(content, 0, 0);
        layout.Controls.Add(footer, 0, 1);
        form.Controls.Add(layout);
    }

    internal static string? ProjectDirectory(Form owner, string? current)
    {
        using var form = Window("打开本地项目", new Size(640, 245));
        form.ShowInTaskbar = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var path = new TextBox
        {
            AccessibleName = "项目目录路径",
            AccessibleDescription = "输入完整目录路径，按回车打开项目。",
            Dock = DockStyle.Top,
            Text = current ?? "",
            TabIndex = 0
        };
        var validation = MainForm.Caption("");
        validation.LiveSetting = System.Windows.Forms.Automation.AutomationLiveSetting.Polite;
        layout.Controls.Add(MainForm.Caption("项目目录路径"), 0, 0);
        layout.Controls.Add(path, 0, 1);
        layout.Controls.Add(validation, 0, 2);

        var open = new Button { Text = "打开项目 (&O)", AutoSize = true };
        var browse = new Button { Text = "浏览文件夹 (&B)", AutoSize = true };
        var close = new Button { Text = "取消 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        string? directory = null;
        open.Click += (_, _) =>
        {
            try
            {
                directory = LocalSettings.ProjectPath(path.Text.Trim().Trim('"'));
                form.DialogResult = DialogResult.OK;
            }
            catch (Exception error) when (error is ArgumentException or IOException)
            {
                validation.Text = error.Message;
                path.AccessibleDescription = error.Message;
                path.Focus();
                path.SelectAll();
                ScreenReaderSpeech.TrySpeak(error.Message);
            }
        };
        browse.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog
            {
                Description = "选择 Antigravity 可操作的本地项目目录",
                UseDescriptionForTitle = true,
                InitialDirectory = Directory.Exists(path.Text) ? path.Text : current ?? "",
                ShowNewFolderButton = true
            };
            if (picker.ShowDialog(form) == DialogResult.OK)
            {
                path.Text = picker.SelectedPath;
                open.PerformClick();
            }
        };
        form.AcceptButton = open;
        form.CancelButton = close;
        Vertical(form, layout, MainForm.Buttons(1, open, browse, close));
        form.ActiveControl = path;
        form.Shown += (_, _) => path.SelectAll();
        return form.ShowDialog(owner) == DialogResult.OK ? directory : null;
    }

    internal static string? Permission(Form owner, PermissionRequest request, CancellationToken cancellation,
        Func<Task> cancelTurn)
    {
        if (cancellation.IsCancellationRequested)
            return null;

        using var form = new PermissionWindow
        {
            Text = "Agent 请求工具审批", ClientSize = new Size(760, 535),
            MinimumSize = new Size(460, 320), ShowInTaskbar = true,
            StartPosition = FormStartPosition.CenterParent, AutoScaleMode = AutoScaleMode.Dpi,
            MinimizeBox = false, MaximizeBox = false
        };
        using var registration = cancellation.Register(() =>
        {
            try { form.BeginInvoke(() => form.Close()); }
            catch (InvalidOperationException) { }
        });
        var details = MainForm.ReadableText("工具、命令和文件修改详情");
        details.AccessibleDescription = "Tab 浏览审批按钮。回车或 Esc 拒绝。允许本次请用 Alt 加 Y，或聚焦允许按钮后按空格。Ctrl 加 Shift 加 X 中止任务。";
        details.TabIndex = 0;
        details.Text = request.Description;
        details.Select(0, 0);
        var reject = new Button { Text = "拒绝本次 (&R)", AutoSize = true, DialogResult = DialogResult.Cancel };
        var abort = new Button { Text = "中止任务 (&X)", AutoSize = true };
        var buttons = new List<Button> { reject };
        if (request.Changes.Count > 0)
        {
            var review = new Button { Text = "逐段审查文件 (&W)", AutoSize = true };
            review.Click += (_, _) => ReviewChanges(form, request.Changes, cancellation);
            buttons.Add(review);
            details.AccessibleDescription += "Alt 加 W 打开逐段审查。";
        }
        string? selected = null;
        foreach (var option in request.Options.Where(option => option.Kind != "reject_once"))
        {
            var label = option.Kind == "allow_once" ? "允许本次 (&Y)" : "始终拒绝 (&D)";
            var choice = new Button { Text = label, AccessibleDescription = option.ToString(), AutoSize = true };
            choice.Click += (_, _) =>
            {
                selected = option.Id;
                form.Close();
            };
            buttons.Add(choice);
        }
        buttons.Add(abort);
        bool stopping = false;
        abort.Click += async (_, _) =>
        {
            stopping = true;
            foreach (var button in buttons)
                button.Enabled = false;
            await cancelTurn();
            if (!form.IsDisposed && !cancellation.IsCancellationRequested)
                form.Close();
        };
        form.CancelButton = reject;
        form.AcceptButton = reject;
        Vertical(form, details, MainForm.Buttons(1, buttons.ToArray()));
        form.ActiveControl = details;
        form.Reject = reject.PerformClick;
        form.Abort = abort.PerformClick;
        form.ReadDetails = () =>
        {
            if (!ScreenReaderSpeech.TrySpeak(request.Description))
                details.Focus();
        };
        form.Shown += (_, _) =>
        {
            if (cancellation.IsCancellationRequested)
                form.Close();
        };
        form.ShowDialog(owner);
        if (cancellation.IsCancellationRequested || stopping)
            return null;
        return selected ?? request.Reject();
    }

    private sealed class PermissionWindow : Form
    {
        internal Action? Reject;
        internal Action? Abort;
        internal Action? ReadDetails;

        protected override bool ProcessCmdKey(ref Message message, Keys keys)
        {
            // Enter must stay a refusal even when a different button has focus.
            if (keys is Keys.Enter or Keys.Escape)
            {
                Reject?.Invoke();
                return true;
            }
            if (keys == (Keys.Control | Keys.Shift | Keys.X))
            {
                Abort?.Invoke();
                return true;
            }
            if (keys == (Keys.Control | Keys.Shift | Keys.R))
            {
                ReadDetails?.Invoke();
                return true;
            }
            return base.ProcessCmdKey(ref message, keys);
        }
    }

    internal static (SessionOption Option, Choice Choice)? SessionSettings(Form owner, IReadOnlyList<SessionOption> options)
    {
        using var form = Window("模型、权限模式与推理设置", new Size(680, 370));
        var categories = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "配置项",
            AccessibleDescription = "用方向键选择配置项。按 Tab 到可选值，再按一次 Tab 查看说明。",
            Dock = DockStyle.Top, TabIndex = 0 };
        var values = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "可选值", Dock = DockStyle.Top, TabIndex = 1 };
        var details = MainForm.ReadableText("选项说明");
        details.TabIndex = 2;
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(MainForm.Caption("配置项"), 0, 0);
        body.Controls.Add(categories, 0, 1);
        body.Controls.Add(MainForm.Caption("当前值与可选值"), 0, 2);
        body.Controls.Add(values, 0, 3);
        body.Controls.Add(details, 0, 4);
        var apply = new Button { Text = "应用 (&A)", AutoSize = true, DialogResult = DialogResult.OK };
        var close = new Button { Text = "取消 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };

        categories.SelectedIndexChanged += (_, _) =>
        {
            if (categories.SelectedItem is not SessionOption option)
                return;
            values.AccessibleName = option.Label + "的可选值";
            values.AccessibleDescription = "用方向键选择，按 Tab 查看说明。选定后按回车应用到当前会话。";
            values.Items.Clear();
            values.Items.AddRange(option.Choices.Cast<object>().ToArray());
            values.SelectedItem = option.Choices.FirstOrDefault(choice => choice.Id == option.Current);
            ShowDetails();
        };
        values.SelectedIndexChanged += (_, _) => ShowDetails();
        void ShowDetails()
        {
            if (categories.SelectedItem is not SessionOption option)
                return;
            var choice = values.SelectedItem as Choice;
            apply.Enabled = choice is not null;
            var current = option.Choices.FirstOrDefault(item => item.Id == option.Current)?.Name
                ?? option.Current ?? "未报告";
            details.Text = string.Join("\n\n", new[] { $"当前值：{current}",
                choice is not null && choice.Id != option.Current ? $"待应用：{choice.Name}" : null,
                option.Description, choice?.Description }.Where(text => !string.IsNullOrWhiteSpace(text)));
            details.Select(0, 0);
        }

        categories.Items.AddRange(options.Cast<object>().ToArray());
        categories.SelectedItem = options.FirstOrDefault(option => option.Category == "model") ?? options[0];
        form.CancelButton = close;
        form.AcceptButton = apply;
        Vertical(form, body, MainForm.Buttons(1, apply, close));
        form.ActiveControl = categories;

        return form.ShowDialog(owner) == DialogResult.OK && categories.SelectedItem is SessionOption selected &&
               values.SelectedItem is Choice value ? (selected, value) : null;
    }
}
