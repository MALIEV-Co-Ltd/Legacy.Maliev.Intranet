using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Employees;

/// <summary>Forwards employee confirmation and recovery actions to AuthService.</summary>
public sealed class EmployeeRecoveryAuthProxy(HttpClient httpClient)
{
    /// <summary>Requests an employee password-reset challenge for trusted BFF delivery.</summary>
    public Task<HttpResponseMessage> RequestPasswordResetAsync(
        EmployeeRecoveryEmailRequest request,
        CancellationToken cancellationToken) =>
        httpClient.PostAsJsonAsync(
            "/auth/v1/employee-self-service/password-reset/request",
            request,
            cancellationToken);

    /// <summary>Completes an employee password reset.</summary>
    public Task<HttpResponseMessage> CompletePasswordResetAsync(
        EmployeePasswordResetRequest request,
        CancellationToken cancellationToken) =>
        httpClient.PostAsJsonAsync(
            "/auth/v1/employee-self-service/password-reset/complete",
            new { request.Email, request.Token, request.Password },
            cancellationToken);

    /// <summary>Completes an employee email confirmation.</summary>
    public Task<HttpResponseMessage> CompleteEmailConfirmationAsync(
        EmployeeEmailConfirmationRequest request,
        CancellationToken cancellationToken) =>
        httpClient.PostAsJsonAsync(
            "/auth/v1/employee-self-service/email-confirmation/complete",
            request,
            cancellationToken);
}

/// <summary>Sends employee recovery messages through NotificationService.</summary>
public sealed class EmployeeRecoveryNotificationProxy(HttpClient httpClient)
{
    /// <summary>Sends a provider-independent no-reply password recovery message.</summary>
    public Task<HttpResponseMessage> SendPasswordResetAsync(
        string email,
        string callbackUrl,
        CancellationToken cancellationToken) =>
        httpClient.PostAsJsonAsync(
            "/notifications/v1/email/NoReply",
            new
            {
                To = email,
                Subject = "MALIEV Intranet password reset",
                Body = $"<p>A password reset was requested for your MALIEV employee account.</p><p><a href=\"{System.Net.WebUtility.HtmlEncode(callbackUrl)}\">Reset your password</a></p><p>If you did not request this, you can ignore this message.</p>",
                ReplyTo = (string?)null,
                Cc = (IReadOnlyList<string>?)null,
                Bcc = (IReadOnlyList<string>?)null,
            },
            cancellationToken);
}

/// <summary>Trusted AuthService challenge envelope retained only inside the BFF.</summary>
public sealed record EmployeeRecoveryChallenge(bool Accepted, string? Token);
