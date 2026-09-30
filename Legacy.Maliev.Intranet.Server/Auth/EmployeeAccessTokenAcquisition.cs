namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Server-only credential acquisition outcome; never a browser response contract.</summary>
public enum EmployeeAccessTokenStatus
{
    /// <summary>A server-held employee credential is available.</summary>
    Available,
    /// <summary>The employee session is missing or no longer valid.</summary>
    Unauthenticated,
    /// <summary>Refresh authority is temporarily unavailable; the ticket is preserved.</summary>
    Unavailable,
    /// <summary>Refresh authority is throttled; the ticket is preserved.</summary>
    Throttled,
}

/// <summary>Pairs a server-held credential with its validated current permission projection.</summary>
public sealed record EmployeeAccessTokenAcquisition(
    EmployeeAccessTokenStatus Status,
    string? AccessToken,
    IReadOnlyList<string> Permissions,
    int? RetryAfterSeconds = null);
