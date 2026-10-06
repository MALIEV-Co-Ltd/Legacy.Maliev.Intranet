namespace Legacy.Maliev.Intranet.Client.Shared.Infrastructure;

public sealed record LookupSearchPage<T>(IReadOnlyList<T> Items, string DatasetVersion, bool HasMore, string? NextCursor);
