namespace MyAntigravity;

internal sealed partial class MainForm
{
    private readonly System.Windows.Forms.Timer workspaceSave = new() { Interval = 800 };
    private bool workspaceDirty;
    private bool readingReady;
    private bool renderingMessages;

    private void QueueWorkspaceSave(bool changed)
    {
        if (!changed)
            return;
        workspaceDirty = true;
        workspaceSave.Stop();
        workspaceSave.Start();
    }

    private void FlushWorkspace()
    {
        workspaceSave.Stop();
        if (!workspaceDirty)
            return;
        try
        {
            storage.Save(settings);
            workspaceDirty = false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SetStatus("当前无法保存本机草稿或阅读位置。", record: false, speak: true);
        }
    }

    private void RememberDraft((string Project, string Session) owner, ComposerDraft current)
    {
        string text = string.Join(Environment.NewLine,
            current.Sending.Select(pending => pending.Text).Append(current.Text).Where(part => part.Length != 0));
        QueueWorkspaceSave(settings.SaveDraft(owner.Session, owner.Project, text));
    }

    private void RememberReadingPosition()
    {
        if (!readingReady || renderingMessages || draftOwner is not { } owner || SelectedMessage is not { } message)
            return;
        QueueWorkspaceSave(settings.SaveBookmark(owner.Session, owner.Project, ReadingBookmark.From(message)));
    }

    private bool RestoreReadingPosition()
    {
        bool restored = false;
        if (draftOwner is { } owner && settings.FindWorkspace(owner.Session, owner.Project)?.Bookmark is { } bookmark &&
            messages.Items.Count > 0)
        {
            int position = bookmark.Locate(transcript.Messages);
            messages.SelectedIndex = position;
            messages.TopIndex = Math.Max(0, position - 2);
            restored = true;
        }
        readingReady = true;
        return restored;
    }
}
