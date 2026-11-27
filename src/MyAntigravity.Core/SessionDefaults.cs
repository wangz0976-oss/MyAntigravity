namespace MyAntigravity;

public sealed record SessionDefaults(string? Mode = null, string? Model = null, string? Reasoning = null)
{
    public const string AutoPermissionNotice = "官方 CLI 1.5.13–1.5.18 的 ACP Auto 模式有已知缺陷，可能跳过本地拒绝和询问规则直接执行命令。需要确认权限时，请选需审批的模式。按 Ctrl+Shift+X 可请求中止。";

    public static bool CanRememberMode(string? mode) => !string.IsNullOrWhiteSpace(mode) &&
        !string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<SessionSettingChange> Changes()
    {
        var result = new List<SessionSettingChange>();
        if (Model is { Length: > 0 } model)
            result.Add(new(null, model, "模型", "model"));
        if (Mode is { Length: > 0 } mode)
            result.Add(new(null, mode, "权限模式", "mode"));
        if (Model is { Length: > 0 } && Reasoning is { Length: > 0 } reasoning)
            result.Add(new(null, reasoning, "推理等级", "thought_level", RequiredModel: Model));
        return result;
    }

    public static string? ConfirmedValue(IReadOnlyList<SessionOption> options, string category) =>
        CurrentChoice(options, category)?.Id;

    public static string? CurrentName(IReadOnlyList<SessionOption> options, string category) =>
        CurrentChoice(options, category)?.Name;

    public static string ValueName(IReadOnlyList<SessionOption> options, string category, string value) =>
        options.FirstOrDefault(option => option.Category == category)?.Choices.FirstOrDefault(choice => choice.Id == value)?.Name
        ?? AcpDisplayText.LocalizeChoice(category, value, value, null).Name;

    private static Choice? CurrentChoice(IReadOnlyList<SessionOption> options, string category)
    {
        var option = options.FirstOrDefault(item => item.Category == category);
        return option?.Choices.FirstOrDefault(choice => choice.Id == option.Current);
    }
}
