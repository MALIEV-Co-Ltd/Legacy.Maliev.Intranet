using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Legacy.Maliev.Intranet.Client;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeeAuthenticationStateProviderTests
{
    [Fact]
    public async Task AuthenticatedSession_ProducesEmployeePrincipalAndRoles()
    {
        var provider = CreateProvider(new EmployeeSessionSummary(
            true,
            "employee-id",
            "MALIEV Employee",
            ["Employee", "Accounting"],
            "csrf-token"));

        var state = await provider.GetAuthenticationStateAsync();

        Assert.True(state.User.Identity?.IsAuthenticated);
        Assert.Equal("employee-id", state.User.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("MALIEV Employee", state.User.Identity?.Name);
        Assert.Equal("employee", state.User.FindFirstValue("identity_kind"));
        Assert.True(state.User.IsInRole("Employee"));
        Assert.True(state.User.IsInRole("Accounting"));
        Assert.DoesNotContain(state.User.Claims, claim => claim.Value == "csrf-token");
    }

    [Fact]
    public async Task AnonymousSession_ProducesUnauthenticatedPrincipal()
    {
        var provider = CreateProvider(new EmployeeSessionSummary(
            false,
            null,
            null,
            [],
            "anonymous-csrf-token"));

        var state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated);
        Assert.Empty(state.User.Claims);
    }

    private static EmployeeAuthenticationStateProvider CreateProvider(EmployeeSessionSummary session)
    {
        var httpClient = new HttpClient(new SessionHandler(session))
        {
            BaseAddress = new Uri("https://localhost"),
        };
        var sessionClient = new EmployeeSessionClient(
            httpClient,
            NullLogger<EmployeeSessionClient>.Instance);
        return new EmployeeAuthenticationStateProvider(sessionClient);
    }

    private sealed class SessionHandler(EmployeeSessionSummary session) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("/bff/session", request.RequestUri?.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(session),
            });
        }
    }
}
