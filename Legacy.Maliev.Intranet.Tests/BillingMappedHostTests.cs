extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using BillingMapper = Bff::Legacy.Maliev.Intranet.Bff.Accounting.BillingEndpointMapper;
using BillingProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.BillingProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BillingMappedHostTests
{
    [Fact]
    public async Task MappedRoutesRejectAnonymousAndMissingOrInvalidCsrfBeforeDownstreamResourceDenial()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Billing:ReadEnabled"] = "true";
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; });
        builder.Services.AddAuthorization(options => options.AddPolicy(LegacyEmployeePermissions.AccountingRead,
            policy => policy.RequireAuthenticatedUser().RequireClaim("permissions", LegacyEmployeePermissions.AccountingRead)));
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        var downstream = new DeniedTransport();
        builder.Services.AddSingleton(new BillingProxy(new HttpClient(downstream) { BaseAddress = new("https://accounting.invalid") },
            new EmployeeSessionService(null!, TimeProvider.System, NullLogger<EmployeeSessionService>.Instance,
                Options.Create(new LegacyEmployeeCompatibilityOptions()))));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        BillingMapper.MapBillingEndpoints(app);
        app.MapGet("/fixture/session", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "fixture-employee"),
                new Claim("permissions", LegacyEmployeePermissions.AccountingRead)], CookieAuthenticationDefaults.AuthenticationScheme));
            var properties = new AuthenticationProperties();
            properties.StoreTokens([new() { Name = "legacy_access_token", Value = "fixture-employee-credential" },
                new() { Name = "legacy_access_expires_at", Value = DateTimeOffset.UtcNow.AddMinutes(10).ToString("O") }]);
            await context.SignInAsync(principal, properties);
            context.User = principal;
            return Results.Text(antiforgery.GetAndStoreTokens(context).RequestToken!);
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        var path = $"/bff/customers/7/billing/accounts/{Guid.NewGuid():D}";
        using var anonymous = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(0, downstream.Calls);
        using var session = await client.GetAsync("/fixture/session");
        var csrf = await session.Content.ReadAsStringAsync();
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", session.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0])));
        var input = new BillingPreviewRequest(BillingStageKind.Remaining, null, null, null, new(7, "tax", "Office", null, "Address"), 2);
        foreach (var token in new string?[] { null, "invalid" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path + "/preview") { Content = JsonContent.Create(input) };
            if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, downstream.Calls);
        }
        using var validRequest = new HttpRequestMessage(HttpMethod.Post, path + "/preview") { Content = JsonContent.Create(input) };
        validRequest.Headers.Add("X-CSRF-TOKEN", csrf);
        using var denied = await client.SendAsync(validRequest);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(1, downstream.Calls);
        Assert.True(downstream.EmployeeCredential);
    }

    private sealed class DeniedTransport : HttpMessageHandler
    {
        public int Calls;
        public bool EmployeeCredential;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            EmployeeCredential = request.Headers.Authorization?.ToString() == "Bearer fixture-employee-credential";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        }
    }
}
