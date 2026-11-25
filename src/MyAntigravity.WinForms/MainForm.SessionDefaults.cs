namespace MyAntigravity;

internal sealed partial class MainForm
{
    private string? RememberSessionDefaults(SessionDefaults requested, List<string> problems)
    {
        var active = settings.NewSessionDefaults ?? new();
        var requestedChanges = new List<string>();
        if (requested.Mode is { } mode)
        {
            if (SessionDefaults.ConfirmedValue(options, "mode") == mode)
            {
                active = active with { Mode = mode };
                requestedChanges.Add("权限模式");
            }
            else
                problems.Add("新会话权限默认未保存：CLI 未确认所选模式。");
        }
        if (requested.Model is { } model)
        {
            if (SessionDefaults.ConfirmedValue(options, "model") == model)
            {
                var reasoning = requested.Reasoning == SessionDefaults.ConfirmedValue(options, "thought_level")
                    ? requested.Reasoning : null;
                active = active with { Model = model, Reasoning = reasoning };
                requestedChanges.Add(reasoning is null ? "模型" : "模型和推理等级");
                if (requested.Reasoning is not null && reasoning is null)
                    problems.Add("新会话推理默认未保存：CLI 未确认所选等级。");
            }
            else
                problems.Add("新会话模型默认未保存：CLI 未确认所选模型。");
        }
        if (requestedChanges.Count == 0)
            return null;
        SaveSessionDefaults(active);
        return "已保存新会话默认：" + string.Join("、", requestedChanges) + "。";
    }

    private async Task<string> ApplyNewSessionDefaultsAsync(AcpAgent current, string session)
    {
        if (settings.NewSessionDefaults?.Changes() is not { Count: > 0 } changes)
            return "";
        var result = await current.ApplySettingsAsync(session, options, changes);
        if (closing || sessionId != session || !ReferenceEquals(agent, current) || !current.Connected)
            return "";
        options = result.Options;
        if (result.Problems.Count > 0)
        {
            Log(AgentText.Visible(string.Join("\n", result.Problems)), "新会话默认应用情况", TaskActivityKind.Failure);
            AddMessage(MessageSpeaker.Notice, "有新会话默认未应用。按 Ctrl+L 查看原因。");
        }
        return SessionDefaults.CurrentName(options, "mode") is { } mode
            ? $" 权限模式：{AgentText.Visible(mode)}。" : "";
    }

    private void SaveSessionDefaults(SessionDefaults? next)
    {
        var previous = settings.NewSessionDefaults;
        settings.NewSessionDefaults = next;
        try { storage.Save(settings); }
        catch
        {
            settings.NewSessionDefaults = previous;
            throw;
        }
    }

    private Task ManageSessionDefaultsAsync() => RunAsync(() =>
    {
        var current = settings.NewSessionDefaults ?? new();
        string Describe(string category, string? value) => value is null ? "由官方 CLI 决定"
            : AgentText.Visible(SessionDefaults.ValueName(options, category, value));
        using var form = Dialogs.Window("新会话默认设置", new Size(650, 365));
        var details = ReadableText("本机的新会话默认设置");
        details.Text = $"权限模式：{Describe("mode", current.Mode)}\n模型：{Describe("model", current.Model)}\n" +
            $"推理等级：{Describe("thought_level", current.Reasoning)}\n\n可在 Ctrl+M 保存当前选择。";
        details.Select(0, 0);
        var clearMode = new Button { Text = "清除权限默认 (&P)", AutoSize = true, Enabled = current.Mode is not null };
        var clearModel = new Button { Text = "清除模型和推理默认 (&N)", AutoSize = true, Enabled = current.Model is not null };
        var close = new Button { Text = "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        SessionDefaults? next = null;
        clearMode.Click += (_, _) => { next = current with { Mode = null }; form.DialogResult = DialogResult.OK; };
        clearModel.Click += (_, _) => { next = current with { Model = null, Reasoning = null }; form.DialogResult = DialogResult.OK; };
        form.CancelButton = close;
        Dialogs.Vertical(form, details, Buttons(1, clearMode, clearModel, close));
        form.ActiveControl = details;
        if (form.ShowDialog(this) == DialogResult.OK && next is not null)
        {
            SaveSessionDefaults(next.Changes().Count == 0 ? null : next);
            SetStatus("已清除所选的新会话默认设置。");
        }
        return Task.CompletedTask;
    });
}
