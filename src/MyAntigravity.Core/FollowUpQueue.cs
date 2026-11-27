namespace MyAntigravity;

/// <summary>Text follow-ups held for the current conversation until each preceding turn completes.</summary>
public sealed class FollowUpQueue
{
    public const int Capacity = 10;
    public sealed class Entry
    {
        internal Entry(string text) => Text = text;
        public string Text { get; }
    }

    private readonly LinkedList<Entry> items = new();
    public int Count => items.Count;
    public IEnumerable<Entry> Entries => items;
    public IEnumerable<string> PendingTexts => items.Select(entry => entry.Text);

    public bool Enqueue(string text)
    {
        if (Count >= Capacity || string.IsNullOrWhiteSpace(text))
            return false;
        items.AddLast(new Entry(text));
        return true;
    }

    public string? TakeNext() => Remove(items.First);

    public string? WithdrawLatest() => Remove(items.Last);

    public bool Withdraw(Entry entry) => items.Remove(entry);

    public string[] Drain()
    {
        var queued = PendingTexts.ToArray();
        items.Clear();
        return queued;
    }

    private string? Remove(LinkedListNode<Entry>? entry)
    {
        if (entry is null)
            return null;
        items.Remove(entry);
        return entry.Value.Text;
    }
}
