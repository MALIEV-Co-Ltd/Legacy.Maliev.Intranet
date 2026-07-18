namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Legacy-compatible diagnostic event sort values exposed to the employee browser.</summary>
public enum DiagnosticEventSort
{
    LogId_Ascending,
    LogId_Descending,
    LogTimestamp_Ascending,
    LogTimestamp_Descending,
    LogLevel_Ascending,
    LogLevel_Descending,
}

/// <summary>Redacted operational event safe for the authenticated employee browser.</summary>
public sealed record DiagnosticEventItem(
    long Id,
    string Level,
    string Code,
    string Category,
    string Path,
    string CorrelationId,
    DateTimeOffset Timestamp);

/// <summary>Bounded page of redacted operational events.</summary>
public sealed record DiagnosticEventPage(
    IReadOnlyList<DiagnosticEventItem> Items,
    int PageIndex,
    int TotalPages,
    int TotalRecords,
    bool HasNextPage,
    bool HasPreviousPage);
