using System.Text;

namespace MyAntigravity;

public static class ConversationExport
{
    public static string Format(IEnumerable<ConversationMessage> messages) =>
        string.Join(Environment.NewLine + Environment.NewLine,
            messages.Select(message => $"{message.Label}：{Environment.NewLine}{message.Text.ReplaceLineEndings(Environment.NewLine)}"));

    public static async Task SaveAsync(string destination, string snapshot)
    {
        var fullPath = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new IOException("请选择文件名。");
        var temporary = Path.Combine(directory, $".myantigravity-export-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, snapshot, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
                .ConfigureAwait(false);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
