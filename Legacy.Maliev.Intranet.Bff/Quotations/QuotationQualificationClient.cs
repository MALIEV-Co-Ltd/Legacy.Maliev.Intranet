using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Intranet.Bff.Quotations;

/// <summary>Explicit default-off routing for the qualification-only employee boundary.</summary>
public sealed class QuotationQualificationOptions
{
    /// <summary>Gets or sets employee forwarding; authority grants and producer activation are separate.</summary>
    public bool ForwardEmployeeToken { get; set; }
}

/// <summary>Forwards only qualification operations using the server-held employee credential.</summary>
public sealed class QuotationQualificationClient(
    HttpClient httpClient,
    EmployeeSessionService sessions,
    QuotationRequestsProxy legacyRequests,
    IOptions<QuotationQualificationOptions> options)
{
    /// <summary>Reads a request-bound qualification receipt.</summary>
    public Task<HttpResponseMessage> GetQualificationReceiptAsync(int id, HttpContext context, CancellationToken token, IReadOnlyList<string>? requiredPermissions = null) =>
        options.Value.ForwardEmployeeToken
            ? SendAsync(new(HttpMethod.Get, $"/quotationrequests/{id}/qualification-receipt"), LegacyEmployeePermissions.QuotationRequestsRead, context, token, requiredPermissions)
            : legacyRequests.GetQualificationReceiptAsync(id, token);

    /// <summary>Records a request-bound employee transition without changing its seven-field command.</summary>
    public Task<HttpResponseMessage> UpdateQualificationAsync(int id, QuotationQualificationUpdate input, HttpContext context, CancellationToken token) =>
        options.Value.ForwardEmployeeToken
            ? SendAsync(new(HttpMethod.Put, $"/quotationrequests/{id}/qualification") { Content = JsonContent.Create(input) }, LegacyEmployeePermissions.QuotationRequestsUpdate, context, token)
            : legacyRequests.UpdateQualificationAsync(id, input, token);

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string permission, HttpContext context, CancellationToken token, IReadOnlyList<string>? requiredPermissions = null)
    {
        using (request)
        {
            context.Response.Headers.CacheControl = "no-store";
            var acquired = await sessions.AcquireAccessTokenAsync(context, token);
            token.ThrowIfCancellationRequested();
            if (acquired.Status == EmployeeAccessTokenStatus.Throttled)
            {
                var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                if (acquired.RetryAfterSeconds is > 0 and <= 3600)
                    throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(acquired.RetryAfterSeconds.Value));
                return throttled;
            }
            if (acquired.Status == EmployeeAccessTokenStatus.Unavailable)
                return new(HttpStatusCode.ServiceUnavailable);
            if (acquired.Status != EmployeeAccessTokenStatus.Available || string.IsNullOrWhiteSpace(acquired.AccessToken))
                return new(HttpStatusCode.Unauthorized);
            // Cookie policy ran before refresh. Recheck the renewed projection so a
            // permission removed by Auth cannot survive through this request's old principal.
            if (!acquired.Permissions.Contains(permission, StringComparer.Ordinal)
                || (requiredPermissions is not null && requiredPermissions.Any(required => !acquired.Permissions.Contains(required, StringComparer.Ordinal))))
                return new(HttpStatusCode.Forbidden);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acquired.AccessToken);
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, token);
        }
    }
}
