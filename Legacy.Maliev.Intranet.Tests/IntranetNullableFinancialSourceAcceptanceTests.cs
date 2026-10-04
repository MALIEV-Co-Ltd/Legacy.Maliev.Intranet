extern alias Bff;

using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.Tokens;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

// Source portions: 283832f6c99fdf4445113be6f9ebb985b958c458 (nullable invoice),
// 6c1a4921b3524dd575cab5b1f4aa5774ad9b8997 (server-owned employee decision).
// Real registered BFF/auth/session/proxies/aggregator; only external primary HTTP is controlled.
// This does not establish real AuthService/QuotationService or persistent-data acceptance.
public sealed class IntranetNullableFinancialSourceAcceptanceTests
{
    [Fact]
    public async Task Invoice_SuccessfulNullPayload_ReturnsSafe503BeforeDependentCalls()
    {
        await using var host = new Host(request => request.RequestUri!.AbsolutePath == "/invoices/7"
            ? Json("null") : throw new InvalidOperationException("Invoice dependants must not be loaded."));
        using var client = await host.SignInAsync();

        using var response = await client.GetAsync("/bff/invoices/7");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Invoice workflow unavailable", body.RootElement.GetProperty("title").GetString());
        Assert.False(body.RootElement.TryGetProperty("invoice", out _));
        var request = Assert.Single(host.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/invoices/7", request.Path);
    }

    [Fact]
    public async Task Invoice_NullReceiptId_DoesNotLoadReceiptMetadata()
    {
        await using var host = new Host(request => request.RequestUri!.AbsolutePath switch
        {
            "/invoices/7" => Json(InvoiceJson),
            "/invoices/7/orderitems" or "/invoices/7/files" => Json("[]"),
            _ => throw new InvalidOperationException("A null receipt must not produce a receipt or signing request."),
        });
        using var client = await host.SignInAsync();

        using var response = await client.GetAsync("/bff/invoices/7");
        var detail = await response.Content.ReadFromJsonAsync<InvoiceDetailPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(detail);
        Assert.Null(detail.Invoice.ReceiptId);
        Assert.Empty(detail.ReceiptFiles);
        Assert.Equal(new[] { "/invoices/7", "/invoices/7/files", "/invoices/7/orderitems" },
            host.Requests.Select(request => request.Path).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invoice_NullOrMissingDependentCollections_ReturnsEmptyCollectionsWithoutSigning(bool missing)
    {
        await using var host = new Host(request => request.RequestUri!.AbsolutePath switch
        {
            "/invoices/7" => Json(InvoiceJson.Replace("\"receiptId\":null", "\"receiptId\":9", StringComparison.Ordinal)),
            "/invoices/7/orderitems" or "/invoices/7/files" or "/receipts/9/files" =>
                missing ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json("null"),
            _ => throw new InvalidOperationException("Empty metadata must not produce a signing request."),
        });
        using var client = await host.SignInAsync();

        using var response = await client.GetAsync("/bff/invoices/7");
        var detail = await response.Content.ReadFromJsonAsync<InvoiceDetailPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(detail);
        Assert.Equal(7, detail.Invoice.Id);
        Assert.Equal(9, detail.Invoice.ReceiptId);
        Assert.Empty(detail.OrderItems);
        Assert.Empty(detail.InvoiceFiles);
        Assert.Empty(detail.ReceiptFiles);
        Assert.Equal(new[] { "/invoices/7", "/invoices/7/files", "/invoices/7/orderitems", "/receipts/9/files" },
            host.Requests.Select(request => request.Path).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(true, true, HttpStatusCode.OK)]
    [InlineData(false, true, HttpStatusCode.Forbidden)]
    [InlineData(true, false, HttpStatusCode.BadRequest)]
    public async Task Decline_RegisteredBoundary_RequiresPermissionAndCsrfBeforeServerOwnedDecision(
        bool permission, bool csrf, HttpStatusCode expectedStatus)
    {
        // Literal controlled response proves BFF forwarding/projection, not real producer persistence or calculation.
        await using var host = new Host(_ => Json(
            "{\"status\":0,\"completedOrders\":2,\"totalOrders\":2,\"modifiedDate\":\"2030-07-18T08:31:00Z\"}"), permission);
        using var client = await host.SignInAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/quotations/84/decision")
        {
            Content = JsonContent.Create(new { accepted = false, expectedModifiedDate = "2030-07-18T08:30:00Z" }),
        };
        if (csrf) request.Headers.Add("X-CSRF-TOKEN", await host.GetCsrfAsync(client));

        using var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus != HttpStatusCode.OK)
        {
            Assert.Empty(host.Requests);
            return;
        }
        var forwarded = Assert.Single(host.Requests);
        Assert.Equal("PUT", forwarded.Method);
        Assert.Equal("/quotations/84/decision", forwarded.Path);
        Assert.Equal("2030-07-18T08:30:00.0000000Z", forwarded.ExpectedModifiedDate);
        using var payload = JsonDocument.Parse(Assert.IsType<string>(forwarded.Body));
        Assert.Equal(new[] { "accepted", "employeeInitiated" },
            payload.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.False(payload.RootElement.GetProperty("accepted").GetBoolean());
        Assert.True(payload.RootElement.GetProperty("employeeInitiated").GetBoolean());
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Completed", result.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, result.RootElement.GetProperty("completedOrders").GetInt32());
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private const string InvoiceJson = """
        {"id":7,"customerId":3,"number":"INV-7","comment":null,"internalComment":null,"salesPerson":null,"currency":"THB","purchaseOrderNumber":null,"requisitioner":null,"shippedVia":null,"fob":null,"terms":null,"billingAddressRecipient":null,"billingAddressCompany":null,"billingAddressBuilding":null,"billingAddressLine1":null,"billingAddressLine2":null,"billingAddressCity":null,"billingAddressState":null,"billingAddressPostalCode":null,"billingAddressCountry":null,"shippingAddressRecipient":null,"shippingAddressRecipientTelephone":null,"shippingAddressCompany":null,"shippingAddressBuilding":null,"shippingAddressLine1":null,"shippingAddressLine2":null,"shippingAddressCity":null,"shippingAddressState":null,"shippingAddressPostalCode":null,"shippingAddressCountry":null,"commercialRegistration":null,"taxIdentification":null,"subtotal":100,"vat":7,"total":107,"withholdingTax":null,"outstanding":107,"isPaid":false,"receiptId":null,"paymentDate":null,"createdDate":"2030-07-18T00:00:00Z","modifiedDate":"2030-07-18T08:30:00Z"}
        """;

    private sealed record ExternalRequest(string Method, string Path, string? Body, string? ExpectedModifiedDate);

    private sealed class Host : IAsyncDisposable
    {
        private readonly RSA key = RSA.Create(2048);
        private readonly string password = Guid.NewGuid().ToString("N");
        // Synthetic identity must satisfy the real workspace admission contract.
        private readonly string email = $"fixture-{Guid.NewGuid():N}@{WorkspaceIdentityRules.AllowedEmailDomain}";
        private readonly string serviceSecret = Guid.NewGuid().ToString("N");
        private readonly string serviceToken = Guid.NewGuid().ToString("N");
        private readonly string refreshToken = Guid.NewGuid().ToString("N");
        private readonly bool decisionPermission;
        private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;
        private readonly Factory factory;
        public ConcurrentQueue<ExternalRequest> Requests { get; } = new();

        public Host(Func<HttpRequestMessage, HttpResponseMessage> respond, bool decisionPermission = true)
        {
            this.respond = respond;
            this.decisionPermission = decisionPermission;
            factory = new Factory(this);
        }

        public async Task<HttpClient> SignInAsync()
        {
            var client = factory.CreateClient(new()
            {
                BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true,
            });
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
                {
                    Content = JsonContent.Create(new { email, password, returnUrl = "/Dashboard" }),
                };
                request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                return client;
            }
            catch { client.Dispose(); throw; }
        }

        public async Task<string> GetCsrfAsync(HttpClient client)
        {
            using var response = await client.GetAsync("/bff/session");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return Assert.IsType<string>(body.RootElement.GetProperty("csrfToken").GetString());
        }

        private string EmployeeToken()
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, "fixture-employee"),
                new(JwtRegisteredClaimNames.Name, "Fixture employee"),
                new(JwtRegisteredClaimNames.Email, email), new("identity_kind", "employee"),
                new("legacy_database_id", "7"),
                new("permissions", LegacyEmployeePermissions.AccountingRead),
                new("permissions", LegacyEmployeePermissions.AccountingFilesRead),
                new("permissions", LegacyEmployeePermissions.FileUploadsRead),
            };
            if (decisionPermission) claims.Add(new("permissions", LegacyEmployeePermissions.QuotationsUpdate));
            var signingKey = new RsaSecurityKey(key) { KeyId = "financial-fixture-key" };
            var token = new JwtSecurityToken("https://auth.test", "legacy-test", claims,
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(15),
                new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private void Configure(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Jwt:PublicKeyPem", key.ExportSubjectPublicKeyInfoPem());
            builder.UseSetting("Jwt:KeyId", "financial-fixture-key");
            foreach (var service in new[] { "Auth", "Accounting", "File", "Quotation" })
                builder.UseSetting($"Services:{service}", $"https://{service.ToLowerInvariant()}.invalid/");
            builder.UseSetting("ServiceAuthentication:ClientId", "financial-fixture");
            builder.UseSetting("ServiceAuthentication:ClientSecret", serviceSecret);
            builder.ConfigureServices(services => services.PostConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handler =>
                {
                    var previous = handler.PrimaryHandler;
                    handler.PrimaryHandler = new Transport(this);
                    previous.Dispose();
                })));
        }

        public async ValueTask DisposeAsync()
        {
            try { await factory.DisposeAsync(); }
            finally { key.Dispose(); }
        }

        private sealed class Factory(Host owner) : WebApplicationFactory<BffProgram>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder) => owner.Configure(builder);
        }

        private sealed class Transport(Host owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var uri = request.RequestUri!;
                if (uri.Host == "auth.invalid" && request.Method == HttpMethod.Post)
                {
                    if (uri.AbsolutePath == "/auth/v1/login")
                    {
                        using var login = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                        // EmployeeLoginRequest serializes the email as userName, with integer IdentityKind default 1.
                        Assert.Equal(new[] { "identityKind", "password", "userName" },
                            login.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
                        Assert.True(string.Equals(owner.email, login.RootElement.GetProperty("userName").GetString(), StringComparison.Ordinal),
                            "Employee login userName must match the runtime fixture email.");
                        Assert.True(string.Equals(owner.password, login.RootElement.GetProperty("password").GetString(), StringComparison.Ordinal),
                            "Employee login password must match the runtime fixture credential.");
                        Assert.Equal(1, login.RootElement.GetProperty("identityKind").GetInt32());
                        return Json(JsonSerializer.Serialize(new
                        {
                            accessToken = owner.EmployeeToken(), refreshToken = owner.refreshToken,
                            tokenType = "Bearer", expiresIn = 900, refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                        }));
                    }
                    if (uri.AbsolutePath == "/auth/v1/service/login")
                    {
                        using var login = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                        // ServiceAccessTokenProvider sends only these two fields; no permission/scope request exists.
                        Assert.Equal(new[] { "clientId", "clientSecret" },
                            login.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
                        Assert.Equal("financial-fixture", login.RootElement.GetProperty("clientId").GetString());
                        Assert.True(string.Equals(owner.serviceSecret, login.RootElement.GetProperty("clientSecret").GetString(), StringComparison.Ordinal),
                            "Service login secret must match the runtime fixture credential.");
                        return Json(JsonSerializer.Serialize(new { accessToken = owner.serviceToken, expiresIn = 900 }));
                    }
                }
                if (uri.Host is not ("accounting.invalid" or "quotation.invalid" or "file.invalid"))
                    throw new InvalidOperationException("Unexpected external fixture boundary.");
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.True(string.Equals(owner.serviceToken, request.Headers.Authorization?.Parameter, StringComparison.Ordinal),
                    "Controlled dependency requests must carry the runtime service token, never an employee token.");
                request.Headers.TryGetValues("X-Expected-Modified-Date", out var expected);
                owner.Requests.Enqueue(new(request.Method.Method, uri.PathAndQuery,
                    request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken), expected?.SingleOrDefault()));
                return owner.respond(request);
            }
        }
    }
}
