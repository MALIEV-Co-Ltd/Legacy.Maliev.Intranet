extern alias Bff;

using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class AggregateOutcomeReadbackTests
{
    private const string From = "2026-08-25T17:00:00Z";
    private const string To = "2026-09-01T17:00:00Z";

    [Fact]
    public async Task AnonymousOperationsReadbackIsChallengedByRealRouting()
    {
        await using var factory = new WebApplicationFactory<BffProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await client.GetAsync(Route("quotation"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Login", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("quotation", "/quotations/outcomes/readback", "quotation")]
    [InlineData("invoice", "/invoices/outcomes/readback", "invoice")]
    public async Task EmployeeSessionUsesFixedProducerRouteAndReturnsAggregateOnlyEnvelope(
        string source,
        string expectedPath,
        string fixture)
    {
        var upstream = new RecordingHandler(Fixture(fixture), HttpStatusCode.OK);
        await using var factory = new OutcomeBffFactory(upstream, PermissionsFor(source));
        using var client = factory.CreateClient(ClientOptions());

        using var response = await client.GetAsync(Route(source));
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(source, document.RootElement.GetProperty("source").GetString());
        Assert.Equal(200, document.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("payload").GetProperty("Days").ValueKind);
        Assert.Equal(expectedPath, upstream.Path);
        Assert.Equal("employee-session-access-token", upstream.Token);
        Assert.Contains("fromUtc=2026-08-25T17%3A00%3A00.0000000Z", upstream.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("employee-session-access-token", json, StringComparison.Ordinal);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("https://other.invalid", From, To)]
    [InlineData("quotation", To, From)]
    [InlineData("quotation", "2026-08-25T17:00:00", To)]
    [InlineData("quotation", "2026-07-01T00:00:00Z", To)]
    [InlineData("quotation", From, "2026-09-06T00:00:00Z")]
    public async Task InvalidSourceOrUtcWindowFailsBeforeProducerCall(string source, string from, string to)
    {
        var upstream = new RecordingHandler(Fixture("quotation"), HttpStatusCode.OK);
        await using var factory = new OutcomeBffFactory(upstream, PermissionsFor("quotation"));
        using var client = factory.CreateClient(ClientOptions());

        using var response = await client.GetAsync(
            $"/Operations/OutcomeReadback?source={Uri.EscapeDataString(source)}&fromUtc={Uri.EscapeDataString(from)}&toUtc={Uri.EscapeDataString(to)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, upstream.Calls);
    }

    [Fact]
    public async Task ExactlyThirtyOneDayUtcWindowIsAccepted()
    {
        const string boundaryFrom = "2026-08-05T12:00:00Z";
        const string boundaryTo = "2026-09-05T12:00:00Z";
        var body = Fixture("quotation")
            .Replace(From, boundaryFrom, StringComparison.Ordinal)
            .Replace(To, boundaryTo, StringComparison.Ordinal);
        var upstream = new RecordingHandler(body, HttpStatusCode.OK);
        await using var factory = new OutcomeBffFactory(upstream, PermissionsFor("quotation"));
        using var client = factory.CreateClient(ClientOptions());

        using var response = await client.GetAsync(
            $"/Operations/OutcomeReadback?source=quotation&fromUtc={Uri.EscapeDataString(boundaryFrom)}&toUtc={Uri.EscapeDataString(boundaryTo)}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(200, document.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal(1, upstream.Calls);
    }

    [Theory]
    [InlineData("quotation")]
    [InlineData("invoice")]
    public async Task SourceSpecificReadPermissionIsRequiredBeforeProducerCall(string source)
    {
        var upstream = new RecordingHandler(Fixture(source), HttpStatusCode.OK);
        await using var factory = new OutcomeBffFactory(upstream, []);
        using var client = factory.CreateClient(ClientOptions());

        using var response = await client.GetAsync(Route(source));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, upstream.Calls);
    }

    [Fact]
    public async Task MissingEmployeeSessionTokenReturnsUnavailableReceiptWithoutProducerCall()
    {
        var upstream = new RecordingHandler(Fixture("invoice"), HttpStatusCode.OK);
        await using var factory = new OutcomeBffFactory(
            upstream,
            PermissionsFor("invoice"),
            includeAccessToken: false);
        using var client = factory.CreateClient(ClientOptions());

        using var response = await client.GetAsync(Route("invoice"));
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(401, document.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("payload").ValueKind);
        Assert.Equal(0, upstream.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ProducerFailureReturnsStatusOnlyAndNeverLeaksBody(HttpStatusCode status)
    {
        var upstream = new RecordingHandler("private customer details", status);
        await using var factory = new OutcomeBffFactory(upstream, PermissionsFor("quotation"));
        using var client = factory.CreateClient(ClientOptions());

        using var response = await client.GetAsync(Route("quotation"));
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((int)status, document.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("payload").ValueKind);
        Assert.DoesNotContain("private", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{\"customerId\":42}")]
    [InlineData("{\"FromUtc\":\"2026-08-25T17:00:00Z\",\"ToUtc\":\"2026-09-01T17:00:00Z\",\"Days\":[],\"Days\":[]}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("not json")]
    public async Task MalformedOrNonAggregateSuccessFailsClosedWithoutLeakingPayload(string body)
    {
        var upstream = new RecordingHandler(body, HttpStatusCode.OK);
        await using var factory = new OutcomeBffFactory(upstream, PermissionsFor("quotation"));
        using var client = factory.CreateClient(ClientOptions());

        using var response = await client.GetAsync(Route("quotation"));
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(502, document.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("payload").ValueKind);
        Assert.DoesNotContain("customerId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("quotation", "\"PersistedQuotationCount\":3", "\"PersistedQuotationCount\":9")]
    [InlineData("quotation", "\"PersistedQuotationCount\":3", "\"JourneyId\":\"private\",\"PersistedQuotationCount\":3")]
    [InlineData("invoice", "\"Currency\":\"THB\",", "")]
    public async Task InconsistentCountsAndNestedIdentifiersFailClosed(
        string source,
        string oldValue,
        string newValue)
    {
        var body = Fixture(source).Replace(oldValue, newValue, StringComparison.Ordinal);
        var upstream = new RecordingHandler(body, HttpStatusCode.OK);
        await using var factory = new OutcomeBffFactory(upstream, PermissionsFor(source));
        using var client = factory.CreateClient(ClientOptions());

        using var response = await client.GetAsync(Route(source));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(502, document.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("payload").ValueKind);
    }

    private static WebApplicationFactoryClientOptions ClientOptions() => new()
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    };

    private static string Route(string source) =>
        $"/Operations/OutcomeReadback?source={source}&fromUtc={Uri.EscapeDataString(From)}&toUtc={Uri.EscapeDataString(To)}";

    private static string[] PermissionsFor(string source) =>
        source == "quotation" ? [LegacyEmployeePermissions.QuotationsRead] : [LegacyEmployeePermissions.AccountingRead];

    private static string Fixture(string source) => source == "quotation"
        ? """{"FromUtc":"2026-08-25T17:00:00Z","ToUtc":"2026-09-01T17:00:00Z","Days":[{"DayUtc":"2026-08-26T00:00:00","PersistedQuotationCount":3,"AcceptedQuotationCount":2,"SourceAttributedPersistedQuotationCount":1,"SourceAttributedAcceptedQuotationCount":1,"UnattributedPersistedQuotationCount":2,"UnattributedAcceptedQuotationCount":1,"AcceptedQuotedAmountsByCurrency":[{"CurrencyId":1,"QuotedAmount":123.4500,"AcceptedQuotationCount":1},{"CurrencyId":2,"QuotedAmount":9876543210.12345678,"AcceptedQuotationCount":1}]}],"TechnicalConversionAvailability":"unavailable","QualifiedCustomerAvailability":"unavailable","RevenueAvailability":"unavailable"}"""
        : """{"FromUtc":"2026-08-25T17:00:00Z","ToUtc":"2026-09-01T17:00:00Z","Days":[{"DayUtc":"2026-08-26T00:00:00","PaidInvoiceCount":3,"SourceAttributedPaidInvoiceCount":1,"UnattributedPaidInvoiceCount":2,"PaidInvoiceAmountsByCurrency":[{"Currency":"THB","PaidInvoiceTotal":120.5000,"PaidInvoiceCount":2},{"Currency":"USD","PaidInvoiceTotal":25.01,"PaidInvoiceCount":1}]}]}""";

    private sealed class OutcomeBffFactory(
        RecordingHandler upstream,
        IReadOnlyList<string> permissions,
        bool includeAccessToken = true) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Services:Auth", "http://auth/");
            builder.UseSetting("Services:Accounting", "http://accounting/");
            builder.UseSetting("Services:Quotation", "http://quotation/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero)));
                services.RemoveAll<IAuthenticationService>();
                services.AddSingleton<IAuthenticationService>(new SessionAuthenticationService(permissions, includeAccessToken));
                services.AddHttpClient("aggregate-outcome-accounting")
                    .ConfigurePrimaryHttpMessageHandler(() => upstream);
                services.AddHttpClient("aggregate-outcome-quotation")
                    .ConfigurePrimaryHttpMessageHandler(() => upstream);
            });
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SessionAuthenticationService : IAuthenticationService
    {
        private readonly AuthenticationTicket ticket;

        public SessionAuthenticationService(IReadOnlyList<string> permissions, bool includeAccessToken)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "employee-id"),
                new(ClaimTypes.Name, "MALIEV Employee"),
                new(ClaimTypes.Role, "Employee"),
                new("identity_kind", "employee"),
            };
            claims.AddRange(permissions.Select(permission => new Claim("permissions", permission)));
            var properties = new AuthenticationProperties();
            if (includeAccessToken)
            {
                properties.StoreTokens(
                [
                    new AuthenticationToken { Name = "legacy_access_token", Value = "employee-session-access-token" },
                    new AuthenticationToken { Name = "legacy_access_expires_at", Value = "2026-09-05T13:00:00Z" },
                ]);
            }

            ticket = new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
                properties,
                CookieAuthenticationDefaults.AuthenticationScheme);
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.Success(ticket));

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private sealed class RecordingHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Path { get; private set; }
        public string? Query { get; private set; }
        public string? Token { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Path = request.RequestUri?.AbsolutePath;
            Query = request.RequestUri?.Query;
            Token = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
