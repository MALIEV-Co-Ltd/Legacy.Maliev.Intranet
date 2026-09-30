extern alias Bff;
extern alias AuthApi;
extern alias LegacyHost;

using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.AuthService.Domain;
using Legacy.Maliev.AuthService.Infrastructure;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using BffProgram = Bff::Program;
using AuthProgram = AuthApi::Program;
using LegacyProgram = LegacyHost::Program;
using LegacyConfirmationDelivery = LegacyHost::Legacy.Maliev.Intranet.Employees.LegacyEmployeeConfirmationDelivery;
using RecoveryAuthProxy = Bff::Legacy.Maliev.Intranet.Bff.Employees.EmployeeRecoveryAuthProxy;
using RecoveryNotificationProxy = Bff::Legacy.Maliev.Intranet.Bff.Employees.EmployeeRecoveryNotificationProxy;

namespace Legacy.Maliev.Intranet.EmployeeRecovery.Acceptance;

public sealed class RecoveryPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    public Task InitializeAsync() => postgres.StartAsync();
    public async Task DisposeAsync() => await postgres.DisposeAsync();
    public async Task<string> DatabaseAsync()
    {
        var name = $"recovery_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{name}\"";
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = name }.ConnectionString;
    }
}

public sealed class EmployeeRecoveryChainTests(RecoveryPostgresFixture postgres) : IClassFixture<RecoveryPostgresFixture>
{
    private const string Email = "recovery-employee@maliev.test";
    private const string Root = "/bff/employee-recovery/";

    [Fact]
    public async Task ServiceLogin_ActualHostIssuesTheConfiguredSubjectAndRecoveryPermission()
    {
        await using var scenario = await Scenario.CreateAsync(postgres, true);
        using var auth = scenario.Auth.CreateClient();
        using var response = await auth.PostAsJsonAsync("/auth/v1/service/login",
            new { clientId = "legacy-intranet", clientSecret = "disposable-service-secret" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = new JwtSecurityTokenHandler().ReadJwtToken(payload.RootElement.GetProperty("accessToken").GetString());
        Assert.Equal("service:legacy-intranet", token.Subject);
        Assert.Contains(token.Claims, claim => claim.Type == "permissions" && claim.Value == "legacy-auth.employee-self-service");
    }

    [Fact]
    public async Task RetainedHostDeliveryAndBffCompletionShareTheSameStableServiceOwner()
    {
        await using var scenario = await Scenario.CreateAsync(postgres, false);
        using var legacy = new LegacyFactory(scenario.Auth, scenario.Notifications);
        Assert.True(await legacy.Services.GetRequiredService<LegacyConfirmationDelivery>().SendAsync(Email, CancellationToken.None));
        var action = await scenario.State.IdentityActionTokens.AsNoTracking().SingleAsync();
        Assert.Equal("service:legacy-intranet", action.OwnerSubject);
        var token = QueryHelpers.ParseQuery(new Uri(scenario.Notifications.Callback()).Query)["token"].ToString();
        using var bff = scenario.Bff();
        using var browser = Browser(bff);
        using var response = await PostAsync(browser, "email-confirmation/complete", Payload("email-confirmation", token), await CsrfAsync(browser));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(2, scenario.Auth.ServiceLogins);
        Assert.True((await scenario.Employees.Users.AsNoTracking().SingleAsync()).EmailConfirmed);
    }

    [Fact]
    public async Task AuthenticatedEmployeeCookieDoesNotBecomeRecoveryOwner()
    {
        await using var scenario = await Scenario.CreateAsync(postgres, true);
        // A synthetic workspace-domain identity passes the unchanged domain policy;
        // recovery still targets the separate reserved-domain fixture identity.
        var employee = new LegacyIdentityRow
        {
            Id = "disposable-cookie-employee",
            DatabaseID = 2,
            UserName = "disposable-only-recovery@maliev.com",
            NormalizedUserName = "DISPOSABLE-ONLY-RECOVERY@MALIEV.COM",
            Email = "disposable-only-recovery@maliev.com",
            NormalizedEmail = "DISPOSABLE-ONLY-RECOVERY@MALIEV.COM",
            EmailConfirmed = true,
            SecurityStamp = "cookie-employee-epoch",
            ConcurrencyStamp = "cookie-employee-concurrency",
        };
        employee.PasswordHash = new PasswordHasher<LegacyIdentityRow>().HashPassword(employee, "original-password");
        scenario.Employees.Users.Add(employee);
        await scenario.Employees.SaveChangesAsync();
        using var bff = scenario.Bff();
        using var browser = Browser(bff);
        using var login = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email = employee.Email, password = "original-password", returnUrl = "/Dashboard" }),
        };
        login.Headers.Add("X-CSRF-TOKEN", await CsrfAsync(browser));
        using var signedIn = await browser.SendAsync(login);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        using var session = await browser.GetAsync("/bff/session");
        using var summary = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        Assert.True(summary.RootElement.GetProperty("isAuthenticated").GetBoolean());
        var csrf = await CsrfAsync(browser);
        using var request = await PostAsync(browser, "password-reset/request", new { email = Email }, csrf);
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        Assert.Equal("service:legacy-intranet", (await scenario.State.IdentityActionTokens.AsNoTracking().SingleAsync()).OwnerSubject);
        Assert.Equal("disposable-employee", (await scenario.State.IdentityActionTokens.AsNoTracking().SingleAsync()).IdentityId);
    }

    [Fact]
    public async Task UnappliedIdentitySaveFailurePreservesChallengeForMatchingRetry()
    {
        await using var scenario = await Scenario.CreateAsync(postgres, true);
        using var bff = scenario.Bff();
        using var browser = Browser(bff);
        var csrf = await CsrfAsync(browser);
        using var request = await PostAsync(browser, "password-reset/request", new { email = Email }, csrf);
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var token = QueryHelpers.ParseQuery(new Uri(scenario.Notifications.Callback()).Query)["token"].ToString();
        var original = await scenario.Employees.Users.AsNoTracking().SingleAsync();
        scenario.Fault.Save.Fail = true;
        using var failed = await PostAsync(browser, "password-reset/complete", Payload("password-reset", token), csrf);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Empty(await scenario.Employees.RecoveryEffects.ToListAsync());
        Assert.Equal(original.PasswordHash, (await scenario.Employees.Users.AsNoTracking().SingleAsync()).PasswordHash);
        Assert.Null((await scenario.State.IdentityActionTokens.AsNoTracking().SingleAsync()).ConsumedAt);
        Assert.Null((await scenario.State.RefreshSessions.AsNoTracking().SingleAsync()).RevokedAt);
        using var retry = await PostAsync(browser, "password-reset/complete", Payload("password-reset", token), csrf);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.Single(await scenario.Employees.RecoveryEffects.ToListAsync());
    }

    [Fact]
    public async Task PendingResetWrongEmailOrPurposeCannotFinalizeReceipt()
    {
        await using var scenario = await Scenario.CreateAsync(postgres, true);
        using var bff = scenario.Bff();
        using var browser = Browser(bff);
        var csrf = await CsrfAsync(browser);
        using var request = await PostAsync(browser, "password-reset/request", new { email = Email }, csrf);
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var token = QueryHelpers.ParseQuery(new Uri(scenario.Notifications.Callback()).Query)["token"].ToString();
        scenario.Fault.FailFinalization = true;
        using var pending = await PostAsync(browser, "password-reset/complete", Payload("password-reset", token), csrf);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, pending.StatusCode);
        using var wrongEmail = await PostAsync(browser, "password-reset/complete",
            new { email = "other@maliev.test", token, password = "replacement-password", confirmPassword = "replacement-password" }, csrf);
        using var wrongPurpose = await PostAsync(browser, "email-confirmation/complete", Payload("email-confirmation", token), csrf);
        Assert.Equal(HttpStatusCode.BadRequest, wrongEmail.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongPurpose.StatusCode);
        using var otherBff = scenario.Bff("other-service");
        using var other = Browser(otherBff);
        using var wrongOwner = await PostAsync(other, "password-reset/complete", Payload("password-reset", token), await CsrfAsync(other));
        Assert.Equal(HttpStatusCode.BadRequest, wrongOwner.StatusCode);
        Assert.DoesNotContain(token, await wrongOwner.Content.ReadAsStringAsync());
        Assert.Null((await scenario.Employees.RecoveryEffects.AsNoTracking().SingleAsync()).FinalizedAcknowledgedAt);
        using var retry = await PostAsync(browser, "password-reset/complete", Payload("password-reset", token), csrf);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
    }

    [Fact]
    public async Task ProductionReconcilerCannotAuthorizeMismatchedImmutableActionOwner()
    {
        await using var scenario = await Scenario.CreateAsync(postgres, true);
        using var bff = scenario.Bff();
        using var browser = Browser(bff);
        var csrf = await CsrfAsync(browser);
        using var request = await PostAsync(browser, "password-reset/request", new { email = Email }, csrf);
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var token = QueryHelpers.ParseQuery(new Uri(scenario.Notifications.Callback()).Query)["token"].ToString();
        scenario.Fault.FailFinalization = true;
        using var pending = await PostAsync(browser, "password-reset/complete", Payload("password-reset", token), csrf);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, pending.StatusCode);
        var applied = await scenario.Employees.Users.AsNoTracking().SingleAsync();
        await scenario.State.IdentityActionTokens.ExecuteUpdateAsync(s => s.SetProperty(x => x.OwnerSubject, "service:other-service"));
        await using var scope = scenario.Auth.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<EmployeeRecoveryUnavailableException>(() =>
            scope.ServiceProvider.GetRequiredService<EmployeeSelfService>().ReconcileOutstandingAsync(CancellationToken.None));
        Assert.Null((await scenario.Employees.RecoveryEffects.AsNoTracking().SingleAsync()).FinalizedAcknowledgedAt);
        Assert.Null((await scenario.State.IdentityActionTokens.AsNoTracking().SingleAsync()).ConsumedAt);
        Assert.Null((await scenario.State.RefreshSessions.AsNoTracking().SingleAsync()).RevokedAt);
        Assert.Equal(applied.SecurityStamp, (await scenario.Employees.Users.AsNoTracking().SingleAsync()).SecurityStamp);
    }

    [Theory]
    [InlineData("password-reset")]
    [InlineData("email-confirmation")]
    public async Task AnonymousRequest_DeliveredCallbackCompletesOnceWithRealServiceOwner(string purpose)
    {
        await using var scenario = await Scenario.CreateAsync(postgres, confirmed: purpose == "password-reset");
        using var bff = scenario.Bff();
        using var browser = Browser(bff);
        var csrf = await CsrfAsync(browser);
        // Spoofed browser bearer is overwritten by the actual outbound service handler.
        browser.DefaultRequestHeaders.Authorization = new("Bearer", "untrusted-browser-credential");
        using var request = await PostAsync(browser, purpose + "/request", new { email = Email }, csrf);
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        Assert.DoesNotContain(Email, await request.Content.ReadAsStringAsync());
        var callback = scenario.Notifications.Callback();
        var query = QueryHelpers.ParseQuery(new Uri(callback).Query);
        Assert.Equal("https://intranet.test", new Uri(callback).GetLeftPart(UriPartial.Authority));
        Assert.Equal(Email, query["email"].ToString());
        var token = query["token"].ToString();
        Assert.DoesNotContain(token, await request.Content.ReadAsStringAsync());
        var action = await scenario.State.IdentityActionTokens.AsNoTracking().SingleAsync();
        Assert.Equal("service:legacy-intranet", action.OwnerSubject);
        using var complete = await PostAsync(browser, purpose + "/complete", Payload(purpose, token), csrf);
        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        using var replay = await PostAsync(browser, purpose + "/complete", Payload(purpose, token), csrf);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        var receipt = await scenario.Employees.RecoveryEffects.AsNoTracking().SingleAsync();
        Assert.Equal("service:legacy-intranet", receipt.OwnerSubject);
        Assert.NotNull(receipt.FinalizedAcknowledgedAt);
        Assert.All(scenario.Auth.ServiceSubjects, subject => Assert.Equal("service:legacy-intranet", subject));
        Assert.Equal(1, scenario.Auth.ServiceLogins);
        var row = await scenario.Employees.Users.AsNoTracking().SingleAsync();
        if (purpose == "email-confirmation") Assert.True(row.EmailConfirmed);
        else Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<LegacyIdentityRow>().VerifyHashedPassword(row, row.PasswordHash!, "replacement-password"));
    }

    [Theory]
    [InlineData("password-reset")]
    [InlineData("email-confirmation")]
    public async Task KnownUnknownAndNotificationFailuresKeepSameAcceptedBody(string purpose)
    {
        await using var scenario = await Scenario.CreateAsync(postgres, purpose == "password-reset");
        using var bff = scenario.Bff();
        using var browser = Browser(bff);
        var csrf = await CsrfAsync(browser);
        using var unknown = await PostAsync(browser, purpose + "/request", new { email = "missing@maliev.test" }, csrf);
        scenario.Notifications.Failure = true;
        using var known = await PostAsync(browser, purpose + "/request", new { email = Email }, csrf);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await known.Content.ReadAsStringAsync());
        Assert.Single(scenario.Notifications.Bodies);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledOrPhysicalSchemaDriftIsGeneric503WithoutChallengeOrDelivery(bool physicalDrift)
    {
        await using var scenario = await Scenario.CreateAsync(postgres, false, enabled: physicalDrift);
        if (physicalDrift) await scenario.Employees.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_EmployeeRecoveryEffects_TokenSha256_Purpose\"");
        using var bff = scenario.Bff();
        using var browser = Browser(bff);
        var csrf = await CsrfAsync(browser);
        using var response = await PostAsync(browser, "email-confirmation/request", new { email = Email }, csrf);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Email, body);
        Assert.DoesNotContain("EmployeeRecoveryEffects", body);
        Assert.DoesNotContain("Npgsql", body);
        Assert.Empty(await scenario.State.IdentityActionTokens.ToListAsync());
        Assert.Empty(scenario.Notifications.Bodies);
        using var auth = scenario.Auth.CreateClient();
        using var readiness = await auth.GetAsync("/auth/readiness");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
    }

    [Fact]
    public async Task RealPermissionAndOwnerBindingRejectOtherServiceWithoutMutation()
    {
        await using var scenario = await Scenario.CreateAsync(postgres, false);
        using var ownerBff = scenario.Bff();
        using var owner = Browser(ownerBff);
        var csrf = await CsrfAsync(owner);
        using var request = await PostAsync(owner, "email-confirmation/request", new { email = Email }, csrf);
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var token = QueryHelpers.ParseQuery(new Uri(scenario.Notifications.Callback()).Query)["token"].ToString();
        using var otherBff = scenario.Bff("other-service");
        using var other = Browser(otherBff);
        using var wrongOwner = await PostAsync(other, "email-confirmation/complete", Payload("email-confirmation", token), await CsrfAsync(other));
        Assert.Equal(HttpStatusCode.BadRequest, wrongOwner.StatusCode);
        using var deniedBff = scenario.Bff("no-recovery-permission");
        using var denied = Browser(deniedBff);
        using var deniedResponse = await PostAsync(denied, "email-confirmation/request", new { email = Email }, await CsrfAsync(denied));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, deniedResponse.StatusCode);
        Assert.False((await scenario.Employees.Users.AsNoTracking().SingleAsync()).EmailConfirmed);
        Assert.Empty(await scenario.Employees.RecoveryEffects.ToListAsync());
        Assert.Null((await scenario.State.IdentityActionTokens.AsNoTracking().SingleAsync()).ConsumedAt);
    }

    [Fact]
    public async Task CommittedResetPendingRetryRequiresSamePasswordAndNeverAppliesSecondEffect()
    {
        await using var scenario = await Scenario.CreateAsync(postgres, true);
        using var bff = scenario.Bff();
        using var browser = Browser(bff);
        var csrf = await CsrfAsync(browser);
        using var request = await PostAsync(browser, "password-reset/request", new { email = Email }, csrf);
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var token = QueryHelpers.ParseQuery(new Uri(scenario.Notifications.Callback()).Query)["token"].ToString();
        scenario.Fault.FailFinalization = true;
        using var pending = await PostAsync(browser, "password-reset/complete", Payload("password-reset", token), csrf);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, pending.StatusCode);
        Assert.DoesNotContain(token, await pending.Content.ReadAsStringAsync());
        var applied = await scenario.Employees.Users.AsNoTracking().SingleAsync();
        var receipt = await scenario.Employees.RecoveryEffects.AsNoTracking().SingleAsync();
        Assert.Null(receipt.FinalizedAcknowledgedAt);
        foreach (var epoch in new string?[] { null, applied.SecurityStamp, "later-generation" })
            scenario.State.RefreshSessions.Add(new()
            {
                Id = Guid.NewGuid(),
                FamilyId = Guid.NewGuid(),
                IdentityId = applied.Id,
                IdentityKind = IdentityKind.Employee,
                SecurityStamp = epoch,
                TokenHash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
                CreatedAt = scenario.Clock.GetUtcNow(),
                ExpiresAt = scenario.Clock.GetUtcNow().AddDays(30)
            });
        await scenario.State.SaveChangesAsync();
        using var wrong = await PostAsync(browser, "password-reset/complete", Payload("password-reset", token, "different-password"), csrf);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        // Warm the second host through real service login before advancing recovery time.
        // JWT validation uses wall time, independently of the controlled recovery clock.
        using var laterBff = scenario.Bff();
        using var later = Browser(laterBff);
        var laterCsrf = await CsrfAsync(later);
        Assert.NotNull(await laterBff.Services.GetRequiredService<IServiceAccessTokenProvider>().GetAccessTokenAsync(CancellationToken.None));
        scenario.Clock.Advance(TimeSpan.FromHours(25));
        using var retry = await PostAsync(browser, "password-reset/complete", Payload("password-reset", token), csrf);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        var after = await scenario.Employees.Users.AsNoTracking().SingleAsync();
        Assert.Equal(applied.PasswordHash, after.PasswordHash);
        Assert.Equal(applied.SecurityStamp, after.SecurityStamp);
        Assert.Equal(applied.ConcurrencyStamp, after.ConcurrencyStamp);
        Assert.Single(await scenario.Employees.RecoveryEffects.ToListAsync());
        var sessions = await scenario.State.RefreshSessions.AsNoTracking().ToListAsync();
        Assert.All(sessions.Where(x => x.SecurityStamp is null || x.SecurityStamp == "original-generation"), x => Assert.NotNull(x.RevokedAt));
        Assert.All(sessions.Where(x => x.SecurityStamp == applied.SecurityStamp || x.SecurityStamp == "later-generation"), x => Assert.Null(x.RevokedAt));
        using var replay = await PostAsync(browser, "password-reset/complete", Payload("password-reset", token), csrf);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        // A fresh BFF instance has its own unchanged limiter but the same durable owner.
        using var finalizedWrongPayload = await PostAsync(later, "password-reset/complete", Payload("password-reset", token, "different-password"), laterCsrf);
        Assert.Equal(HttpStatusCode.BadRequest, finalizedWrongPayload.StatusCode);
    }

    [Fact]
    public async Task ProductionReconcilerFinalizesCommittedReceiptWithoutSecondIdentityWrite()
    {
        await using var scenario = await Scenario.CreateAsync(postgres, true);
        using var bff = scenario.Bff();
        using var browser = Browser(bff);
        var csrf = await CsrfAsync(browser);
        using var request = await PostAsync(browser, "password-reset/request", new { email = Email }, csrf);
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var token = QueryHelpers.ParseQuery(new Uri(scenario.Notifications.Callback()).Query)["token"].ToString();
        scenario.Fault.FailFinalization = true;
        using var pending = await PostAsync(browser, "password-reset/complete", Payload("password-reset", token), csrf);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, pending.StatusCode);
        var applied = await scenario.Employees.Users.AsNoTracking().SingleAsync();
        await using var scope = scenario.Auth.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<EmployeeSelfService>().ReconcileOutstandingAsync(CancellationToken.None));
        var after = await scenario.Employees.Users.AsNoTracking().SingleAsync();
        Assert.Equal(applied.PasswordHash, after.PasswordHash);
        Assert.Equal(applied.SecurityStamp, after.SecurityStamp);
        Assert.NotNull((await scenario.Employees.RecoveryEffects.AsNoTracking().SingleAsync()).FinalizedAcknowledgedAt);
        using var replay = await PostAsync(browser, "password-reset/complete", Payload("password-reset", token), csrf);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task HeldIdentityLockExpiryRejectsUnappliedChallengeAcrossActualBffChain()
    {
        await using var scenario = await Scenario.CreateAsync(postgres, true);
        using var bff = scenario.Bff();
        using var browser = Browser(bff);
        var csrf = await CsrfAsync(browser);
        using var request = await PostAsync(browser, "password-reset/request", new { email = Email }, csrf);
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var token = QueryHelpers.ParseQuery(new Uri(scenario.Notifications.Callback()).Query)["token"].ToString();
        var original = await scenario.Employees.Users.AsNoTracking().SingleAsync();
        await using var held = await scenario.Employees.Database.BeginTransactionAsync();
        await scenario.Employees.Users.FromSqlRaw("SELECT * FROM \"AspNetUsers\" FOR UPDATE").SingleAsync();
        scenario.Fault.ObserveIdentityLock = true;
        var operation = PostAsync(browser, "password-reset/complete", Payload("password-reset", token), csrf);
        await scenario.Fault.LockAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(operation.IsCompleted);
        scenario.Clock.Advance(TimeSpan.FromHours(25));
        await held.CommitAsync();
        using var response = await operation;
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await scenario.Employees.Users.AsNoTracking().SingleAsync();
        Assert.Equal(original.PasswordHash, after.PasswordHash);
        Assert.Equal(original.SecurityStamp, after.SecurityStamp);
        Assert.Empty(await scenario.Employees.RecoveryEffects.ToListAsync());
        Assert.Null((await scenario.State.IdentityActionTokens.AsNoTracking().SingleAsync()).ConsumedAt);
        Assert.Null((await scenario.State.RefreshSessions.AsNoTracking().SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task AnonymousCsrfAndActualRateLimitPreventExcessDownstreamCalls()
    {
        await using var scenario = await Scenario.CreateAsync(postgres, true);
        using var bff = scenario.Bff();
        using var browser = Browser(bff);
        using var noCsrf = await browser.PostAsJsonAsync(Root + "password-reset/request", new { email = Email });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Equal(0, scenario.Auth.ServiceLogins);
        Assert.Empty(await scenario.State.IdentityActionTokens.ToListAsync());
        var csrf = await CsrfAsync(browser);
        // Missing-CSRF request also uses one real recovery limiter permit.
        for (var i = 0; i < 4; i++)
        {
            using var allowed = await PostAsync(browser, "password-reset/request", new { email = "missing@maliev.test" }, csrf);
            Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
        }
        using var limited = await PostAsync(browser, "password-reset/request", new { email = Email }, csrf);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Empty(scenario.Notifications.Bodies);
    }

    private static object Payload(string purpose, string token, string password = "replacement-password") => purpose == "password-reset"
        ? new { email = Email, token, password, confirmPassword = password }
        : new { email = Email, token };
    private static HttpClient Browser(BffFactory bff) => bff.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
    private static async Task<string> CsrfAsync(HttpClient browser)
    {
        using var response = await browser.GetAsync("/bff/session");
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("csrfToken").GetString()!;
    }
    private static async Task<HttpResponseMessage> PostAsync(HttpClient browser, string path, object payload, string csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await browser.SendAsync(request);
    }

    private sealed class Scenario(EmployeeIdentityDbContext employees, RefreshSessionDbContext state,
        CustomerIdentityDbContext customers, AuthFactory auth, FakeTimeProvider clock, Faults fault) : IAsyncDisposable
    {
        public EmployeeIdentityDbContext Employees => employees;
        public RefreshSessionDbContext State => state;
        public AuthFactory Auth => auth;
        public FakeTimeProvider Clock => clock;
        public Faults Fault => fault;
        public Notifications Notifications { get; } = new();
        public BffFactory Bff(string caller = "legacy-intranet") => new(auth, Notifications, caller);
        public static async Task<Scenario> CreateAsync(RecoveryPostgresFixture fixture, bool confirmed, bool enabled = true)
        {
            var employees = new EmployeeIdentityDbContext(new DbContextOptionsBuilder<EmployeeIdentityDbContext>().UseNpgsql(await fixture.DatabaseAsync()).Options);
            var state = new RefreshSessionDbContext(new DbContextOptionsBuilder<RefreshSessionDbContext>().UseNpgsql(await fixture.DatabaseAsync()).Options);
            var customers = new CustomerIdentityDbContext(new DbContextOptionsBuilder<CustomerIdentityDbContext>().UseNpgsql(await fixture.DatabaseAsync()).Options);
            foreach (var db in new DbContext[] { employees, state, customers }) await db.Database.MigrateAsync();
            var row = new LegacyIdentityRow
            {
                Id = "disposable-employee",
                DatabaseID = 1,
                UserName = Email,
                NormalizedUserName = Email.ToUpperInvariant(),
                Email = Email,
                NormalizedEmail = Email.ToUpperInvariant(),
                EmailConfirmed = confirmed,
                SecurityStamp = "original-generation",
                ConcurrencyStamp = "original-concurrency"
            };
            row.PasswordHash = new PasswordHasher<LegacyIdentityRow>().HashPassword(row, "original-password");
            employees.Users.Add(row);
            await employees.SaveChangesAsync();
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            state.RefreshSessions.Add(new()
            {
                Id = Guid.NewGuid(),
                FamilyId = Guid.NewGuid(),
                IdentityId = row.Id,
                IdentityKind = IdentityKind.Employee,
                SecurityStamp = row.SecurityStamp,
                TokenHash = new string('a', 64),
                CreatedAt = clock.GetUtcNow(),
                ExpiresAt = clock.GetUtcNow().AddDays(30)
            });
            await state.SaveChangesAsync();
            var fault = new Faults();
            return new(employees, state, customers, new(employees.Database.GetConnectionString()!, state.Database.GetConnectionString()!,
                customers.Database.GetConnectionString()!, clock, fault, enabled), clock, fault);
        }
        public async ValueTask DisposeAsync()
        {
            await auth.DisposeAsync();
            foreach (var db in new DbContext[] { employees, state, customers })
            {
                var connection = db.Database.GetConnectionString();
                await db.DisposeAsync();
                using var pool = new NpgsqlConnection(connection);
                NpgsqlConnection.ClearPool(pool);
            }
        }
    }

    private sealed class Faults : DbCommandInterceptor
    {
        public SaveFault Save { get; } = new();
        public bool FailFinalization { get; set; }
        public bool ObserveIdentityLock { get; set; }
        public TaskCompletionSource LockAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (FailFinalization && command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal) && command.CommandText.Contains("refresh_sessions", StringComparison.Ordinal))
            {
                FailFinalization = false;
                throw new InvalidOperationException("Controlled finalization fault");
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (ObserveIdentityLock && command.CommandText.Contains("AspNetUsers", StringComparison.Ordinal) && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) LockAttempt.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SaveFault : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Fail && eventData.Context!.ChangeTracker.Entries<EmployeeRecoveryEffect>().Any(x => x.State == EntityState.Added))
            {
                Fail = false;
                throw new InvalidOperationException("Controlled unapplied save failure");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class AuthFactory(string employees, string state, string customers, FakeTimeProvider clock, Faults faults, bool enabled) : WebApplicationFactory<AuthProgram>
    {
        private readonly RSA signing = RSA.Create(2048);
        public string PublicKey => signing.ExportSubjectPublicKeyInfoPem();
        public int ServiceLogins { get; set; }
        public List<string> ServiceSubjects { get; } = [];
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            // CORS is read during bootstrap before ConfigureAppConfiguration.
            builder.UseSetting("CORS:AllowedOrigins", "https://localhost");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CORS:AllowedOrigins"] = "https://localhost",
                ["Jwt:Issuer"] = "https://recovery-chain.test",
                ["Jwt:Audience"] = "recovery-chain",
                ["Jwt:PrivateKeyPem"] = signing.ExportPkcs8PrivateKeyPem(),
                ["Jwt:KeyId"] = "disposable-chain",
                ["ConnectionStrings:EmployeeIdentity"] = employees,
                ["ConnectionStrings:RefreshSessions"] = state,
                ["ConnectionStrings:CustomerIdentity"] = customers,
                ["EmployeeRecovery:Enabled"] = enabled.ToString(),
                ["ServiceClients:Clients:legacy-intranet:SecretSha256"] = ServiceClientCredential.HashSecret("disposable-service-secret"),
                ["ServiceClients:Clients:legacy-intranet:Permissions:0"] = "legacy-auth.employee-self-service",
                ["ServiceClients:Clients:other-service:SecretSha256"] = ServiceClientCredential.HashSecret("disposable-service-secret"),
                ["ServiceClients:Clients:other-service:Permissions:0"] = "legacy-auth.employee-self-service",
                ["ServiceClients:Clients:no-recovery-permission:SecretSha256"] = ServiceClientCredential.HashSecret("disposable-service-secret"),
                ["ServiceClients:Clients:no-recovery-permission:Permissions:0"] = "legacy-customer.customers.read",
            }));
            Legacy.Maliev.Intranet.Tests.TestHostLifecycle.Configure(builder);
            builder.ConfigureTestServices(services =>
            {
                // Only remove the periodic worker; invoke its production reconciler explicitly.
                foreach (var item in services.Where(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(EmployeeRecoveryWorker)).ToArray()) services.Remove(item);
                services.AddSingleton<TimeProvider>(clock);
                services.AddDbContext<RefreshSessionDbContext>(options => options.AddInterceptors(faults));
                services.AddDbContext<EmployeeIdentityDbContext>(options => options.AddInterceptors(faults, faults.Save));
            });
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) signing.Dispose(); }
    }

    private sealed class BffFactory(AuthFactory auth, Notifications notifications, string caller) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Jwt:Issuer", "https://recovery-chain.test");
            builder.UseSetting("Jwt:Audience", "recovery-chain");
            builder.UseSetting("Jwt:PublicKeyPem", auth.PublicKey);
            builder.UseSetting("Jwt:KeyId", "disposable-chain");
            builder.UseSetting("EmployeeConfirmation:PublicOrigin", "https://intranet.test/");
            builder.UseSetting("ServiceAuthentication:ClientId", caller);
            builder.UseSetting("ServiceAuthentication:ClientSecret", "disposable-service-secret");
            builder.UseSetting("Services:Auth", "http://disposable-auth/");
            builder.UseSetting("Services:Notification", "http://disposable-notification/");
            Legacy.Maliev.Intranet.Tests.TestHostLifecycle.Configure(builder);
            builder.ConfigureServices(services => services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(handler =>
            {
                // Retain every production delegating handler and typed registration;
                // replace only transport and block every unrelated integration.
                handler.PrimaryHandler = handler.Name switch
                {
                    "service-auth" => new AuthTransport(auth) { InnerHandler = auth.Server.CreateHandler() },
                    var name when name == typeof(ILegacyAuthClient).Name => new AuthTransport(auth) { InnerHandler = auth.Server.CreateHandler() },
                    var name when name == typeof(RecoveryAuthProxy).Name => new AuthTransport(auth) { InnerHandler = auth.Server.CreateHandler() },
                    var name when name == typeof(RecoveryNotificationProxy).Name => new NotificationTransport(notifications),
                    _ => new BlockOutbound(),
                };
            })));
        }
    }

    private sealed class LegacyFactory(AuthFactory auth, Notifications notifications) : WebApplicationFactory<LegacyProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Jwt:Issuer", "https://recovery-chain.test");
            builder.UseSetting("Jwt:Audience", "recovery-chain");
            builder.UseSetting("Jwt:PublicKeyPem", auth.PublicKey);
            builder.UseSetting("Jwt:KeyId", "disposable-chain");
            builder.UseSetting("EmployeeConfirmation:PublicOrigin", "https://intranet.test/");
            builder.UseSetting("ServiceAuthentication:ClientId", "legacy-intranet");
            builder.UseSetting("ServiceAuthentication:ClientSecret", "disposable-service-secret");
            builder.UseSetting("Services:Auth", "http://disposable-auth/");
            builder.UseSetting("Services:Notification", "http://disposable-notification/");
            builder.ConfigureServices(services => services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(handler =>
            {
                handler.PrimaryHandler = handler.Name switch
                {
                    "service-auth" or "employee-confirmation-auth" => new AuthTransport(auth) { InnerHandler = auth.Server.CreateHandler() },
                    "employee-confirmation-notification" => new NotificationTransport(notifications),
                    _ => new BlockOutbound(),
                };
            })));
        }
    }

    private sealed class AuthTransport(AuthFactory auth) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/auth/v1/service/login") auth.ServiceLogins++;
            else if (request.Headers.Authorization?.Parameter is { } bearer)
                auth.ServiceSubjects.Add(new JwtSecurityTokenHandler().ReadJwtToken(bearer).Subject);
            return await base.SendAsync(request, cancellationToken);
        }
    }
    private sealed class Notifications
    {
        public bool Failure { get; set; }
        public List<string> Bodies { get; } = [];
        public string Callback()
        {
            using var payload = JsonDocument.Parse(Assert.Single(Bodies));
            var html = payload.RootElement.GetProperty("body").GetString()!;
            var start = html.IndexOf("href=\"", StringComparison.Ordinal) + 6;
            Assert.True(start >= 6);
            return WebUtility.HtmlDecode(html[start..html.IndexOf('"', start)]);
        }
    }
    private sealed class NotificationTransport(Notifications notifications) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/notifications/v1/email/NoReply", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("service:legacy-intranet", new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter!).Subject);
            notifications.Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new(notifications.Failure ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        }
    }
    private sealed class BlockOutbound : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
