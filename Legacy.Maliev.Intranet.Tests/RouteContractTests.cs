using Legacy.Maliev.Intranet;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Security.Claims;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class RouteContractTests : IClassFixture<IntranetFactory>
{
    private readonly IntranetFactory factory;

    public RouteContractTests(IntranetFactory factory) => this.factory = factory;

    [Fact]
    public void Inventory_PreservesEveryHistoricalRouteExactlyOnce()
    {
        Assert.Equal(43, LegacyRoutes.All.Count);
        Assert.Equal(LegacyRoutes.All.Count, LegacyRoutes.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("/QuotationRequests/View", LegacyRoutes.All);
        Assert.Contains("/Finances/YearlyActivityChart", LegacyRoutes.All);
        Assert.Contains("/Error", LegacyRoutes.All);
        Assert.Contains("/Travelers", LegacyRoutes.All);
        Assert.Contains("/Travelers/Create", LegacyRoutes.All);
        Assert.Equal(3, LegacyRoutes.Retired.Count);
        Assert.Contains("/Travelers", LegacyRoutes.Retired);
        Assert.Contains("/Travelers/Create", LegacyRoutes.Retired);
        Assert.Contains("/Travelers/Index", LegacyRoutes.Retired);
        Assert.DoesNotContain("/Travelers", LegacyRoutes.ActiveMigrationCandidates);
        Assert.DoesNotContain("/Travelers/Create", LegacyRoutes.ActiveMigrationCandidates);
        Assert.DoesNotContain("/Travelers/Index", LegacyRoutes.ActiveMigrationCandidates);
    }

    [Theory]
    [InlineData("/Travelers")]
    [InlineData("/Travelers/Create")]
    [InlineData("/Travelers/Index")]
    public async Task RetiredTravelerRoutes_RequireEmployeeSession(string route)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/Login", response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/Travelers")]
    [InlineData("/Travelers/Create")]
    [InlineData("/Travelers/Index")]
    public async Task RetiredTravelerRoutes_ReturnGoneToEmployee(string route)
    {
        await using var authenticatedFactory = new AuthenticatedIntranetFactory();
        using var client = authenticatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync(route);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Contains("Retired legacy route", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_IsAnonymousAndNotIndexable()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("noindex,nofollow,noarchive", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/Dashboard")]
    [InlineData("/Customers/Index")]
    [InlineData("/Employees/Index")]
    [InlineData("/Materials/Index")]
    [InlineData("/Suppliers/Index")]
    [InlineData("/Orders/View?id=1")]
    [InlineData("/Server/ErrorReport")]
    [InlineData("/Error")]
    public async Task StaffRoutes_RequireEmployeeSession(string route)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/Login", response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Source_HasNoDatabaseOrRetiredServiceDependencies()
    {
        var project = File.ReadAllText(Path.Combine(FindRoot(), "Legacy.Maliev.Intranet", "Legacy.Maliev.Intranet.csproj"));

        Assert.DoesNotContain("EntityFrameworkCore", project, StringComparison.Ordinal);
        Assert.DoesNotContain("PredictionService", project, StringComparison.Ordinal);
        Assert.DoesNotContain("LoggerService", project, StringComparison.Ordinal);
        Assert.DoesNotContain("PayPal", project, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}

internal sealed class AuthenticatedIntranetFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPolicyEvaluator>();
            services.AddSingleton<IPolicyEvaluator, EmployeePolicyEvaluator>();
        });
    }

    private sealed class EmployeePolicyEvaluator : IPolicyEvaluator
    {
        private static readonly ClaimsPrincipal Principal = new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "traveler-route-test-employee")], "TravelerRouteTest"));

        public Task<AuthenticateResult> AuthenticateAsync(AuthorizationPolicy policy, HttpContext context)
        {
            context.User = Principal;
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(Principal, "TravelerRouteTest")));
        }

        public Task<PolicyAuthorizationResult> AuthorizeAsync(
            AuthorizationPolicy policy,
            AuthenticateResult authenticationResult,
            HttpContext context,
            object? resource) => Task.FromResult(PolicyAuthorizationResult.Success());
    }
}

public sealed class IntranetFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
}
