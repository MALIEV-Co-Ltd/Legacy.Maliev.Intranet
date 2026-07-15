using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Distributed;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Stores complete authentication tickets server-side so browser cookies contain only opaque keys.</summary>
public sealed class DistributedTicketStore(IDistributedCache cache, TimeProvider timeProvider) : ITicketStore
{
    private const string Prefix = "legacy-intranet:session:";

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
    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var bytes = await cache.GetAsync(key, cancellationToken);
        return bytes is null ? null : TicketSerializer.Default.Deserialize(bytes);
    }

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
            TicketSerializer.Default.Serialize(ticket),
            new DistributedCacheEntryOptions { AbsoluteExpiration = absoluteExpiration },
            cancellationToken);
    }
}