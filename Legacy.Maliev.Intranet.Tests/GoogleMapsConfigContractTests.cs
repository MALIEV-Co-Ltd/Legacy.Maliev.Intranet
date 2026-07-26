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
using Legacy.Maliev.Intranet.Auth;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class GoogleMapsConfigContractTests
{
    [Fact]
    public async Task GoogleConfig_AnonymousRequest_IsRejected()
    {
        await using var factory = new GoogleMapsBffFactory(hasCustomerRead: false, browserApiKey: "browser-key");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/address/google-config");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GoogleConfig_CustomerReader_ReceivesOnlyBrowserSafeContract()
    {
        await using var factory = new GoogleMapsBffFactory(hasCustomerRead: true, browserApiKey: "browser-key");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/address/google-config");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"apiKey\":\"browser-key\"", body, StringComparison.Ordinal);
        Assert.Contains("\"defaultLatitude\":13.7563", body, StringComparison.Ordinal);
        Assert.Contains("\"defaultLongitude\":100.5018", body, StringComparison.Ordinal);
        Assert.Contains("\"defaultZoom\":12", body, StringComparison.Ordinal);
        Assert.Contains("\"includedRegionCodes\":[\"th\"]", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EmbedApiKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ServiceAuthentication", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Jwt", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GoogleConfig_MissingBrowserKey_DoesNotFallbackToServerOnlyKey()
    {
        await using var factory = new GoogleMapsBffFactory(
            hasCustomerRead: true,
            browserApiKey: null,
            embedApiKey: "server-only-embed-key");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/address/google-config");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"apiKey\":\"\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only-embed-key", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GoogleConfig_UsesConfiguredMapDefaultsAndRegionAllowList()
    {
        await using var factory = new GoogleMapsBffFactory(
            hasCustomerRead: true,
            browserApiKey: "browser-key",
            mapId: "maliev-map",
            defaultLatitude: "14.1234",
            defaultLongitude: "100.5678",
            defaultZoom: "15",
            includedRegionCodes: ["TH", " sg ", "", "th"]);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/address/google-config");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("browser-key", root.GetProperty("apiKey").GetString());
        Assert.Equal("maliev-map", root.GetProperty("mapId").GetString());
        Assert.Equal(14.1234, root.GetProperty("defaultLatitude").GetDouble(), precision: 4);
        Assert.Equal(100.5678, root.GetProperty("defaultLongitude").GetDouble(), precision: 4);
        Assert.Equal(15, root.GetProperty("defaultZoom").GetInt32());
        Assert.Equal(["th", "sg"], root.GetProperty("includedRegionCodes").EnumerateArray()
            .Select(element => element.GetString()!)
            .ToArray());
    }

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    private sealed class GoogleMapsBffFactory(
        bool hasCustomerRead,
        string? browserApiKey,
        string? embedApiKey = null,
        string? mapId = null,
        string? defaultLatitude = null,
        string? defaultLongitude = null,
        string? defaultZoom = null,
        string[]? includedRegionCodes = null) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Services:Auth", "http://auth/");
            if (browserApiKey is not null)
            {
                builder.UseSetting("GoogleMaps:BrowserApiKey", browserApiKey);
            }

            if (embedApiKey is not null)
            {
                builder.UseSetting("GoogleMaps:EmbedApiKey", embedApiKey);
            }

            if (mapId is not null)
            {
                builder.UseSetting("GoogleMaps:MapId", mapId);
            }

            if (defaultLatitude is not null)
            {
                builder.UseSetting("GoogleMaps:DefaultLatitude", defaultLatitude);
            }

            if (defaultLongitude is not null)
            {
                builder.UseSetting("GoogleMaps:DefaultLongitude", defaultLongitude);
            }

            if (defaultZoom is not null)
            {
                builder.UseSetting("GoogleMaps:DefaultZoom", defaultZoom);
            }

            for (var index = 0; index < (includedRegionCodes?.Length ?? 0); index++)
            {
                builder.UseSetting($"GoogleMaps:IncludedRegionCodes:{index}", includedRegionCodes![index]);
            }

            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = GoogleMapsAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = GoogleMapsAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<GoogleMapsAuthenticationOptions, GoogleMapsAuthenticationHandler>(
                        GoogleMapsAuthenticationHandler.SchemeName,
                        options => options.HasCustomerRead = hasCustomerRead);
            });
        }
    }

    private sealed class GoogleMapsAuthenticationOptions : AuthenticationSchemeOptions
    {
        public bool HasCustomerRead { get; set; }
    }

    private sealed class GoogleMapsAuthenticationHandler(
        IOptionsMonitor<GoogleMapsAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<GoogleMapsAuthenticationOptions>(options, logger, encoder)
    {
        public const string SchemeName = "GoogleMapsContractTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Options.HasCustomerRead)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "employee-id"),
                new(ClaimTypes.Name, "MALIEV Employee"),
            };
            claims.Add(new Claim("permissions", LegacyEmployeePermissions.CustomersRead));

            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
