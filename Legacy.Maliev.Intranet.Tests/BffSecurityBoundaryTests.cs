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

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffSecurityBoundaryTests
{
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
