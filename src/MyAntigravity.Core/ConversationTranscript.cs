using System.Text;
using System.Security.Cryptography;

namespace MyAntigravity;

public enum MessageSpeaker { User, Assistant, FollowUp, Notice }

public sealed record ReadingBookmark(int Index, MessageSpeaker Speaker, string Digest)
{
    public static ReadingBookmark From(ConversationMessage message) =>
        new(message.Index, message.Speaker, Fingerprint(message));

    public int Locate(IReadOnlyList<ConversationMessage> messages)
    {
        if (messages.Count == 0)
            return -1;
        int nearest = Math.Clamp(Index, 0, messages.Count - 1);
        if (Matches(messages[nearest]))
            return nearest;
        for (int offset = 1; offset < messages.Count; offset++)
        {
            if (nearest - offset >= 0 && Matches(messages[nearest - offset]))
                return nearest - offset;
            if (nearest + offset < messages.Count && Matches(messages[nearest + offset]))
                return nearest + offset;
        }
        return nearest;
    }

    private bool Matches(ConversationMessage message) =>
        message.Speaker == Speaker && Fingerprint(message) == Digest;

    private static string Fingerprint(ConversationMessage message) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message.Text.ReplaceLineEndings("\n"))));
}

public sealed class ConversationMessage
{
    private readonly StringBuilder body;

    internal ConversationMessage(int index, MessageSpeaker speaker, string text, string? messageId)
    {
        Index = index;
        Speaker = speaker;
        MessageId = messageId;
        body = new StringBuilder(text);
    }

    public int Index { get; }
    public MessageSpeaker Speaker { get; }
    internal string? MessageId { get; }
    public string Text => body.ToString();
    public string Label => Speaker switch
    {
        MessageSpeaker.User => "你",
        MessageSpeaker.Assistant => "Agent",
        MessageSpeaker.FollowUp => "你（追加指令）",
        _ => "提示"
    };

    public override string ToString()
    {
        const int previewLength = 80;
        int sampleLength = Math.Min(body.Length, 240);
        var sample = body.ToString(0, sampleLength);
        var text = string.Join(" ", sample.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        int end = Math.Min(text.Length, previewLength);
        if (end > 0 && char.IsHighSurrogate(text[end - 1]))
            end--;
        return $"{Label}：{text[..end]}{(end < text.Length || sampleLength < body.Length ? "…" : "")}";
    }

    internal void Append(string text) => body.Append(text);
}

public sealed class ConversationTranscript
{
    private readonly List<ConversationMessage> messages = [];
    private ConversationMessage? current;

    public IReadOnlyList<ConversationMessage> Messages => messages;

    public ConversationMessage Add(MessageSpeaker speaker, string text)
    {
        EndChunks();
        return AddMessage(speaker, text, null);
    }

    public ConversationMessage AppendChunk(MessageSpeaker speaker, string text, string? messageId, out bool started)
    {
        if (current is not null && current.Speaker == speaker && current.MessageId == messageId)
        {
            started = false;
            current.Append(text);
            return current;
        }

        started = true;
        return current = AddMessage(speaker, text, messageId);
    }

    public void EndChunks() => current = null;

    public void Clear()
    {
        EndChunks();
        messages.Clear();
    }

    private ConversationMessage AddMessage(MessageSpeaker speaker, string text, string? messageId)
    {
        var message = new ConversationMessage(messages.Count, speaker, text, messageId);
        messages.Add(message);
        return message;
    }
}
