namespace MyAntigravity;

internal sealed class ComposerTextBox : TextBox
{
    internal bool IsComposing { get; private set; }

    protected override void WndProc(ref Message message)
    {
        const int imeStartComposition = 0x010D;
        const int imeEndComposition = 0x010E;
        if (message.Msg == imeStartComposition)
            IsComposing = true;
        base.WndProc(ref message);
        if (message.Msg == imeEndComposition)
            IsComposing = false;
    }
}
