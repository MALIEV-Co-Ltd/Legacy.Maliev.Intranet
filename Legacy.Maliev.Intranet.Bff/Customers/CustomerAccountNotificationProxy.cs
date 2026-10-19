using Maliev.Aspire.ServiceDefaults.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Customers;

/// <summary>Sends the one-time customer onboarding message after account creation commits.</summary>
public sealed class CustomerAccountNotificationProxy(HttpClient httpClient)
{
    /// <summary>Delivers the generated temporary credential without exposing it to the browser.</summary>
    public Task<HttpResponseMessage> SendCreatedAsync(
        string email,
        string customerName,
        string setupUrl,
        CancellationToken cancellationToken) =>
        httpClient.SendWithPrivateFailureObservationAsync(new HttpRequestMessage(HttpMethod.Post, "/notifications/v1/email/NoReply")
        {
            Content = JsonContent.Create(new
            {
                To = email,
                Subject = "Your MALIEV customer account",
                Body = $"<p>Hello {WebUtility.HtmlEncode(customerName)},</p><p>Your MALIEV customer account has been created.</p><p><a href=\"{WebUtility.HtmlEncode(setupUrl)}\">Create your password</a></p><p>This single-use link expires in 24 hours. This message was automatically generated. Please do not reply.</p>",
                ReplyTo = (string?)null,
                Cc = (IReadOnlyList<string>?)null,
                Bcc = (IReadOnlyList<string>?)null,
            })
        }, cancellationToken, new PrivateDependencyFailureObservation(), HttpCompletionOption.ResponseContentRead);
}
