extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;
using EmployeeRecoveryAuthProxy = Bff::Legacy.Maliev.Intranet.Bff.Employees.EmployeeRecoveryAuthProxy;
using EmployeeRecoveryNotificationProxy = Bff::Legacy.Maliev.Intranet.Bff.Employees.EmployeeRecoveryNotificationProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffEmployeeRecoveryContractTests
{
    [Fact]
    public async Task PasswordResetRequest_UnknownAndKnownEmployeesReturnSameGenericAcceptedContract()
    {
        var downstream = new RecoveryDownstreamHandler();
        await using var factory = new RecoveryBffFactory(downstream);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfTokenAsync(client);

        downstream.ChallengeToken = null;
        using var unknown = await SendAsync(
            client,
            "/bff/employee-recovery/password-reset/request",
            new { email = "missing@example.com" },
            csrf);
        var unknownBody = await unknown.Content.ReadAsStringAsync();

        downstream.ChallengeToken = "opaque-reset-token-012345678901234567890123";
        using var known = await SendAsync(
            client,
            "/bff/employee-recovery/password-reset/request",
            new { email = "employee@example.com" },
            csrf);
        var knownBody = await known.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(unknownBody, knownBody);
        Assert.DoesNotContain("opaque-reset-token", knownBody, StringComparison.Ordinal);
        var notification = Assert.Single(downstream.Requests, request =>
            request.Path == "/notifications/v1/email/NoReply");
        Assert.Contains("employee@example.com", notification.Body, StringComparison.Ordinal);
        Assert.Contains("opaque-reset-token-012345678901234567890123", notification.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecoveryWrites_RequireAntiforgeryBeforeCallingDownstream()
    {
        var downstream = new RecoveryDownstreamHandler();
        await using var factory = new RecoveryBffFactory(downstream);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/bff/employee-recovery/password-reset/request",
            new { email = "employee@example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(downstream.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PasswordResetRequest_NotificationFailureRemainsEnumerationSafe(bool notificationThrows)
    {
        var downstream = new RecoveryDownstreamHandler
        {
            ChallengeToken = "opaque-reset-token-012345678901234567890123",
            NotificationThrows = notificationThrows,
            NotificationStatusCode = HttpStatusCode.ServiceUnavailable,
        };
        await using var factory = new RecoveryBffFactory(downstream);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfTokenAsync(client);

        using var response = await SendAsync(
            client,
            "/bff/employee-recovery/password-reset/request",
            new { email = "employee@example.com" },
            csrf);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain("opaque-reset-token", body, StringComparison.Ordinal);

        downstream.ChallengeToken = null;
        using var unknown = await SendAsync(client, "/bff/employee-recovery/password-reset/request",
            new { email = "missing@example.com" }, csrf);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), body);
        Assert.Single(downstream.Requests, item => item.Path == "/notifications/v1/email/NoReply");
    }

    [Theory]
    [InlineData("attacker.example", null, null)]
    [InlineData(null, "forwarded-attacker.example", null)]
    [InlineData("attacker.example", "forwarded-attacker.example", "host=rfc-attacker.example;proto=https")]
    public async Task PasswordResetRequest_UntrustedHostHeadersCannotControlEmailLink(
        string? host, string? forwardedHost, string? forwarded)
    {
        var downstream = new RecoveryDownstreamHandler { ChallengeToken = "opaque-reset-token" };
        await using var factory = new RecoveryBffFactory(downstream);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/employee-recovery/password-reset/request")
        {
            Content = JsonContent.Create(new { email = "employee@example.com" }),
        };
        request.Headers.Host = host;
        if (forwardedHost is not null) request.Headers.Add("X-Forwarded-Host", forwardedHost);
        if (forwarded is not null) request.Headers.Add("Forwarded", forwarded);
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var notification = Assert.Single(downstream.Requests, item => item.Path == "/notifications/v1/email/NoReply");
        using var payload = JsonDocument.Parse(notification.Body);
        var html = payload.RootElement.GetProperty("body").GetString()!;
        Assert.Equal("https://intranet.example.com/Employees/ResetPassword?email=employee@example.com&token=opaque-reset-token",
            ReadCallback(html));
        Assert.DoesNotContain("attacker.example", html, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasswordResetRequest_TrustedOriginPreservesEncodedOpaqueTokenAndEmail()
    {
        var downstream = new RecoveryDownstreamHandler { ChallengeToken = "opaque+/=&?" };
        await using var factory = new RecoveryBffFactory(downstream, "https://trusted.example:8443/");
        using var client = CreateClient(factory);
        var csrf = await GetCsrfTokenAsync(client);

        using var response = await SendAsync(client, "/bff/employee-recovery/password-reset/request",
            new { email = "employee+reset@example.com" }, csrf);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain(downstream.ChallengeToken, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var notification = Assert.Single(downstream.Requests, item => item.Path == "/notifications/v1/email/NoReply");
        using var payload = JsonDocument.Parse(notification.Body);
        Assert.Equal("employee+reset@example.com", payload.RootElement.GetProperty("to").GetString());
        var callback = new Uri(ReadCallback(payload.RootElement.GetProperty("body").GetString()!));
        Assert.Equal("https://trusted.example:8443/Employees/ResetPassword", callback.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(callback.Query);
        Assert.Equal("employee+reset@example.com", query["email"].ToString());
        Assert.Equal("opaque+/=&?", query["token"].ToString());
        Assert.Equal(2, query.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-origin")]
    [InlineData("http://attacker.example/")]
    [InlineData("https://user@attacker.example/")]
    [InlineData("https://trusted.example/path")]
    [InlineData("https://trusted.example/?callback=attacker")]
    [InlineData("https://trusted.example/#attacker")]
    public async Task PasswordResetRequest_MissingOrUnsafeOriginRejectsBeforeAnyDownstreamCall(string? publicOrigin)
    {
        var downstream = new RecoveryDownstreamHandler { ChallengeToken = "opaque-reset-token" };
        await using var factory = new RecoveryBffFactory(downstream, publicOrigin);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfTokenAsync(client);

        using var response = await SendAsync(client, "/bff/employee-recovery/password-reset/request",
            new { email = "employee@example.com" }, csrf);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(downstream.Requests);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("employee@example.com", body, StringComparison.Ordinal);
        Assert.DoesNotContain(downstream.ChallengeToken, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasswordResetComplete_ForwardsJsonOnlyAndNeverPlacesSecretsInTheRoute()
    {
        var downstream = new RecoveryDownstreamHandler();
        await using var factory = new RecoveryBffFactory(downstream);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfTokenAsync(client);

        using var response = await SendAsync(
            client,
            "/bff/employee-recovery/password-reset/complete",
            new
            {
                email = "employee@example.com",
                token = "opaque-reset-token-012345678901234567890123",
                password = "new-password",
                confirmPassword = "new-password",
            },
            csrf);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var auth = Assert.Single(downstream.Requests, request =>
            request.Path == "/auth/v1/employee-self-service/password-reset/complete");
        Assert.DoesNotContain("new-password", auth.Path, StringComparison.Ordinal);
        Assert.DoesNotContain("opaque-reset-token", auth.Path, StringComparison.Ordinal);
        Assert.Contains("\"password\":\"new-password\"", auth.Body, StringComparison.Ordinal);
        Assert.Contains("\"token\":\"opaque-reset-token", auth.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmailConfirmation_ForwardsOpaqueActionAndPreservesInvalidOrExpiredResult()
    {
        var downstream = new RecoveryDownstreamHandler
        {
            ConfirmationStatusCode = HttpStatusCode.BadRequest,
        };
        await using var factory = new RecoveryBffFactory(downstream);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfTokenAsync(client);

        using var response = await SendAsync(
            client,
            "/bff/employee-recovery/email-confirmation/complete",
            new
            {
                email = "employee@example.com",
                token = "opaque-confirm-token-0123456789012345678901",
            },
            csrf);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var auth = Assert.Single(downstream.Requests, request =>
            request.Path == "/auth/v1/employee-self-service/email-confirmation/complete");
        Assert.Contains("opaque-confirm-token", auth.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmationRequest_KnownAndUnknownReturnSameBodyAndNeverExposeToken()
    {
        var downstream = new RecoveryDownstreamHandler();
        await using var factory = new RecoveryBffFactory(downstream);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfTokenAsync(client);

        using var unknown = await SendAsync(client, "/bff/employee-recovery/email-confirmation/request",
            new { email = "missing@example.com" }, csrf);
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        downstream.ChallengeToken = "opaque-confirm-token-0123456789012345678901";
        using var known = await SendAsync(client, "/bff/employee-recovery/email-confirmation/request",
            new { email = "employee@example.com" }, csrf);
        var knownBody = await known.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(unknownBody, knownBody);
        Assert.DoesNotContain(downstream.ChallengeToken, knownBody, StringComparison.Ordinal);
        Assert.Equal(2, downstream.Requests.Count(request => request.Path ==
            "/auth/v1/employee-self-service/email-confirmation/request"));
        var notification = Assert.Single(downstream.Requests, request => request.Path == "/notifications/v1/email/NoReply");
        Assert.Contains("https://intranet.example.com/Employees/EmailConfirmation", notification.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmationRequest_UntrustedHostCannotControlEmailLink()
    {
        var downstream = new RecoveryDownstreamHandler { ChallengeToken = "opaque-confirm-token-0123456789012345678901" };
        await using var factory = new RecoveryBffFactory(downstream);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/employee-recovery/email-confirmation/request")
        {
            Content = JsonContent.Create(new { email = "employee@example.com" }),
        };
        request.Headers.Host = "attacker.example";
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var notification = Assert.Single(downstream.Requests, item => item.Path == "/notifications/v1/email/NoReply");
        Assert.DoesNotContain("attacker.example", notification.Body, StringComparison.Ordinal);
        Assert.Contains("intranet.example.com", notification.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmationRequest_UnsafeOriginFailsBeforeChallenge()
    {
        var downstream = new RecoveryDownstreamHandler { ChallengeToken = "opaque-confirm-token-0123456789012345678901" };
        await using var factory = new RecoveryBffFactory(downstream, "http://attacker.example/");
        using var client = CreateClient(factory);
        var csrf = await GetCsrfTokenAsync(client);

        using var response = await SendAsync(client, "/bff/employee-recovery/email-confirmation/request",
            new { email = "employee@example.com" }, csrf);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(downstream.Requests);
    }

    [Fact]
    public async Task PasswordResetComplete_MismatchedPasswordsIsRejectedBeforeAuthService()
    {
        var downstream = new RecoveryDownstreamHandler();
        await using var factory = new RecoveryBffFactory(downstream);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfTokenAsync(client);

        using var response = await SendAsync(
            client,
            "/bff/employee-recovery/password-reset/complete",
            new
            {
                email = "employee@example.com",
                token = "opaque-reset-token-012345678901234567890123",
                password = "new-password",
                confirmPassword = "different-password",
            },
            csrf);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(downstream.Requests, request =>
            request.Path == "/auth/v1/employee-self-service/password-reset/complete");
    }

    private static string ReadCallback(string html)
    {
        const string attribute = "href=\"";
        var start = html.IndexOf(attribute, StringComparison.Ordinal);
        Assert.True(start >= 0);
        start += attribute.Length;
        var end = html.IndexOf('"', start);
        Assert.True(end > start);
        return WebUtility.HtmlDecode(html[start..end]);
    }

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/bff/session");
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement.GetProperty("csrfToken").GetString()
            ?? throw new InvalidOperationException("The BFF did not return an antiforgery token.");
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string path,
        object payload,
        string csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private sealed class RecoveryBffFactory(RecoveryDownstreamHandler downstream, string? publicOrigin = "https://intranet.example.com/")
        : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("EmployeeConfirmation:PublicOrigin", publicOrigin);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<EmployeeRecoveryAuthProxy>();
                services.RemoveAll<EmployeeRecoveryNotificationProxy>();
                services.AddSingleton(new EmployeeRecoveryAuthProxy(
                    new HttpClient(downstream) { BaseAddress = new("http://auth/") }));
                services.AddSingleton(new EmployeeRecoveryNotificationProxy(
                    new HttpClient(downstream) { BaseAddress = new("http://notification/") }));
            });
        }
    }

    private sealed class RecoveryDownstreamHandler : HttpMessageHandler
    {
        public string? ChallengeToken { get; set; }
        public HttpStatusCode ConfirmationStatusCode { get; set; } = HttpStatusCode.NoContent;
        public bool NotificationThrows { get; set; }
        public HttpStatusCode NotificationStatusCode { get; set; } = HttpStatusCode.OK;
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(request.Method.Method, request.RequestUri!.AbsolutePath, body));
            if (NotificationThrows
                && request.RequestUri.AbsolutePath == "/notifications/v1/email/NoReply")
            {
                throw new HttpRequestException("notification unavailable");
            }

            return request.RequestUri.AbsolutePath switch
            {
                "/auth/v1/employee-self-service/password-reset/request" => Json(
                    HttpStatusCode.OK,
                    new { accepted = true, token = ChallengeToken }),
                "/auth/v1/employee-self-service/email-confirmation/request" => Json(
                    HttpStatusCode.OK,
                    new { accepted = true, token = ChallengeToken }),
                "/auth/v1/employee-self-service/password-reset/complete" =>
                    new HttpResponseMessage(HttpStatusCode.NoContent),
                "/auth/v1/employee-self-service/email-confirmation/complete" =>
                    new HttpResponseMessage(ConfirmationStatusCode),
                "/notifications/v1/email/NoReply" => Json(
                    NotificationStatusCode,
                    new { providerMessageId = "test-message" }),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        private static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status)
        {
            Content = JsonContent.Create(value),
        };
    }

    private sealed record CapturedRequest(string Method, string Path, string Body);
}
