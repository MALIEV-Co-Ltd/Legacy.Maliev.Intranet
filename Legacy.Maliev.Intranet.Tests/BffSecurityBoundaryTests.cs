extern alias Bff;

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BffProgram = Bff::Program;
using DiagnosticEventStore = Bff::Legacy.Maliev.Intranet.Bff.Diagnostics.DiagnosticEventStore;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffSecurityBoundaryTests
{
    [Fact]
    public async Task AuthenticatedDiagnostics_ReturnsOnlyTheRedactedContract()
    {
        await using var factory = new BffFactory();
        factory.Services.GetRequiredService<DiagnosticEventStore>()
            .RecordResponseFailure(503, "/orders/42?access_token=do-not-return", "trace-42");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await client.GetAsync("/bff/diagnostics/events?sort=LogTimestamp_Descending&index=1&size=50");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("HTTP_503", item.GetProperty("code").GetString());
        Assert.Equal("/orders/{id}", item.GetProperty("path").GetString());
        Assert.Equal("trace-42", item.GetProperty("correlationId").GetString());
        Assert.DoesNotContain("access_token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stackTrace", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("message", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CookieAuthenticatedWrite_RequiresMatchingCsrfCookieAndHeader()
    {
        await using var factory = new BffFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

        using var sessionResponse = await client.GetAsync("/bff/session");
        var sessionJson = await sessionResponse.Content.ReadAsStringAsync();
        using var session = JsonDocument.Parse(sessionJson);
        var csrfToken = session.RootElement.GetProperty("csrfToken").GetString();

        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        Assert.True(session.RootElement.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal("employee-id", session.RootElement.GetProperty("employeeId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(csrfToken));
        var antiforgeryCookie = Assert.Single(
            sessionResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-Legacy.Maliev.Intranet.Antiforgery=", StringComparison.Ordinal));
        Assert.Contains("secure", antiforgeryCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", antiforgeryCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", antiforgeryCookie, StringComparison.OrdinalIgnoreCase);

        using var missingTokenResponse = await client.PostAsync("/bff/logout", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, missingTokenResponse.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        using var validResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, validResponse.StatusCode);
    }

    private sealed class BffFactory : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Services:Auth", "http://auth/");
            builder.ConfigureServices(services => services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { }));
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "employee-id"),
                new Claim(ClaimTypes.Name, "MALIEV Employee"),
                new Claim(ClaimTypes.Role, "Employee"),
            ], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
