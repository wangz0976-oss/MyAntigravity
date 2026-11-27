namespace MyAntigravity;

internal sealed partial class MainForm
{
    private string? RememberSessionDefaults(string directory, SessionDefaults requested, bool clearProjectMode, List<string> problems)
    {
        var active = settings.NewSessionDefaults ?? new();
        var requestedChanges = new List<string>();
        if (requested.Mode is { } mode)
        {
            if (SessionDefaults.ConfirmedValue(options, "mode") == mode)
            {
                if (settings.PermissionForProject(directory) != mode)
                {
                    SaveProjectPermission(directory, mode);
                    requestedChanges.Add("当前项目权限模式");
                }
            }
            else
                problems.Add("新会话权限默认未保存：CLI 未确认所选模式。");
        }
        else if (clearProjectMode)
        {
            SaveProjectPermission(directory, null);
            requestedChanges.Add("已清除当前项目权限偏好");
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
        if (active != (settings.NewSessionDefaults ?? new()))
            SaveSessionDefaults(active);
        return string.Join("、", requestedChanges) + "，新会话偏好已更新。";
    }

    private async Task<string> ApplyNewSessionDefaultsAsync(AcpAgent current, string session)
    {
        if (project is null)
            return "";
        var changes = settings.DefaultsForProject(project).Changes();
        if (changes.Count == 0)
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

    private void SaveProjectPermission(string directory, string? mode)
    {
        var previous = settings.ProjectPermissions.ToList();
        settings.SetProjectPermission(directory, mode);
        try { storage.Save(settings); }
        catch
        {
            settings.ProjectPermissions = previous;
            throw;
        }
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
        var directory = project;
        var projectMode = directory is null ? null : settings.PermissionForProject(directory);
        using var form = Dialogs.Window("新会话默认设置", new Size(650, 365));
        var details = ReadableText("本机的新会话默认设置");
        details.Text = $"当前项目权限：{(projectMode is null ? "沿用全局默认" : Describe("mode", projectMode))}\n" +
            $"全局权限默认（旧版保存）：{Describe("mode", current.Mode)}\n模型：{Describe("model", current.Model)}\n" +
            $"推理等级：{Describe("thought_level", current.Reasoning)}\n\n可在 Ctrl+M 保存当前选择。";
        details.Select(0, 0);
        var clearMode = new Button { Text = "清除全局权限默认 (&P)", AutoSize = true, Enabled = current.Mode is not null };
        var clearModel = new Button { Text = "清除模型和推理默认 (&N)", AutoSize = true, Enabled = current.Model is not null };
        var close = new Button { Text = "关闭 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        var clearProject = new Button { Text = "清除当前项目权限默认 (&J)", AutoSize = true, Enabled = projectMode is not null };
        bool clearProjectMode = false;
        clearProject.Click += (_, _) => { clearProjectMode = true; form.DialogResult = DialogResult.OK; };
        SessionDefaults? next = null;
        clearMode.Click += (_, _) => { next = current with { Mode = null }; form.DialogResult = DialogResult.OK; };
        clearModel.Click += (_, _) => { next = current with { Model = null, Reasoning = null }; form.DialogResult = DialogResult.OK; };
        form.CancelButton = close;
        Dialogs.Vertical(form, details, Buttons(1, clearProject, clearMode, clearModel, close));
        form.ActiveControl = details;
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            if (clearProjectMode && directory is not null)
                SaveProjectPermission(directory, null);
            else if (next is not null)
                SaveSessionDefaults(next.Changes().Count == 0 ? null : next);
            SetStatus("已清除所选的新会话默认设置。");
        }
        return Task.CompletedTask;
    });
}
