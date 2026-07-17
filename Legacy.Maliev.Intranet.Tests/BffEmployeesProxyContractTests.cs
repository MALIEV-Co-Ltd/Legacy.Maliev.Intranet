extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffEmployeesProxyContractTests
{
    [Fact]
    public async Task AuthorizedEmployee_ForwardsExactQueryAndServerOnlyBearerToken()
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.OK, EmployeePageJson);
        await using var factory = new EmployeesBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync(
            "/bff/employees?sort=EmployeeEmail_Ascending&search=ada%20lovelace&index=2&size=25");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/employees?sort=EmployeeEmail_Ascending&search=ada%20lovelace&index=2&size=25", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Contains("\"pageIndex\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"fullName\":\"Ada Lovelace\"", json, StringComparison.Ordinal);
        Assert.Contains("\"role\":{\"id\":7,\"name\":\"Engineer\"}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only-access-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("signed-service-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("phoneNumber", json, StringComparison.Ordinal);
        Assert.DoesNotContain("dateOfBirth", json, StringComparison.Ordinal);
        Assert.DoesNotContain("homeAddress", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmployeeWithoutExactPermission_IsForbiddenBeforeEmployeeServiceCall()
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.OK, EmployeePageJson);
        await using var factory = new EmployeesBffFactory(downstream, hasPermission: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/employees");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task AnonymousRequest_IsUnauthorizedBeforeEmployeeServiceCall()
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.OK, EmployeePageJson);
        await using var factory = new EmployeesBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/employees");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task InvalidPaging_IsClampedAndNotFoundBecomesAnEmptyPage()
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.NotFound, "{}");
        await using var factory = new EmployeesBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/employees?index=-5&size=999");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/employees?sort=EmployeeId_Descending&search=&index=1&size=250", downstream.PathAndQuery);
        Assert.Contains("\"items\":[]", json, StringComparison.Ordinal);
        Assert.Contains("\"pageIndex\":1", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task DownstreamAuthorizationFailure_IsPreserved(HttpStatusCode statusCode)
    {
        var downstream = new RecordingEmployeeHandler(statusCode, "{}");
        await using var factory = new EmployeesBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/employees");

        Assert.Equal(statusCode, response.StatusCode);
    }

    [Fact]
    public async Task RateLimit_PreservesStatusAndBoundedRetryAfter()
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.TooManyRequests, "{}", retryAfterSeconds: 1);
        await using var factory = new EmployeesBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/employees");

        Assert.Equal(1, downstream.RequestCount);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task InvalidPayload_IsMappedToBadGatewayWithoutLeakingPayload()
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.OK, "not-json");
        await using var factory = new EmployeesBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/employees");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("not-json", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidPageShape_IsMappedToBadGateway()
    {
        var downstream = new RecordingEmployeeHandler(
            HttpStatusCode.OK,
            """{"Items":null,"PageIndex":1,"TotalPages":0,"TotalRecords":0,"HasNextPage":false,"HasPreviousPage":false}""");
        await using var factory = new EmployeesBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/employees");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task TransportFailure_IsMappedToServiceUnavailable(Exception exception)
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.OK, "{}", exception: exception);
        await using var factory = new EmployeesBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/employees");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    public static TheoryData<Exception> TransportFailures => new()
    {
        new HttpRequestException("employee unavailable"),
        new TaskCanceledException("employee timeout"),
    };

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static async Task SignInAsync(HttpClient client)
    {
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new
            {
                email = "employee@maliev.com",
                password = "password",
                returnUrl = "/Employees/Index",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed class EmployeesBffFactory(HttpMessageHandler downstream, bool hasPermission)
        : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Services:Auth", "http://auth/");
            builder.UseSetting("Services:Catalog", "http://catalog/");
            builder.UseSetting("Services:Customer", "http://customer/");
            builder.UseSetting("Services:Employee", "http://employee/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new EmployeesAuthClient(hasPermission));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokenProvider = new EmployeesServiceTokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokenProvider);

                var proxyType = typeof(BffProgram).Assembly.GetType(
                    "Legacy.Maliev.Intranet.Bff.Employees.EmployeesProxy");
                if (proxyType is null)
                {
                    return;
                }

                for (var index = services.Count - 1; index >= 0; index--)
                {
                    if (services[index].ServiceType == proxyType)
                    {
                        services.RemoveAt(index);
                    }
                }

                var authHandler = new LegacyServiceAuthenticationHandler(tokenProvider)
                {
                    InnerHandler = downstream,
                };
                var httpClient = new HttpClient(authHandler)
                {
                    BaseAddress = new Uri("http://employee/"),
                    Timeout = TimeSpan.FromSeconds(10),
                };
                var proxy = Activator.CreateInstance(proxyType, httpClient)
                    ?? throw new InvalidOperationException("EmployeesProxy could not be created.");
                services.AddSingleton(proxyType, proxy);
            });
        }
    }

    private sealed class EmployeesServiceTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("signed-service-token");

        public void Invalidate(string token)
        {
        }
    }

    private sealed class EmployeesAuthClient(bool hasPermission) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse(
                    "server-only-access-token",
                    "server-only-refresh-token",
                    "Bearer",
                    900,
                    DateTimeOffset.UtcNow.AddDays(1)),
                new EmployeeIdentity(
                    "employee-id",
                    email,
                    email,
                    hasPermission ? ["legacy-employee.employees.list"] : [])));

        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
            Task.FromResult<EmployeeRefreshResult?>(null);

        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(
            int databaseId,
            CreateCustomerIdentityRequest request,
            string accessToken,
            CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);

        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(
            int databaseId,
            CreateEmployeeIdentityRequest request,
            string accessToken,
            CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class RecordingEmployeeHandler(
        HttpStatusCode statusCode,
        string body,
        int? retryAfterSeconds = null,
        Exception? exception = null) : HttpMessageHandler
    {
        public string? PathAndQuery { get; private set; }
        public string? Authorization { get; private set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
            if (exception is not null)
            {
                throw exception;
            }

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (retryAfterSeconds is not null)
            {
                response.Headers.RetryAfter = new(TimeSpan.FromSeconds(retryAfterSeconds.Value));
            }

            return Task.FromResult(response);
        }
    }

    private const string EmployeePageJson =
        """{"Items":[{"Id":42,"RoleId":7,"FirstName":"Ada","LastName":"Lovelace","FullName":"Ada Lovelace","PhoneNumber":null,"Email":"ada@example.com","DateOfBirth":null,"HomeAddressId":null,"CreatedDate":null,"ModifiedDate":null,"HomeAddress":null,"Role":{"Id":7,"Name":"Engineer","Description":null,"CreatedDate":null,"ModifiedDate":null}}],"PageIndex":2,"TotalPages":4,"TotalRecords":75,"HasNextPage":true,"HasPreviousPage":true}""";
}
