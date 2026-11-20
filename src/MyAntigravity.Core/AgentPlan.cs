using System.Text.Json;

namespace MyAntigravity;

public sealed record PlanStep(string Content, string Priority, string Status)
{
    public static IReadOnlyList<PlanStep> Parse(JsonElement update) => update.Field("entries").Items()
        .Where(entry => entry.Text("content") is not null)
        .Select(entry => new PlanStep(entry.Text("content")!, entry.Text("priority") ?? "", entry.Text("status") ?? ""))
        .ToArray();

    public override string ToString()
    {
        var status = Status switch
        {
            "pending" => "待处理",
            "in_progress" => "进行中",
            "completed" => "已完成",
            _ => "状态未知"
        };
        var priority = Priority switch
        {
            "high" => "高优先级",
            "medium" => "中优先级",
            "low" => "低优先级",
            _ => "优先级未知"
        };
        return $"{status}，{priority}。{AgentText.Visible(Content)}";
    }
}
