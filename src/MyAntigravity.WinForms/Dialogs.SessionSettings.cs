namespace MyAntigravity;

internal static partial class Dialogs
{
    internal sealed record SessionSettingsSelection(IReadOnlyList<SessionSettingChange> Changes, SessionDefaults Defaults, bool ClearProjectMode);

    internal static SessionSettingsSelection? SessionSettings(Form owner, IReadOnlyList<SessionOption> options, string? projectMode)
    {
        using var form = Window("模型、权限模式与推理设置", new Size(690, 555));
        var optionsPanel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, TabIndex = 0 };
        scroll.Controls.Add(optionsPanel);
        var details = MainForm.ReadableText("选项说明");
        details.TabIndex = 3;
        var inputs = new List<(SessionOption Option, ComboBox Values)>();

        void AddField(string label, SessionOption? option, string? unavailable = null)
        {
            int row = optionsPanel.RowCount;
            optionsPanel.RowCount += 2;
            optionsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            optionsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            optionsPanel.Controls.Add(MainForm.Caption(label), 0, row);
            var values = new ComboBox
            {
                AccessibleName = label, Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList,
                TabIndex = row, Enabled = option is not null
            };
            optionsPanel.Controls.Add(values, 0, row + 1);
            if (option is null)
            {
                values.Items.Add(unavailable ?? "当前会话未提供此设置。");
                values.SelectedIndex = 0;
                return;
            }

            inputs.Add((option, values));
            values.Items.AddRange(option.Choices.Cast<object>().ToArray());
            values.SelectedItem = option.Choices.FirstOrDefault(choice => choice.Id == option.Current);
            void ShowDescription()
            {
                var chosen = values.SelectedItem as Choice;
                string current = option.Choices.FirstOrDefault(choice => choice.Id == option.Current)?.Name
                    ?? option.Current ?? "未报告";
                details.Text = AgentText.Visible(string.Join("\n\n", new[] { label, $"当前值：{current}",
                    chosen is not null && chosen.Id != option.Current ? $"待应用：{chosen.Name}" : null,
                    option.Description, chosen?.Description, option.Category == "mode" ? SessionDefaults.AutoPermissionNotice : null }
                    .Where(text => !string.IsNullOrWhiteSpace(text))));
                details.Select(0, 0);
            }
            values.Enter += (_, _) => ShowDescription();
            values.SelectedIndexChanged += (_, _) => ShowDescription();
        }

        AddField("模型", options.FirstOrDefault(option => option.Category == "model"));
        AddField("权限模式", options.FirstOrDefault(option => option.Category == "mode"));
        AddField("推理等级", options.FirstOrDefault(option => option.Category == "thought_level"),
            "当前模型未提供推理等级。");
        foreach (var option in options.Where(option => !inputs.Any(input => input.Option.Id == option.Id)).ToArray())
            AddField(AgentText.Visible(option.Label), option);

        var rememberMode = new CheckBox { Text = "为此项目的新会话记住所选权限模式 (&P)", AutoSize = true, TabIndex = 1,
            Enabled = inputs.Any(input => input.Option.Category == "mode"), Checked = projectMode is not null,
            AccessibleDescription = "当前版本可保存经 CLI 确认的权限模式。Auto 模式不保存为新会话偏好。" };
        var rememberModel = new CheckBox { Text = "把所选模型和推理等级设为新会话默认 (&N)", AutoSize = true, TabIndex = 2,
            Enabled = inputs.Any(input => input.Option.Category == "model") };
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        body.Controls.Add(MainForm.Caption("更换模型后会核对推理等级，选完按回车统一应用。"), 0, 0);
        body.Controls.Add(scroll, 0, 1);
        body.Controls.Add(rememberMode, 0, 2);
        body.Controls.Add(rememberModel, 0, 3);
        body.Controls.Add(MainForm.Caption("选项说明"), 0, 4);
        body.Controls.Add(details, 0, 5);
        var apply = new Button { Text = "确定 (&A)", AutoSize = true, DialogResult = DialogResult.OK };
        var close = new Button { Text = "取消 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        form.AcceptButton = apply;
        form.CancelButton = close;
        Vertical(form, body, MainForm.Buttons(1, apply, close));
        form.ActiveControl = inputs.Count > 0 ? inputs[0].Values : details;
        if (form.ShowDialog(owner) != DialogResult.OK)
            return null;

        var selected = inputs.Select(input => (input.Option, Choice: input.Values.SelectedItem as Choice)).ToArray();
        string? Value(string category) => selected.FirstOrDefault(input => input.Option.Category == category).Choice?.Id;
        var changes = selected
            .Where(input => input.Choice is not null && input.Option.Current != input.Choice.Id)
            .Select(input => new SessionSettingChange(input.Option.Id, input.Choice!.Id, input.Option.Label)).ToArray();
        bool modeChanged = selected.Any(input => input.Option.Category == "mode" &&
            input.Choice is not null && input.Option.Current != input.Choice.Id);
        var rememberedMode = rememberMode.Enabled && rememberMode.Checked && (projectMode is null || modeChanged)
            ? Value("mode") : null;
        return new(changes, new SessionDefaults(rememberedMode,
            rememberModel.Checked ? Value("model") : null, rememberModel.Checked ? Value("thought_level") : null),
            rememberMode.Enabled && !rememberMode.Checked && projectMode is not null);
    }
}
