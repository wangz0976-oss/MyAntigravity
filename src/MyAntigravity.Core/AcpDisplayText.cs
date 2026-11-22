namespace MyAntigravity;

internal static class AcpDisplayText
{
    public static string? OptionCategory(string id) => id switch
    {
        "model" => "model",
        "mode" => "mode",
        "thought_level" or "thinking_level" => "thought_level",
        "speed" => "speed",
        _ => null
    };

    public static string OptionLabel(string? category, string fallback) => category switch
    {
        "model" => "模型",
        "mode" => "权限模式",
        "thought_level" => "推理等级",
        "speed" => "响应速度",
        _ => fallback
    };

    public static string? OptionDescription(string? category, string? fallback) => category switch
    {
        "model" => "选择当前会话使用的模型。更换模型可能改变可选的推理等级。",
        "mode" => "决定当前会话遇到文件修改或命令时怎样审批。具体操作由 Antigravity CLI 按其权限规则判断。",
        "thought_level" => "调整当前模型回答前的推理深度。可选等级由官方 CLI 提供。",
        _ => fallback
    };

    public static Choice LocalizeChoice(string? category, string id, string name, string? description)
    {
        (string? label, string? explanation) = (category, id) switch
        {
            ("mode", "default") => ("默认（按规则询问）",
                "根据官方 CLI 的规则处理文件修改和命令。需要确认的操作会暂停等待你决定。"),
            ("mode", "accept-edits") => ("允许工作区文件编辑",
                "自动允许工作区内的文件编辑。终端命令等其他操作仍按 CLI 的权限规则审批。"),
            ("mode", "auto") => ("自动（按风险审批）",
                "自动允许工作区内的文件编辑。终端命令通常在沙箱内运行，高风险或不可逆操作仍需确认。"),
            ("mode", "plan") => ("规划模式",
                "先了解项目并拟定实施计划，再按官方 CLI 的规划流程继续。"),
            ("thought_level", "minimal") => ("最低", null),
            ("thought_level", "low") => ("低", null),
            ("thought_level", "medium") => ("中", null),
            ("thought_level", "high") => ("高", null),
            ("thought_level", "extra" or "xhigh") => ("超高", null),
            ("thought_level", "max") => ("最大", null),
            ("speed", "normal" or "standard") => ("标准", null),
            ("speed", "fast") => ("快速", null),
            _ => (null, null)
        };
        return new Choice(id, label ?? name, explanation ?? description);
    }

    public static string ToolKind(string value) => value switch
    {
        "read" => "读取",
        "edit" => "编辑",
        "delete" => "删除",
        "move" => "移动",
        "search" => "搜索",
        "execute" => "执行命令",
        "think" => "推理",
        "fetch" => "获取内容",
        "switch_mode" => "切换模式",
        "other" => "其他",
        _ => value
    };

    public static string ToolStatus(string value) => value switch
    {
        "pending" => "等待执行",
        "in_progress" => "执行中",
        "completed" => "已完成",
        "failed" => "失败",
        "cancelled" => "已取消",
        _ => value
    };
}
