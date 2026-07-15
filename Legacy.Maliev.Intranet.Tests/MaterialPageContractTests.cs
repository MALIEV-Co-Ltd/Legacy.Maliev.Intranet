using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Materials;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed partial class MaterialPageContractTests
{
    [Fact]
    public async Task AuthenticatedMaterialPages_RenderTypedCatalogResults()
    {
        var catalog = new StubCatalogClient();
        await using var factory = new MaterialIntranetFactory(catalog, new StubAuthClient());
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(client);

        var index = await client.GetStringAsync("/Materials/Index?search=4140&index=1&size=25");
        var view = await client.GetStringAsync("/Materials/View?id=42");

        Assert.Contains("4140", index, StringComparison.Ordinal);
        Assert.Contains("Material #42", view, StringComparison.Ordinal);
        Assert.Contains("Black", view, StringComparison.Ordinal);
        Assert.Equal("employee-access-token", catalog.LastAccessToken);
    }

    [Fact]
    public async Task CreateMaterial_PostsCompleteTypedPayloadAndRedirectsToView()
    {
        var catalog = new StubCatalogClient();
        await using var factory = new MaterialIntranetFactory(catalog, new StubAuthClient());
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(client);
        var createPage = await client.GetStringAsync("/Materials/Create");
        var token = AntiForgeryToken().Match(createPage).Groups[1].Value;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"] = "4140",
            ["Input.MaterialGroupId"] = "1",
            ["Input.DensityKilogramPerCubicMeter"] = "7850",
            ["Input.Machinable"] = "true",
            ["Input.Printable"] = "false",
            ["Input.Aisi"] = "",
            ["Input.Din"] = "",
            ["Input.Bts"] = "",
            ["Input.Jis"] = "",
            ["Input.Uns"] = "",
            ["Input.En"] = "",
            ["Input.Afnor"] = "",
            ["Input.Uni"] = "",
            ["Input.Sis"] = "",
            ["Input.Sae"] = "",
            ["Input.Astm"] = "",
            ["Input.Ams"] = "",
            ["Input.MaterialNumber"] = "",
            ["Input.ManufacturerReference"] = "",
            ["Input.HardnessBrinell"] = "",
            ["Input.HardnessKnoop"] = "",
            ["Input.HardnessRockwellA"] = "",
            ["Input.HardnessRockwellB"] = "",
            ["Input.HardnessRockwellC"] = "",
            ["Input.HardnessVickers"] = "",
            ["Input.TensileStrengthUltimateGigaPascal"] = "",
            ["Input.TensileStrengthYieldMegaPascal"] = "",
            ["Input.MachinabilityPercent"] = "",
            ["Input.ShearModulusGigaPascal"] = "",
            ["Input.ThermalConductivityWattPerMeterKelvin"] = "",
            ["Input.Url"] = "",
            ["Input.PricePerKilogram"] = "",
            ["Input.CurrencyId"] = "",
            ["Input.Comment"] = "",
            ["__RequestVerificationToken"] = token,
        });

        var response = await client.PostAsync("/Materials/Create", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Materials/View?id=42", response.Headers.Location?.ToString());
        Assert.Equal("4140", catalog.CreatedRequest?.Name);
        Assert.Equal(7850m, catalog.CreatedRequest?.DensityKilogramPerCubicMeter);
    }

    private static async Task LoginAsync(HttpClient client)
    {
        var loginPage = await client.GetStringAsync("/Login");
        var token = AntiForgeryToken().Match(loginPage).Groups[1].Value;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "employee@maliev.com",
            ["Password"] = "password",
            ["__RequestVerificationToken"] = token,
        });
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Login", form)).StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex AntiForgeryToken();

    private sealed class MaterialIntranetFactory(ILegacyCatalogClient catalog, ILegacyAuthClient auth) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyCatalogClient>(); services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton(catalog); services.AddSingleton(auth);
            });
        }
    }

    private sealed class StubCatalogClient : ILegacyCatalogClient
    {
        private static readonly MaterialResponse Material = JsonSerializer.Deserialize<MaterialResponse>(
            """{"Id":42,"MaterialGroupId":1,"Machinable":true,"Printable":false,"Name":"4140","DensityKilogramPerCubicMeter":7850,"MaterialGroup":{"Id":1,"Name":"Steel"}}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        public string? LastAccessToken { get; private set; }
        public UpsertMaterialRequest? CreatedRequest { get; private set; }
        public Task<PaginatedMaterialResponse?> GetMaterialsAsync(MaterialSortType sort, string? search, int index, int size, string accessToken, CancellationToken cancellationToken) { LastAccessToken = accessToken; return Task.FromResult<PaginatedMaterialResponse?>(new([Material], 1, 1, 1, false, false)); }
        public Task<MaterialResponse?> GetMaterialAsync(int id, string accessToken, CancellationToken cancellationToken) { LastAccessToken = accessToken; return Task.FromResult<MaterialResponse?>(id == 42 ? Material : null); }
        public Task<IReadOnlyList<CountryResponse>> GetCountriesAsync(string accessToken, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CountryResponse>>([new(66, "Thailand", "Asia", "+66", "TH", "THA", null, null)]);
        public Task<IReadOnlyList<MaterialGroupResponse>> GetMaterialGroupsAsync(string accessToken, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MaterialGroupResponse>>([new(1, "Steel", null, null, null)]);
        public Task<IReadOnlyList<CurrencyResponse>> GetCurrenciesAsync(string accessToken, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CurrencyResponse>>([new(1, "THB", "Thai baht", null, null)]);
        public Task<IReadOnlyList<ColorResponse>> GetColorsAsync(string accessToken, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ColorResponse>>([new(1, "Black", null, null)]);
        public Task<IReadOnlyList<SurfaceFinishResponse>> GetSurfaceFinishesAsync(string accessToken, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SurfaceFinishResponse>>([new(1, "Polished", null, null)]);
        public Task<IReadOnlyList<ColorResponse>> GetMaterialColorsAsync(int id, string accessToken, CancellationToken cancellationToken) => GetColorsAsync(accessToken, cancellationToken);
        public Task<IReadOnlyList<SurfaceFinishResponse>> GetMaterialSurfaceFinishesAsync(int id, string accessToken, CancellationToken cancellationToken) => GetSurfaceFinishesAsync(accessToken, cancellationToken);
        public Task<MaterialResponse> CreateMaterialAsync(UpsertMaterialRequest request, string accessToken, CancellationToken cancellationToken) { CreatedRequest = request; LastAccessToken = accessToken; return Task.FromResult(Material); }
        public Task UpdateMaterialAsync(int id, UpsertMaterialRequest request, string accessToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SyncMaterialColorsAsync(int id, IReadOnlyCollection<int> selectedIds, string accessToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SyncMaterialSurfaceFinishesAsync(int id, IReadOnlyCollection<int> selectedIds, string accessToken, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubAuthClient : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => Task.FromResult(new EmployeeLoginResult(true, new("employee-access-token", "refresh", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)), new("id", email, email)));
        public Task<AuthTokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<AuthTokenResponse?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }
}