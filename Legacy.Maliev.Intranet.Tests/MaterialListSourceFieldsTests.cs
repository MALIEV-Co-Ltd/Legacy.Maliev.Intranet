using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Bunit;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.ShadcnBlazor.Components.DataDisplay;
using Microsoft.Extensions.DependencyInjection;
using MaterialsPage = Legacy.Maliev.Intranet.Client.Features.Catalog.Pages.Materials;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class MaterialListSourceFieldsTests
{
    [Theory]
    [InlineData("machinability", ShadcnSortDirection.Ascending, CatalogMaterialSort.MaterialMachinability_Ascending)]
    [InlineData("machinability", ShadcnSortDirection.Descending, CatalogMaterialSort.MaterialMachinability_Descending)]
    [InlineData("thermal", ShadcnSortDirection.Ascending, CatalogMaterialSort.MaterialThermalConductivity_Ascending)]
    [InlineData("thermal", ShadcnSortDirection.Descending, CatalogMaterialSort.MaterialThermalConductivity_Descending)]
    public void SortTargetsTheMatchingNumericProperty(string column, ShadcnSortDirection direction, CatalogMaterialSort expected)
    {
        var component = new MaterialsPage();
        var method = typeof(MaterialsPage).GetMethod("MapSort", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var actual = method.Invoke(component, [new[] { new ShadcnDataTableSort(column, direction) }]);
        Assert.Equal(expected, Assert.IsType<CatalogMaterialSort>(actual));
    }

    [Fact]
    public void ListRendersSourceSpecificationsAndNumericProperties()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddLogging();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        using var handler = new MaterialHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddSingleton(http);
        var page = context.Render<MaterialsPage>();
        page.WaitForAssertion(() => Assert.Contains("4140-AISI", page.Markup, StringComparison.Ordinal));
        foreach (var expected in new[] { "4140-AISI", "DIN-X", "BTS-X", "JIS-X", "UNS-X", "EN-X", "AFNOR-X", "UNI-X", "SIS-X", "SAE-X", "ASTM-X", "AMS-X" })
        {
            Assert.Contains(expected, page.Markup, StringComparison.Ordinal);
        }
        Assert.Contains("55", page.Find("td[data-column='machinability']").TextContent, StringComparison.Ordinal);
        Assert.Contains("42", page.Find("td[data-column='thermal']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ListEncodesSpecificationTextAndShowsDashForBlankValues()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddLogging();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        using var handler = new MaterialHandler(special: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddSingleton(http);
        var page = context.Render<MaterialsPage>();
        page.WaitForAssertion(() => Assert.Contains("&lt;img", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("img[src='x']"));
        Assert.Equal("-", page.Find("td[data-column='din']").TextContent.Trim());
    }

    private sealed class MaterialHandler(bool special = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    items = new[] { new { id = 1, materialGroupId = 1, machinable = true, printable = false, name = "Steel", materialNumber = "4140", densityKilogramPerCubicMeter = 7850, materialGroup = new { id = 1, name = "Steel" }, aisi = special ? "<img src=x onerror=alert(1)>" : "4140-AISI", din = special ? "" : "DIN-X", bts = "BTS-X", jis = "JIS-X", uns = "UNS-X", en = "EN-X", afnor = "AFNOR-X", uni = "UNI-X", sis = "SIS-X", sae = "SAE-X", astm = "ASTM-X", ams = "AMS-X", machinabilityPercent = 55, thermalConductivityWattPerMeterKelvin = 42 } },
                    pageIndex = 1, totalPages = 1, totalRecords = 1, hasNextPage = false, hasPreviousPage = false,
                }),
            });
    }
}
