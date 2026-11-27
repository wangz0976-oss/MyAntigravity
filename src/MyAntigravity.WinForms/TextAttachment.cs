namespace MyAntigravity;

internal sealed record TextAttachment(string Path, PromptTextFile Payload)
{
    internal string Name => AgentText.Visible(Payload.Name);

    internal string Details => $"文件名：{Name}\n文字大小：{Payload.Length:N0} 字节（UTF-8）\n" +
        (DocumentText.Supports(Path) ? "内容：从原文件提取的文字。\n" : "") + $"\n{AgentText.Visible(Payload.Text)}";

    public override string ToString() => $"{Name} · {Payload.Length:N0} 字节";

    internal static async Task<TextAttachment> LoadAsync(string path, int remaining, CancellationToken cancellation) =>
        new(path, await (DocumentText.Supports(path)
            ? DocumentText.LoadAsync(path, remaining, cancellation)
            : PromptTextFile.LoadAsync(path, remaining, cancellation)).ConfigureAwait(false));
}
