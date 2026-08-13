namespace Legacy.Maliev.Intranet.Client.Shared.Components;

public sealed class OperationalTableState<TKey> where TKey : notnull
{
    public bool HasExpandedKey { get; private set; }
    public TKey ExpandedKey { get; private set; } = default!;

    public bool IsExpanded(TKey key) =>
        HasExpandedKey && EqualityComparer<TKey>.Default.Equals(ExpandedKey, key);

    public void Toggle(TKey key)
    {
        if (IsExpanded(key))
        {
            Clear();
            return;
        }

        ExpandedKey = key;
        HasExpandedKey = true;
    }

    public void Clear()
    {
        ExpandedKey = default!;
        HasExpandedKey = false;
    }
}
