extern alias Bff;
extern alias AuthApi;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AuthProgram = AuthApi::Program;
using BffProgram = Bff::Program;
using CustomersProxy = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomersProxy;
using CustomerUpdateProxy = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomerUpdateProxy;
using Legacy.Maliev.AuthService.Infrastructure;
using Legacy.Maliev.CustomerService.Application.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace CustomerRevision.Acceptance;

public sealed partial class CustomerRevisionAcceptanceTests
{
    [Fact]
    public async Task AuthIssuedEmployeeSessions_WithRedisTickets_RejectStaleCustomerEdit()
    {
        await using var redis = new RedisBuilder("redis:7-alpine").Build();
        await redis.StartAsync();
        using var rsa = RSA.Create(2048);
        var privateKey = rsa.ExportPkcs8PrivateKeyPem();
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var employeeConnection = await CreateAuthDatabaseAsync();
        var customerConnection = await CreateAuthDatabaseAsync();
        var refreshConnection = await CreateAuthDatabaseAsync();
        await SeedAuthAsync(employeeConnection, customerConnection, refreshConnection);

        await using var auth = new AuthFactory(privateKey, publicKey,
            employeeConnection, customerConnection, refreshConnection);
        using var authClient = auth.CreateClient();

        await using var customer = await StartCustomerAsync(publicKey);
        using var customerClient = customer.GetTestClient();
        customerClient.DefaultRequestHeaders.Add("Test-Identity", "allowed");
        using var created = await customerClient.PostAsJsonAsync("/customers", InitialProfile());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<CustomerResponse>())!.Id;

        using var certificateKey = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=disposable-intranet-acceptance",
            certificateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        const string certificatePassword = "disposable-acceptance-password";
        var pfx = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, certificatePassword));
        await using var bff = new RealAuthBffFactory(auth, customer, publicKey,
            redis.GetConnectionString(), pfx, certificatePassword);
        using var first = CreateEmployeeClient(bff);
        using var second = CreateEmployeeClient(bff);
        await SignInAsync(first, "revision-first@maliev.test");
        await SignInAsync(second, "revision-second@maliev.test");
        await using (var sessions = new RefreshSessionDbContext(
            new DbContextOptionsBuilder<RefreshSessionDbContext>().UseNpgsql(refreshConnection).Options))
        {
            Assert.Equal(2, await sessions.RefreshSessions.CountAsync());
        }
        using (var redisConnection = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString()))
        {
            var redisServer = redisConnection.GetServer(redisConnection.GetEndPoints()[0]);
            Assert.Equal(2, redisServer.Keys(pattern: "*legacy-intranet:session:*").Count());
            Assert.NotEmpty(redisServer.Keys(pattern: "legacy:intranet:data-protection-keys"));
        }

        using var firstRead = await first.GetAsync($"/bff/customers/{id}/versioned");
        using var secondRead = await second.GetAsync($"/bff/customers/{id}/versioned");
        Assert.Equal(HttpStatusCode.OK, firstRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondRead.StatusCode);
        var staleRevision = firstRead.Headers.ETag?.ToString();
        Assert.NotNull(staleRevision);
        Assert.Equal(staleRevision, secondRead.Headers.ETag?.ToString());

        using var missingCsrf = await UpdateAsync(first, id, Profile("Rejected"), staleRevision, false);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        Assert.Equal("Initial", (await ReadPersistedAsync(customerClient, id)).FirstName);
        using var winner = await UpdateAsync(first, id, Profile("Winner"), staleRevision, true);
        using var loser = await UpdateAsync(second, id, Profile("Loser"), staleRevision, true);
        Assert.Equal(HttpStatusCode.NoContent, winner.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, loser.StatusCode);
        Assert.Equal("Winner", (await ReadPersistedAsync(customerClient, id)).FirstName);

        using var reloaded = await second.GetAsync($"/bff/customers/{id}/versioned");
        Assert.Equal(HttpStatusCode.OK, reloaded.StatusCode);
        var freshRevision = reloaded.Headers.ETag?.ToString();
        Assert.NotNull(freshRevision);
        Assert.NotEqual(staleRevision, freshRevision);
        using var fresh = await UpdateAsync(second, id, Profile("Fresh"), freshRevision, true);
        Assert.Equal(HttpStatusCode.NoContent, fresh.StatusCode);
        Assert.Equal("Fresh", (await ReadPersistedAsync(customerClient, id)).FirstName);
    }

    private async Task<string> CreateAuthDatabaseAsync()
    {
        var database = $"auth_revision_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{database}\"";
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = database }.ConnectionString;
    }

    private static async Task SeedAuthAsync(string employeeConnection, string customerConnection, string refreshConnection)
    {
        await using var employees = new EmployeeIdentityDbContext(
            new DbContextOptionsBuilder<EmployeeIdentityDbContext>().UseNpgsql(employeeConnection).Options);
        await employees.Database.EnsureCreatedAsync();
        var hasher = new PasswordHasher<LegacyIdentityRow>();
        foreach (var name in new[] { "first", "second" })
        {
            var email = $"revision-{name}@maliev.test";
            var row = new LegacyIdentityRow
            {
                Id = $"disposable-{name}",
                DatabaseID = name == "first" ? 101 : 102,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString(),
                LockoutEnabled = true,
            };
            row.PasswordHash = hasher.HashPassword(row, "disposable");
            employees.Users.Add(row);
        }
        await employees.SaveChangesAsync();
        await using var customers = new CustomerIdentityDbContext(
            new DbContextOptionsBuilder<CustomerIdentityDbContext>().UseNpgsql(customerConnection).Options);
        await customers.Database.EnsureCreatedAsync();
        await using var refresh = new RefreshSessionDbContext(
            new DbContextOptionsBuilder<RefreshSessionDbContext>().UseNpgsql(refreshConnection).Options);
        await refresh.Database.MigrateAsync();
    }

    private sealed class AuthFactory(string privateKey, string publicKey, string employeeConnection,
        string customerConnection, string refreshConnection) : WebApplicationFactory<AuthProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("CORS:AllowedOrigins", "https://localhost");
            builder.UseSetting("Jwt:Issuer", "https://disposable-auth.test");
            builder.UseSetting("Jwt:Audience", "disposable-intranet");
            builder.UseSetting("Jwt:PrivateKeyPem", privateKey);
            builder.UseSetting("Jwt:PublicKeyPem", publicKey);
            builder.UseSetting("Jwt:KeyId", "disposable-revision");
            builder.UseSetting("ConnectionStrings:EmployeeIdentity", employeeConnection);
            builder.UseSetting("ConnectionStrings:CustomerIdentity", customerConnection);
            builder.UseSetting("ConnectionStrings:RefreshSessions", refreshConnection);
            builder.UseSetting("ServiceClients:Clients:legacy-intranet:SecretSha256",
                ServiceClientCredential.HashSecret("disposable-service-secret"));
            builder.UseSetting("ServiceClients:Clients:legacy-intranet:Permissions:0", "legacy-customer.customers.read");
            builder.UseSetting("ServiceClients:Clients:legacy-intranet:Permissions:1", "legacy-customer.customers.update");
        }
    }

    private sealed class RealAuthBffFactory(WebApplicationFactory<AuthProgram> auth,
        Microsoft.AspNetCore.Builder.WebApplication customer, string publicKey,
        string redisConnection, string pfx, string certificatePassword) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Workspace:AllowLocalTestDomain", "true");
            builder.UseSetting("Jwt:Issuer", "https://disposable-auth.test");
            builder.UseSetting("Jwt:Audience", "disposable-intranet");
            builder.UseSetting("Jwt:PublicKeyPem", publicKey);
            builder.UseSetting("Jwt:KeyId", "disposable-revision");
            builder.UseSetting("ConnectionStrings:redis", redisConnection);
            builder.UseSetting("DataProtection:CertificatePfxBase64", pfx);
            builder.UseSetting("DataProtection:CertificatePassword", certificatePassword);
            builder.UseSetting("ServiceAuthentication:ClientId", "legacy-intranet");
            builder.UseSetting("ServiceAuthentication:ClientSecret", "disposable-service-secret");
            builder.UseSetting("Services:Auth", "http://disposable-auth/");
            builder.UseSetting("Services:Customer", "http://disposable-customer/");
            builder.UseSetting("Services:Catalog", "http://disposable-catalog/");
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient("service-auth")
                    .ConfigurePrimaryHttpMessageHandler(() => auth.Server.CreateHandler());
                services.AddHttpClient<Legacy.Maliev.Intranet.Auth.ILegacyAuthClient,
                        Legacy.Maliev.Intranet.Auth.LegacyAuthClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => auth.Server.CreateHandler());
                services.AddHttpClient<CustomersProxy>()
                    .ConfigurePrimaryHttpMessageHandler(() => customer.GetTestServer().CreateHandler());
                services.AddHttpClient<CustomerUpdateProxy>()
                    .ConfigurePrimaryHttpMessageHandler(() => customer.GetTestServer().CreateHandler());
            });
        }
    }
}
