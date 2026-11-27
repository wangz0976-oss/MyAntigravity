namespace MyAntigravity;

internal sealed partial class MainForm
{
    private sealed class ComposerDraft
    {
        internal string Text = "";
        internal readonly List<SendingText> Sending = [];
        internal readonly List<ImageAttachment> Images = [];
        internal readonly List<TextAttachment> TextFiles = [];
        internal bool HasAttachments => Images.Count != 0 || TextFiles.Count != 0;
        internal bool IsEmpty => Text.Length == 0 && !HasAttachments && Sending.Count == 0;
    }

    private sealed class SendingText(string text)
    {
        internal string Text { get; } = text;
    }

    private readonly Dictionary<(string Project, string Session), ComposerDraft> savedDrafts = [];
    private readonly Dictionary<string, ComposerDraft> projectDrafts = new(StringComparer.OrdinalIgnoreCase);
    private string? draftProject;
    private (string Project, string Session)? draftOwner;
    private ComposerDraft draft = new();
    private readonly FollowUpQueue followUps = new();
    private ComposerDraft? queueDraft;
    private (string Project, string Session)? queueOwner;
    private bool steering;
    private bool interactionOpen;

    private void SwitchDraft(string? targetSession)
    {
        (string Project, string Session)? next = project is not null && targetSession is not null
            ? (project, targetSession)
            : null;
        bool sameProject = string.Equals(draftProject, project, StringComparison.OrdinalIgnoreCase);
        if (next == draftOwner && sameProject)
            return;

        ComposerDraft? transfer = null;
        if (draftOwner is { } previous)
        {
            RememberReadingPosition();
            RememberDraft(previous, draft);
            if (!draft.IsEmpty && !settings.IsSessionRemoved(previous.Session, previous.Project))
                savedDrafts[previous] = draft;
        }
        else if (sameProject || draftProject is null)
            transfer = draft;
        else if (!draft.IsEmpty)
            projectDrafts[draftProject] = draft;
        readingReady = false;

        draftOwner = next;
        draftProject = project;
        if (next is { } key)
        {
            draft = savedDrafts.Remove(key, out var existing) ? existing : new ComposerDraft
            {
                Text = settings.FindWorkspace(key.Session, key.Project)?.Draft ?? ""
            };
        }
        else
            draft = project is not null && projectDrafts.Remove(project, out var pending) ? pending : transfer ?? new ComposerDraft();
        if (transfer is { Text.Length: > 0 } && !ReferenceEquals(draft, transfer))
            draft.Text = draft.Text.Length == 0 ? transfer.Text : draft.Text + Environment.NewLine + transfer.Text;
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
        bool idleProject = !busy && !closing && project is not null;
        bool available = idleProject && draftOwner is not null && sessionId is not null && agent is { Connected: true };
        bool canQueue = prompting && !cancelRequested && !closing && draftOwner == queueOwner &&
            agent is { Connected: true } && followUps.Count < FollowUpQueue.Capacity &&
            !draft.HasAttachments && !string.IsNullOrWhiteSpace(draft.Text);
        send.Text = !prompting ? "发送 (&S)" : $"排队发送（{followUps.Count}）(&S)";
        sendMessageMenu.Text = !prompting ? "发送消息 (&S)" : $"排队发送（{followUps.Count}）(&S)";
        queuedFollowUps.Enabled = withdrawFollowUp.Enabled = followUps.Count > 0 && !closing;
        send.Enabled = sendMessageMenu.Enabled = canQueue || idleProject && (!string.IsNullOrWhiteSpace(draft.Text) || draft.HasAttachments) &&
            (draft.Images.Count == 0 || agent?.CanSendImages == true) &&
            (draft.TextFiles.Count == 0 || agent?.CanSendTextFiles == true);
        steer.Enabled = steerMenu.Enabled = prompting && !steering && !interactionOpen && !cancelRequested && !closing &&
            sessionId is not null && draftOwner is not null && agent is { Connected: true, CanSteer: true } &&
            !string.IsNullOrWhiteSpace(draft.Text);
        string captionText = settings.EnterToSend ? "消息输入 · Enter 发送" : "消息输入 · Ctrl+Enter 发送";
        if (inputCaption.Text != captionText)
            inputCaption.Text = captionText;
        imageMenu.Enabled = imageButton.Enabled = available && (agent?.CanSendImages == true || draft.Images.Count > 0);
        var caption = $"图片附件（{draft.Images.Count}）(&I)";
        if (imageButton.Text != caption)
        {
            imageButton.Text = caption;
            imageButton.AccessibleName = $"图片附件，待发送 {draft.Images.Count} 张";
        }
        textAttachmentsMenu.Enabled = textAttachmentsButton.Enabled = available &&
            (agent?.CanSendTextFiles == true || draft.TextFiles.Count > 0);
        var filesCaption = $"文件附件（{draft.TextFiles.Count}）(&J)";
        if (textAttachmentsButton.Text != filesCaption)
        {
            textAttachmentsButton.Text = filesCaption;
            textAttachmentsButton.AccessibleName = $"文件附件，待发送 {draft.TextFiles.Count} 个";
        }
    }

    private void QueueFollowUp()
    {
        if (cancelRequested || closing || !ReferenceEquals(queueDraft, draft) ||
            agent is not { Connected: true } || draft.HasAttachments)
            return;
        if (!followUps.Enqueue(draft.Text))
        {
            if (followUps.Count == FollowUpQueue.Capacity)
                SetStatus("排队已满。可从消息菜单撤回最后一条。", speak: true);
            return;
        }
        draft.Text = "";
        RefreshComposer();
        SetStatus($"已排队 {followUps.Count} 条。任务正常结束后按顺序发送。", speak: true);
    }

    private sealed record QueuedPrompt(FollowUpQueue.Entry Entry, int Number)
    {
        public override string ToString()
        {
            var text = Entry.Text.Trim().AsSpan();
            int lineEnd = text.IndexOfAny('\r', '\n');
            int length = lineEnd < 0 ? text.Length : lineEnd;
            var preview = AgentText.Visible(text[..Math.Min(90, length)].ToString());
            return $"{Number}. {preview}{(length < text.Length || length > 90 ? "…" : "")}";
        }
    }

    private void ShowQueuedFollowUps()
    {
        var pending = followUps.Entries.Select((entry, index) => new QueuedPrompt(entry, index + 1)).ToArray();
        if (pending.Length == 0)
            return;
        var (selected, withdraw) = Dialogs.Browse(this, "排队消息", "筛选消息", "待发送追问", "查看全文 (&V)",
            pending, item => item.Entry.Text, item => item.Entry.Text, removeText: "撤回所选 (&W)");
        if (selected is null)
            return;
        if (!withdraw)
        {
            Dialogs.Text(this, "待发送追问全文", selected.Entry.Text);
            return;
        }

        if (queueDraft is not { } sending || queueOwner is not { } owner || !followUps.Withdraw(selected.Entry))
        {
            SetStatus("这条追问已发送或已退回草稿。", speak: true);
            return;
        }
        RestoreFailedDraft(sending, owner, selected.Entry.Text, [], []);
        RememberDraft(owner, sending);
        SetStatus($"已把所选追问退回草稿。还有 {followUps.Count} 条排队。", speak: true);
    }

    private void WithdrawFollowUp()
    {
        if (queueDraft is not { } sending || queueOwner is not { } owner ||
            followUps.WithdrawLatest() is not { } text)
            return;
        RestoreFailedDraft(sending, owner, text, [], []);
        RememberDraft(owner, sending);
        SetStatus($"已把最后一条排队消息退回草稿。还有 {followUps.Count} 条排队。", speak: true);
    }

    private void RestoreQueuedDraft()
    {
        var pending = followUps.Drain();
        if (pending.Length == 0 || queueDraft is not { } sending || queueOwner is not { } owner)
            return;
        RestoreFailedDraft(sending, owner, string.Join(Environment.NewLine + Environment.NewLine, pending), [], []);
        RememberDraft(owner, sending);
        if (!closing)
            Log("本轮未正常完成，待发送的追问已退回草稿。", "排队消息已退回草稿", TaskActivityKind.Interaction);
    }

    private async Task SendSteeringAsync()
    {
        if (!steerMenu.Enabled || agent is not { Connected: true } current || sessionId is not { } session ||
            draftOwner is not { } owner)
            return;
        var sending = draft;
        var text = sending.Text;
        var pending = new SendingText(text);
        bool cleared = false;
        steering = true;
        UpdateComposerCommands();
        try
        {
            var request = current.SteerAsync(session, text);
            sending.Sending.Add(pending);
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
            if (!closing)
            {
                sending.Sending.Remove(pending);
                RememberDraft(owner, sending);
            }
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
            SetStatus($"草稿中有 {draft.TextFiles.Count} 个文件附件待发送。");
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
