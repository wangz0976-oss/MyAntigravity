namespace MyAntigravity;

internal sealed partial class MainForm
{
    private sealed class ComposerDraft
    {
        internal string Text = "";
        internal readonly List<ImageAttachment> Images = [];
        internal readonly List<TextAttachment> TextFiles = [];
        internal bool HasAttachments => Images.Count != 0 || TextFiles.Count != 0;
        internal bool IsEmpty => Text.Length == 0 && !HasAttachments;
    }

    private readonly Dictionary<(string Project, string Session), ComposerDraft> savedDrafts = [];
    private (string Project, string Session)? draftOwner;
    private ComposerDraft draft = new();
    private bool steering;
    private bool interactionOpen;

    private void SwitchDraft(string? targetSession)
    {
        (string Project, string Session)? next = project is not null && targetSession is not null
            ? (project, targetSession)
            : null;
        if (next == draftOwner)
            return;

        var unassignedText = draftOwner is null ? draft.Text : "";
        if (draftOwner is { } previous && !draft.IsEmpty)
            savedDrafts[previous] = draft;

        draftOwner = next;
        if (next is { } key && savedDrafts.Remove(key, out var existing))
        {
            draft = existing;
            if (unassignedText.Length > 0)
                draft.Text = draft.Text.Length > 0 ? draft.Text + Environment.NewLine + unassignedText : unassignedText;
        }
        else
            draft = new ComposerDraft { Text = unassignedText };
        RefreshComposer();
    }

    private void RefreshComposer()
    {
        input.Text = draft.Text;
        input.Select(input.TextLength, 0);
        UpdateComposerCommands();
    }

    private void UpdateComposerCommands()
    {
        bool available = !busy && !closing && draftOwner is not null && sessionId is not null &&
            agent is { Connected: true };
        send.Enabled = sendMessageMenu.Enabled = available && (!string.IsNullOrWhiteSpace(draft.Text) || draft.HasAttachments) &&
            (draft.Images.Count == 0 || agent?.CanSendImages == true) &&
            (draft.TextFiles.Count == 0 || agent?.CanSendTextFiles == true);
        steer.Enabled = steerMenu.Enabled = prompting && !steering && !interactionOpen && !cancelRequested && !closing &&
            sessionId is not null && draftOwner is not null && agent is { Connected: true, CanSteer: true } &&
            !string.IsNullOrWhiteSpace(draft.Text);
        string sendingKeys = settings.EnterToSend ? "回车发送。Ctrl 加回车换行。" : "Ctrl 加回车发送。回车换行。";
        var inputHelp = prompting && agent is { CanSteer: true }
            ? "任务进行中。输入补充文字后，从消息菜单选择追加指令。"
            : $"{sendingKeys} 可从消息菜单发送或插入官方命令。";
        string captionText = settings.EnterToSend ? "消息输入 · Enter 发送" : "消息输入 · Ctrl+Enter 发送";
        if (inputCaption.Text != captionText)
            inputCaption.Text = captionText;
        if (input.AccessibleDescription != inputHelp)
            input.AccessibleDescription = inputHelp;
        imageMenu.Enabled = imageButton.Enabled = available && (agent?.CanSendImages == true || draft.Images.Count > 0);
        var caption = $"图片附件（{draft.Images.Count}）(&I)";
        if (imageButton.Text != caption)
        {
            imageButton.Text = caption;
            imageButton.AccessibleName = $"图片附件，待发送 {draft.Images.Count} 张";
        }
        textAttachmentsMenu.Enabled = textAttachmentsButton.Enabled = available &&
            (agent?.CanSendTextFiles == true || draft.TextFiles.Count > 0);
        var filesCaption = $"文本与代码附件（{draft.TextFiles.Count}）(&J)";
        if (textAttachmentsButton.Text != filesCaption)
        {
            textAttachmentsButton.Text = filesCaption;
            textAttachmentsButton.AccessibleName = $"文本与代码附件，待发送 {draft.TextFiles.Count} 个";
        }
    }

    private async Task SendSteeringAsync()
    {
        if (!steerMenu.Enabled || agent is not { Connected: true } current || sessionId is not { } session ||
            draftOwner is not { } owner)
            return;
        var sending = draft;
        var text = sending.Text;
        bool cleared = false;
        steering = true;
        UpdateComposerCommands();
        try
        {
            var request = current.SteerAsync(session, text);
            sending.Text = "";
            cleared = true;
            RefreshComposer();
            AddMessage(MessageSpeaker.FollowUp, AgentText.Visible(text));
            SetStatus("追加指令已发送，等待当前任务处理。", speak: true);
            await request;
        }
        catch (Exception error) when (error is AcpException or IOException or InvalidOperationException or OperationCanceledException)
        {
            if (!closing)
            {
                if (cleared)
                    RestoreFailedDraft(sending, owner, text, [], []);
                if (ReferenceEquals(agent, current) && sessionId == session)
                {
                    if (cleared)
                        AddMessage(MessageSpeaker.Notice, "刚才的追加指令未获确认，已保留在草稿。");
                    SetStatus("追加指令未获确认，文字已保留在草稿。请先处理当前审批或提问，再重试。", speak: true);
                }
            }
        }
        finally
        {
            steering = false;
            UpdateComposerCommands();
        }
    }

    private Task ManageImagesAsync() => RunAsync(() =>
    {
        if (project is null || draftOwner is null || agent is not { Connected: true } current)
            return Task.CompletedTask;
        var windowDraft = draft;
        Dialogs.Images(this, windowDraft.Images, project, current.CanSendImages);
        if (ReferenceEquals(windowDraft, draft))
        {
            input.Focus();
            SetStatus($"草稿中有 {draft.Images.Count} 张图片待发送。");
        }
        return Task.CompletedTask;
    });

    private Task ManageTextFilesAsync() => RunAsync(() =>
    {
        if (project is null || draftOwner is null || agent is not { Connected: true } current)
            return Task.CompletedTask;
        var windowDraft = draft;
        Dialogs.TextFiles(this, windowDraft.TextFiles, project, current.CanSendTextFiles);
        if (ReferenceEquals(windowDraft, draft))
        {
            input.Focus();
            SetStatus($"草稿中有 {draft.TextFiles.Count} 个文本与代码附件待发送。");
        }
        return Task.CompletedTask;
    });

    private void RestoreFailedDraft(ComposerDraft failedDraft, (string Project, string Session) owner,
        string text, ImageAttachment[] images, TextAttachment[] files)
    {
        if (text.Length > 0)
            failedDraft.Text = failedDraft.Text.Length == 0 ? text : text + Environment.NewLine + failedDraft.Text;
        failedDraft.Images.InsertRange(0, images);
        failedDraft.TextFiles.InsertRange(0, files);
        if (ReferenceEquals(draft, failedDraft))
            RefreshComposer();
        else
            savedDrafts[owner] = failedDraft;
    }
}
