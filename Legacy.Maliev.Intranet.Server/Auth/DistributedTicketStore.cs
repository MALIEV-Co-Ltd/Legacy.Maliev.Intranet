using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Stores complete authentication tickets server-side so browser cookies contain only opaque keys.</summary>
public sealed class DistributedTicketStore : ITicketStore
{
    private const string Prefix = "legacy-intranet:session:";
    private const string ProtectionPurpose = "Legacy.Maliev.Intranet.AuthenticationTicketStore.v1";
    private static readonly object SessionKeyItem = new();
    private static readonly object FreshObservationItem = new();
    private static readonly object ExplicitRevocationItem = new();
    private static readonly object ConditionalTeardownItem = new();
    private sealed record Observation(string Key, byte[] Ciphertext, DateTimeOffset? Expiry);
    private readonly IDistributedCache cache;
    private readonly TimeProvider timeProvider;
    private readonly IDataProtector protector;
    private readonly ISessionTicketPersistence persistence;

    /// <summary>Initializes an encrypted distributed authentication-ticket store.</summary>
    public DistributedTicketStore(
        IDistributedCache cache,
        TimeProvider timeProvider,
        IDataProtectionProvider dataProtectionProvider,
        ISessionTicketPersistence? sessionPersistence = null)
    {
        this.cache = cache;
        this.timeProvider = timeProvider;
        protector = dataProtectionProvider.CreateProtector(ProtectionPurpose);
        persistence = sessionPersistence ?? (cache is MemoryDistributedCache
            ? new TestingSessionTicketPersistence(cache, timeProvider)
            : throw new InvalidOperationException("An atomic session persistence provider is required."));
    }

    /// <inheritdoc />
    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        StoreAsync(ticket, new DefaultHttpContext(), default);

    /// <inheritdoc />
    public async Task<string> StoreAsync(
        AuthenticationTicket ticket,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var key = Prefix + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        if (!await WriteAsync(key, ticket, null, cancellationToken)) throw new SessionRenewalRejectedException();
        return key;
    }

    /// <inheritdoc />
    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewAsync(key, ticket, new DefaultHttpContext(), default);

    /// <inheritdoc />
    public async Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!httpContext.Items.TryGetValue(SessionKeyItem, out var value) || value is not Observation observation ||
            observation.Key != key || observation.Expiry is null || observation.Expiry <= timeProvider.GetUtcNow() ||
            observation.Expiry != ticket.Properties.ExpiresUtc)
            throw new SessionRenewalRejectedException();
        if (!await WriteAsync(key, ticket, observation.Ciphertext, cancellationToken)) throw new SessionRenewalRejectedException();
    }

    /// <inheritdoc />
    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        RetrieveAsync(key, new DefaultHttpContext(), default);

    /// <inheritdoc />
    public Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        HttpContext httpContext,
        CancellationToken cancellationToken) => ReadAsync(key, httpContext, cancellationToken, removeCorrupt: true);

    private async Task<AuthenticationTicket?> ReadAsync(string key, HttpContext httpContext,
        CancellationToken cancellationToken, bool removeCorrupt)
    {
        var bytes = await cache.GetAsync(key, cancellationToken);
        if (bytes is null)
        {
            return null;
        }

        try
        {
            var ticket = TicketSerializer.Default.Deserialize(protector.Unprotect(bytes));
            if (ticket is not null)
            {
                var observation = new Observation(key, bytes, ticket.Properties.ExpiresUtc);
                if (removeCorrupt) httpContext.Items[SessionKeyItem] = observation;
                else httpContext.Items[FreshObservationItem] = observation;
            }
            return ticket;
        }
        catch (CryptographicException)
        {
            if (removeCorrupt) await persistence.RemoveObservedAsync(key, bytes, cancellationToken);
            return null;
        }
    }

    // The key comes only from the genuine cookie handler's protected opaque ticket.
    // Fresh reads do not renew or recreate a missing session, and are not cache CAS.
    internal Task<AuthenticationTicket?> RetrieveCurrentAsync(HttpContext context, CancellationToken token) =>
        context.Items.TryGetValue(SessionKeyItem, out var value) && value is Observation observation
            ? ReadAsync(observation.Key, context, token, removeCorrupt: false)
            : Task.FromResult<AuthenticationTicket?>(null);

    /// <inheritdoc />
    public Task RemoveAsync(string key) =>
        persistence.RevokeAsync(key, default);

    /// <inheritdoc />
    public Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken) =>
        httpContext.Items.ContainsKey(ExplicitRevocationItem)
            ? persistence.RevokeAsync(key, cancellationToken)
            : RemoveObservedAsync(key, httpContext, cancellationToken);

    internal void MarkExplicitRevocation(HttpContext context) => context.Items[ExplicitRevocationItem] = true;
    internal void MarkConditionalTeardown(HttpContext context) => context.Items[ConditionalTeardownItem] = true;

    private async Task RemoveObservedAsync(string key, HttpContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var value = context.Items.TryGetValue(FreshObservationItem, out var fresh) ? fresh :
            context.Items.TryGetValue(SessionKeyItem, out var initial) ? initial : null;
        var removed = value is Observation observation && observation.Key == key &&
            await persistence.RemoveObservedAsync(key, observation.Ciphertext, token);
        if (!removed && context.Items.ContainsKey(ConditionalTeardownItem))
            throw new SessionConditionalTeardownRejectedException();
    }

    private Task<bool> WriteAsync(string key, AuthenticationTicket ticket, byte[]? expectedCiphertext, CancellationToken cancellationToken)
    {
        var absoluteExpiration = ticket.Properties.ExpiresUtc ?? timeProvider.GetUtcNow().AddHours(8);
        return persistence.WriteAsync(
            key,
            protector.Protect(TicketSerializer.Default.Serialize(ticket)),
            expectedCiphertext,
            absoluteExpiration,
            cancellationToken);
    }
}
