namespace Legacy.Maliev.Intranet.Client.Shared.Components;

public readonly record struct ListToolbarState<TSort>(string? Search, TSort Sort, int PageSize)
    where TSort : struct, Enum
{
    public ListToolbarState<TSort> Normalize() =>
        this with { Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim() };
}

public readonly record struct ListToolbarOption<TSort>(TSort Value, string Label)
    where TSort : struct, Enum;

public readonly record struct ListToolbarRequest<TSort>(
    ListToolbarState<TSort> State,
    ListToolbarChangeReason Reason)
    where TSort : struct, Enum;

public enum ListToolbarChangeReason
{
    SearchDebounced,
    SortChanged,
    PageSizeChanged,
    Cleared,
    Refreshed,
}
