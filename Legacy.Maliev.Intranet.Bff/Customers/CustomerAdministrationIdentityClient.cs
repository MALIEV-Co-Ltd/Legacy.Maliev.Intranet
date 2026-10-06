using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Customers;

// Only a local pre-send rejection may establish that no downstream write was attempted.
internal sealed class CustomerAdministrationCredentialRejection(HttpStatusCode status) : HttpResponseMessage(status);

/// <summary>Dedicated single-send Auth customer transport using the acting employee's server-held credential only.</summary>
/// <remarks>Registration must remove inherited retry/resilience handlers and disable redirects. Callers own returned responses.</remarks>
public sealed class CustomerAdministrationIdentityClient(HttpClient client, EmployeeSessionService sessions)
{
    /// <summary>Checks all joined read/write permissions before a coordinator starts any mutation.</summary>
    public async Task<HttpResponseMessage> CheckPermissionsAsync(
        IReadOnlyList<string> permissions, HttpContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(LegacyPresentation.RequestTimeout);
        context.Response.Headers.CacheControl = "no-store";
        var acquired = await sessions.AcquireAccessTokenAsync(context, timeout.Token);
        timeout.Token.ThrowIfCancellationRequested();
        return Failure(acquired, permissions) ?? new(HttpStatusCode.NoContent);
    }

    /// <summary>Reads only the identity selected by the verified legacy customer identifier.</summary>
    public Task<HttpResponseMessage> ReadAsync(int customerId, HttpContext context, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);
        return SendAsync(client, new(HttpMethod.Get, $"/auth/v1/customer-identities/{customerId}"),
            "legacy-auth.customer-identities.read", context, token);
    }

    /// <summary>Forwards the original strong Auth version without fallback, version replacement or application replay.</summary>
    public Task<HttpResponseMessage> UpdateAsync(int customerId, CustomerAdministrationIdentityUpdate input,
        string version, HttpContext context, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);
        ArgumentNullException.ThrowIfNull(input);
        if (!CustomerAdministrationVersion.IsIdentity(version))
            throw new ArgumentException("A captured customer identity version is required.", nameof(version));
        var request = new HttpRequestMessage(HttpMethod.Put, $"/auth/v1/customer-identities/{customerId}/versioned")
        {
            Content = JsonContent.Create(input),
        };
        request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(version));
        return SendAsync(client, request, "legacy-auth.customer-identities.update", context, token);
    }

    /// <summary>Shares bounded, rechecked employee credential transport with the joined profile client; consumes the request.</summary>
    /// <remarks>The supplied client must have no inherited retry handlers or redirects. Returned responses belong to the caller.</remarks>
    public async Task<HttpResponseMessage> SendAsync(HttpClient target, HttpRequestMessage request, string permission,
        HttpContext context, CancellationToken token, string? additionalPermission = null)
    {
        using (request)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            timeout.CancelAfter(LegacyPresentation.RequestTimeout);
            context.Response.Headers.CacheControl = "no-store";
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            var acquired = await sessions.AcquireAccessTokenAsync(context, timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            if (Failure(acquired, additionalPermission is null ? [permission] : [permission, additionalPermission]) is { } failure) return failure;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acquired.AccessToken);
            return await target.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
        }
    }

    private static HttpResponseMessage? Failure(EmployeeAccessTokenAcquisition acquired, IReadOnlyList<string> permissions)
    {
        if (acquired.Status == EmployeeAccessTokenStatus.Throttled)
        {
            var response = new CustomerAdministrationCredentialRejection(HttpStatusCode.TooManyRequests);
            if (acquired.RetryAfterSeconds is > 0 and <= 3600)
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(acquired.RetryAfterSeconds.Value));
            return response;
        }
        if (acquired.Status == EmployeeAccessTokenStatus.Unavailable)
            return new CustomerAdministrationCredentialRejection(HttpStatusCode.ServiceUnavailable);
        if (acquired.Status != EmployeeAccessTokenStatus.Available || string.IsNullOrWhiteSpace(acquired.AccessToken))
            return new CustomerAdministrationCredentialRejection(HttpStatusCode.Unauthorized);
        return permissions.Any(permission => !acquired.Permissions.Contains(permission, StringComparer.Ordinal))
            ? new CustomerAdministrationCredentialRejection(HttpStatusCode.Forbidden) : null;
    }
}
