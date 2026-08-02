extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffEmployeeSelfProfileContractTests
{
    [Fact]
    public void UpdateContract_ContainsOnlyEmployeeOwnedFieldsAndRejectsUnknownJson()
    {
        Assert.Equal(
            ["DateOfBirth", "FirstName", "LastName", "PhoneNumber"],
            typeof(EmployeeSelfProfileUpdateRequest).GetProperties().Select(property => property.Name).Order());

        var json = """{"FirstName":"Ada","LastName":"Lovelace","PhoneNumber":"0690","DateOfBirth":null,"Email":"other@example.com"}""";

        Assert.Throws<System.Text.Json.JsonException>(
            () => System.Text.Json.JsonSerializer.Deserialize<EmployeeSelfProfileUpdateRequest>(json));
    }

    [Fact]
    public async Task GET_Profile_UsesServerOwnedLegacyIdAndServiceCredential()
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.OK, EmployeeDetailJson);
        await using var factory = new SelfProfileBffFactory(downstream, legacyDatabaseId: 42);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpMethod.Get, downstream.Method);
        Assert.Equal("/employees/42", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
    }

    [Fact]
    public async Task PUT_Profile_ForwardsOnlyEmployeeOwnedFieldsToSessionEmployee()
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.NoContent, string.Empty);
        await using var factory = new SelfProfileBffFactory(downstream, legacyDatabaseId: 42);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/profile")
        {
            Content = JsonContent.Create(new
            {
                firstName = "Ada",
                lastName = "Lovelace",
                phoneNumber = "0690",
                dateOfBirth = "1815-12-10T00:00:00",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpMethod.Put, downstream.Method);
        Assert.Equal("/employees/42/profile", downstream.PathAndQuery);
        Assert.Contains("\"firstName\":\"Ada\"", downstream.Body, StringComparison.Ordinal);
        Assert.Contains("\"lastName\":\"Lovelace\"", downstream.Body, StringComparison.Ordinal);
        Assert.Contains("\"phoneNumber\":\"0690\"", downstream.Body, StringComparison.Ordinal);
        Assert.Contains("\"dateOfBirth\":\"1815-12-10T00:00:00\"", downstream.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("id", downstream.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", downstream.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("role", downstream.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("address", downstream.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{\"firstName\":\"Ada\"")]
    [InlineData("{\"firstName\":\"Ada\",\"lastName\":\"Lovelace\",\"phoneNumber\":null,\"dateOfBirth\":null,\"employeeId\":99}")]
    [InlineData("{\"firstName\":\"\",\"lastName\":\"Lovelace\",\"phoneNumber\":null,\"dateOfBirth\":null}")]
    public async Task PUT_Profile_InvalidOrExpandedPayload_IsRejectedBeforeEmployeeService(string json)
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.NoContent, string.Empty);
        await using var factory = new SelfProfileBffFactory(downstream, legacyDatabaseId: 42);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/profile")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task PUT_Profile_WithoutAntiforgeryToken_IsRejectedBeforeEmployeeService()
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.NoContent, string.Empty);
        await using var factory = new SelfProfileBffFactory(downstream, legacyDatabaseId: 42);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.PutAsJsonAsync(
            "/bff/profile",
            new { firstName = "Ada", lastName = "Lovelace", phoneNumber = "0690", dateOfBirth = (DateTime?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task Profile_MissingServerOwnedLegacyId_IsForbiddenBeforeEmployeeService()
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.OK, EmployeeDetailJson);
        await using var factory = new SelfProfileBffFactory(downstream, legacyDatabaseId: null);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/profile");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task Profile_AnonymousRequest_IsUnauthorizedBeforeEmployeeService()
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.OK, EmployeeDetailJson);
        await using var factory = new SelfProfileBffFactory(downstream, legacyDatabaseId: 42);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task GET_Profile_MalformedEmployeeServiceResponse_FailsClosed()
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.OK, "not-json");
        await using var factory = new SelfProfileBffFactory(downstream, legacyDatabaseId: 42);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/profile");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task GET_Profile_MismatchedEmployeeServiceId_FailsClosed()
    {
        var downstream = new RecordingEmployeeHandler(
            HttpStatusCode.OK,
            EmployeeDetailJson.Replace("\"Id\":42", "\"Id\":99", StringComparison.Ordinal));
        await using var factory = new SelfProfileBffFactory(downstream, legacyDatabaseId: 42);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/profile");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task Profile_TransportFailure_IsServiceUnavailable(Exception exception)
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.OK, string.Empty, exception: exception);
        await using var factory = new SelfProfileBffFactory(downstream, legacyDatabaseId: 42);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/profile");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    public static TheoryData<Exception> TransportFailures => new()
    {
        new HttpRequestException("employee unavailable"),
        new TaskCanceledException("employee timeout"),
    };

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task PUT_Profile_ExpectedDownstreamFailure_IsPreserved(HttpStatusCode statusCode)
    {
        var downstream = new RecordingEmployeeHandler(statusCode, string.Empty, retryAfterSeconds: 2);
        await using var factory = new SelfProfileBffFactory(downstream, legacyDatabaseId: 42);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/profile")
        {
            Content = JsonContent.Create(new { firstName = "Ada", lastName = "Lovelace", phoneNumber = "0690", dateOfBirth = (DateTime?)null }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(statusCode, response.StatusCode);
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            Assert.Equal(TimeSpan.FromSeconds(2), response.Headers.RetryAfter?.Delta);
        }
    }

    [Fact]
    public async Task PUT_Profile_UnexpectedSuccessfulResponse_FailsClosed()
    {
        var downstream = new RecordingEmployeeHandler(HttpStatusCode.OK, "{\"unexpected\":true}");
        await using var factory = new SelfProfileBffFactory(downstream, legacyDatabaseId: 42);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/profile")
        {
            Content = JsonContent.Create(new { firstName = "Ada", lastName = "Lovelace", phoneNumber = "0690", dateOfBirth = (DateTime?)null }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static async Task<string> SignInAsync(HttpClient client)
    {
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new
            {
                email = "employee@maliev.com",
                password = "password",
                returnUrl = "/hr/profile",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var authenticatedSessionResponse = await client.GetAsync("/bff/session");
        var authenticatedSession = await authenticatedSessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return authenticatedSession.GetProperty("csrfToken").GetString()
            ?? throw new InvalidOperationException("The BFF did not issue an antiforgery token.");
    }

    private sealed class SelfProfileBffFactory(HttpMessageHandler downstream, int? legacyDatabaseId)
        : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Services:Auth", "http://auth/");
            builder.UseSetting("Services:Employee", "http://employee/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new SelfProfileAuthClient(legacyDatabaseId));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokenProvider = new SelfProfileServiceTokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokenProvider);

                var proxyType = typeof(BffProgram).Assembly.GetType("Legacy.Maliev.Intranet.Bff.Employees.EmployeesProxy")
                    ?? throw new InvalidOperationException("EmployeesProxy was not found.");
                services.RemoveAll(proxyType);
                var authHandler = new LegacyServiceAuthenticationHandler(tokenProvider) { InnerHandler = downstream };
                var httpClient = new HttpClient(authHandler)
                {
                    BaseAddress = new Uri("http://employee/"),
                    Timeout = TimeSpan.FromSeconds(10),
                };
                services.AddSingleton(proxyType, Activator.CreateInstance(proxyType, httpClient)
                    ?? throw new InvalidOperationException("EmployeesProxy could not be created."));
            });
        }
    }

    private sealed class SelfProfileServiceTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("signed-service-token");

        public void Invalidate(string token)
        {
        }
    }

    private sealed class SelfProfileAuthClient(int? legacyDatabaseId) : ILegacyAuthClient
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
                new EmployeeIdentity("employee-id", email, email, [], legacyDatabaseId)));

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
        public HttpMethod? Method { get; private set; }
        public string? PathAndQuery { get; private set; }
        public string? Authorization { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
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

            return response;
        }
    }

    private const string EmployeeDetailJson =
        """{"Id":42,"RoleId":7,"FirstName":"Ada","LastName":"Lovelace","FullName":"Ada Lovelace","PhoneNumber":"0690","Email":"ada@example.com","DateOfBirth":"1815-12-10T00:00:00","HomeAddressId":13,"CreatedDate":null,"ModifiedDate":null,"HomeAddress":null,"Role":{"Id":7,"Name":"Engineer","Description":null,"CreatedDate":null,"ModifiedDate":null}}""";
}
