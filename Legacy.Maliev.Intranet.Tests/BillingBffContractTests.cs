extern alias Bff;

using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using BillingProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.BillingProxy;
using BillingMapper = Bff::Legacy.Maliev.Intranet.Bff.Accounting.BillingEndpointMapper;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BillingBffContractTests
{
    [Fact]
    public async Task ReadPreservesCustomerAccountAndActingEmployeeCredentialWithoutServiceFallback()
    {
        var account = Account();
        var handler = new Transport(JsonSerializer.Serialize(account));
        var (context, proxy) = Setup(handler);
        var result = await BillingMapper.MapReadAsync(7, account.Id, context, proxy, default);
        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal($"/v1/customers/7/billing/accounts/{account.Id:D}", handler.Path);
        Assert.True(handler.ActingEmployeeCredential);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.Equal(account.Id, Assert.IsType<BillingAccountView>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value).Id);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("account")]
    [InlineData("debt")]
    [InlineData("kind")]
    [InlineData("camelcase")]
    [InlineData("malformed")]
    [InlineData("nullstage")]
    [InlineData("nullline")]
    [InlineData("nullrequirements")]
    public async Task ForeignOrUnreconciledProducerProjectionIsRefused(string scenario)
    {
        var account = Account();
        var returned = scenario switch
        {
            "customer" => account with { Snapshot = account.Snapshot with { CustomerId = 8 } },
            "account" => account with { Id = Guid.NewGuid() },
            "debt" => account with { Outstanding = -1 },
            "kind" => account with { Stages = [account.Stages[0] with { Kind = (BillingStageKind)99 }] },
            "nullstage" => account with { Stages = [null!] },
            "nullline" => account with { Snapshot = account.Snapshot with { Lines = [null!] } },
            "nullrequirements" => account with { Stages = [account.Stages[0] with { Requirements = null! }] },
            _ => account
        };
        var json = scenario == "malformed" ? "private-error-not-json" : JsonSerializer.Serialize(returned,
            scenario == "camelcase" ? new JsonSerializerOptions(JsonSerializerDefaults.Web) : new JsonSerializerOptions());
        var handler = new Transport(json);
        var (context, proxy) = Setup(handler);
        var result = await BillingMapper.MapReadAsync(7, account.Id, context, proxy, default);
        Assert.Equal(502, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.IsNotAssignableFrom<IValueHttpResult<BillingAccountView>>(result);
    }

    [Theory]
    [InlineData(false, true, 503)]
    [InlineData(true, false, 403)]
    public async Task DisabledModuleOrMissingFreshCredentialGrantMakesNoProducerCall(bool enabled, bool permission, int expected)
    {
        var handler = new Transport("{}");
        var (context, proxy) = Setup(handler, enabled, permission);
        var result = await BillingMapper.MapReadAsync(7, Guid.NewGuid(), context, proxy, default);
        Assert.Equal(expected, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task PreviewForwardsExactPascalCaseRevisionWithoutBrowserIdentityOrEvidenceAuthority()
    {
        var account = Account();
        var expected = new BillingStagePreview(account.Id, 2, new(800, 0, 800, "THB"), [new(1, new(800, 0, 800, "THB"), "synthetic")]);
        var handler = new Transport(JsonSerializer.Serialize(expected));
        var (context, proxy) = Setup(handler);
        var request = new BillingPreviewRequest(BillingStageKind.Remaining, null, null, null, account.Stages[0].Recipient, 2);
        var result = await BillingMapper.MapPreviewAsync(7, account.Id, request, context, proxy, default);
        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(new[] { "Amount", "DueDate", "ExpectedRevision", "Kind", "Percentage", "Recipient" },
            body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(2, body.RootElement.GetProperty("ExpectedRevision").GetInt64());
        Assert.True(handler.ActingEmployeeCredential);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("components")]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("category")]
    public async Task PreviewRefusesContradictoryOrMalformedLineAllocations(string scenario)
    {
        var account = Account();
        BillingLine line = new(1, new(800, 0, 800, "THB"), "synthetic");
        IReadOnlyList<BillingLine> portions = scenario switch
        {
            "components" => [line with { Amount = new(700, 100, 800, "THB") }],
            "duplicate" => [line with { Amount = new(400, 0, 400, "THB") }, line with { Amount = new(400, 0, 400, "THB") }],
            "null" => [null!],
            _ => [line with { TaxCategory = "" }]
        };
        var handler = new Transport(JsonSerializer.Serialize(new BillingStagePreview(account.Id, 2, new(800, 0, 800, "THB"), portions)));
        var (context, proxy) = Setup(handler);
        var request = new BillingPreviewRequest(BillingStageKind.Remaining, null, null, null, account.Stages[0].Recipient, 2);
        var result = await BillingMapper.MapPreviewAsync(7, account.Id, request, context, proxy, default);
        Assert.Equal(502, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task ResponseHeadersDoNotRemoveFiniteDeadlineForStalledBody()
    {
        var handler = new Transport("{}") { StallBody = true };
        var (context, proxy) = Setup(handler);
        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await BillingMapper.MapReadAsync(7, Guid.NewGuid(), context, proxy, default);
        Assert.Equal(503, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.InRange(started.Elapsed.TotalSeconds, 10, 30);
        Assert.Equal(1, handler.Calls);
    }

    internal static BillingAccountView Account()
    {
        var recipient = new BillingRecipient(7, "legal-tax", "Head office", null, "Address");
        var source = new BillingSnapshot(17, 7, "legal-tax", "source-version", new string('a', 64), new(1000, 0, 1000, "THB"),
            [new(1, new(1000, 0, 1000, "THB"), "synthetic")], 2);
        return new(Guid.NewGuid(), 2, source,
            [new(Guid.NewGuid(), BillingStageKind.Deposit, new(200, 0, 200, "THB"), 0, 0, 0, new(2026, 10, 30), recipient, [],
                [new(1, new(200, 0, 200, "THB"), "synthetic")], [])], new(200, 0, 200, "THB"), 0, 0, 200, 800, 0);
    }

    private static (HttpContext, BillingProxy) Setup(Transport handler, bool enabled = true, bool permission = true)
    {
        var services = new ServiceCollection().AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Billing:ReadEnabled"] = enabled.ToString() }).Build())
            .AddSingleton<IAuthenticationService>(new Authentication(permission)).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        var sessions = new EmployeeSessionService(null!, TimeProvider.System, NullLogger<EmployeeSessionService>.Instance,
            Options.Create(new LegacyEmployeeCompatibilityOptions()));
        return (context, new BillingProxy(new HttpClient(handler) { BaseAddress = new("https://accounting.invalid") }, sessions));
    }

    private sealed class Authentication(bool permission) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            var properties = new AuthenticationProperties();
            properties.StoreTokens([new() { Name = "legacy_access_token", Value = "synthetic-employee-credential" },
                new() { Name = "legacy_access_expires_at", Value = DateTimeOffset.UtcNow.AddMinutes(10).ToString("O") }]);
            var claims = permission ? new[] { new Claim("permissions", LegacyEmployeePermissions.AccountingRead) } : [];
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "fixture")),
                properties, CookieAuthenticationDefaults.AuthenticationScheme)));
        }
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private sealed class Transport(string body) : HttpMessageHandler
    {
        public bool StallBody;
        public string? Path;
        public string? Body;
        public bool ActingEmployeeCredential;
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Path = request.RequestUri!.PathAndQuery;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
            ActingEmployeeCredential = request.Headers.Authorization?.ToString() == "Bearer synthetic-employee-credential";
            return new(HttpStatusCode.OK) { Content = StallBody ? new StreamContent(new StalledStream()) : new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
