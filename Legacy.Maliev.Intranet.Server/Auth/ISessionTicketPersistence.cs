namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Atomic mutation boundary for encrypted employee sessions, not general cache operations.</summary>
public interface ISessionTicketPersistence
{
    /// <summary>Creates a new key or replaces only an observed, unexpired ciphertext generation.</summary>
    Task<bool> WriteAsync(string key, byte[] ciphertext, byte[]? expectedCiphertext, DateTimeOffset expiry, CancellationToken token);
    /// <summary>Deletes only the ciphertext generation observed by implicit teardown.</summary>
    Task<bool> RemoveObservedAsync(string key, byte[] expectedCiphertext, CancellationToken token);
    /// <summary>Unconditionally revokes an explicit logout's opaque session key.</summary>
    Task RevokeAsync(string key, CancellationToken token);
}

/// <summary>Signals that a renewal did not commit; credentials must not be forwarded as if it did.</summary>
public sealed class SessionRenewalRejectedException() : InvalidOperationException("The observed employee session could not be renewed.");

/// <summary>Stops stale refresh teardown before the cookie handler deletes a peer's browser cookie.</summary>
public sealed class SessionConditionalTeardownRejectedException() : InvalidOperationException("The observed employee session could not be removed.");
