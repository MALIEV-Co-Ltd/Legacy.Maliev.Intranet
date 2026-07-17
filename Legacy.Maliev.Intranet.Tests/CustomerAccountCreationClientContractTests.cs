using System.Net;
using System.Net.Http.Headers;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Customers;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomerAccountCreationClientContractTests
{
    [Fact]
    public async Task ProfileCreate_UsesExactCustomerServiceJsonWithoutPassword()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, "{\"id\":42}");
        var client = new CustomerProfileCreationClient(CreateHttpClient(handler));

        using var response = await client.CreateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/customers", handler.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", handler.Authorization);
        Assert.Contains("\"firstName\":\"Ada\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"lastName\":\"Lovelace\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"email\":\"ada@example.com\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"companyId\":null", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("password", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confirm", handler.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IdentityCreate_UsesExactAuthServiceJsonAndPasswordNeverEntersUrl()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, "{\"databaseID\":42}");
        var client = new CustomerIdentityCreationClient(CreateHttpClient(handler));

        using var response = await client.CreateAsync(42, ValidRequest(), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/auth/v1/customer-identities/42", handler.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", handler.Authorization);
        Assert.Contains("\"userName\":\"ada@example.com\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"password\":\"correct horse battery staple\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"emailConfirmed\":true", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("confirmPassword", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("password", handler.PathAndQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProfileCompensation_UsesOwnedDeleteRoute()
    {
        var handler = new RecordingHandler(HttpStatusCode.NoContent, string.Empty);
        var client = new CustomerProfileCreationClient(CreateHttpClient(handler));

        using var response = await client.DeleteAsync(42, CancellationToken.None);

        Assert.Equal(HttpMethod.Delete, handler.Method);
        Assert.Equal("/customers/42", handler.PathAndQuery);
        Assert.Null(handler.Body);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://downstream") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "signed-service-token");
        return client;
    }

    private static CreateCustomerAccountRequest ValidRequest() => new()
    {
        FirstName = "Ada",
        LastName = "Lovelace",
        Email = "ada@example.com",
        Password = "correct horse battery staple",
        ConfirmPassword = "correct horse battery staple",
        Telephone = "+66 2 123 4567",
        Mobile = "+66 81 234 5678",
        Fax = "+66 2 765 4321",
        DateOfBirth = new DateTime(1815, 12, 10),
    };

    private sealed class RecordingHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? PathAndQuery { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body),
            };
        }
    }
}
