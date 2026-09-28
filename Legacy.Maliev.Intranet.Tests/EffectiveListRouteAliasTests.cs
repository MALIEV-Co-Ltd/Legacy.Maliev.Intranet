extern alias Bff;

using System.Net;
using System.Reflection;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class EffectiveListRouteAliasTests
{
    [Theory]
    [InlineData("/Finances", "/bff/finances")]
    [InlineData("/Invoices", "/bff/invoices")]
    [InlineData("/QuotationRequests", "/bff/quotation-requests")]
    [InlineData("/Quotations", "/bff/quotations")]
    public async Task BareLegacyListGetUsesTheProtectedExistingPage(string route, string dataRoute)
    {
        var page = route switch
        {
            "/Finances" => typeof(Legacy.Maliev.Intranet.Client.Features.Accounting.Pages.Finances),
            "/Invoices" => typeof(Legacy.Maliev.Intranet.Client.Features.Accounting.Pages.Invoices),
            "/QuotationRequests" => typeof(Legacy.Maliev.Intranet.Client.Features.Quotations.Pages.QuotationRequests.Index),
            _ => typeof(Legacy.Maliev.Intranet.Client.Features.Quotations.Pages.Quotations.Index),
        };

        Assert.Contains(page.GetCustomAttributes<RouteAttribute>(), attribute => attribute.Template == route);
        Assert.NotEmpty(page.GetCustomAttributes<AuthorizeAttribute>());

        await using var factory = new WebApplicationFactory<BffProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var pageResponse = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        Assert.Equal("text/html", pageResponse.Content.Headers.ContentType?.MediaType);

        using var dataResponse = await client.GetAsync(dataRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, dataResponse.StatusCode);
    }

    [Theory]
    [InlineData("Finances", "Legacy.Maliev.Intranet.Client.Features.Accounting.wasm")]
    [InlineData("Invoices", "Legacy.Maliev.Intranet.Client.Features.Accounting.wasm")]
    [InlineData("QuotationRequests", "Legacy.Maliev.Intranet.Client.Features.Quotations.wasm")]
    [InlineData("Quotations", "Legacy.Maliev.Intranet.Client.Features.Quotations.wasm")]
    public void BareLegacyListLoadsItsFeatureAssembly(string route, string assembly)
    {
        var app = File.ReadAllText(Path.Combine(FindRoot(), "Legacy.Maliev.Intranet.Client", "App.razor"));
        Assert.Contains($"path.Equals(\"{route}\", StringComparison.OrdinalIgnoreCase)", app, StringComparison.Ordinal);
        Assert.Contains(assembly, app, StringComparison.Ordinal);
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
