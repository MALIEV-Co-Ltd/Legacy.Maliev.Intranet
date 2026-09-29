using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Legacy.Maliev.Intranet.Employees;

/// <summary>Delivers a confirmation challenge without returning its token to the browser.</summary>
public sealed class LegacyEmployeeConfirmationDelivery(
    IHttpClientFactory clients,
    IConfiguration configuration,
    ILogger<LegacyEmployeeConfirmationDelivery> logger)
{
    /// <summary>Requests a single-use challenge and sends it to the new employee.</summary>
    public async Task<bool> SendAsync(string email, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(configuration["EmployeeConfirmation:PublicOrigin"], UriKind.Absolute, out var origin)
            || origin.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(origin.UserInfo)
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment)
            || origin.AbsolutePath != "/")
        {
            logger.LogError("Employee confirmation callback origin is not configured safely");
            return false;
        }

        try
        {
            using var challengeResponse = await clients.CreateClient("employee-confirmation-auth")
                .PostAsJsonAsync("/auth/v1/employee-self-service/email-confirmation/request", new { Email = email }, cancellationToken);
            if (!challengeResponse.IsSuccessStatusCode) return false;
            var challenge = await challengeResponse.Content.ReadFromJsonAsync<ConfirmationChallenge>(cancellationToken);
            if (challenge?.Accepted != true || string.IsNullOrWhiteSpace(challenge.Token)) return false;

            var callback = QueryHelpers.AddQueryString(new Uri(origin, "/Employees/EmailConfirmation").ToString(),
                new Dictionary<string, string?> { ["email"] = email.Trim(), ["token"] = challenge.Token });
            using var notification = await clients.CreateClient("employee-confirmation-notification")
                .PostAsJsonAsync("/notifications/v1/email/NoReply", new
                {
                    To = email.Trim(),
                    Subject = "Confirm your MALIEV Intranet email",
                    Body = $"<p>Confirm your MALIEV employee email address.</p><p><a href=\"{WebUtility.HtmlEncode(callback)}\">Confirm email</a></p><p>This single-use link expires in 24 hours.</p>",
                    ReplyTo = (string?)null,
                    Cc = (IReadOnlyList<string>?)null,
                    Bcc = (IReadOnlyList<string>?)null,
                }, cancellationToken);
            return notification.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning("Employee confirmation delivery was unavailable");
            return false;
        }
    }

    private sealed record ConfirmationChallenge(bool Accepted, string? Token);
}
