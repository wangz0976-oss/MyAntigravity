namespace MyAntigravity;

internal sealed partial class MainForm
{
    private Task StartCustomAgentAsync() => RunAsync(async () =>
    {
        if (project is null)
            return;
        string directory = WorkingDirectory();
        var names = await Cli.ListAgentsAsync(RequireCli(), directory);
        var local = await Task.Run(() => SkillCatalog.ReadProjectAgents(directory));
        var presets = local.Concat(names.Select(name => new AgentPreset(name, "官方 CLI", "")))
            .DistinctBy(item => item.Name).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (closing)
            return;
        if (presets.Length == 0)
        {
            Dialogs.Text(this, "自定义代理", "官方 CLI 和当前项目的代理目录没有发现自定义代理。");
            return;
        }

        var selected = Dialogs.Search(this, "使用自定义代理新建会话", "按名称、来源或说明筛选", "可用的自定义代理",
            "新建会话 (&N)", presets, item => $"{item.Name} {item.Source} {item.Description}",
            item => $"{item}\n{AgentText.Visible(item.Description)}");
        if (selected is not null)
            await StartSessionCoreAsync(null, customAgent: selected.Name);
    });

    private Task SelectOrchestratorAsync() => RunAsync(async () =>
    {
        if (agent is not { Connected: true } current || sessionId is not { } session ||
            options.FirstOrDefault(option => option.Category == "model") is not { } model)
            return;

        var choices = model.Choices.Where(choice => choice.IsOrchestrator).ToArray();
        var selected = Dialogs.Search(this, "选择编排模型", "按模型名称筛选", "官方编排模型",
            "应用到当前会话 (&A)", choices, choice => choice.Name,
            choice => $"{AgentText.Visible(choice.Name)}\n{AgentText.Visible(choice.Description ?? "")}\n" +
                "编排模型负责规划和复核。执行模型由官方 CLI 分配。");
        if (selected is null || sessionId != session || !ReferenceEquals(agent, current))
            return;

        var result = await current.ApplySettingsAsync(session, options,
            [new(model.Id, selected.Id, "编排模型")]);
        if (closing || sessionId != session || !ReferenceEquals(agent, current))
            return;
        options = result.Options;
        if (result.Problems.Count > 0)
        {
            ReportError(string.Join("\n", result.Problems));
            return;
        }
        SetStatus($"当前会话编排模型：{AgentText.Visible(selected.Name)}。", speak: true);
    });
}
