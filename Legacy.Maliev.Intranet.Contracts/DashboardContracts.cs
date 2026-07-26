namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>One permission-scoped dashboard card projected by the legacy BFF.</summary>
public sealed record LegacyDashboardCard(
    string Key,
    string Label,
    int Count,
    string NavigateTo);

/// <summary>
/// Browser-safe legacy dashboard projection. Counts are fetched only for the
/// employee permissions carried by the server-side session; downstream errors
/// are isolated in <see cref="DegradedSources"/>.
/// </summary>
public sealed record LegacyDashboardSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<LegacyDashboardCard> Cards,
    IReadOnlyList<string> DegradedSources);
