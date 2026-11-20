namespace MyAntigravity;

internal static partial class Dialogs
{
    private sealed record AttachmentOptions<T>(string Title, string List, string Add, string FileDialog, string Filter,
        int MaxCount, int MaxBytes, string Limits, Func<T, string> Path, Func<T, int> Length,
        Func<string, int, CancellationToken, Task<T>> Load, Action<Form, T> Preview) where T : class;

    internal static void Images(Form owner, List<ImageAttachment> images, string directory, bool canAdd) =>
        Attachments(owner, images, directory, canAdd, new AttachmentOptions<ImageAttachment>(
            "图片附件", "待发送图片", "添加图片", "选择要附上的图片",
            "图片（PNG、JPEG、GIF、BMP、TIFF）|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff",
            PromptImage.MaxCount, PromptImage.MaxPromptBytes, "单张最多 8 MiB，合计最多 12 MiB。",
            image => image.Path, image => image.Payload.Length, ImageAttachment.LoadAsync, ImagePreview));

    internal static void TextFiles(Form owner, List<TextAttachment> files, string directory, bool canAdd) =>
        Attachments(owner, files, directory, canAdd, new AttachmentOptions<TextAttachment>(
            "文本与代码附件", "待发送文本与代码", "添加文件", "选择要附上的文本或代码文件",
            "文本、文档与代码|*.txt;*.md;*.log;*.csv;*.tsv;*.json;*.jsonl;*.yaml;*.yml;*.toml;*.xml;*.html;*.css;*.js;*.ts;*.cs;*.py;*.ps1;*.c;*.cpp;*.h;*.java;*.go;*.rs;*.sql|所有文件|*.*",
            PromptTextFile.MaxCount, PromptTextFile.MaxPromptBytes, "单个最多 256 KiB，合计最多 1 MiB。",
            file => file.Path, file => file.Payload.Length, TextAttachment.LoadAsync,
            (owner, file) => Text(owner, "查看文本与代码附件", file.Details, showInTaskbar: true)));

    private static void Attachments<T>(Form owner, List<T> items, string directory, bool canAdd,
        AttachmentOptions<T> settings) where T : class
    {
        using var form = new AttachmentListWindow
        {
            Text = settings.Title, ClientSize = new Size(690, 430), MinimumSize = new Size(480, 310),
            ShowInTaskbar = true, StartPosition = FormStartPosition.CenterParent,
            AutoScaleMode = AutoScaleMode.Dpi, MinimizeBox = false, MaximizeBox = false
        };
        using var loading = new CancellationTokenSource();
        form.FormClosing += (_, _) => loading.Cancel();
        var list = new ListBox
        {
            AccessibleName = settings.List, AccessibleDescription = "用方向键选择。回车查看。Delete 从草稿移除。",
            IntegralHeight = false, Dock = DockStyle.Fill, HorizontalScrollbar = true
        };
        var summary = MainForm.Caption("");
        summary.LiveSetting = System.Windows.Forms.Automation.AutomationLiveSetting.Polite;
        var add = new Button { Text = $"{settings.Add} (&B)", AutoSize = true };
        var view = new Button { Text = "查看所选 (&V)", AutoSize = true };
        var remove = new Button { Text = "从草稿移除 (&R)", AutoSize = true, AccessibleDescription = "移除附件后源文件仍在。" };
        var close = new Button { Text = "完成 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        bool busy = false;

        void UpdateButtons()
        {
            add.Enabled = !busy && canAdd && items.Count < settings.MaxCount &&
                items.Sum(settings.Length) < settings.MaxBytes;
            view.Enabled = remove.Enabled = !busy && list.SelectedItem is T;
        }

        void Refresh(int index)
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                list.Items.AddRange(items.Cast<object>().ToArray());
                if (items.Count > 0)
                    list.SelectedIndex = Math.Clamp(index, 0, items.Count - 1);
            }
            finally { list.EndUpdate(); }
            summary.Text = !canAdd ? $"当前 CLI 不支持添加{settings.Title}。待发送 {items.Count} 项。"
                : $"{settings.Title}待发送 {items.Count} 项。{settings.Limits}";
            UpdateButtons();
        }

        add.Click += async (_, _) =>
        {
            using var picker = new OpenFileDialog
            {
                Title = settings.FileDialog, InitialDirectory = directory, Multiselect = true,
                CheckFileExists = true,
                Filter = settings.Filter
            };
            if (picker.ShowDialog(form) != DialogResult.OK)
                return;

            var token = loading.Token;
            var errors = new List<string>();
            int first = items.Count;
            busy = true;
            UpdateButtons();
            summary.Text = "正在读取所选附件。";
            try
            {
                foreach (string path in picker.FileNames)
                {
                    if (items.Any(item => LocalSettings.PathEquals(settings.Path(item), path)))
                        continue;
                    if (items.Count >= settings.MaxCount)
                    {
                        errors.Add($"一条消息最多附 {settings.MaxCount} 项{settings.Title}。");
                        break;
                    }
                    try
                    {
                        int remaining = settings.MaxBytes - items.Sum(settings.Length);
                        var item = await Task.Run(() => settings.Load(path, remaining, token), token);
                        token.ThrowIfCancellationRequested();
                        items.Add(item);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
                    {
                        errors.Add($"{AgentText.Visible(Path.GetFileName(path))}：{AgentText.Visible(error.Message)}");
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            finally { busy = false; }

            if (token.IsCancellationRequested || form.IsDisposed)
                return;
            Refresh(first);
            if (items.Count > 0)
                list.Focus();
            if (errors.Count > 0)
                Text(form, items.Count > first ? "部分附件未添加" : "附件未添加", string.Join("\n", errors));
        };
        view.Click += (_, _) =>
        {
            if (list.SelectedItem is T item)
                settings.Preview(form, item);
        };
        remove.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0)
                return;
            int index = list.SelectedIndex;
            items.RemoveAt(index);
            Refresh(index);
            if (items.Count > 0)
                list.Focus();
            else if (!add.Focus())
                close.Focus();
        };
        list.SelectedIndexChanged += (_, _) => UpdateButtons();
        list.DoubleClick += (_, _) => view.PerformClick();
        form.ChooseFiles = add.PerformClick;
        form.ViewSelected = view.PerformClick;
        form.RemoveSelected = remove.PerformClick;
        form.Items = list;
        form.CancelButton = close;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(summary, 0, 0);
        panel.Controls.Add(list, 0, 1);
        Vertical(form, panel, MainForm.Buttons(1, add, view, remove, close));
        Refresh(0);
        form.ActiveControl = items.Count > 0 ? list : add.Enabled ? add : close;
        form.ShowDialog(owner);
    }

    private static void ImagePreview(Form owner, ImageAttachment image)
    {
        using var form = Window("查看图片附件", new Size(780, 540));
        form.ShowInTaskbar = true;
        var info = MainForm.ReadableText("图片文件信息");
        info.Text = image.Details;
        info.Select(0, 0);
        var picture = new PictureBox
        {
            Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = image.Name,
            AccessibleRole = AccessibleRole.Graphic, TabStop = false
        };
        Bitmap? bitmap = null;
        try
        {
            bitmap = image.Preview();
            picture.Image = bitmap;
        }
        catch (IOException error) { info.AppendText($"\n\n{error.Message}"); }
        using (bitmap)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
            panel.Controls.Add(info, 0, 0);
            panel.Controls.Add(picture, 0, 1);
            var close = new Button { Text = "返回附件 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
            form.CancelButton = close;
            Vertical(form, panel, MainForm.Buttons(1, close));
            form.ActiveControl = info;
            info.Select(0, 0);
            form.ShowDialog(owner);
            picture.Image = null;
        }
    }

    private sealed class AttachmentListWindow : Form
    {
        internal Action? ChooseFiles;
        internal Action? ViewSelected;
        internal Action? RemoveSelected;
        internal ListBox? Items;

        protected override bool ProcessCmdKey(ref Message message, Keys keys)
        {
            switch (keys)
            {
                case Keys.Control | Keys.O: ChooseFiles?.Invoke(); return true;
                case Keys.Enter when Items?.Focused == true: ViewSelected?.Invoke(); return true;
                case Keys.Delete when Items?.Focused == true: RemoveSelected?.Invoke(); return true;
                default: return base.ProcessCmdKey(ref message, keys);
            }
        }
    }
}
