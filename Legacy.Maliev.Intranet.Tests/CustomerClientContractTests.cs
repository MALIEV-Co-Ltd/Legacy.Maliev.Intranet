using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Customers;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomerClientContractTests
{
    [Fact]
    public async Task ListCustomers_ForwardsBearerTokenAndLegacyQueryShape()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK,
            """{"Items":[],"PageIndex":2,"TotalPages":4,"TotalRecords":75,"HasNextPage":true,"HasPreviousPage":true}""");
        var client = new LegacyCustomerClient(new HttpClient(handler) { BaseAddress = new("http://customer/") });

        var result = await client.GetCustomersAsync(
            CustomerSortType.CustomerEmail_Ascending, "ada@example.com", 2, 25, "employee-token", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("/customers?sort=CustomerEmail_Ascending&search=ada%40example.com&index=2&size=25", handler.RequestUri?.PathAndQuery);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("employee-token", handler.AuthorizationParameter);
    }

    [Fact]
    public async Task CreateIdentity_CarriesPasswordOnlyInJsonBody()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created,
            """{"Id":"identity-id","UserName":"ada@example.com","Email":"ada@example.com","EmailConfirmed":true,"PhoneNumber":null,"PhoneNumberConfirmed":false,"TwoFactorEnabled":false,"LockoutEnd":null,"LockoutEnabled":true,"AccessFailedCount":0,"DatabaseID":42,"FaxNumber":null,"MobileNumber":null}""");
        var client = new LegacyAuthClient(new HttpClient(handler) { BaseAddress = new("http://auth/") },
            new TestAccessTokenValidator(),
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LegacyAuthClient>.Instance);

        var result = await client.CreateCustomerIdentityAsync(
            42,
            new CreateCustomerIdentityRequest("ada@example.com", "ada@example.com", "secret / ?", true, null, null, null),
            "employee-token",
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("/auth/v1/customer-identities/42", handler.RequestUri?.AbsolutePath);
        Assert.DoesNotContain("secret", handler.RequestUri?.ToString(), StringComparison.Ordinal);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("secret / ?", json.RootElement.GetProperty("password").GetString());
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
