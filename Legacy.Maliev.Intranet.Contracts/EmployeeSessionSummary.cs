namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Browser-safe projection of the current employee session.</summary>
public sealed record EmployeeSessionSummary(
    bool IsAuthenticated,
    string? EmployeeId,
    string? DisplayName,
    IReadOnlyList<string> Roles,
    string? CsrfToken);
