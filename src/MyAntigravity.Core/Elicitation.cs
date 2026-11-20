using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace MyAntigravity;

public enum AnswerKind { Text, Number, Integer, Boolean, Choice, Multiple }

public sealed record ElicitationField(string Key, string Title, string Description, AnswerKind Kind, bool Required,
    IReadOnlyList<Choice> Choices, JsonElement Schema)
{
    public JsonElement Default => Schema.Field("default");

    public bool TryAnswer(object? input, out object? value, out string error)
    {
        value = null;
        error = "";
        bool empty = input is null || input is string { Length: 0 } && Kind != AnswerKind.Choice
            || input is string[] { Length: 0 } && !Required;
        if (empty)
        {
            if (Required)
                error = "请填写这一项。";
            return !Required;
        }
        switch (Kind)
        {
            case AnswerKind.Text when input is string text:
                if (!Within(text.EnumerateRunes().Count(), "minLength", "maxLength"))
                    error = $"文字长度须在 {LimitText("minLength", "maxLength")} 范围内。";
                else if (!ValidFormat(text, Schema.Text("format")))
                    error = "格式与问题要求不符。";
                else
                    value = text;
                break;
            case AnswerKind.Number or AnswerKind.Integer when input is string number:
                const NumberStyles format = NumberStyles.Float;
                if (!decimal.TryParse(number, format, CultureInfo.CurrentCulture, out decimal parsed) &&
                    !decimal.TryParse(number, format, CultureInfo.InvariantCulture, out parsed))
                    error = "请输入数字。";
                else if (Kind == AnswerKind.Integer && decimal.Truncate(parsed) != parsed)
                    error = "请输入整数。";
                else if (!Within(parsed, "minimum", "maximum"))
                    error = $"数字须在 {LimitText("minimum", "maximum")} 范围内。";
                else
                    value = parsed;
                break;
            case AnswerKind.Choice when input is string choice && Choices.Any(item => item.Id == choice):
                value = choice;
                break;
            case AnswerKind.Multiple when input is string[] options &&
                options.Distinct(StringComparer.Ordinal).Count() == options.Length &&
                options.All(option => Choices.Any(item => item.Id == option)):
                if (!Within(options.Length, "minItems", "maxItems"))
                    error = $"选项数量须在 {LimitText("minItems", "maxItems")} 范围内。";
                else
                    value = options;
                break;
            case AnswerKind.Boolean when input is bool boolean:
                value = boolean;
                break;
            default:
                error = "请选择有效的值。";
                break;
        }
        return error.Length == 0;
    }

    private bool Within(decimal value, string minimum, string maximum) =>
        (Limit(minimum) is not { } min || value >= min) && (Limit(maximum) is not { } max || value <= max);

    private decimal? Limit(string key) =>
        Schema.Field(key) is { ValueKind: JsonValueKind.Number } bound && bound.TryGetDecimal(out decimal number)
            ? number : null;

    private string LimitText(string min, string max) => (Limit(min), Limit(max)) switch
    {
        ({ } low, { } high) => $"{low} 到 {high}",
        ({ } low, _) => $"至少 {low}",
        (_, { } high) => $"至多 {high}",
        _ => "指定"
    };

    private static bool ValidFormat(string value, string? format) => format switch
    {
        "email" => MailAddress.TryCreate(value, out var address) && address.Address == value,
        "uri" => System.Uri.TryCreate(value, UriKind.Absolute, out _),
        "date" => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "date-time" => value.Contains('T') && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        _ => true
    };
}

public sealed record ElicitationRequest(string SessionId, string AgentName, string Message, IReadOnlyList<ElicitationField> Fields)
{
    public static ElicitationRequest Parse(JsonElement parameters, string agentName)
    {
        string session = parameters.Text("sessionId") ?? "";
        var schema = parameters.Field("requestedSchema");
        var properties = schema.Field("properties");
        if (parameters.Text("mode") != "form" || schema.Text("type") != "object" ||
            properties.ValueKind != JsonValueKind.Object || properties.EnumerateObject().Take(25).Count() > 24)
            throw Unsupported();
        var required = schema.Field("required").Items().Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
        var fields = new List<ElicitationField>();
        foreach (var property in properties.EnumerateObject())
        {
            var field = property.Value;
            var type = field.Text("type");
            var container = type == "array" ? field.Field("items") : field;
            var choices = ReadChoices(container, type == "array" ? "anyOf" : "oneOf");
            var kind = type switch
            {
                "string" when choices.Count > 0 => AnswerKind.Choice,
                "string" => AnswerKind.Text,
                "boolean" => AnswerKind.Boolean,
                "number" => AnswerKind.Number,
                "integer" => AnswerKind.Integer,
                "array" when choices.Count > 0 => AnswerKind.Multiple,
                _ => throw Unsupported()
            };
            bool isRequired = required.Remove(property.Name);
            if (property.Name.Length == 0 || fields.Any(existing => existing.Key == property.Name))
                throw Unsupported();
            fields.Add(new ElicitationField(property.Name, Display(field.Text("title") ?? property.Name, 240),
                Display(field.Text("description") ?? "", 800), kind, isRequired, choices, field));
        }
        if (required.Count != 0)
            throw Unsupported();
        return new ElicitationRequest(session, Display(agentName, 160), Display(parameters.Text("message") ?? "", 4000), fields);
    }

    private static List<Choice> ReadChoices(JsonElement schema, string composition)
    {
        var values = schema.Field("enum");
        var defined = schema.Field(composition);
        if (values.ValueKind == JsonValueKind.Undefined && defined.ValueKind == JsonValueKind.Undefined)
            return [];
        var choices = new List<Choice>();
        var options = values.ValueKind != JsonValueKind.Undefined ? values : defined;
        if (options.ValueKind != JsonValueKind.Array || options.GetArrayLength() is 0 or > 80)
            throw Unsupported();
        foreach (var item in options.EnumerateArray())
        {
            string? id = values.ValueKind != JsonValueKind.Undefined
                ? item.ValueKind == JsonValueKind.String ? item.GetString() : null
                : item.Text("const");
            if (id is null || id.Length > 400 || choices.Any(choice => choice.Id == id))
                throw Unsupported();
            choices.Add(new Choice(id, Display(item.Text("title") ?? (id.Length == 0 ? "空字符串" : id), 240), null));
        }
        return choices;
    }

    private static string Display(string text, int maximum) => AgentText.Visible(text.Length <= maximum ? text : text[..maximum]);
    private static AcpException Unsupported() => new(-32602, "当前客户端无法显示这个 ACP 表单。请用会话消息提问。");
}

public sealed record ElicitationResponse(string Action, IReadOnlyDictionary<string, object>? Content = null)
{
    public static readonly ElicitationResponse Cancelled = new("cancel");
    public object ToWire() => Action switch
    {
        "accept" when Content is not null => new { action = "accept", content = Content },
        "decline" => new { action = "decline" },
        _ => new { action = "cancel" }
    };
}
