extern alias AuthApi;
extern alias Bff;
extern alias EmployeeApi;

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
using Legacy.Maliev.EmployeeService.Data;
using Legacy.Maliev.EmployeeService.Domain;
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
using EmployeeProgram = EmployeeApi::Program;
using Role = Legacy.Maliev.EmployeeService.Domain.Role;
using IdentityClient = Bff::Legacy.Maliev.Intranet.Bff.Employees.EmployeeAdministrationIdentityClient;
using ProfileClient = Bff::Legacy.Maliev.Intranet.Bff.Employees.EmployeeAdministrationProfileClient;
using CountryClient = Bff::Legacy.Maliev.Intranet.Bff.Employees.EmployeeAdministrationCountryClient;

namespace EmployeeAdministration.Acceptance;

public sealed class EmployeeJoinContainers : IAsyncLifetime
{
    private static readonly string ResourceRun = Environment.GetEnvironmentVariable("MALIEV_EMPLOYEE_JOIN_RUN")
        ?? Guid.NewGuid().ToString("N");
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:18-alpine")
        .WithLabel("maliev.validation.employee-join", ResourceRun)
        .WithCreateParameterModifier(parameters =>
        {
            parameters.HostConfig.Memory = 1024L * 1024 * 1024;
            parameters.HostConfig.NanoCPUs = 1_000_000_000;
            foreach (var bindings in parameters.HostConfig.PortBindings.Values)
                foreach (var binding in bindings) binding.HostIP = "127.0.0.1";
        }).Build();
    public RedisContainer Redis { get; } = new RedisBuilder("redis:7-alpine")
        .WithLabel("maliev.validation.employee-join", ResourceRun)
        .WithCreateParameterModifier(parameters =>
        {
            parameters.HostConfig.Memory = 128L * 1024 * 1024;
            parameters.HostConfig.NanoCPUs = 500_000_000;
            foreach (var bindings in parameters.HostConfig.PortBindings.Values)
                foreach (var binding in bindings) binding.HostIP = "127.0.0.1";
        }).Build();
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
            catch { /* Preserve setup failure after attempting both owned disposals. */ }
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

public sealed class EmployeeAdministrationAcceptanceTests(EmployeeJoinContainers containers) : IClassFixture<EmployeeJoinContainers>
{
    [Fact]
    public async Task RealEmployeeSession_OrdersIdentityBoundAddressAndProfile_WithIndependentReadbacks()
    {
        await using var fixture = new JoinedFixture(containers);
        await fixture.InitializeAsync();
        using var client = await fixture.SignedInAsync();
        var edit = await ReadAsync(client);
        fixture.ExpectedEdit = edit;
        Assert.Equal(1, edit.Profile.HomeAddressId);
        Assert.Equal(1, edit.Address!.Id);
        Assert.Contains(edit.Countries, country => country.Id == 764);
        var before = await fixture.SnapshotAsync();
        using var response = await SaveAsync(client, Input(edit));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<EmployeeAdministrationSaveResult>())!;
        Assert.Equal("complete", receipt.Stage);
        Assert.False(receipt.OutcomeUnknown);
        Assert.Equal(new[] { "identity", "address", "profile" }, receipt.CompletedStages);
        Assert.Equal(new[] { "identity", "address", "profile" }, fixture.Writes.ToArray());
        var after = await fixture.SnapshotAsync();
        Assert.Equal("changed@maliev.test", after.Identity.Email);
        Assert.Equal(after.Identity.Email, after.Identity.UserName);
        Assert.Equal(before.Identity.PasswordHash, after.Identity.PasswordHash);
        Assert.Equal(before.Identity.TwoFactorEnabled, after.Identity.TwoFactorEnabled);
        Assert.Equal(before.Identity.LockoutEnd, after.Identity.LockoutEnd);
        Assert.Equal("แก้ไข", after.Employee.FirstName);
        Assert.Equal("changed@maliev.test", after.Employee.Email);
        Assert.Equal("Updated family", after.Employee.LastName);
        Assert.Equal(new DateTime(1990, 1, 2), after.Employee.DateOfBirth);
        Assert.Equal("+6621111111", after.Employee.PhoneNumber);
        Assert.Equal(after.Employee.PhoneNumber, after.Identity.PhoneNumber);
        Assert.Equal(2, after.Employee.RoleId);
        Assert.False(after.Identity.EmailConfirmed);
        Assert.True(after.Identity.PhoneNumberConfirmed);
        Assert.False(after.Identity.LockoutEnabled);
        Assert.Equal(1, after.Employee.HomeAddressId);
        Assert.Equal("บ้านใหม่", after.Address.AddressLine1);
        Assert.Equal(before.OtherAddress, after.OtherAddress);
        Assert.Equal(before.OtherEmployee, after.OtherEmployee);
        Assert.Equal(2, after.EmployeeCount);
        Assert.Equal(2, after.AddressCount);
        Assert.Equal(2, after.IdentityCount);
        Assert.True(fixture.IamCalls > 0);
        Assert.Empty(fixture.TransportFailures);
    }

    [Fact]
    public async Task ConcurrentRealRelationMove_AfterIdentityCommit_StopsCoupledAddressAndProfile()
    {
        await using var fixture = new JoinedFixture(containers) { HoldAddress = true };
        await fixture.InitializeAsync();
        using var client = await fixture.SignedInAsync();
        var edit = await ReadAsync(client);
        fixture.ExpectedEdit = edit;
        var before = await fixture.SnapshotAsync();
        var saving = SaveAsync(client, Input(edit));
        Exception? primary = null;
        try
        {
            await fixture.AddressEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using var mover = fixture.Employee.CreateClient();
            mover.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.ActorToken);
            using var moved = await mover.PutAsJsonAsync("/employees/1", new
            {
                RoleId = 1, FirstName = "Concurrent", LastName = "Fixture", Email = "target@maliev.test",
                HomeAddressId = 2,
            });
            Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
            var afterMove = await fixture.SnapshotAsync();
            fixture.AddressRelease.TrySetResult();
            using var response = await saving.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            var receipt = (await response.Content.ReadFromJsonAsync<EmployeeAdministrationSaveResult>())!;
            Assert.Equal("address", receipt.Stage);
            Assert.False(receipt.OutcomeUnknown);
            Assert.Equal(new[] { "identity" }, receipt.CompletedStages);
            var after = await fixture.SnapshotAsync();
            Assert.Equal(afterMove.Employee, after.Employee);
            Assert.Equal(before.Address, after.Address);
            Assert.Equal(before.OtherAddress, after.OtherAddress);
            Assert.Equal("changed@maliev.test", after.Identity.Email);
            Assert.Equal(new[] { "identity", "address" }, fixture.Writes.ToArray());
            Assert.Empty(fixture.TransportFailures);
        }
        catch (Exception exception)
        {
            primary = exception;
            throw;
        }
        finally
        {
            fixture.AddressRelease.TrySetResult();
            // Observe and dispose the owned pending response even when an earlier assertion fails.
            try { using var pending = await saving.WaitAsync(TimeSpan.FromSeconds(30)); }
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

    [Theory]
    [InlineData("http")]
    [InlineData("io")]
    [InlineData("cancellation")]
    public async Task LostRealAddress204Acknowledgement_IsUnknownAndNeverReplaysOrWritesProfile(string failure)
    {
        await using var fixture = new JoinedFixture(containers) { LoseAddressAcknowledgement = true, AcknowledgementFailure = failure };
        await fixture.InitializeAsync();
        using var client = await fixture.SignedInAsync();
        var edit = await ReadAsync(client);
        fixture.ExpectedEdit = edit;
        var before = await fixture.SnapshotAsync();
        using var response = await SaveAsync(client, Input(edit));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<EmployeeAdministrationSaveResult>())!;
        Assert.Equal("address", receipt.Stage);
        Assert.True(receipt.OutcomeUnknown);
        Assert.Equal(new[] { "identity" }, receipt.CompletedStages);
        Assert.Equal(1, fixture.AddressCommitsAcknowledged);
        Assert.Equal(new[] { "identity", "address" }, fixture.Writes.ToArray());
        Assert.Empty(fixture.TransportFailures);
        var after = await fixture.SnapshotAsync();
        Assert.Equal("changed@maliev.test", after.Identity.Email);
        Assert.Equal("บ้านใหม่", after.Address.AddressLine1);
        Assert.Equal(before.Employee, after.Employee);
        Assert.Equal(before.OtherAddress, after.OtherAddress);
        // Explicit GET-only readback obtains new versions; it does not silently replay the save.
        var reloaded = await ReadAsync(client);
        Assert.NotEqual(edit.IdentityVersion, reloaded.IdentityVersion);
        Assert.NotEqual(edit.AddressVersion, reloaded.AddressVersion);
        Assert.Equal(edit.ProfileVersion, reloaded.ProfileVersion);
        Assert.Equal(new[] { "identity", "address" }, fixture.Writes.ToArray());
    }

    [Fact]
    public async Task RealNewAddress_ConfirmedProducerIdLinksOnceAndPreservesExistingRows()
    {
        await using var fixture = new JoinedFixture(containers) { NoInitialAddress = true };
        await fixture.InitializeAsync();
        using var client = await fixture.SignedInAsync();
        var edit = await ReadAsync(client);
        fixture.ExpectedEdit = edit;
        Assert.Null(edit.Profile.HomeAddressId);
        Assert.Null(edit.Address);
        Assert.Null(edit.AddressVersion);
        var before = await fixture.SnapshotAsync();
        using var response = await SaveAsync(client, Input(edit));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<EmployeeAdministrationSaveResult>())!;
        Assert.Equal("complete", receipt.Stage);
        Assert.False(receipt.OutcomeUnknown);
        Assert.Equal(new[] { "identity", "address", "profile" }, receipt.CompletedStages);
        Assert.True(fixture.CreatedAddressId > 2);
        Assert.Equal(fixture.CreatedAddressId, receipt.CreatedAddressId);
        Assert.Equal(new[] { "identity", "address", "profile" }, fixture.Writes.ToArray());
        var after = await fixture.SnapshotAsync();
        Assert.Equal(fixture.CreatedAddressId, after.Employee.HomeAddressId);
        Assert.Equal(3, after.AddressCount);
        Assert.Equal(before.EmployeeCount, after.EmployeeCount);
        Assert.Equal(before.IdentityCount, after.IdentityCount);
        Assert.Equal(before.Address, after.Address);
        Assert.Equal(before.OtherAddress, after.OtherAddress);
        Assert.Equal(before.OtherEmployee, after.OtherEmployee);
        Assert.Equal(before.Identity.PasswordHash, after.Identity.PasswordHash);
        Assert.Equal(before.Identity.TwoFactorEnabled, after.Identity.TwoFactorEnabled);
        Assert.Equal(before.Identity.LockoutEnd, after.Identity.LockoutEnd);
        Assert.Equal("บ้านใหม่", (await fixture.StoredAddressAsync(fixture.CreatedAddressId)).AddressLine1);
        var reloaded = await ReadAsync(client);
        Assert.Equal(fixture.CreatedAddressId, reloaded.Profile.HomeAddressId);
        Assert.Equal(fixture.CreatedAddressId, reloaded.Address!.Id);
        Assert.Equal(after, await fixture.SnapshotAsync());
        Assert.Equal(new[] { "identity", "address", "profile" }, fixture.Writes.ToArray());
        Assert.Empty(fixture.TransportFailures);
    }

    private static EmployeeAdministrationSaveRequest Input(EmployeeAdministrationEdit edit) => new(
        "แก้ไข", "Updated family", "changed@maliev.test", "+6621111111", new DateTime(1990, 1, 2), 2, false, true, false,
        edit.Profile.HomeAddressId, edit.ProfileVersion, edit.IdentityVersion, edit.AddressVersion,
        new(null, "บ้านใหม่", null, "กรุงเทพฯ", null, "10100", 764));

    private static async Task<EmployeeAdministrationEdit> ReadAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/bff/employees/1/edit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EmployeeAdministrationEdit>())!;
    }

    private static async Task<string> CsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/bff/session");
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("csrfToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> SaveAsync(HttpClient client, EmployeeAdministrationSaveRequest input)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/employees/1/edit") { Content = JsonContent.Create(input) };
        request.Headers.Add("X-CSRF-TOKEN", await CsrfAsync(client));
        return await client.SendAsync(request);
    }

    private sealed record IdentityState(string? Email, string? UserName, string? PasswordHash, bool TwoFactorEnabled,
        DateTimeOffset? LockoutEnd, string? PhoneNumber, bool EmailConfirmed, bool PhoneNumberConfirmed, bool LockoutEnabled);
    private sealed record EmployeeState(string FirstName, string LastName, string Email, string? PhoneNumber,
        DateTime? DateOfBirth, int? RoleId, int? HomeAddressId, DateTime? ModifiedDate);
    private sealed record AddressState(string? AddressLine1, string? City, int CountryId, DateTime? ModifiedDate);
    private sealed record Snapshot(IdentityState Identity, EmployeeState Employee, AddressState Address,
        AddressState OtherAddress, EmployeeState OtherEmployee, int EmployeeCount, int AddressCount, int IdentityCount);

    private sealed class JoinedFixture(EmployeeJoinContainers containers) : IAsyncDisposable
    {
        private const string Issuer = "https://disposable-employee-auth.invalid";
        private const string Audience = "disposable-intranet";
        private PostgreSqlContainer postgres => containers.Postgres;
        private readonly int redisDatabase = containers.AllocateRedisDatabase();
        private string RedisConnection => containers.Redis.GetConnectionString() + ",defaultDatabase="
            + redisDatabase.ToString(System.Globalization.CultureInfo.InvariantCulture);
        private readonly RSA rsa = RSA.Create(2048);
        private string employeeConnection = "";
        private string identityConnection = "";
        private string customerConnection = "";
        private string refreshConnection = "";
        private WebApplicationFactory<AuthProgram>? auth;
        private WebApplicationFactory<BffProgram>? bff;
        private WebApplicationFactory<EmployeeProgram>? employee;
        public WebApplicationFactory<EmployeeProgram> Employee => employee!;
        public ConcurrentQueue<string> Writes { get; } = new();
        public ConcurrentQueue<string> TransportFailures { get; } = new();
        public TaskCompletionSource AddressEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AddressRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldAddress { get; init; }
        public bool LoseAddressAcknowledgement { get; init; }
        public string AcknowledgementFailure { get; init; } = "http";
        public bool NoInitialAddress { get; init; }
        public int AddressCommitsAcknowledged;
        public int CreatedAddressId;
        public int IamCalls;
        public string ActorToken { get; private set; } = "";
        public EmployeeAdministrationEdit? ExpectedEdit { get; set; }

        public async Task InitializeAsync()
        {
            employeeConnection = await DatabaseAsync();
            identityConnection = await DatabaseAsync();
            customerConnection = await DatabaseAsync();
            refreshConnection = await DatabaseAsync();
            await using (var context = EmployeeContext())
            {
                await context.Database.MigrateAsync();
                context.Roles.AddRange(new Role { Name = "Engineer" }, new Role { Name = "Technician" });
                context.Addresses.AddRange(new Address { AddressLine1 = "Original", CountryId = 764 },
                    new Address { AddressLine1 = "Other", CountryId = 764 });
                await context.SaveChangesAsync();
                context.Employees.AddRange(new Employee { FirstName = "Target", LastName = "Fixture", Email = "target@maliev.test", HomeAddressId = NoInitialAddress ? null : 1, RoleId = 1 },
                    new Employee { FirstName = "Other", LastName = "Fixture", Email = "other@maliev.test", HomeAddressId = 2 });
                await context.SaveChangesAsync();
            }
            await using (var context = IdentityContext())
            {
                await context.Database.MigrateAsync();
                var hasher = new PasswordHasher<LegacyIdentityRow>();
                foreach (var (id, email) in new[] { (101, "actor@maliev.test"), (1, "target@maliev.test") })
                {
                    var row = new LegacyIdentityRow { Id = $"disposable-{id}", DatabaseID = id, UserName = email,
                        NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(),
                        EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true };
                    row.PasswordHash = hasher.HashPassword(row, "disposable");
                    if (id == 1)
                    {
                        row.TwoFactorEnabled = true;
                        row.LockoutEnd = DateTimeOffset.UtcNow.AddDays(1);
                    }
                    context.Users.Add(row);
                }
                await context.SaveChangesAsync();
            }
            await using (var context = new CustomerIdentityDbContext(new DbContextOptionsBuilder<CustomerIdentityDbContext>().UseNpgsql(customerConnection).Options))
                await context.Database.MigrateAsync();
            await using (var context = new RefreshSessionDbContext(new DbContextOptionsBuilder<RefreshSessionDbContext>().UseNpgsql(refreshConnection).Options))
                await context.Database.MigrateAsync();
            auth = new AuthFactory(this);
            using (var client = auth.CreateClient())
            {
                using var login = await client.PostAsJsonAsync("/auth/v1/login", new { userName = "actor@maliev.test", password = "disposable", identityKind = 1 });
                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
                ActorToken = body.RootElement.GetProperty("accessToken").GetString()!;
                var issued = new JwtSecurityTokenHandler().ReadJwtToken(ActorToken);
                Assert.Equal("employee", issued.Claims.Single(claim => claim.Type == "identity_kind").Value);
                Assert.Equal("disposable-101", issued.Subject);
                // Fail closed on an old issuer: the fixture must never manufacture grants.
                foreach (var permission in new[] { "legacy-auth.employee-identities.read", "legacy-auth.employee-identities.update",
                    "legacy-employee.employees.read", "legacy-employee.employees.update", "legacy-employee.addresses.read",
                    "legacy-employee.addresses.update", "legacy-employee.addresses.create", "legacy-employee.roles.read" })
                    Assert.Contains(issued.Claims, claim => claim.Type == "permissions" && claim.Value == permission);
            }
            employee = new EmployeeFactory(this);
            using var initialized = employee.CreateClient();
            bff = new BffFactory(this);
        }

        private async Task<string> DatabaseAsync()
        {
            var name = "employee_join_" + Guid.NewGuid().ToString("N");
            await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
            return new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = name }.ConnectionString;
        }

        private EmployeeDbContext EmployeeContext() => new(new DbContextOptionsBuilder<EmployeeDbContext>().UseNpgsql(employeeConnection).Options);
        private EmployeeIdentityDbContext IdentityContext() => new(new DbContextOptionsBuilder<EmployeeIdentityDbContext>().UseNpgsql(identityConnection).Options);

        public async Task<Snapshot> SnapshotAsync()
        {
            await using var identities = IdentityContext();
            await using var profiles = EmployeeContext();
            var identity = await identities.Users.AsNoTracking().SingleAsync(row => row.DatabaseID == 1);
            var employees = await profiles.Employees.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync();
            var addresses = await profiles.Addresses.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync();
            static EmployeeState E(Employee row) => new(row.FirstName, row.LastName, row.Email, row.PhoneNumber,
                row.DateOfBirth, row.RoleId, row.HomeAddressId, row.ModifiedDate);
            static AddressState A(Address row) => new(row.AddressLine1, row.City, row.CountryId, row.ModifiedDate);
            return new(new(identity.Email, identity.UserName, identity.PasswordHash, identity.TwoFactorEnabled, identity.LockoutEnd,
                identity.PhoneNumber, identity.EmailConfirmed, identity.PhoneNumberConfirmed, identity.LockoutEnabled),
                E(employees[0]), A(addresses[0]), A(addresses[1]), E(employees[1]), employees.Length, addresses.Length, await identities.Users.CountAsync());
        }

        public async Task<HttpClient> SignedInAsync()
        {
            var client = bff!.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
                { Content = JsonContent.Create(new { email = "actor@maliev.test", password = "disposable", returnUrl = "/Employees/Edit?id=1" }) };
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

        public async Task<AddressState> StoredAddressAsync(int id)
        {
            await using var context = EmployeeContext();
            var row = await context.Addresses.AsNoTracking().SingleAsync(address => address.Id == id);
            return new(row.AddressLine1, row.City, row.CountryId, row.ModifiedDate);
        }

        public async ValueTask DisposeAsync()
        {
            AddressRelease.TrySetResult();
            // Nested finally guarantees every owned resource is attempted even after failed setup or cleanup.
            try { if (bff is not null) await bff.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)); }
            finally
            {
                try { if (employee is not null) await employee.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)); }
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
            ["Jwt:Issuer"] = Issuer, ["Jwt:Audience"] = Audience, ["Jwt:KeyId"] = "disposable-employee-join",
            ["Jwt:PublicKeyPem"] = rsa.ExportSubjectPublicKeyInfoPem(),
            ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
            ["Logging:LogLevel:Default"] = "Warning", ["CORS:AllowedOrigins"] = "https://localhost",
        };

        private sealed class AuthFactory(JoinedFixture fixture) : WebApplicationFactory<AuthProgram>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Production");
                var settings = fixture.JwtSettings();
                settings["Jwt:PrivateKeyPem"] = fixture.rsa.ExportPkcs8PrivateKeyPem();
                settings["ConnectionStrings:EmployeeIdentity"] = fixture.identityConnection;
                settings["ConnectionStrings:CustomerIdentity"] = fixture.customerConnection;
                settings["ConnectionStrings:RefreshSessions"] = fixture.refreshConnection;
                settings["EmployeeRecovery:Enabled"] = "true";
                settings["Services:IAMService:BaseUrl"] = "https://controlled-iam.invalid";
                settings["ServiceAuthentication:ClientId"] = "legacy-auth";
                settings["ServiceAuthentication:ClientSecret"] = "disposable-iam-transport-secret";
                settings["Services:Auth"] = "https://controlled-exchange.invalid";
                settings["Features:ResourceScopedAuthEnabled"] = "true";
                settings["IAM:LivePermissionChecks:Credential"] = "disposable-live-key";
                foreach (var item in settings) builder.UseSetting(item.Key, item.Value);
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
                builder.ConfigureServices(services =>
                {
                    services.AddHttpClient("IAMService").ConfigurePrimaryHttpMessageHandler(() => new ReferenceTransport(fixture));
                    services.AddHttpClient("LegacyAuthServiceTokenExchange").ConfigurePrimaryHttpMessageHandler(() => new ReferenceTransport(fixture));
                });
            }
        }

        private sealed class EmployeeFactory(JoinedFixture fixture) : WebApplicationFactory<EmployeeProgram>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Production");
                var settings = fixture.JwtSettings();
                settings["ConnectionStrings:EmployeeDbContext"] = fixture.employeeConnection;
                settings["ConnectionStrings:redis"] = fixture.RedisConnection;
                settings["Cache:RedisEnabled"] = "true";
                settings["Features:ResourceScopedAuthEnabled"] = "true";
                settings["IAM:LivePermissionChecks:Credential"] = "disposable-live-key";
                foreach (var item in settings) builder.UseSetting(item.Key, item.Value);
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
                builder.ConfigureServices(services =>
                {
                    // Same explicit remote-client composition as Employee48's normal Program HTTP fixture.
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
                var request = new CertificateRequest("CN=disposable-employee-join", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
                settings["DataProtection:CertificatePfxBase64"] = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, "disposable-pfx"));
                settings["DataProtection:CertificatePassword"] = "disposable-pfx";
                settings["Services:Auth"] = "https://disposable-auth.invalid";
                settings["Services:Employee"] = "https://disposable-employee.invalid";
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
                    { InnerHandler = fixture.Employee.Server.CreateHandler() });
                    services.AddHttpClient<CountryClient>().ConfigurePrimaryHttpMessageHandler(() => new ReferenceTransport(fixture));
                    services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(handler =>
                    {
                        if (!new[] { nameof(Legacy.Maliev.Intranet.Auth.ILegacyAuthClient), typeof(IdentityClient).Name, typeof(ProfileClient).Name, typeof(CountryClient).Name }.Contains(handler.Name))
                            handler.PrimaryHandler = new BlockOutbound();
                    }));
                });
            }
        }

        private sealed class OrderedTransport(JoinedFixture fixture, bool identity) : DelegatingHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var isAddress = request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath == "/employees/1/home-address/versioned";
                var createAddress = request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/employees/addresses";
                if (request.Method == HttpMethod.Put || createAddress)
                {
                    Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                    Assert.NotNull(request.Headers.Authorization?.Parameter);
                    var actor = new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter!);
                    Assert.Equal("employee", actor.Claims.Single(claim => claim.Type == "identity_kind").Value);
                    Assert.Equal("disposable-101", actor.Subject);
                    var edit = fixture.ExpectedEdit!;
                    if (createAddress) Assert.Empty(request.Headers.IfMatch);
                    else Assert.Equal(identity ? edit.IdentityVersion : isAddress ? edit.AddressVersion : edit.ProfileVersion,
                        request.Headers.IfMatch.Single().ToString());
                    if (isAddress)
                    {
                        Assert.Equal(edit.ProfileVersion, request.Headers.GetValues("X-Employee-If-Match").Single());
                        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                        Assert.Equal(edit.Profile.HomeAddressId, body.RootElement.GetProperty("AddressId").GetInt32());
                    }
                    fixture.Writes.Enqueue(identity ? "identity" : isAddress || createAddress ? "address" : "profile");
                }
                if (isAddress && fixture.HoldAddress)
                {
                    fixture.AddressEntered.TrySetResult();
                    await fixture.AddressRelease.Task.WaitAsync(token);
                }
                var response = await base.SendAsync(request, token);
                if (createAddress)
                {
                    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                    var bytes = await response.Content.ReadAsByteArrayAsync(token);
                    var created = JsonSerializer.Deserialize<EmployeeAddressDetail>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    Assert.NotNull(created);
                    Assert.True(created.Id > 0);
                    fixture.CreatedAddressId = created.Id;
                }
                if (isAddress && fixture.LoseAddressAcknowledgement)
                {
                    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                    Interlocked.Increment(ref fixture.AddressCommitsAcknowledged);
                    response.Dispose();
                    Assert.False(token.IsCancellationRequested);
                    Exception failure = fixture.AcknowledgementFailure switch
                    {
                        "io" => new IOException("Controlled lost acknowledgement after real producer commit."),
                        "cancellation" => new OperationCanceledException("Controlled noncaller loss after real producer commit."),
                        _ => new HttpRequestException("Controlled lost acknowledgement after real producer commit."),
                    };
                    throw failure;
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
                if (path == "/Countries" && request.Method == HttpMethod.Get)
                {
                    Assert.Null(request.Headers.Authorization);
                    return Json(new[] { new { Id = 764, Name = "Thailand" } });
                }
                if (path == "/auth/v1/service/login" && request.Method == HttpMethod.Post)
                {
                    using var exchange = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                    Assert.Equal("legacy-auth", exchange.RootElement.GetProperty("clientId").GetString());
                    Assert.Equal("disposable-iam-transport-secret", exchange.RootElement.GetProperty("clientSecret").GetString());
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
                    "legacy-auth.employee-identities.read" or "legacy-auth.employee-identities.update" or "legacy-employee.roles.read" => resource == "global",
                    "legacy-employee.employees.read" or "legacy-employee.employees.update" => resource == "/employees/1",
                    "legacy-employee.addresses.read" or "legacy-employee.addresses.update" => resource == "/employees/addresses/1"
                        || (fixture.NoInitialAddress && fixture.CreatedAddressId > 0 && resource == $"/employees/addresses/{fixture.CreatedAddressId}"),
                    "legacy-employee.addresses.create" => resource == "global",
                    _ => false,
                };
                Assert.True(allowed, "Unexpected permission/resource tuple in controlled IAM transport.");
                if (body.RootElement.GetProperty("bypassCache").GetBoolean())
                    Assert.Equal("disposable-live-key", request.Headers.GetValues("X-Maliev-IAM-Live-Check-Key").Single());
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
