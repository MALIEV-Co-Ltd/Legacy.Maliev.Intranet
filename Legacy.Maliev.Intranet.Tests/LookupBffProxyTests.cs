extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;
using Proxy = Bff::Legacy.Maliev.Intranet.Bff.Catalog.LookupCatalogProxy;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Normal cookie/CSRF BFF tests; require the startup owner to integrate the documented hooks.</summary>
public sealed class LookupBffProxyTests
{
    [Fact]
    public async Task NormalCookieBoundaryForwardsAndFiltersWithOnlyServerServiceToken()
    {
        using var downstream = new Handler();
        await using var factory = new Factory(downstream, true);
        using var client = Client(factory);
        await SignInAsync(client);
        using var response = await client.GetAsync("/bff/lookups/thai-addresses/autocomplete?q=Bangkok&provinceCode=10&districtCode=1033&subdistrictCode=103301&postcode=๑๐๑๑๐&limit=20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("q=Bangkok&provinceCode=10&districtCode=1033&subdistrictCode=103301&postcode=10110", downstream.Path);
        Assert.Equal("Bearer service-only-token", downstream.Authorization);
        Assert.DoesNotContain("service-only-token", await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData(false, false, HttpStatusCode.Unauthorized)]
    [InlineData(true, false, HttpStatusCode.Forbidden)]
    public async Task MissingSessionOrPermissionStopsBeforeCatalog(bool signedIn, bool permission, HttpStatusCode expected)
    {
        using var downstream = new Handler();
        await using var factory = new Factory(downstream, permission);
        using var client = Client(factory);
        if (signedIn) await SignInAsync(client);
        using var response = await client.GetAsync("/bff/lookups/thai-addresses/provinces");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, downstream.Calls);
    }

    [Theory]
    [InlineData("postcode=1011")]
    [InlineData("provinceCode=abc")]
    [InlineData("limit=51")]
    [InlineData("provinceCode=10&provinceCode=50")]
    public async Task InvalidFilterDoesNotReachCatalog(string query)
    {
        using var downstream = new Handler();
        await using var factory = new Factory(downstream, true);
        using var client = Client(factory);
        await SignInAsync(client);
        using var response = await client.GetAsync("/bff/lookups/thai-addresses/autocomplete?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, downstream.Calls);
    }

    [Fact]
    public async Task PasteWithoutCsrfIsRejectedBeforeCatalog()
    {
        using var downstream = new Handler();
        await using var factory = new Factory(downstream, true);
        using var client = Client(factory);
        await SignInAsync(client);
        using var response = await client.PostAsJsonAsync("/bff/lookups/thai-addresses/resolve", new LookupResolveRequest("Bangkok 10110"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, downstream.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task CompanyFailureIsExplicitAndNotRetriedOrLeaked(HttpStatusCode failure)
    {
        using var downstream = new Handler { Status = failure };
        await using var factory = new Factory(downstream, true);
        using var client = Client(factory);
        await SignInAsync(client);
        using var response = await client.GetAsync("/bff/lookups/companies/search?q=test&queryType=name&language=en&limit=20");
        Assert.Equal(failure, response.StatusCode);
        Assert.Equal(1, downstream.Calls);
        Assert.DoesNotContain("private-provider-data", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MalformedSuccessfulPayloadIsBadGateway()
    {
        using var downstream = new Handler { Invalid = true };
        await using var factory = new Factory(downstream, true);
        using var client = Client(factory);
        await SignInAsync(client);
        using var response = await client.GetAsync("/bff/lookups/thai-addresses/autocomplete");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task PasteWithCookieAndCsrfForwardsBoundedTextAndPreservesResolution()
    {
        const string text = "1 Main Road Bangkok 10110";
        using var downstream = new Handler();
        await using var factory = new Factory(downstream, true);
        using var client = Client(factory);
        await SignInAsync(client);
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/lookups/thai-addresses/resolve")
        { Content = JsonContent.Create(new LookupResolveRequest(text, new(Postcode: "10110"))) };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LookupResolveResponse>();
        Assert.Equal(text, result?.OriginalText);
        Assert.Equal("1 Main Road", result?.DetailText);
        Assert.Equal("exact", result?.Outcome);
        Assert.Equal("/api/v1/thai-addresses/resolve", downstream.Path);
        Assert.Equal("Bearer service-only-token", downstream.Authorization);
        Assert.Equal(text, downstream.ResolveInput?.Text);
        Assert.Equal("10110", downstream.ResolveInput?.Constraints?.Postcode);
        Assert.Equal(1, downstream.Calls);
    }

    [Theory]
    [InlineData(0, false, HttpStatusCode.BadRequest)]
    [InlineData(2049, false, HttpStatusCode.BadRequest)]
    [InlineData(1, true, HttpStatusCode.RequestEntityTooLarge)]
    public async Task InvalidOrOversizedPastedBodyStopsBeforeCatalog(int textLength, bool oversized, HttpStatusCode expected)
    {
        using var downstream = new Handler();
        await using var factory = new Factory(downstream, true);
        using var client = Client(factory);
        await SignInAsync(client);
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        var payload = JsonSerializer.Serialize(new { text = new string('x', textLength) });
        if (oversized) payload = new string(' ', 17 * 1024) + payload;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/lookups/thai-addresses/resolve")
        { Content = new UnknownLengthContent(payload) };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, downstream.Calls);
    }

    private static HttpClient Client(WebApplicationFactory<BffProgram> factory) => factory.CreateClient(new()
    { AllowAutoRedirect = false, BaseAddress = new("https://localhost"), HandleCookies = true });

    private static async Task SignInAsync(HttpClient client)
    {
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        { Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Suppliers/Create" }) };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed class Factory(Handler downstream, bool permission) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            foreach (var service in new[] { "Auth", "Catalog", "Customer", "Employee", "Order", "Procurement" })
                builder.UseSetting("Services:" + service, "http://" + service.ToLowerInvariant() + "/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new Auth(permission));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokens = new Tokens();
                services.AddSingleton<IServiceAccessTokenProvider>(tokens);
                services.RemoveAll<Proxy>();
                var authenticated = new LegacyServiceAuthenticationHandler(tokens) { InnerHandler = downstream };
                services.AddSingleton(new Proxy(new HttpClient(authenticated)
                { BaseAddress = new("http://catalog/"), Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 256 * 1024 }));
            });
        }
    }

    private sealed class Tokens : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken ct) => ValueTask.FromResult<string?>("service-only-token");
        public void Invalidate(string token) { }
    }
    private sealed class Auth(bool permission) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken ct) => Task.FromResult(new EmployeeLoginResult(true,
            new("employee-only-token", "employee-only-refresh", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
            new("employee", email, email, permission ? ["legacy-catalog.locations.read", "legacy-catalog.companies.read"] : [], 7)));
        public Task<EmployeeRefreshResult?> RefreshAsync(string token, CancellationToken ct) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string token, CancellationToken ct) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int id, CreateCustomerIdentityRequest input, string token, CancellationToken ct) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int id, CreateEmployeeIdentityRequest input, string token, CancellationToken ct) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }
    private sealed class UnknownLengthContent(string payload) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(payload));
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public bool Invalid { get; init; }
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }
        public int Calls { get; private set; }
        public LookupResolveRequest? ResolveInput { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Path = request.RequestUri?.PathAndQuery; Authorization = request.Headers.Authorization?.ToString();
            if (request.Method == HttpMethod.Post)
            {
                ResolveInput = await request.Content!.ReadFromJsonAsync<LookupResolveRequest>(cancellationToken: ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new LookupResolveResponse(
                    "v1", ResolveInput!.Text, ResolveInput.Text, "exact", [LookupBehaviorTests.Combination], false,
                    new(null, null, null, null), "1 Main Road", [], []))
                };
            }
            HttpContent content = Status != HttpStatusCode.OK ? new StringContent("private-provider-data") :
                Invalid ? new StringContent("{}", System.Text.Encoding.UTF8, "application/json") :
                JsonContent.Create(new LookupAddressPage("v1", [LookupBehaviorTests.Combination], false, null));
            return new HttpResponseMessage(Status) { Content = content };
        }
    }
}
