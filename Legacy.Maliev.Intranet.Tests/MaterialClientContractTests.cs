using Legacy.Maliev.Intranet.Materials;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class MaterialClientContractTests
{
    [Fact]
    public async Task ListMaterials_ForwardsBearerTokenAndLegacyQueryShape()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            """{"Items":[],"PageIndex":2,"TotalPages":4,"TotalRecords":75,"HasNextPage":true,"HasPreviousPage":true}"""));
        var client = new LegacyCatalogClient(new HttpClient(handler) { BaseAddress = new("http://catalog/") });

        var result = await client.GetMaterialsAsync(MaterialSortType.MaterialName_Ascending, "tool steel", 2, 25, "employee-token", CancellationToken.None);

        Assert.NotNull(result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/Materials?sort=MaterialName_Ascending&search=tool%20steel&index=2&size=25", request.Uri.PathAndQuery);
        Assert.Equal("Bearer employee-token", request.Authorization);
    }

    [Fact]
    public async Task CreateMaterial_SendsCompleteJsonPayloadToCatalogService()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Created, MaterialJson));
        var client = new LegacyCatalogClient(new HttpClient(handler) { BaseAddress = new("http://catalog/") });
        var payload = MaterialFixtures.Request;

        var result = await client.CreateMaterialAsync(payload, "employee-token", CancellationToken.None);

        Assert.Equal(42, result.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/Materials", request.Uri.AbsolutePath);
        using var json = JsonDocument.Parse(request.Body!);
        Assert.Equal("4140", json.RootElement.GetProperty("name").GetString());
        Assert.Equal(7850m, json.RootElement.GetProperty("densityKilogramPerCubicMeter").GetDecimal());
        Assert.True(json.RootElement.GetProperty("machinable").GetBoolean());
    }

    [Fact]
    public async Task SyncColors_AddsAndRemovesOnlyChangedLinks()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/materials/42/colors" => Json(HttpStatusCode.OK, """[{"Id":1,"Name":"Black","CreatedDate":null,"ModifiedDate":null},{"Id":2,"Name":"Blue","CreatedDate":null,"ModifiedDate":null}]"""),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent),
        });
        var client = new LegacyCatalogClient(new HttpClient(handler) { BaseAddress = new("http://catalog/") });

        await client.SyncMaterialColorsAsync(42, [2, 3], "employee-token", CancellationToken.None);

        Assert.Contains(handler.Requests, request => request.Method == "DELETE" && request.Uri.AbsolutePath == "/materials/42/colors/1");
        Assert.Contains(handler.Requests, request => request.Method == "POST" && request.Uri.AbsolutePath == "/materials/42/colors/3");
        Assert.DoesNotContain(handler.Requests, request => request.Uri.AbsolutePath.EndsWith("/2", StringComparison.Ordinal));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private const string MaterialJson = """{"Id":42,"MaterialGroupId":1,"Machinable":true,"Printable":false,"Name":"4140","Aisi":null,"Din":null,"Bts":null,"Jis":null,"Uns":null,"En":null,"Afnor":null,"Uni":null,"Sis":null,"Sae":null,"Astm":null,"Ams":null,"MaterialNumber":"4140","ManufacturerReference":null,"HardnessBrinell":null,"HardnessKnoop":null,"HardnessRockwellA":null,"HardnessRockwellB":null,"HardnessRockwellC":null,"HardnessVickers":null,"DensityKilogramPerCubicMeter":7850,"TensileStrengthUltimateGigaPascal":null,"TensileStrengthYieldMegaPascal":null,"MachinabilityPercent":70,"ShearModulusGigaPascal":null,"ThermalConductivityWattPerMeterKelvin":null,"Url":null,"PricePerKilogram":null,"CurrencyId":null,"Comment":null,"CreatedDate":null,"ModifiedDate":null,"MaterialGroup":{"Id":1,"Name":"Steel","Description":null,"CreatedDate":null,"ModifiedDate":null}}""";

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(request.Method.Method, request.RequestUri!, request.Headers.Authorization?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return responder(request);
        }
    }

    private sealed record RecordedRequest(string Method, Uri Uri, string? Authorization, string? Body);
}

internal static class MaterialFixtures
{
    public static UpsertMaterialRequest Request { get; } = new(
        1, true, false, "4140", null, null, null, null, null, null, null, null, null, null, null, null,
        "4140", null, null, null, null, null, null, null, 7850, null, null, 70, null, null, null, null, null, null);
}