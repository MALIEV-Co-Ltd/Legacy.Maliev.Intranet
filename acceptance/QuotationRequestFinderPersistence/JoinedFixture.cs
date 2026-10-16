extern alias Bff;
extern alias QuotationApi;

using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.QuotationService.Data;
using Legacy.Maliev.QuotationService.Domain;
using Maliev.Aspire.ServiceDefaults.IAM;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using BffProgram = Bff::Program;
using QuotationProgram = QuotationApi::Program;

namespace QuotationRequestFinderPersistence.Acceptance;

public sealed class OwnedContainers : IAsyncLifetime
{
    private readonly string owner = "intranet241-" + Guid.NewGuid().ToString("N");
    private readonly DateTimeOffset expiryUtc = DateTimeOffset.UtcNow.AddMinutes(25);
    public PostgreSqlContainer Postgres { get; }
    public RedisContainer Redis { get; }
    private readonly object releaseGate = new();
    private RetainedRelease? release;
    private string? postgresId;
    private string? redisId;
    private ushort postgresPort;
    private ushort redisPort;

    public OwnedContainers()
    {
        Postgres = new PostgreSqlBuilder("postgres:18-alpine").WithName(owner + "-pg")
            .WithLabel("maliev.owner", owner).WithLabel("maliev.persistent", "false")
            .WithCreateParameterModifier(p => { p.HostConfig.Memory = 768L * 1024 * 1024; p.HostConfig.NanoCPUs = 1_000_000_000; }).Build();
        Redis = new RedisBuilder("redis:7-alpine").WithName(owner + "-redis")
            .WithLabel("maliev.owner", owner).WithLabel("maliev.persistent", "false")
            .WithCreateParameterModifier(p => { p.HostConfig.Memory = 192L * 1024 * 1024; p.HostConfig.NanoCPUs = 500_000_000; }).Build();
    }

    public async Task InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            await Postgres.StartAsync(deadline.Token);
            postgresId = Postgres.Id;
            postgresPort = Postgres.GetMappedPublicPort(5432);
            await Redis.StartAsync(deadline.Token);
            redisId = Redis.Id;
            redisPort = Redis.GetMappedPublicPort(6379);
            Receipt("started");
        }
        catch (Exception failure)
        {
            try { await DisposeAsync(); }
            catch (Exception cleanup) { throw new AggregateException(failure, cleanup); }
            throw;
        }
    }

    private void Receipt(string state)
    {
        var folder = Environment.GetEnvironmentVariable("FINDER_JOIN_RESOURCES");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, owner + ".json"), JsonSerializer.Serialize(new
        {
            owner, state, observedUtc = DateTimeOffset.UtcNow, persistent = false,
            expiryUtc,
            postgresId, redisId, postgresPort, redisPort,
        }));
    }

    public async Task DisposeAsync()
    {
        RetainedRelease operation;
        lock (releaseGate) operation = release ??= new RetainedRelease("containers", this, ReleaseCoreAsync);
        await operation.ObserveAsync();
    }

    private async Task ReleaseCoreAsync()
    {
        // Start both actual releases before awaiting either. The owner retains the tasks.
        var redisRelease = ReleaseRedisAsync();
        var postgresRelease = ReleasePostgresAsync();
        var failures = new List<Exception>();
        try { await redisRelease; }
        catch (Exception exception) { failures.Add(exception); }
        try { await postgresRelease; }
        catch (Exception exception) { failures.Add(exception); }
        try { Receipt(failures.Count == 0 ? "disposed" : "cleanup-failed"); }
        catch (Exception exception) { failures.Add(exception); }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private async Task ReleaseRedisAsync() => await Redis.DisposeAsync();
    private async Task ReleasePostgresAsync() => await Postgres.DisposeAsync();
}

internal sealed record Snapshot(string Row, string History, string Files, string Xmin, string? Comment, DateTime? ModifiedDate);

internal sealed class JoinedFixture(OwnedContainers containers) : IAsyncDisposable
{
    private const string Origin = "https://finder-auth.example.test";
    private const string Issuer = "https://finder-issuer.example.test";
    private const string Audience = "finder-join";
    private const string BffSubject = "service:legacy-intranet";
    private readonly RSA key = RSA.Create(2048);
    private readonly string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly string database = "finder_" + Guid.NewGuid().ToString("N");
    private QuoteFactory? quote;
    private BffFactory? bff;
    private readonly object releaseGate = new();
    private RetainedRelease? release;
    private OwnedContainers Containers => containers;
    public int Id { get; private set; }
    public int DetailReads;
    public int NoteWrites;
    public int IamCalls;
    public HttpStatusCode? LastNoteStatus;
    public ConcurrentQueue<string> Requests { get; } = new();
    public ConcurrentQueue<string> UnexpectedOutbound { get; } = new();
    private string Connection => new NpgsqlConnectionStringBuilder(containers.Postgres.GetConnectionString()) { Database = database }.ConnectionString;

    public async Task InitializeAsync(string envelope)
    {
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await using (var connection = new NpgsqlConnection(containers.Postgres.GetConnectionString()))
            {
                await connection.OpenAsync(setup.Token);
                await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
                await command.ExecuteNonQueryAsync(setup.Token);
            }
            await using var db = Context();
            await db.Database.MigrateAsync(setup.Token);
            var timestamp = new DateTime(2030, 7, 18, 8, 30, 0, DateTimeKind.Unspecified);
            var row = new QuotationRequest
            {
                FirstName = "Synthetic", LastName = "Fixture", Email = "fixture@example.invalid",
                TelephoneNumber = "0000000000", Country = "Thailand", CompanyName = "Fixture",
                Message = "Synthetic finder request", InternalComment = envelope, Done = false,
                JourneyId = Guid.NewGuid(), CreatedDate = timestamp, ModifiedDate = timestamp,
                QualificationState = "qualified", QualificationVersion = 1, QualificationStateChangedUtc = timestamp,
            };
            db.Requests.Add(row);
            await db.SaveChangesAsync(setup.Token);
            row.TransactionId = $"request-{row.Id}";
            // Imported server history only: no qualification route or authority is exercised.
            db.RequestQualificationAudit.Add(new()
            {
                RequestId = row.Id, JourneyId = row.JourneyId, TransactionId = row.TransactionId,
                IdempotencyKey = Guid.NewGuid().ToString("N"), PreviousState = "unreviewed", NewState = "qualified",
                ChangedBy = "historical-fixture", ChangedUtc = timestamp, Version = 1,
            });
            await db.SaveChangesAsync(setup.Token);
            Id = row.Id;
            quote = new QuoteFactory(this);
            _ = quote.Server;
            Assert.False(quote.Services.GetRequiredService<IOptions<QuotationApi::Legacy.Maliev.QuotationService.Api.Authorization.QualificationAuthorityOptions>>().Value.Enabled);
            bff = new BffFactory(this);
            _ = bff.Server;
            Assert.False(bff.Services.GetRequiredService<IOptions<Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationQualificationOptions>>().Value.ForwardEmployeeToken);
        }
        catch (Exception primary)
        {
            try { await DisposeAsync(); }
            catch (Exception cleanup) { throw new AggregateException("Setup and owned cleanup failed.", primary, cleanup); }
            throw;
        }
    }

    private QuotationRequestDbContext Context() => new(new DbContextOptionsBuilder<QuotationRequestDbContext>().UseNpgsql(Connection).Options);

    public async Task<Snapshot> SnapshotAsync()
    {
        await using var db = Context();
        var row = await db.Requests.AsNoTracking().SingleAsync(x => x.Id == Id);
        var history = await db.RequestQualificationAudit.AsNoTracking().Where(x => x.RequestId == Id).OrderBy(x => x.Id).ToListAsync();
        Assert.Single(history);
        var files = await db.Files.AsNoTracking().Where(x => x.RequestId == Id).OrderBy(x => x.Id).ToListAsync();
        var xmin = await db.Database.SqlQueryRaw<string>("SELECT xmin::text AS \"Value\" FROM \"Request\" WHERE \"ID\" = {0}", Id).SingleAsync();
        return new(JsonSerializer.Serialize(row), JsonSerializer.Serialize(history), JsonSerializer.Serialize(files), xmin, row.InternalComment, row.ModifiedDate);
    }

    public HttpClient AnonymousClient()
    {
        var client = bff!.CreateClient(new() { BaseAddress = new("https://localhost/"), AllowAutoRedirect = false });
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    }

    public async Task<HttpClient> SignInAsync(bool canUpdate = true)
    {
        var client = AnonymousClient();
        try
        {
            using var session = await client.GetAsync("/bff/session");
            session.EnsureSuccessStatusCode();
            var state = await session.Content.ReadFromJsonAsync<EmployeeSessionSummary>();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
            {
                Content = JsonContent.Create(new { email = canUpdate ? "finder@maliev.com" : "reader@maliev.com", password = "fixture-only", returnUrl = "/QuotationRequests/View" }),
            };
            request.Headers.Add("X-CSRF-TOKEN", state!.CsrfToken);
            using var login = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    public async Task<JsonDocument> ReadApiAsync()
    {
        using var client = quote!.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(BffSubject, "service", true));
        using var response = await client.GetAsync($"/quotationrequests/{Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private string Token(string subject, string kind, bool canUpdate)
    {
        var claims = new List<Claim>
        {
            new("sub", subject), new("identity_kind", kind),
            new("permissions", "legacy.quotation-requests.read"), new("permissions", "legacy.quotation-files.read"),
            new("permissions", "legacy-file.uploads.read"),
        };
        if (canUpdate) claims.Add(new("permissions", "legacy.quotation-requests.update"));
        if (kind == "employee")
        {
            claims.AddRange([new("role", "Employee"), new("name", "Finder fixture"),
                new("email", "finder@maliev.com"), new("legacy_database_id", "7")]);
        }
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Issuer, Audience, claims,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(15), new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256)));
    }

    private void ConfigureIdentity(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:PublicKey", Convert.ToBase64String(Encoding.UTF8.GetBytes(key.ExportSubjectPublicKeyInfoPem())));
        builder.UseSetting("Jwt:PublicKeyPem", key.ExportSubjectPublicKeyInfoPem());
        builder.UseSetting("ServiceAuthentication:ClientSecret", secret);
        builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");
        builder.UseSetting("Observability:RuntimeMetricsEnabled", "false");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services => services.Configure<HostOptions>(options =>
        {
            options.StartupTimeout = TimeSpan.FromSeconds(20);
            options.ShutdownTimeout = TimeSpan.FromSeconds(20);
        }));
    }

    private sealed class BffFactory(JoinedFixture fixture) : OwnedFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            Legacy.Maliev.Intranet.Tests.TestHostLifecycle.Configure(builder);
            fixture.ConfigureIdentity(builder);
            builder.UseSetting("ServiceAuthentication:ClientId", "legacy-intranet");
            builder.UseSetting("Services:Auth", Origin);
            builder.UseSetting("Services:Quotation", "https://quotation.finder.test");
            builder.UseSetting("Services:Order", "https://blocked.finder.test");
            builder.ConfigureTestServices(services => services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(http => http.PrimaryHandler = new Boundary(fixture, true))));
        }
    }

    private sealed class QuoteFactory(JoinedFixture fixture) : OwnedFactory<QuotationProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            fixture.ConfigureIdentity(builder);
            builder.UseSetting("ConnectionStrings:QuotationDbContext", fixture.Containers.Postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:QuotationRequestDbContext", fixture.Connection);
            builder.UseSetting("ConnectionStrings:redis", fixture.Containers.Redis.GetConnectionString());
            builder.UseSetting("Services:Auth:BaseUrl", Origin);
            builder.UseSetting("ServiceAuthentication:ClientId", "legacy-quotation");
            builder.UseSetting("IAM:LivePermissionChecks:Credential", fixture.secret);
            builder.UseSetting("MALIEV_OBSERVABILITY_STANDBY", "true");
            builder.ConfigureTestServices(services =>
            {
                // Exact unrelated expiry worker only. Keep all ordinary request/auth/persistence services intact.
                var expiry = services.Single(x => x.ServiceType == typeof(IHostedService)
                    && x.ImplementationType?.FullName == "Legacy.Maliev.QuotationService.Api.Workers.ExpiredQuotationWorker");
                services.Remove(expiry);
                Assert.DoesNotContain(services, x => x.ServiceType == typeof(IHostedService)
                    && x.ImplementationType?.Name == "GoogleAnalyticsOutboxWorker");
                services.AddScoped<IIamServiceClient, IamServiceClient>();
                services.AddHttpClient("IAMService", client => client.BaseAddress = new("https://iam.finder.test"));
                services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(
                    http => http.PrimaryHandler = new Boundary(fixture, false)));
            });
        }
    }

    private sealed class Boundary(JoinedFixture fixture, bool fromBff) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "finder-auth.example.test" && request.Method == HttpMethod.Post)
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                if (fromBff && uri.AbsolutePath == "/auth/v1/login")
                {
                    var email = json.RootElement.GetProperty("email").GetString();
                    Assert.Contains(email, new[] { "finder@maliev.com", "reader@maliev.com" });
                    Assert.Equal("fixture-only", json.RootElement.GetProperty("password").GetString());
                    return Json(new { accessToken = fixture.Token("employee-finder", "employee", email == "finder@maliev.com"), refreshToken = "fixture-refresh", tokenType = "Bearer", expiresIn = 900, refreshExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
                }
                if (uri.AbsolutePath == "/auth/v1/service/login")
                {
                    var client = fromBff ? "legacy-intranet" : "legacy-quotation";
                    Assert.Equal(client, json.RootElement.GetProperty("clientId").GetString());
                    Assert.Equal(fixture.secret, json.RootElement.GetProperty("clientSecret").GetString());
                    Assert.Null(request.Headers.Authorization);
                    return Json(new { accessToken = fixture.Token("service:" + client, "service", true), expiresIn = 900 });
                }
            }
            if (!fromBff && uri.Host == "iam.finder.test" && request.Method == HttpMethod.Post && uri.AbsolutePath == "/iam/v1/auth/check-permission")
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                var workload = new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter);
                Assert.Equal("service:legacy-quotation", workload.Subject);
                Assert.Equal("service", workload.Claims.Single(x => x.Type == "identity_kind").Value);
                Assert.Equal(fixture.secret, Assert.Single(request.Headers.GetValues("X-Maliev-IAM-Live-Check-Key")));
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal(BffSubject, json.RootElement.GetProperty("principalId").GetString());
                Assert.Contains(json.RootElement.GetProperty("permissionId").GetString(), new[] { "legacy.quotation-requests.read", "legacy.quotation-requests.update", "legacy.quotation-files.read" });
                Assert.True(json.RootElement.GetProperty("bypassCache").GetBoolean());
                Interlocked.Increment(ref fixture.IamCalls);
                return Json(new { allowed = true });
            }
            if (fromBff && uri.Host == "quotation.finder.test")
            {
                var read = request.Method == HttpMethod.Get && new[] { $"/quotationrequests/{fixture.Id}", $"/quotationrequests/{fixture.Id}/files", $"/quotationrequests/{fixture.Id}/qualification-receipt" }.Contains(uri.AbsolutePath);
                var write = request.Method == HttpMethod.Put && uri.AbsolutePath == $"/quotationrequests/{fixture.Id}";
                if (read || write)
                {
                    var actor = new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter);
                    Assert.Equal(BffSubject, actor.Subject);
                    Assert.Equal("service", actor.Claims.Single(x => x.Type == "identity_kind").Value);
                    fixture.Requests.Enqueue(request.Method + " " + uri.AbsolutePath);
                    if (read && uri.AbsolutePath == $"/quotationrequests/{fixture.Id}") Interlocked.Increment(ref fixture.DetailReads);
                    if (write) Interlocked.Increment(ref fixture.NoteWrites);
                    using var transport = new HttpMessageInvoker(fixture.quote!.Server.CreateHandler());
                    var result = await transport.SendAsync(request, cancellationToken);
                    if (write) fixture.LastNoteStatus = result.StatusCode;
                    return result;
                }
            }
            fixture.UnexpectedOutbound.Enqueue(request.Method + " " + uri.Host + uri.AbsolutePath);
            throw new InvalidOperationException("Unapproved outbound boundary in finder persistence acceptance.");
        }
        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    public async ValueTask DisposeAsync()
    {
        RetainedRelease operation;
        lock (releaseGate) operation = release ??= new RetainedRelease("join", this, ReleaseCoreAsync);
        await operation.ObserveAsync();
    }

    private async Task ReleaseCoreAsync()
    {
        var bffRelease = bff?.ReleaseActualAsync() ?? Task.CompletedTask;
        var quoteRelease = quote?.ReleaseActualAsync() ?? Task.CompletedTask;
        var bothReleases = Task.WhenAll(bffRelease, quoteRelease);
        try { await bothReleases; }
        catch (Exception observed)
        {
            // Await alone rethrows only one fault; preserve every independently owned failure.
            if (bothReleases.Exception is { } aggregate)
                throw new AggregateException("Independent host releases failed.", aggregate.Flatten().InnerExceptions);
            throw new AggregateException("Independent host release was cancelled.", observed);
        }
        // A timeout or failed host release keeps both the key and owner rooted, not disposed.
        key.Dispose();
    }
}
