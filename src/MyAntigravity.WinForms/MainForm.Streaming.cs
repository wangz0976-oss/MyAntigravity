namespace MyAntigravity;

internal sealed partial class MainForm
{
    private enum ReplyPhase { Reasoning, Tools, Reply }

    private ReplyPhase? pendingPhase;
    private ReplyPhase? displayedPhase;
    private ActivityRecord? currentReasoning;
    private bool reasoningChanged;

    private void BeginStreamingReply(string prompt)
    {
        pendingPhase = displayedPhase = null;
        currentReasoning = null;
        reasoningChanged = false;
        transcript.Add(MessageSpeaker.User, prompt);
        transcript.BeginReply();
        RenderMessages();
    }

    private void ReceiveReplyText(string text, string? messageId)
    {
        var previous = transcript.LiveReply!;
        var reply = transcript.AppendReply(text, messageId);
        updatedMessages.Add(previous.Index);
        updatedMessages.Add(reply.Index);
        bool firstText = latestReply.Length == 0;
        latestReply.Append(text);
        readReplyMenu.Enabled = true;
        QueueReplyPhase(ReplyPhase.Reply);
        if (firstText)
            RenderMessages();
    }

    private void ReceiveReasoning(string text)
    {
        if (currentReasoning is null)
            currentReasoning = Log(text, "本轮推理记录", TaskActivityKind.Reasoning);
        else
        {
            currentReasoning.AppendDetail(text);
            reasoningChanged = true;
        }
        QueueReplyPhase(ReplyPhase.Reasoning);
    }

    private void QueueReplyPhase(ReplyPhase phase)
    {
        pendingPhase = phase;
        messageRefresh.Start();
    }

    private void FlushStreamingActivity()
    {
        RefreshRecentActivity();
        if (reasoningChanged && currentReasoning is { } record)
        {
            reasoningChanged = false;
            ActivityUpdated?.Invoke(record);
        }
        if (cancelRequested || interactionOpen || sessionId is null || pendingPhase is not { } phase || displayedPhase == phase)
            return;
        displayedPhase = phase;
        SetStatus(phase switch
        {
            ReplyPhase.Reasoning => "Agent 正在推理。可在任务活动查看本轮推理记录。",
            ReplyPhase.Tools => "Agent 正在处理工具操作。可在任务活动查看进度。",
            _ => "Agent 正在输出正文。可在消息列表阅读已生成的内容。"
        }, record: false);
    }

    private void FinishStreamingReply(string emptyNotice)
    {
        if (transcript.FinishReply(emptyNotice) is not { } reply)
            return;
        updatedMessages.Add(reply.Index);
        pendingPhase = displayedPhase = null;
        if (!closing)
        {
            RenderMessages();
            if (messages.SelectedIndex == reply.Index)
                RememberReadingPosition();
        }
        currentReasoning = null;
        reasoningChanged = false;
    }
}
