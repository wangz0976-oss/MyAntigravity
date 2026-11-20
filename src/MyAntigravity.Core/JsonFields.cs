using System.Text.Json;

namespace MyAntigravity;

internal static class JsonFields
{
    public static JsonElement Field(this JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) ? field : default;

    public static string? Text(this JsonElement value, string name) =>
        value.Field(name) is { ValueKind: JsonValueKind.String } field ? field.GetString() : null;

    public static bool Flag(this JsonElement value, string name) => value.Field(name).ValueKind == JsonValueKind.True;

    public static IEnumerable<JsonElement> Items(this JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];

    public static int Int(this JsonElement value, string name) =>
        value.Field(name) is { ValueKind: JsonValueKind.Number } field && field.TryGetInt32(out var number) ? number : 0;
}
