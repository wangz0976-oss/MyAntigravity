using System.Globalization;
using System.Text.Json;
using System.Windows.Forms.Automation;

namespace MyAntigravity;

internal static partial class Dialogs
{
    internal static ElicitationResponse Elicitation(Form owner, ElicitationRequest request, CancellationToken cancellation,
        Func<Task> cancelTurn)
    {
        if (cancellation.IsCancellationRequested)
            return ElicitationResponse.Cancelled;
        using var form = Window("Agent 提问", new Size(790, 620));
        form.ShowInTaskbar = true;
        form.KeyPreview = true;
        using var registration = cancellation.Register(() =>
        {
            try { form.BeginInvoke(form.Close); }
            catch (InvalidOperationException) { }
        });

        var question = MainForm.ReadableText("提问来源与内容");
        question.Text = $"提问来源：{request.AgentName}\n\n{request.Message}\n\n填写并核对回答后，按 Alt+S 提交。登录和凭据请在官方页面处理。";
        question.AccessibleDescription = "用 Tab 遍历字段。Alt 加 S 提交答案，Alt 加 D 拒绝，Esc 取消。";
        question.Select(0, 0);
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(fields);
        var editors = new List<(ElicitationField Field, Control Edit, Func<object?> Read)>();
        foreach (var field in request.Fields)
        {
            var (edit, read) = AnswerEditor(field);
            string name = field.Required ? $"{field.Title}，必填" : $"{field.Title}，可选";
            fields.Controls.Add(MainForm.Caption(name), 0, fields.RowCount++);
            edit.AccessibleName = name;
            edit.AccessibleDescription = field.Description;
            edit.TabIndex = editors.Count;
            fields.Controls.Add(edit, 0, fields.RowCount++);
            if (field.Description.Length != 0)
            {
                var help = MainForm.Caption(field.Description);
                help.MaximumSize = new Size(660, 0);
                fields.Controls.Add(help, 0, fields.RowCount++);
            }
            editors.Add((field, edit, read));
        }

        var validation = MainForm.Caption("");
        validation.LiveSetting = AutomationLiveSetting.Polite;
        var submit = new Button { Text = "提交回答 (&S)", AutoSize = true };
        var decline = new Button { Text = "拒绝回答 (&D)", AutoSize = true, DialogResult = DialogResult.No };
        var cancel = new Button { Text = "取消 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        var result = ElicitationResponse.Cancelled;
        submit.Click += (_, _) =>
        {
            var answers = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var (field, edit, read) in editors)
            {
                if (!field.TryAnswer(read(), out var value, out string error))
                {
                    validation.Text = $"{field.Title}。{error}";
                    edit.AccessibleDescription = $"{field.Description} {error}".Trim();
                    edit.Focus();
                    return;
                }
                if (value is not null)
                    answers.Add(field.Key, value);
            }
            result = new ElicitationResponse("accept", answers);
            form.DialogResult = DialogResult.OK;
        };
        form.KeyDown += async (_, e) =>
        {
            if (e.KeyData == (Keys.Control | Keys.Shift | Keys.X))
            {
                e.SuppressKeyPress = true;
                await cancelTurn();
            }
        };
        form.CancelButton = cancel;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 125));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(question, 0, 0);
        panel.Controls.Add(scroll, 0, 1);
        panel.Controls.Add(validation, 0, 2);
        Vertical(form, panel, MainForm.Buttons(1, submit, decline, cancel));
        form.ActiveControl = question;
        form.Shown += (_, _) =>
        {
            if (cancellation.IsCancellationRequested)
                form.Close();
        };
        var action = form.ShowDialog(owner);
        return cancellation.IsCancellationRequested ? ElicitationResponse.Cancelled
            : action == DialogResult.No ? new ElicitationResponse("decline") : result;
    }

    private static (Control Edit, Func<object?> Read) AnswerEditor(ElicitationField field)
    {
        var initial = field.Default;
        switch (field.Kind)
        {
            case AnswerKind.Boolean:
            {
                var input = ChoicesInput();
                input.Items.AddRange(["未填写", "是", "否"]);
                input.SelectedIndex = initial.ValueKind == JsonValueKind.True ? 1 : initial.ValueKind == JsonValueKind.False ? 2 : 0;
                return (input, () => input.SelectedIndex switch { 1 => true, 2 => false, _ => null });
            }
            case AnswerKind.Choice:
            {
                var input = ChoicesInput();
                input.Items.Add("未填写");
                input.Items.AddRange(field.Choices.Cast<object>().ToArray());
                var chosen = initial.ValueKind == JsonValueKind.String
                    ? field.Choices.FirstOrDefault(choice => choice.Id == initial.GetString()) : null;
                input.SelectedItem = chosen is null ? input.Items[0] : chosen;
                return (input, () => (input.SelectedItem as Choice)?.Id);
            }
            case AnswerKind.Multiple:
            {
                var input = new CheckedListBox
                {
                    Dock = DockStyle.Top, CheckOnClick = true, IntegralHeight = false,
                    Height = Math.Clamp(field.Choices.Count, 2, 6) * 24
                };
                var selected = initial.ValueKind == JsonValueKind.Array
                    ? initial.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()).ToHashSet(StringComparer.Ordinal) : [];
                foreach (var choice in field.Choices)
                    input.Items.Add(choice, selected.Contains(choice.Id));
                return (input, () => input.CheckedItems.Cast<Choice>().Select(choice => choice.Id).ToArray());
            }
            default:
            {
                string text = initial.ValueKind == JsonValueKind.String ? initial.GetString()!
                    : initial.ValueKind == JsonValueKind.Number && initial.TryGetDecimal(out var number)
                        ? number.ToString(CultureInfo.CurrentCulture) : "";
                const int maximum = 16_384;
                var input = new TextBox { Dock = DockStyle.Top, MaxLength = maximum, Text = text.Length <= maximum ? text : "" };
                return (input, () => input.Text);
            }
        }
    }

    private static ComboBox ChoicesInput() => new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
}
