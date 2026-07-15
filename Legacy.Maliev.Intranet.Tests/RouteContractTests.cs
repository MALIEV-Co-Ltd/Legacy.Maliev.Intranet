using Legacy.Maliev.Intranet;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class RouteContractTests : IClassFixture<IntranetFactory>
{
    private readonly IntranetFactory factory;

    public RouteContractTests(IntranetFactory factory) => this.factory = factory;

    [Fact]
    public void Inventory_PreservesEveryHistoricalRouteExactlyOnce()
    {
        Assert.Equal(42, LegacyRoutes.All.Count);
        Assert.Equal(LegacyRoutes.All.Count, LegacyRoutes.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("/QuotationRequests/View", LegacyRoutes.All);
        Assert.Contains("/Finances/YearlyActivityChart", LegacyRoutes.All);
        Assert.Contains("/Travelers/Create", LegacyRoutes.All);
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

public sealed class IntranetFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
}