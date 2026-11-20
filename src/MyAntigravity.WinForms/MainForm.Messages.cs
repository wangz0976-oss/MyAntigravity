namespace MyAntigravity;

internal sealed partial class MainForm
{
    private readonly ConversationTranscript transcript = new();
    private readonly HashSet<int> updatedMessages = [];
    private readonly System.Windows.Forms.Timer messageRefresh = new() { Interval = 350 };
    private readonly ListBox messages = new()
    {
        AccessibleName = "消息列表",
        AccessibleDescription = "上下选择。左右跳到上一轮或下一轮提问。回车阅读全文。Shift 加 F10 打开消息操作。",
        Dock = DockStyle.Fill, IntegralHeight = false
    };
    private readonly ToolStripMenuItem openMessage = Item("阅读所选消息 (&R)");
    private readonly ToolStripMenuItem speakMessage = Item("朗读所选消息 (&S)");
    private readonly ToolStripMenuItem copyMessage = Item("复制所选消息 (&C)");
    private readonly ToolStripMenuItem findMessage = Item("查找消息 (&F)");
    private readonly ToolStripMenuItem readConversation = Item("阅读完整会话 (&L)");

    private ConversationMessage? SelectedMessage => messages.SelectedIndex >= 0
        ? transcript.Messages[messages.SelectedIndex] : null;

    private void WireMessageList()
    {
        openMessage.Click += (_, _) => OpenSelectedMessage();
        speakMessage.Click += (_, _) => SpeakSelectedMessage();
        copyMessage.Click += (_, _) => CopySelectedMessage();
        findMessage.Click += (_, _) => FindMessage();
        readConversation.Click += (_, _) => Dialogs.Text(this, "完整会话",
            string.Join("\n\n", transcript.Messages.Select(message => $"{message.Label}：\n{message.Text}")));
        messages.SelectedIndexChanged += (_, _) => UpdateMessageCommands();
        messages.DoubleClick += (_, _) => OpenSelectedMessage();
        messageRefresh.Tick += (_, _) => RenderMessages();

        var menu = new ContextMenuStrip { AccessibleName = "消息操作" };
        var open = Item("阅读所选消息 (&R)", action: () => { OpenSelectedMessage(); return Task.CompletedTask; });
        var speak = Item("朗读所选消息 (&S)", action: () => { SpeakSelectedMessage(); return Task.CompletedTask; });
        var copy = Item("复制所选消息 (&C)", action: () => { CopySelectedMessage(); return Task.CompletedTask; });
        var find = Item("查找消息 (&F)", action: () => { FindMessage(); return Task.CompletedTask; });
        menu.Items.AddRange([open, speak, copy, find]);
        menu.Opening += (_, _) =>
        {
            open.Enabled = speak.Enabled = copy.Enabled = SelectedMessage is not null;
            find.Enabled = messages.Items.Count > 0;
        };
        AssignListMenu(messages, menu);
        UpdateMessageCommands();
    }

    private void UpdateMessageCommands()
    {
        openMessage.Enabled = speakMessage.Enabled = copyMessage.Enabled = SelectedMessage is not null;
        findMessage.Enabled = readConversation.Enabled = messages.Items.Count > 0;
    }

    private void AddMessage(MessageSpeaker speaker, string text)
    {
        transcript.Add(speaker, text);
        RenderMessages();
    }

    private void RenderMessages()
    {
        messageRefresh.Stop();
        int previousCount = messages.Items.Count;
        int selected = messages.SelectedIndex;
        int top = previousCount > 0 ? messages.TopIndex : 0;
        messages.BeginUpdate();
        try
        {
            for (int i = previousCount; i < transcript.Messages.Count; i++)
                messages.Items.Add(transcript.Messages[i].ToString());
            foreach (int index in updatedMessages.Where(index => index < previousCount))
            {
                var preview = transcript.Messages[index].ToString();
                if (!Equals(messages.Items[index], preview))
                    messages.Items[index] = preview;
            }
            if (messages.Items.Count > 0)
            {
                int keepSelection = Math.Max(0, selected);
                if (messages.SelectedIndex != keepSelection)
                    messages.SelectedIndex = keepSelection;
                if (messages.TopIndex != top)
                    messages.TopIndex = top;
            }
        }
        finally
        {
            messages.EndUpdate();
            updatedMessages.Clear();
        }
        UpdateMessageCommands();
    }

    private void ClearMessages()
    {
        messageRefresh.Stop();
        updatedMessages.Clear();
        messages.Items.Clear();
        transcript.Clear();
        UpdateMessageCommands();
    }

    private void OpenSelectedMessage()
    {
        if (SelectedMessage is not { } message)
            return;
        Dialogs.ReadMessage(this, $"第 {message.Index + 1} 条消息 · {message.Label}", message.Text,
            prompting ? () => message.Text : null);
    }

    private void SpeakSelectedMessage()
    {
        if (SelectedMessage is { } message && !ScreenReaderSpeech.TrySpeak(message.Text))
            OpenSelectedMessage();
    }

    private void CopySelectedMessage()
    {
        if (SelectedMessage is not { } message || message.Text is not { Length: > 0 } text)
            return;
        try
        {
            Clipboard.SetText(text);
            SetStatus("已复制所选消息全文。", record: false, speak: true);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            SetStatus("剪贴板暂不可用，请重试。", record: false, speak: true);
        }
    }

    private void FindMessage()
    {
        if (messages.Items.Count == 0)
            return;
        var entries = transcript.Messages.Take(messages.Items.Count).ToArray();
        var (match, _) = Dialogs.Browse(this, "查找当前会话的消息", "输入消息关键词", "匹配的消息",
            "定位消息 (&R)", entries, message => message.Text, initial: SelectedMessage);
        if (match is not null && match.Index < messages.Items.Count &&
            ReferenceEquals(transcript.Messages[match.Index], match))
        {
            messages.SelectedIndex = match.Index;
            messages.Focus();
        }
    }

    private void MoveToUserMessage(int direction)
    {
        for (int index = messages.SelectedIndex + direction; index >= 0 && index < messages.Items.Count; index += direction)
        {
            if (transcript.Messages[index].Speaker is not (MessageSpeaker.User or MessageSpeaker.FollowUp))
                continue;
            messages.SelectedIndex = index;
            break;
        }
    }
}
