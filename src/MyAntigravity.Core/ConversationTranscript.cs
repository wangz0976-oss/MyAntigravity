using System.Text;

namespace MyAntigravity;

public enum MessageSpeaker { User, Assistant, FollowUp, Notice }

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
