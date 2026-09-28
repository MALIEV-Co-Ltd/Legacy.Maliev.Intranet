extern alias Bff;

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class ErrorRouteParityTests
{
    [Theory]
    [InlineData("404", 404)]
    [InlineData("500", 500)]
    [InlineData("200", null)]
    [InlineData("999", null)]
    [InlineData("404<script>", null)]
    [InlineData(null, null)]
    public void StatusInput_OnlyDisplaysHttpFailures(string? input, int? expected) =>
        Assert.Equal(expected, ErrorPageStatus.DisplayCode(input));

    [Fact]
    public void BlazorRoute_IsOwnedByAuthorizedStaffPage()
    {
        var page = typeof(Legacy.Maliev.Intranet.Client.Pages.Error);
        Assert.Contains(page.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>(),
            route => route.Template == "/Error");
        Assert.NotEmpty(page.GetCustomAttributes(typeof(AuthorizeAttribute), false));
        Assert.Contains("/Error", Legacy.Maliev.Intranet.LegacyRoutes.ActiveMigrationCandidates);
        Assert.DoesNotContain("/Error", Legacy.Maliev.Intranet.LegacyRoutes.Anonymous);
    }

    [Fact]
    public async Task CompatibilityRoute_RequiresEmployeeAndDoesNotReflectPrivateInput()
    {
        await using var factory = CreateCompatibilityFactory();
        using var client = CreateClient(factory);

        using var anonymous = await client.GetAsync("/Error?code=404");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/Error?code=404&email=private%40example.com&incidentId=forged-reference");
        request.Headers.Add("X-Test-Employee", "yes");
        request.Headers.Referrer = new Uri("https://private.example/path?token=secret-referrer");
        using var response = await client.SendAsync(request);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Contains("404", html, StringComparison.Ordinal);
        Assert.Contains("Page reference", html, StringComparison.Ordinal);
        Assert.DoesNotContain("private@example.com", html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-referrer", html, StringComparison.Ordinal);
        Assert.DoesNotContain("forged-reference", html, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompatibilityRoute_InvalidStatusIsHiddenAndThaiIsLocalized()
    {
        await using var factory = CreateCompatibilityFactory();
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Error?code=200");
        request.Headers.Add("X-Test-Employee", "yes");
        request.Headers.Add("Cookie", "maliev_culture=th-TH");
        using var response = await client.SendAsync(request);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ไม่สามารถดำเนินการตามคำขอได้", html, StringComparison.Ordinal);
        Assert.DoesNotContain("รหัสสถานะ:", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BffRoute_PreservesShellStatusAndProtectsServerGeneratedReference()
    {
        await using var factory = CreateBffFactory();
        using var client = CreateClient(factory);

        using var shell = await client.GetAsync("/Error?code=503");
        Assert.Equal(HttpStatusCode.OK, shell.StatusCode);
        Assert.Equal("text/html", shell.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", shell.Headers.CacheControl?.ToString(), StringComparison.Ordinal);

        using var anonymous = await client.GetAsync("/bff/error-context?incidentId=forged-reference");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/bff/error-context?incidentId=forged-reference&email=private%40example.com");
        request.Headers.Add("X-Test-Employee", "yes");
        using var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Single(document.RootElement.EnumerateObject());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("pageReference").GetString()));
        Assert.DoesNotContain("forged-reference", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private@example.com", json, StringComparison.Ordinal);

        using var missingApi = await client.GetAsync("/bff/no-such-route");
        Assert.Equal(HttpStatusCode.NotFound, missingApi.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateCompatibilityFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => Configure(builder));

    private static WebApplicationFactory<BffProgram> CreateBffFactory() =>
        new WebApplicationFactory<BffProgram>().WithWebHostBuilder(builder => Configure(builder));

    private static void Configure(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        TestJwtConfiguration.Configure(builder);
        builder.ConfigureServices(services => services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = EmployeeTestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = EmployeeTestAuthenticationHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, EmployeeTestAuthenticationHandler>(
                EmployeeTestAuthenticationHandler.SchemeName, _ => { }));
    }

    private static HttpClient CreateClient<T>(WebApplicationFactory<T> factory) where T : class =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    private sealed class EmployeeTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ErrorRouteTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Employee"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "employee-id")], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
