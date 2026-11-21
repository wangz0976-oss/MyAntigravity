namespace MyAntigravity;

internal static partial class Dialogs
{
    private sealed record ResultImage(int Number, PromptImage Payload)
    {
        public string Name => $"工具结果图片 {Number}";
        public override string ToString() => $"{Name}，{Payload.MimeType}，{Payload.Length:N0} 字节";
    }

    private static IEnumerable<Button> ToolResultActions(Form form, RichTextBox body, Label status,
        TurnToolResult result, IReadOnlyList<string> workspaces)
    {
        if (result.IsFailure)
        {
            var copyError = new Button { Text = "复制错误详情 (&E)", AutoSize = true };
            copyError.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(result.Details());
                    status.Text = "已复制所选工具的错误详情。";
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    status.Text = "剪贴板暂不可用，请重试。";
                }
            };
            body.AccessibleDescription += "Alt 加 E 复制这个工具的错误详情。";
            yield return copyError;
        }

        var locations = result.ProjectLocations();
        if (locations.Count > 0 && workspaces.Count > 0)
        {
            var files = new Button { Text = "定位项目文件 (&J)", AutoSize = true };
            files.Click += async (_, _) =>
            {
                files.Enabled = false;
                try { await ShowToolFileAsync(form, locations, workspaces); }
                finally
                {
                    if (!form.IsDisposed)
                        files.Enabled = true;
                }
            };
            body.AccessibleDescription += "Alt 加 J 在项目中定位文件行号。";
            yield return files;
        }

        if (result.Images.Count > 0)
        {
            var images = new Button { Text = "查看结果图片 (&I)", AutoSize = true };
            images.Click += (_, _) => ShowToolImages(form, result.Images);
            body.AccessibleDescription += "Alt 加 I 查看工具返回的图片。";
            yield return images;
        }

        if (result.Changes.Count > 0)
        {
            var review = new Button { Text = "逐段审查差异 (&W)", AutoSize = true };
            review.Click += (_, _) => ReviewChanges(form, result.Changes);
            body.AccessibleDescription += "Alt 加 W 打开文件差异审查。";
            yield return review;
        }
    }

    private static async Task ShowToolFileAsync(Form owner, IReadOnlyList<ToolLocation> locations,
        IReadOnlyList<string> workspaces)
    {
        var selected = locations.Count == 1 ? locations[0] : Search(owner, "关联项目文件", "按文件路径筛选",
            "工具报告的文件位置", "定位所选文件 (&J)", locations, item => item.Path, item => item.ToString());
        if (selected is null)
            return;
        using var pending = new CancellationTokenSource();
        void CancelPreview(object? sender, FormClosedEventArgs args) => pending.Cancel();
        owner.FormClosed += CancelPreview;
        try
        {
            var file = await Task.Run(() => ProjectFilePreview.ReadAsync(selected, workspaces, pending.Token), pending.Token);
            if (!owner.IsDisposed && owner.Visible)
            {
                ShowProjectFile(owner, file);
                owner.Close();
            }
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (!owner.IsDisposed && owner.Visible)
                Text(owner, "无法预览项目文件", $"{selected}\n\n{AgentText.Visible(error.Message)}");
        }
        finally { owner.FormClosed -= CancelPreview; }
    }

    private static void ShowProjectFile(Form owner, ProjectFilePreview file)
    {
        string position = file.RequestedLine == file.ActualLine ? $"第 {file.ActualLine} 行"
            : $"请求第 {file.RequestedLine} 行，已定位到文件末尾第 {file.ActualLine} 行";
        using var form = Window($"{AgentText.Visible(Path.GetFileName(file.FullPath))} - {position}", new Size(820, 535));
        form.ShowInTaskbar = true;
        form.KeyPreview = true;
        var body = MainForm.ReadableText($"只读项目文件，{position}");
        body.WordWrap = false;
        body.ScrollBars = RichTextBoxScrollBars.Both;
        body.AccessibleDescription = "用方向键阅读。Ctrl 加 F 查找文字，F3 找下一处，Ctrl 加 G 跳到行号。Esc 返回结果列表。";
        body.Text = file.Text;
        var query = new TextBox { AccessibleName = "文件查找关键词", Width = 185, MaxLength = 512 };
        var line = new TextBox { AccessibleName = "目标行号", Width = 85, MaxLength = 10 };
        var find = new Button { Text = "查找下一处 (&F)", AutoSize = true };
        var jump = new Button { Text = "跳到行号 (&G)", AutoSize = true };
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        controls.Controls.AddRange([MainForm.Caption("关键词"), query, find, MainForm.Caption("行号"), line, jump]);
        var status = MainForm.Caption("");
        status.LiveSetting = System.Windows.Forms.Automation.AutomationLiveSetting.Polite;
        var area = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.Controls.Add(MainForm.Caption($"{AgentText.Visible(file.FullPath)} · {position}"), 0, 0);
        area.Controls.Add(controls, 0, 1);
        area.Controls.Add(body, 0, 2);
        area.Controls.Add(status, 0, 3);
        var close = new Button { Text = "返回结果列表 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        form.CancelButton = close;
        Vertical(form, area, MainForm.Buttons(1, close));
        form.ActiveControl = body;

        void SelectText(int start, int length)
        {
            body.Focus();
            body.Select(start, length);
            body.ScrollToCaret();
        }

        void FindNext(bool repeat)
        {
            string term = query.Text;
            if (term.Length == 0)
            {
                query.Focus();
                return;
            }
            int start = body.SelectionStart + (repeat ? body.SelectionLength : 0);
            int index = file.Text.IndexOf(term, Math.Min(start, file.Text.Length), StringComparison.OrdinalIgnoreCase);
            bool wrapped = index < 0;
            if (wrapped)
                index = file.Text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                status.Text = "文件中没有匹配的文字。";
                query.Focus();
                query.SelectAll();
                return;
            }
            SelectText(index, term.Length);
            status.Text = $"{(wrapped ? "从文件开头继续，" : "")}在第 {body.GetLineFromCharIndex(index) + 1} 行找到。";
        }

        find.Click += (_, _) => FindNext(repeat: true);
        jump.Click += (_, _) =>
        {
            int start = int.TryParse(line.Text, out int number) && number > 0
                ? body.GetFirstCharIndexFromLine(number - 1) : -1;
            if (start < 0)
            {
                status.Text = "请输入文件中存在的行号，从 1 开始。";
                line.Focus();
                line.SelectAll();
                return;
            }
            int end = file.Text.IndexOf('\n', start);
            SelectText(start, (end < 0 ? file.Text.Length : end) - start);
            status.Text = $"已跳到第 {number} 行。";
        };
        form.KeyDown += (_, e) =>
        {
            switch (e.KeyData)
            {
                case Keys.Control | Keys.F: query.Focus(); query.SelectAll(); break;
                case Keys.Control | Keys.G: line.Focus(); line.SelectAll(); break;
                case Keys.F3: FindNext(repeat: true); break;
                case Keys.Enter when query.Focused: FindNext(repeat: false); break;
                case Keys.Enter when line.Focused: jump.PerformClick(); break;
                default: return;
            }
            e.SuppressKeyPress = true;
        };
        form.Shown += (_, _) => SelectText(file.Start, file.Length);
        form.ShowDialog(owner);
    }

    private static void ShowToolImages(Form owner, IReadOnlyList<PromptImage> images)
    {
        var entries = images.Select((image, index) => new ResultImage(index + 1, image)).ToArray();
        ResultImage? selected = entries[0];
        if (entries.Length > 1)
            selected = Search(owner, "工具结果图片", "按图片编号或格式筛选", "工具结果图片列表",
                "查看所选图片 (&V)", entries, item => item.ToString(), item => item.ToString());
        if (selected is null)
            return;
        try { ImagePreview(owner, ImageAttachment.FromToolResult(selected.Name, selected.Payload)); }
        catch (IOException error) { Text(owner, "无法预览工具结果图片", AgentText.Visible(error.Message)); }
    }
}
