namespace MyAntigravity;

public sealed record SessionSettingChange(string? OptionId, string Value, string Label, string? Category = null,
    string? RequiredModel = null);

public sealed record SessionSettingsResult(IReadOnlyList<SessionOption> Options, IReadOnlyList<string> Applied,
    IReadOnlyList<string> Problems);

public static class SessionSettings
{
    public static async Task<SessionSettingsResult> ApplyAsync(IReadOnlyList<SessionOption> options,
        IReadOnlyList<SessionSettingChange> changes,
        Func<SessionOption, Choice, Task<IReadOnlyList<SessionOption>>> setOption)
    {
        var applied = new List<string>();
        var problems = new List<string>();
        foreach (var change in changes)
        {
            if (change.RequiredModel is { } model && SessionDefaults.ConfirmedValue(options, "model") != model)
            {
                problems.Add($"{change.Label}：当前模型与已保存的模型不同，已沿用此模型的推理设置。");
                continue;
            }
            var option = options.FirstOrDefault(item => change.Category is { } category
                ? item.Category == category : item.Id == change.OptionId);
            var choice = option?.Choices.FirstOrDefault(item => item.Id == change.Value);
            if (option is null || choice is null)
            {
                problems.Add($"{change.Label}：该值已不可选，请重新选择。");
                continue;
            }
            if (option.Current == choice.Id)
                continue;
            try
            {
                options = await setOption(option, choice).ConfigureAwait(false);
                if (options.Any(item => item.Id == option.Id && item.Current == choice.Id))
                    applied.Add($"{option.Label}：{choice.Name}");
                else
                    problems.Add($"{option.Label}：CLI 未确认所选值，请核对当前设置。");
            }
            catch (Exception error) when (error is AcpException or IOException or TimeoutException or
                InvalidOperationException or OperationCanceledException or System.Text.Json.JsonException)
            {
                problems.Add($"{option.Label}：{error.Message}");
                problems.Add("后续设置未提交。");
                break;
            }
        }
        return new(options, applied, problems);
    }
}
