using System.Windows.Forms.Automation;

namespace MyAntigravity;

internal static partial class Dialogs
{
    internal static void ReadMessage(Form owner, string title, string text, Func<string>? readLatest)
    {
        using var form = Window(title, new Size(820, 535));
        form.ShowInTaskbar = true;
        var body = MainForm.ReadableText("消息正文");
        body.WordWrap = false;
        body.ScrollBars = RichTextBoxScrollBars.Both;
        body.AccessibleDescription = "按 Alt 加 B 选择代码块，Alt 加 Y 复制代码，Alt 加 L 查看链接，Esc 返回原消息。";
        string rendered = text.ReplaceLineEndings("\n");
        body.Text = rendered;
        body.Select(0, 0);
        var outline = MessageOutline.Parse(rendered);
        var code = new Button { Text = "跳到代码块 (&B)", AutoSize = true };
        var copy = new Button { Text = "复制代码 (&Y)", AutoSize = true };
        var links = new Button { Text = "查看链接 (&L)", AutoSize = true };
        var close = new Button { Text = "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        var status = MainForm.Caption("");
        status.LiveSetting = AutomationLiveSetting.Polite;

        void UpdateCommands()
        {
            code.Enabled = copy.Enabled = outline.CodeBlocks.Count > 0;
            links.Enabled = outline.Links.Count > 0;
        }

        MessageCodeBlock? ChooseCode() => Browse(form, "代码块", "筛选语言或行号", "代码块列表",
            "选中代码块 (&B)", outline.CodeBlocks, entry => entry.ToString()).Item;

        void Jump(int offset, int length = 0)
        {
            body.Select(Math.Min(offset, body.TextLength), length);
            body.Focus();
            body.ScrollToCaret();
        }

        code.Click += (_, _) =>
        {
            if (ChooseCode() is { } selected)
                Jump(selected.Offset);
        };
        copy.Click += (_, _) =>
        {
            var selected = outline.CodeBlocks.FirstOrDefault(entry => body.SelectionStart >= entry.Offset &&
                body.SelectionStart < entry.End) ?? ChooseCode();
            if (selected is null)
                return;
            if (selected.Code.Length == 0)
            {
                status.Text = "这个代码块是空的。";
                return;
            }
            try
            {
                Clipboard.SetText(selected.Code);
                status.Text = $"已复制第 {selected.Line} 行的代码块。";
            }
            catch (System.Runtime.InteropServices.ExternalException) { status.Text = "剪贴板暂不可用，请重试。"; }
        };
        links.Click += (_, _) =>
        {
            var selected = Browse(form, "消息中的链接", "筛选链接地址", "链接列表", "定位链接 (&R)",
                outline.Links, entry => entry.Address, entry => entry.Address).Item;
            if (selected is not null)
                Jump(selected.Offset, selected.Address.Length);
        };

        using var timer = readLatest is null ? null : new System.Windows.Forms.Timer { Interval = 350 };
        if (timer is not null && readLatest is not null)
        {
            timer.Tick += (_, _) =>
            {
                string latest = readLatest().ReplaceLineEndings("\n");
                if (latest.Length <= rendered.Length)
                    return;
                int caret = body.SelectionStart;
                int selection = body.SelectionLength;
                body.AppendText(latest[rendered.Length..]);
                body.Select(caret, selection);
                body.ScrollToCaret();
                rendered = latest;
                outline = MessageOutline.Parse(rendered);
                UpdateCommands();
            };
            timer.Start();
        }

        var area = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.Controls.Add(body, 0, 0);
        area.Controls.Add(status, 0, 1);
        form.CancelButton = close;
        Vertical(form, area, MainForm.Buttons(1, code, copy, links, close));
        form.ActiveControl = body;
        UpdateCommands();
        form.ShowDialog(owner);
    }
}
