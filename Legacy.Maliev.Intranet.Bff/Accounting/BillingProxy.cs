using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

/// <summary>Single-send billing reads/previews with only the acting employee's server-held credential.</summary>
public sealed class BillingProxy(HttpClient client, EmployeeSessionService sessions)
{
    private static readonly JsonSerializerOptions ProducerJson = new() { PropertyNamingPolicy = null };

    /// <summary>Reads one fixed customer/account resource; no browser-supplied URL or service-token fallback.</summary>
    public Task<HttpResponseMessage> ReadAsync(int customerId, Guid accountId, HttpContext context, CancellationToken token) =>
        SendAsync(new(HttpMethod.Get, Path(customerId, accountId)), context, token);

    /// <summary>Forwards the captured revision and financial preview without identity or evidence overrides.</summary>
    public Task<HttpResponseMessage> PreviewAsync(int customerId, Guid accountId, BillingPreviewRequest input,
        HttpContext context, CancellationToken token) => SendAsync(new(HttpMethod.Post, Path(customerId, accountId) + "/preview")
        { Content = JsonContent.Create(input, options: ProducerJson) }, context, token);

    private static string Path(int customerId, Guid accountId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);
        if (accountId == Guid.Empty) throw new ArgumentException("Account identity is required.", nameof(accountId));
        return $"/v1/customers/{customerId}/billing/accounts/{accountId:D}";
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpContext context, CancellationToken token)
    {
        using (request)
        {
            context.Response.Headers.CacheControl = "no-store";
            var acquired = await sessions.AcquireAccessTokenAsync(context, token);
            token.ThrowIfCancellationRequested();
            if (acquired.Status == EmployeeAccessTokenStatus.Throttled) return new(HttpStatusCode.TooManyRequests);
            if (acquired.Status == EmployeeAccessTokenStatus.Unavailable) return new(HttpStatusCode.ServiceUnavailable);
            if (acquired.Status != EmployeeAccessTokenStatus.Available || string.IsNullOrWhiteSpace(acquired.AccessToken))
                return new(HttpStatusCode.Unauthorized);
            if (!acquired.Permissions.Contains(LegacyEmployeePermissions.AccountingRead, StringComparer.Ordinal))
                return new(HttpStatusCode.Forbidden);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acquired.AccessToken);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        }
    }
}
