using System.Net;
using System.Net.Http.Headers;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Replaces browser-session credentials with the BFF's short-lived service token.</summary>
public sealed class LegacyServiceAuthenticationHandler(IServiceAccessTokenProvider tokenProvider) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            throw new HttpRequestException("Intranet service authentication is unavailable.", null, HttpStatusCode.ServiceUnavailable);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            tokenProvider.Invalidate(token);
        return response;
    }
}
