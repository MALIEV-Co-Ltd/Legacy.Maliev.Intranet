using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Customers;

/// <summary>Sends the one-time customer onboarding message after account creation commits.</summary>
public sealed class CustomerAccountNotificationProxy(HttpClient httpClient)
{
    /// <summary>Delivers the generated temporary credential without exposing it to the browser.</summary>
    public Task<HttpResponseMessage> SendCreatedAsync(
        CreateCustomerAccountRequest request,
        string temporaryPassword,
        CancellationToken cancellationToken) =>
        httpClient.PostAsJsonAsync(
            "/notifications/v1/email/NoReply",
            new
            {
                To = request.Email,
                Subject = "Your MALIEV customer account",
                Body = $"<p>Hello {WebUtility.HtmlEncode($"{request.FirstName} {request.LastName}")},</p><p>Your MALIEV customer account has been created. Sign in with your email and the temporary password below, then create your own password immediately.</p><p><strong>Temporary password:</strong> {WebUtility.HtmlEncode(temporaryPassword)}</p><p>This message was automatically generated. Please do not reply.</p>",
                ReplyTo = (string?)null,
                Cc = (IReadOnlyList<string>?)null,
                Bcc = (IReadOnlyList<string>?)null,
            },
            cancellationToken);
}
