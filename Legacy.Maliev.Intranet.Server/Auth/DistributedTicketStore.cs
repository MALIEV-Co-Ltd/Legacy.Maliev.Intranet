using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using System.Security.Cryptography;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Stores complete authentication tickets server-side so browser cookies contain only opaque keys.</summary>
public sealed class DistributedTicketStore : ITicketStore
{
    private const string Prefix = "legacy-intranet:session:";
    private const string ProtectionPurpose = "Legacy.Maliev.Intranet.AuthenticationTicketStore.v1";
    private static readonly object SessionKeyItem = new();
    private readonly IDistributedCache cache;
    private readonly TimeProvider timeProvider;
    private readonly IDataProtector protector;

    /// <summary>Initializes an encrypted distributed authentication-ticket store.</summary>
    public DistributedTicketStore(
        IDistributedCache cache,
        TimeProvider timeProvider,
        IDataProtectionProvider dataProtectionProvider)
    {
        this.cache = cache;
        this.timeProvider = timeProvider;
        protector = dataProtectionProvider.CreateProtector(ProtectionPurpose);
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
        await WriteAsync(key, ticket, cancellationToken);
        return key;
    }

    /// <inheritdoc />
    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewAsync(key, ticket, new DefaultHttpContext(), default);

    /// <inheritdoc />
    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        HttpContext httpContext,
        CancellationToken cancellationToken) => WriteAsync(key, ticket, cancellationToken);

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
            if (removeCorrupt && ticket is not null) httpContext.Items[SessionKeyItem] = key;
            return ticket;
        }
        catch (CryptographicException)
        {
            if (removeCorrupt) await cache.RemoveAsync(key, cancellationToken);
            return null;
        }
    }

    // The key comes only from the genuine cookie handler's protected opaque ticket.
    // Fresh reads do not renew or recreate a missing session, and are not cache CAS.
    internal Task<AuthenticationTicket?> RetrieveCurrentAsync(HttpContext context, CancellationToken token) =>
        context.Items.TryGetValue(SessionKeyItem, out var value) && value is string key
            ? ReadAsync(key, context, token, removeCorrupt: false)
            : Task.FromResult<AuthenticationTicket?>(null);

    /// <inheritdoc />
    public Task RemoveAsync(string key) =>
        RemoveAsync(key, new DefaultHttpContext(), default);

    /// <inheritdoc />
    public Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken) =>
        cache.RemoveAsync(key, cancellationToken);

    private Task WriteAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var absoluteExpiration = ticket.Properties.ExpiresUtc ?? timeProvider.GetUtcNow().AddHours(8);
        return cache.SetAsync(
            key,
            protector.Protect(TicketSerializer.Default.Serialize(ticket)),
            new DistributedCacheEntryOptions { AbsoluteExpiration = absoluteExpiration },
            cancellationToken);
    }
}
