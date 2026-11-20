using System.Text.Json;

namespace MyAntigravity;

public sealed record ModelQuota(string Group, string Window, double? RemainingFraction, DateTimeOffset? ResetAt)
{
    public static IReadOnlyList<ModelQuota> Parse(string json)
    {
        using var response = JsonDocument.Parse(json);
        var root = response.RootElement;
        var command = root.Field("command");
        var groups = command.Field("data").Field("groups");
        if (root.Text("status") != "SUCCESS" || command.Text("name") != "usage" ||
            groups.ValueKind != JsonValueKind.Array)
            throw new IOException("官方 CLI 未返回结构化模型额度。");

        var quotas = new List<ModelQuota>();
        foreach (var group in groups.Items())
        {
            string? name = group.Text("name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            foreach (var bucket in group.Field("buckets").Items())
            {
                string? window = bucket.Text("window") switch
                {
                    "5h" => "5 小时额度",
                    "weekly" => "每周额度",
                    var other => bucket.Text("name") ?? other ?? bucket.Text("id")
                };
                if (string.IsNullOrWhiteSpace(window))
                    continue;

                var fraction = bucket.Field("remaining_fraction");
                var reset = bucket.Field("reset_time");
                quotas.Add(new ModelQuota(AgentText.Visible(name), AgentText.Visible(window),
                    fraction.ValueKind == JsonValueKind.Number && fraction.TryGetDouble(out double value)
                        && value is >= 0 and <= 1 ? value : null,
                    reset.ValueKind == JsonValueKind.String && reset.TryGetDateTimeOffset(out var time) ? time : null));
            }
        }
        return quotas;
    }

    public override string ToString()
    {
        string remaining = RemainingFraction.HasValue ? $"剩余 {RemainingFraction:P1}" : "剩余比例未提供";
        string reset = ResetAt.HasValue ? $"，重置时间 {ResetAt.Value.ToLocalTime():g}" : "";
        return $"{Group}，{Window}，{remaining}{reset}";
    }
}
