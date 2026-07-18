extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

    [Fact]
    public async Task PasswordResetRequest_NotificationFailureRemainsEnumerationSafe()
    {
        var downstream = new RecoveryDownstreamHandler
        {
            ChallengeToken = "opaque-reset-token-012345678901234567890123",
            NotificationThrows = true,
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

    private sealed class RecoveryBffFactory(RecoveryDownstreamHandler downstream)
        : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
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
                "/auth/v1/employee-self-service/password-reset/complete" =>
                    new HttpResponseMessage(HttpStatusCode.NoContent),
                "/auth/v1/employee-self-service/email-confirmation/complete" =>
                    new HttpResponseMessage(ConfirmationStatusCode),
                "/notifications/v1/email/NoReply" => Json(
                    HttpStatusCode.OK,
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
