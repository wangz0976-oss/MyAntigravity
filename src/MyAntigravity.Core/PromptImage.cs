namespace MyAntigravity;

public sealed class PromptImage
{
    public const int MaxCount = 8;
    public const int MaxFileBytes = 8 * 1024 * 1024;
    public const int MaxPromptBytes = 12 * 1024 * 1024;

    private readonly byte[] content;
    public string MimeType { get; }
    public int Length => content.Length;
    internal ReadOnlyMemory<byte> Data => content;

    public PromptImage(string mimeType, ReadOnlySpan<byte> bytes)
    {
        if (mimeType is not ("image/png" or "image/jpeg" or "image/gif" or "image/bmp" or "image/tiff"))
            throw new ArgumentException("不支持此图片格式。", nameof(mimeType));
        if (bytes.Length is 0 or > MaxFileBytes)
            throw new ArgumentException("图片大小须大于零且不超过 8 MiB。", nameof(bytes));
        MimeType = mimeType;
        content = bytes.ToArray();
    }

    public Stream OpenRead() => new MemoryStream(content, writable: false);
}
