using System.Drawing.Imaging;

namespace MyAntigravity;

internal sealed record ImageAttachment(string Path, PromptImage Payload, int Width, int Height, bool IsToolResult = false)
{
    internal string Name => AgentText.Visible(System.IO.Path.GetFileName(Path));

    internal string Details =>
        $"文件名：{Name}\n尺寸：{Width} × {Height} 像素\n格式：{Payload.MimeType}\n大小：{Payload.Length:N0} 字节\n" +
        (IsToolResult ? "来源：官方 ACP 工具结果" : $"位置：{AgentText.Visible(Path)}");

    public override string ToString() => $"{Name}，{Width} × {Height} 像素，{Payload.Length:N0} 字节";

    internal static async Task<ImageAttachment> LoadAsync(string path, int remainingBytes, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length is <= 0 or > PromptImage.MaxFileBytes)
            throw new IOException("单张图片须大于零且不超过 8 MiB。");
        if (file.Length > remainingBytes)
            throw new IOException("图片合计不能超过 12 MiB。");

        var bytes = new byte[(int)file.Length];
        await file.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(bytes, writable: false);
        using var image = Decode(stream);
        var format = InspectImage(image);
        return new ImageAttachment(path, new PromptImage(format, bytes), image.Width, image.Height);
    }

    internal static ImageAttachment FromToolResult(string name, PromptImage payload)
    {
        using var stream = payload.OpenRead();
        using var image = Decode(stream);
        if (InspectImage(image) != payload.MimeType)
            throw new IOException("工具返回的图片格式和数据不一致。");
        return new ImageAttachment(name, payload, image.Width, image.Height, IsToolResult: true);
    }

    private static string InspectImage(Image image)
    {
        if (image.Width <= 0 || image.Height <= 0 || (long)image.Width * image.Height > 40_000_000)
            throw new IOException("图片尺寸超出 4000 万像素的预览限制。");
        var kind = image.RawFormat.Guid;
        return kind == ImageFormat.Png.Guid ? "image/png"
            : kind == ImageFormat.Jpeg.Guid ? "image/jpeg"
            : kind == ImageFormat.Gif.Guid ? "image/gif"
            : kind == ImageFormat.Bmp.Guid ? "image/bmp"
            : kind == ImageFormat.Tiff.Guid ? "image/tiff"
            : throw new IOException("请选择 PNG、JPEG、GIF、BMP 或 TIFF 图片。");
    }

    internal Bitmap Preview()
    {
        using var stream = Payload.OpenRead();
        using var image = Decode(stream);
        var scale = Math.Min(1.0, Math.Min(1200.0 / Width, 900.0 / Height));
        try
        {
            return new Bitmap(image, Math.Max(1, (int)(Width * scale)), Math.Max(1, (int)(Height * scale)));
        }
        catch (Exception error) when (error is ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            throw new IOException("无法生成这张图片的预览。", error);
        }
    }

    private static Image Decode(Stream stream)
    {
        try { return Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false); }
        catch (Exception error) when (error is ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
        {
            throw new IOException("无法读取这张图片。文件可能损坏或格式不受支持。", error);
        }
    }
}
