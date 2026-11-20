using System.Text;

namespace MyAntigravity;

public sealed class PromptTextFile
{
    public const int MaxCount = 8;
    public const int MaxFileBytes = 256 * 1024;
    public const int MaxPromptBytes = 1024 * 1024;

    public string Name { get; }
    public string Text { get; }
    public int Length { get; }
    internal string Uri { get; }

    public PromptTextFile(string name, string text)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.IndexOfAny(['/', '\\']) >= 0 ||
            name.Any(char.IsControl))
            throw new ArgumentException("附件名称须为文件名。", nameof(name));
        if (text.Any(ch => char.IsControl(ch) && ch is not ('\r' or '\n' or '\t' or '\f')))
            throw new ArgumentException("文件包含二进制或不支持的控制字符。", nameof(text));
        Length = Encoding.UTF8.GetByteCount(text);
        if (Length is 0 or > MaxFileBytes)
            throw new ArgumentException("文本须大于零且不超过 256 KiB（UTF-8）。", nameof(text));
        Name = name;
        Text = text;
        Uri = $"myantigravity://attachment/{Guid.NewGuid():N}/{System.Uri.EscapeDataString(name)}";
    }

    public static async Task<PromptTextFile> LoadAsync(string path, int remaining, CancellationToken cancellation)
    {
        var maximum = Math.Min(remaining, MaxFileBytes);
        if (maximum <= 0)
            throw new IOException("文本附件的总大小已达到上限。");
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length > MaxFileBytes)
            throw new IOException("单个文本附件不能超过 256 KiB。");
        var bytes = new byte[MaxFileBytes + 1];
        int count = await file.ReadAtLeastAsync(bytes, bytes.Length, false, cancellation)
            .ConfigureAwait(false);
        if (count > MaxFileBytes)
            throw new IOException("单个文本附件不能超过 256 KiB。");
        var attachment = new PromptTextFile(Path.GetFileName(path), Decode(bytes.AsSpan(0, count)));
        if (attachment.Length > maximum)
            throw new IOException("文本附件合计不能超过 1 MiB（UTF-8）。");
        return attachment;
    }

    private static string Decode(ReadOnlySpan<byte> contents)
    {
        try
        {
            if (contents.StartsWith(new byte[] { 0xff, 0xfe }))
                return new UnicodeEncoding(false, true, true).GetString(contents[2..]);
            if (contents.StartsWith(new byte[] { 0xfe, 0xff }))
                return new UnicodeEncoding(true, true, true).GetString(contents[2..]);
            if (contents.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
                contents = contents[3..];
            return new UTF8Encoding(false, true).GetString(contents);
        }
        catch (DecoderFallbackException)
        {
            throw new IOException("请使用 UTF-8 或带 BOM 的 UTF-16 文本文件。");
        }
    }
}
