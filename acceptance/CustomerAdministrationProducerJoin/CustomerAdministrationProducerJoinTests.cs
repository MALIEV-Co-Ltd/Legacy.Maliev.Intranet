extern alias AuthApi;
extern alias Bff;
extern alias CustomerApi;

using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AuthService.Infrastructure;
using Legacy.Maliev.CustomerService.Data;
using Legacy.Maliev.CustomerService.Domain;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.Aspire.ServiceDefaults.IAM;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using AuthProgram = AuthApi::Program;
using BffProgram = Bff::Program;
using CustomerProgram = CustomerApi::Program;
using IdentityClient = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomerAdministrationIdentityClient;
using ProfileClient = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomerAdministrationProfileClient;

namespace CustomerAdministrationProducerJoin.Acceptance;

public sealed class SharedContainers : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:18-alpine").Build();
    public RedisContainer Redis { get; } = new RedisBuilder("redis:7-alpine").Build();
    private int nextRedisDatabase;
    private int disposed;

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            await Postgres.StartAsync(timeout.Token);
            await Redis.StartAsync(timeout.Token);
        }
        catch
        {
            try { await DisposeAsync(); }
            catch { /* Preserve the primary setup failure after attempting both owned resources. */ }
            throw;
        }
    }

    public int AllocateRedisDatabase()
    {
        var database = Interlocked.Increment(ref nextRedisDatabase);
        if (database > 15) throw new InvalidOperationException("Disposable Redis database inventory exhausted.");
        return database;
    }

    public async Task DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { await Redis.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { await Postgres.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)); }
    }
}

public sealed class CustomerAdministrationProducerJoinTests(SharedContainers containers) : IClassFixture<SharedContainers>
{
    private static readonly string[] Permissions =
    [
        "legacy-customer.customers.read", "legacy-customer.customers.update",
        "legacy-auth.customer-identities.read", "legacy-auth.customer-identities.update",
    ];

    [Fact]
    public async Task RealEmployeeSession_ProfileThenIdentity_PreservesProtectedFieldsAndRelations()
    {
        await using var fixture = new JoinedFixture(containers);
        await fixture.InitializeAsync();
        using var client = await fixture.SignedInAsync();
        var before = await fixture.SnapshotAsync();
        var edit = await ReadAsync(client);
        fixture.ExpectedEdit = edit;
        using var result = await SaveAsync(client, Input(edit));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var receipt = await result.Content.ReadFromJsonAsync<CustomerAdministrationSaveResult>();
        Assert.NotNull(receipt);
        Assert.Equal("complete", receipt.Stage);
        Assert.Equal(new[] { "profile", "identity" }, receipt.CompletedStages);
        Assert.False(receipt.OutcomeUnknown);
        Assert.Equal(200, receipt.StatusCode);
        Assert.Equal(new[] { "profile", "identity" }, fixture.Writes.ToArray());
        var after = await fixture.SnapshotAsync();
        Assert.Equal("Updated", after.Profile.FirstName);
        Assert.Equal("Updated family", after.Profile.LastName);
        Assert.Equal(new DateTime(1990, 1, 2), after.Profile.DateOfBirth);
        Assert.Equal("updated@maliev.test", after.Profile.Email);
        Assert.Equal(after.Profile.Email, after.Identity.Email);
        Assert.Equal(after.Identity.Email, after.Identity.UserName);
        Assert.Equal("+6621111111", after.Profile.Telephone);
        Assert.Equal(after.Profile.Telephone, after.Identity.PhoneNumber);
        Assert.Equal(after.Profile.Mobile, after.Identity.MobileNumber);
        Assert.Equal(after.Profile.Fax, after.Identity.FaxNumber);
        Assert.False(after.Identity.EmailConfirmed);
        Assert.True(after.Identity.PhoneNumberConfirmed);
        Assert.False(after.Identity.LockoutEnabled);
        Protected(before, after);
        Assert.True(fixture.IamCalls > 0);
        Assert.Empty(fixture.TransportFailures);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("identity")]
    public async Task StaleOriginalVersion_RejectsBeforeEitherWrite(string changed)
    {
        await using var fixture = new JoinedFixture(containers);
        await fixture.InitializeAsync();
        using var client = await fixture.SignedInAsync();
        var edit = await ReadAsync(client);
        fixture.ExpectedEdit = edit;
        if (changed == "profile") await fixture.MoveProfileAsync();
        else await fixture.MoveIdentityAsync();
        var committed = await fixture.SnapshotAsync();
        using var result = await SaveAsync(client, Input(edit));
        Assert.Equal(HttpStatusCode.PreconditionFailed, result.StatusCode);
        var receipt = await result.Content.ReadFromJsonAsync<CustomerAdministrationSaveResult>();
        Assert.NotNull(receipt);
        Assert.Equal("preflight", receipt.Stage);
        Assert.Empty(receipt.CompletedStages);
        Assert.False(receipt.OutcomeUnknown);
        Assert.Equal(412, receipt.StatusCode);
        Assert.Empty(fixture.Writes);
        Assert.Equal(committed, await fixture.SnapshotAsync());
        Assert.Empty(fixture.TransportFailures);
    }

    [Fact]
    public async Task ActualAuthConflict_AfterProfileCommit_ReportsPartialWithoutReplay()
    {
        await using var fixture = new JoinedFixture(containers) { HoldIdentity = true };
        await fixture.InitializeAsync();
        using var client = await fixture.SignedInAsync();
        var before = await fixture.SnapshotAsync();
        var edit = await ReadAsync(client);
        fixture.ExpectedEdit = edit;
        var saving = SaveAsync(client, Input(edit));
        Exception? primary = null;
        try
        {
            await fixture.IdentityEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var profileCommit = await fixture.SnapshotAsync();
            Assert.Equal("Updated", profileCommit.Profile.FirstName);
            Assert.Equal(before.Identity, profileCommit.Identity);
            await fixture.MoveIdentityAsync();
            var concurrent = await fixture.SnapshotAsync();
            fixture.IdentityRelease.TrySetResult();
            using var result = await saving.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.PreconditionFailed, result.StatusCode);
            var receipt = await result.Content.ReadFromJsonAsync<CustomerAdministrationSaveResult>();
            Assert.NotNull(receipt);
            Assert.Equal("identity", receipt.Stage);
            Assert.Equal(new[] { "profile" }, receipt.CompletedStages);
            Assert.False(receipt.OutcomeUnknown);
            Assert.Equal(412, receipt.StatusCode);
            Assert.Equal(new[] { "profile", "identity" }, fixture.Writes.ToArray());
            Assert.Equal(concurrent, await fixture.SnapshotAsync());
            var recovered = await ReadAsync(client);
            Assert.NotEqual(edit.ProfileVersion, recovered.ProfileVersion);
            Assert.NotEqual(edit.IdentityVersion, recovered.IdentityVersion);
            Assert.Equal(new[] { "profile", "identity" }, fixture.Writes.ToArray());
            Assert.Equal(concurrent, await fixture.SnapshotAsync());
            Protected(before, concurrent);
            Assert.Empty(fixture.TransportFailures);
        }
        catch (Exception exception)
        {
            primary = exception;
            throw;
        }
        finally
        {
            fixture.IdentityRelease.TrySetResult();
            try { using var drained = await saving.WaitAsync(TimeSpan.FromSeconds(20)); }
            catch when (primary is not null)
            {
                _ = saving.ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully) task.Result.Dispose();
                    else _ = task.Exception;
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    [Fact]
    public async Task LostRealProfileAcknowledgement_RequiresReadbackWithoutIdentityOrReplay()
    {
        await using var fixture = new JoinedFixture(containers) { LoseProfileAcknowledgement = true };
        await fixture.InitializeAsync();
        using var client = await fixture.SignedInAsync();
        var before = await fixture.SnapshotAsync();
        var edit = await ReadAsync(client);
        fixture.ExpectedEdit = edit;
        using var result = await SaveAsync(client, Input(edit));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        var receipt = await result.Content.ReadFromJsonAsync<CustomerAdministrationSaveResult>();
        Assert.NotNull(receipt);
        Assert.Equal("profile", receipt.Stage);
        Assert.Empty(receipt.CompletedStages);
        Assert.True(receipt.OutcomeUnknown);
        Assert.Equal(503, receipt.StatusCode);
        Assert.Equal(1, fixture.ProfileCommitsAcknowledged);
        Assert.Equal(new[] { "profile" }, fixture.Writes.ToArray());
        var committed = await fixture.SnapshotAsync();
        Assert.Equal("Updated", committed.Profile.FirstName);
        Assert.Equal(before.Identity, committed.Identity);
        Protected(before, committed);
        var recovered = await ReadAsync(client);
        Assert.NotEqual(edit.ProfileVersion, recovered.ProfileVersion);
        Assert.Equal(edit.IdentityVersion, recovered.IdentityVersion);
        Assert.Equal(committed, await fixture.SnapshotAsync());
        Assert.Equal(1, fixture.ProfileCommitsAcknowledged);
        Assert.Equal(new[] { "profile" }, fixture.Writes.ToArray());
        Assert.Empty(fixture.TransportFailures);
    }

    private static void Protected(Snapshot before, Snapshot after)
    {
        Assert.Equal(before.Identity.PasswordHash, after.Identity.PasswordHash);
        Assert.Equal(before.Identity.TwoFactorEnabled, after.Identity.TwoFactorEnabled);
        Assert.Equal(before.Identity.LockoutEnd, after.Identity.LockoutEnd);
        Assert.Equal(before.Profile.CompanyId, after.Profile.CompanyId);
        Assert.Equal(before.Profile.BillingAddressId, after.Profile.BillingAddressId);
        Assert.Equal(before.Profile.ShippingAddressId, after.Profile.ShippingAddressId);
        Assert.Equal(before.Profile.InternalRemark, after.Profile.InternalRemark);
        Assert.Equal(before.Relations, after.Relations);
        Assert.Equal(before.OtherProfile, after.OtherProfile);
        Assert.Equal(before.OtherIdentity, after.OtherIdentity);
        Assert.Equal(before.ProfileCount, after.ProfileCount);
        Assert.Equal(before.IdentityCount, after.IdentityCount);
    }

    private static CustomerAdministrationSaveRequest Input(CustomerAdministrationEdit edit) =>
        new("Updated", "Updated family", "updated@maliev.test", "+6621111111", "+66811111111", "+6622222222",
            new DateTime(1990, 1, 2), false, true, false, edit.ProfileVersion, edit.IdentityVersion);

    private static async Task<CustomerAdministrationEdit> ReadAsync(HttpClient client)
    {
        using var result = await client.GetAsync("/bff/customers/1/edit");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        return await result.Content.ReadFromJsonAsync<CustomerAdministrationEdit>() ?? throw new InvalidOperationException("Missing joined edit.");
    }

    private static async Task<string> CsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/bff/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("csrfToken").GetString() ?? throw new InvalidOperationException("Missing CSRF token.");
    }

    private static async Task<HttpResponseMessage> SaveAsync(HttpClient client, CustomerAdministrationSaveRequest input)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/customers/1/edit") { Content = JsonContent.Create(input) };
        request.Headers.Add("X-CSRF-TOKEN", await CsrfAsync(client));
        return await client.SendAsync(request);
    }

    private sealed record IdentityState(string? UserName, string? Email, bool EmailConfirmed, string? PhoneNumber,
        bool PhoneNumberConfirmed, bool LockoutEnabled, bool TwoFactorEnabled, DateTimeOffset? LockoutEnd,
        string? PasswordHash, string? SecurityStamp, string? ConcurrencyStamp, string? FaxNumber, string? MobileNumber);
    private sealed record ProfileState(string FirstName, string LastName, DateTime? DateOfBirth, string Email, string? Telephone, string? Mobile, string? Fax,
        int? CompanyId, int? BillingAddressId, int? ShippingAddressId, string? InternalRemark, DateTime? ModifiedDate);
    private sealed record Snapshot(IdentityState Identity, IdentityState OtherIdentity, ProfileState Profile, ProfileState OtherProfile,
        string Relations, int ProfileCount, int IdentityCount);

    private sealed class JoinedFixture(SharedContainers containers) : IAsyncDisposable
    {
        private const string Issuer = "https://disposable-customer-auth.invalid";
        private const string Audience = "disposable-intranet";
        private PostgreSqlContainer postgres => containers.Postgres;
        private readonly int redisDatabase = containers.AllocateRedisDatabase();
        private string RedisConnection => containers.Redis.GetConnectionString() + ",defaultDatabase="
            + redisDatabase.ToString(System.Globalization.CultureInfo.InvariantCulture);
        private readonly RSA rsa = RSA.Create(2048);
        private readonly string exchangeSecret = Guid.NewGuid().ToString("N");
        private readonly string liveCredential = Guid.NewGuid().ToString("N");
        private readonly string certificatePassword = Guid.NewGuid().ToString("N");
        private string profileConnection = "";
        private string employeeIdentityConnection = "";
        private string customerIdentityConnection = "";
        private string refreshConnection = "";
        private WebApplicationFactory<AuthProgram>? auth;
        private WebApplicationFactory<BffProgram>? bff;
        private WebApplicationFactory<CustomerProgram>? customer;
        public WebApplicationFactory<CustomerProgram> Customer => customer!;
        public ConcurrentQueue<string> Writes { get; } = new();
        public ConcurrentQueue<string> TransportFailures { get; } = new();
        public TaskCompletionSource IdentityEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource IdentityRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldIdentity { get; init; }
        public bool LoseProfileAcknowledgement { get; init; }
        public int ProfileCommitsAcknowledged;
        public int IamCalls;
        public string ActorToken { get; private set; } = "";
        public CustomerAdministrationEdit? ExpectedEdit { get; set; }

        public async Task InitializeAsync()
        {
            profileConnection = await DatabaseAsync();
            employeeIdentityConnection = await DatabaseAsync();
            customerIdentityConnection = await DatabaseAsync();
            refreshConnection = await DatabaseAsync();
            await using (var context = ProfileContext())
            {
                await context.Database.MigrateAsync();
                context.Companies.Add(new Company { Name = "Original company", TaxNumber = "synthetic" });
                context.Addresses.AddRange(new Address { AddressLine1 = "Billing", CountryId = 764 },
                    new Address { AddressLine1 = "Shipping", CountryId = 764 });
                await context.SaveChangesAsync();
                context.Customers.AddRange(new Customer { FirstName = "Target", LastName = "Fixture", Email = "target@maliev.test",
                    CompanyId = 1, BillingAddressId = 1, ShippingAddressId = 2, InternalRemark = "Protected fixture remark" },
                    new Customer { FirstName = "Other", LastName = "Fixture", Email = "other@maliev.test" });
                await context.SaveChangesAsync();
            }
            await using (var context = EmployeeIdentityContext())
            {
                await context.Database.MigrateAsync();
                context.Users.Add(Identity("disposable-101", 101, "actor@maliev.test"));
                await context.SaveChangesAsync();
            }
            await using (var context = CustomerIdentityContext())
            {
                await context.Database.MigrateAsync();
                var target = Identity("disposable-1", 1, "target@maliev.test");
                target.TwoFactorEnabled = true;
                target.LockoutEnd = DateTimeOffset.UtcNow.AddDays(1);
                context.Users.AddRange(target, Identity("disposable-2", 2, "other@maliev.test"));
                await context.SaveChangesAsync();
            }
            await using (var context = new RefreshSessionDbContext(new DbContextOptionsBuilder<RefreshSessionDbContext>().UseNpgsql(refreshConnection).Options))
                await context.Database.MigrateAsync();
            auth = new AuthFactory(this);
            using (var client = auth.CreateClient())
            {
                using var login = await client.PostAsJsonAsync("/auth/v1/login", new { email = "actor@maliev.test", password = "disposable" });
                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
                ActorToken = body.RootElement.GetProperty("accessToken").GetString()!;
                var issued = new JwtSecurityTokenHandler().ReadJwtToken(ActorToken);
                Assert.Equal("employee", issued.Claims.Single(claim => claim.Type == "identity_kind").Value);
                Assert.Equal("disposable-101", issued.Subject);
                // Fail closed on an old issuer: the fixture must never manufacture grants.
                foreach (var permission in Permissions)
                    Assert.Contains(issued.Claims, claim => claim.Type == "permissions" && claim.Value == permission);
            }
            customer = new CustomerFactory(this);
            using var initialized = customer.CreateClient();
            bff = new BffFactory(this);
        }

        private async Task<string> DatabaseAsync()
        {
            var name = "customer_join_" + Guid.NewGuid().ToString("N");
            await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
            return new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = name }.ConnectionString;
        }

        private CustomerDbContext ProfileContext() => new(new DbContextOptionsBuilder<CustomerDbContext>().UseNpgsql(profileConnection).Options);
        private EmployeeIdentityDbContext EmployeeIdentityContext() => new(new DbContextOptionsBuilder<EmployeeIdentityDbContext>().UseNpgsql(employeeIdentityConnection).Options);
        private CustomerIdentityDbContext CustomerIdentityContext() => new(new DbContextOptionsBuilder<CustomerIdentityDbContext>().UseNpgsql(customerIdentityConnection).Options);

        private static LegacyIdentityRow Identity(string id, int databaseId, string email)
        {
            var row = new LegacyIdentityRow { Id = id, DatabaseID = databaseId, UserName = email,
                NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(),
                EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true };
            row.PasswordHash = new PasswordHasher<LegacyIdentityRow>().HashPassword(row, "disposable");
            return row;
        }

        public async Task<Snapshot> SnapshotAsync()
        {
            await using var identities = CustomerIdentityContext();
            await using var profiles = ProfileContext();
            var identityRows = await identities.Users.AsNoTracking().OrderBy(row => row.DatabaseID).ToArrayAsync();
            var customers = await profiles.Customers.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync();
            var relations = JsonSerializer.Serialize(new
            {
                Companies = await profiles.Companies.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
                Addresses = await profiles.Addresses.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            });
            static ProfileState P(Customer row) => new(row.FirstName, row.LastName, row.DateOfBirth, row.Email, row.Telephone, row.Mobile, row.Fax,
                row.CompanyId, row.BillingAddressId, row.ShippingAddressId, row.InternalRemark, row.ModifiedDate);
            static IdentityState I(LegacyIdentityRow identity) => new(identity.UserName, identity.Email, identity.EmailConfirmed, identity.PhoneNumber,
                identity.PhoneNumberConfirmed, identity.LockoutEnabled, identity.TwoFactorEnabled, identity.LockoutEnd,
                identity.PasswordHash, identity.SecurityStamp, identity.ConcurrencyStamp, identity.FaxNumber, identity.MobileNumber);
            return new(I(identityRows.Single(row => row.DatabaseID == 1)), I(identityRows.Single(row => row.DatabaseID == 2)),
                P(customers[0]), P(customers[1]), relations, customers.Length, await identities.Users.CountAsync());
        }

        public async Task MoveProfileAsync()
        {
            using var client = Customer.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ActorToken);
            using var read = await client.GetAsync("/customers/1/versioned");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var profile = await read.Content.ReadFromJsonAsync<CustomerDetail>();
            Assert.NotNull(profile);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/customers/1/versioned")
            {
                Content = JsonContent.Create(new { FirstName = "Concurrent profile", profile.LastName, profile.Email,
                    profile.Telephone, profile.Mobile, profile.Fax, profile.DateOfBirth,
                    profile.CompanyId, profile.BillingAddressId, profile.ShippingAddressId }),
            };
            request.Headers.IfMatch.Add(read.Headers.ETag!);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        public async Task MoveIdentityAsync()
        {
            // An independent real Auth HTTP writer, not a repository substitution.
            using var client = auth!.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ActorToken);
            using var read = await client.GetAsync("/auth/v1/customer-identities/1");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var identity = await read.Content.ReadFromJsonAsync<CustomerAdministrationIdentity>();
            Assert.NotNull(identity);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/auth/v1/customer-identities/1/versioned")
            {
                Content = JsonContent.Create(new CustomerAdministrationIdentityUpdate(identity.UserName!, identity.Email!,
                    identity.EmailConfirmed, identity.PhoneNumber, identity.PhoneNumberConfirmed, identity.TwoFactorEnabled,
                    identity.LockoutEnd, !identity.LockoutEnabled, identity.FaxNumber, identity.MobileNumber)),
            };
            request.Headers.IfMatch.Add(read.Headers.ETag!);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        public async Task<HttpClient> SignedInAsync()
        {
            var client = bff!.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
                { Content = JsonContent.Create(new { email = "actor@maliev.test", password = "disposable", returnUrl = "/Customers/Edit?id=1" }) };
                request.Headers.Add("X-CSRF-TOKEN", await CsrfAsync(client));
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                await using var state = new RefreshSessionDbContext(new DbContextOptionsBuilder<RefreshSessionDbContext>().UseNpgsql(refreshConnection).Options);
                Assert.Equal(2, await state.RefreshSessions.CountAsync());
                using var connection = await ConnectionMultiplexer.ConnectAsync(RedisConnection);
                var server = connection.GetServer(connection.GetEndPoints()[0]);
                Assert.Single(server.Keys(database: redisDatabase, pattern: "*legacy-intranet:session:*"));
                Assert.NotEmpty(server.Keys(database: redisDatabase, pattern: "legacy:intranet:data-protection-keys"));
                return client;
            }
            catch { client.Dispose(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            IdentityRelease.TrySetResult();
            // Nested finally guarantees every owned resource is attempted even after failed setup or cleanup.
            try { if (bff is not null) await bff.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)); }
            finally
            {
                try { if (customer is not null) await customer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)); }
                finally
                {
                    try { if (auth is not null) await auth.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)); }
                    finally
                    {
                        rsa.Dispose();
                    }
                }
            }
        }

        private Dictionary<string, string?> JwtSettings() => new()
        {
            ["Jwt:Issuer"] = Issuer, ["Jwt:Audience"] = Audience, ["Jwt:KeyId"] = "disposable-customer-join",
            ["Jwt:PublicKeyPem"] = rsa.ExportSubjectPublicKeyInfoPem(),
            ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
            ["Logging:LogLevel:Default"] = "Warning", ["CORS:AllowedOrigins"] = "https://localhost",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
        };

        private sealed class AuthFactory(JoinedFixture fixture) : WebApplicationFactory<AuthProgram>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Production");
                var settings = fixture.JwtSettings();
                settings["Jwt:PrivateKeyPem"] = fixture.rsa.ExportPkcs8PrivateKeyPem();
                settings["ConnectionStrings:EmployeeIdentity"] = fixture.employeeIdentityConnection;
                settings["ConnectionStrings:CustomerIdentity"] = fixture.customerIdentityConnection;
                settings["ConnectionStrings:RefreshSessions"] = fixture.refreshConnection;
                settings["EmployeeRecovery:Enabled"] = "true";
                settings["Services:IAMService:BaseUrl"] = "https://controlled-iam.invalid";
                settings["ServiceAuthentication:ClientId"] = "legacy-auth";
                settings["ServiceAuthentication:ClientSecret"] = fixture.exchangeSecret;
                settings["Services:Auth"] = "https://controlled-exchange.invalid";
                settings["Features:ResourceScopedAuthEnabled"] = "true";
                settings["IAM:LivePermissionChecks:Credential"] = fixture.liveCredential;
                foreach (var item in settings) builder.UseSetting(item.Key, item.Value);
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
                builder.ConfigureServices(services =>
                {
                    services.AddHttpClient("IAMService").ConfigurePrimaryHttpMessageHandler(() => new ReferenceTransport(fixture));
                    services.AddHttpClient("LegacyAuthServiceTokenExchange").ConfigurePrimaryHttpMessageHandler(() => new ReferenceTransport(fixture));
                });
            }
        }

        private sealed class CustomerFactory(JoinedFixture fixture) : WebApplicationFactory<CustomerProgram>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Production");
                var settings = fixture.JwtSettings();
                settings["ConnectionStrings:CustomerDbContext"] = fixture.profileConnection;
                settings["ConnectionStrings:redis"] = fixture.RedisConnection;
                settings["Cache:RedisEnabled"] = "true";
                settings["Features:ResourceScopedAuthEnabled"] = "true";
                settings["IAM:LivePermissionChecks:Credential"] = fixture.liveCredential;
                foreach (var item in settings) builder.UseSetting(item.Key, item.Value);
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
                builder.ConfigureServices(services =>
                {
                    // Real remote IAM client with controlled permission transport; no authorization-handler substitution.
                    services.AddScoped<IIamServiceClient, IamServiceClient>();
                    services.AddHttpClient("IAMService", client => client.BaseAddress = new Uri("https://controlled-iam.invalid"))
                        .ConfigurePrimaryHttpMessageHandler(() => new ReferenceTransport(fixture));
                });
            }
        }

        private sealed class BffFactory(JoinedFixture fixture) : WebApplicationFactory<BffProgram>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Development");
                var settings = fixture.JwtSettings();
                settings["Workspace:AllowLocalTestDomain"] = "true";
                settings["ConnectionStrings:redis"] = fixture.RedisConnection;
                using var key = RSA.Create(2048);
                var request = new CertificateRequest("CN=disposable-customer-join", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
                settings["DataProtection:CertificatePfxBase64"] = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, fixture.certificatePassword));
                settings["DataProtection:CertificatePassword"] = fixture.certificatePassword;
                settings["Services:Auth"] = "https://disposable-auth.invalid";
                settings["Services:Customer"] = "https://disposable-customer.invalid";
                settings["Services:Catalog"] = "https://disposable-catalog.invalid";
                foreach (var item in settings) builder.UseSetting(item.Key, item.Value);
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
                builder.ConfigureServices(services =>
                {
                    services.AddHttpClient<Legacy.Maliev.Intranet.Auth.ILegacyAuthClient, Legacy.Maliev.Intranet.Auth.LegacyAuthClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => fixture.auth!.Server.CreateHandler());
                    services.AddHttpClient<IdentityClient>().ConfigurePrimaryHttpMessageHandler(() => new OrderedTransport(fixture, true)
                    { InnerHandler = fixture.auth!.Server.CreateHandler() });
                    services.AddHttpClient<ProfileClient>().ConfigurePrimaryHttpMessageHandler(() => new OrderedTransport(fixture, false)
                    { InnerHandler = fixture.Customer.Server.CreateHandler() });
                    services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(handler =>
                    {
                        if (!new[] { nameof(Legacy.Maliev.Intranet.Auth.ILegacyAuthClient), nameof(IdentityClient), nameof(ProfileClient) }.Contains(handler.Name))
                            handler.PrimaryHandler = new BlockOutbound();
                    }));
                });
            }
        }

        private sealed class OrderedTransport(JoinedFixture fixture, bool identity) : DelegatingHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var writing = request.Method == HttpMethod.Put;
                if (writing)
                {
                    Assert.Equal(identity ? "/auth/v1/customer-identities/1/versioned" : "/customers/1/versioned", request.RequestUri!.AbsolutePath);
                    Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                    var actor = new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter!);
                    Assert.Equal("employee", actor.Claims.Single(claim => claim.Type == "identity_kind").Value);
                    Assert.Equal("disposable-101", actor.Subject);
                    var edit = fixture.ExpectedEdit!;
                    Assert.Equal(identity ? edit.IdentityVersion : edit.ProfileVersion, request.Headers.IfMatch.Single().ToString());
                    fixture.Writes.Enqueue(identity ? "identity" : "profile");
                    if (identity && fixture.HoldIdentity)
                    {
                        fixture.IdentityEntered.TrySetResult();
                        await fixture.IdentityRelease.Task.WaitAsync(token);
                    }
                }
                var response = await base.SendAsync(request, token);
                if (writing && !identity && fixture.LoseProfileAcknowledgement)
                {
                    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                    Interlocked.Increment(ref fixture.ProfileCommitsAcknowledged);
                    response.Dispose();
                    throw new HttpRequestException("Controlled lost acknowledgement after real producer commit.");
                }
                return response;
            }
        }

        private sealed class ReferenceTransport(JoinedFixture fixture) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                try { return await SendCheckedAsync(request, token); }
                catch (Exception exception)
                {
                    // Production IAM catches transport errors and may use exact signed-claim fallback.
                    // Preserve a safe independent oracle so that cannot hide a malformed fixture request.
                    fixture.TransportFailures.Enqueue(exception.GetType().Name);
                    throw;
                }
            }

            private async Task<HttpResponseMessage> SendCheckedAsync(HttpRequestMessage request, CancellationToken token)
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path == "/auth/v1/service/login" && request.Method == HttpMethod.Post)
                {
                    using var exchange = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                    Assert.Equal("legacy-auth", exchange.RootElement.GetProperty("clientId").GetString());
                    Assert.True(string.Equals(fixture.exchangeSecret, exchange.RootElement.GetProperty("clientSecret").GetString(), StringComparison.Ordinal));
                    return Json(new { accessToken = "disposable-iam-workload-transport-only", tokenType = "Bearer", expiresIn = 900 });
                }
                Assert.Equal("/iam/v1/auth/check-permission", path);
                Assert.Equal(HttpMethod.Post, request.Method);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var principal = body.RootElement.GetProperty("principalId").GetString();
                var permission = body.RootElement.GetProperty("permissionId").GetString();
                var resource = body.RootElement.GetProperty("resourcePath").GetString();
                Assert.Equal("disposable-101", principal);
                var allowed = permission switch
                {
                    "legacy-auth.customer-identities.read" or "legacy-auth.customer-identities.update" => resource == "global",
                    "legacy-customer.customers.read" or "legacy-customer.customers.update" => resource == "/customers/1",
                    _ => false,
                };
                Assert.True(allowed, "Unexpected permission/resource tuple in controlled IAM transport.");
                if (body.RootElement.GetProperty("bypassCache").GetBoolean())
                    Assert.True(string.Equals(fixture.liveCredential, request.Headers.GetValues("X-Maliev-IAM-Live-Check-Key").Single(), StringComparison.Ordinal));
                Interlocked.Increment(ref fixture.IamCalls);
                return Json(new { principalId = Guid.Parse("11111111-1111-1111-1111-111111111111"), permissionId = permission,
                    resourcePath = resource, allowed = true, fromCache = false, latencyMs = 0 });
            }
            private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
        }

        private sealed class BlockOutbound : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
                Task.FromException<HttpResponseMessage>(new HttpRequestException("Unrelated outbound integration prohibited."));
        }
    }
}
