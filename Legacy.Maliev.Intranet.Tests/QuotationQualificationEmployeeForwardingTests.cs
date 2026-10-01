extern alias Bff;

using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

// These are BFF transport tests, not fake positive live-authority acceptance.
// Keep production login, JWT validation, cookie/ticket storage, refresh, clients and
// delegating handlers. Only external primary HTTP transport is controlled.
public sealed class QuotationQualificationEmployeeForwardingTests
{
    [Theory]
    [InlineData("receipt")]
    [InlineData("update")]
    [InlineData("linked-create")]
    public async Task ServerTicketEmployeeBearer_ReachesEveryPositiveIdQualificationCaller(string operation)
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await SignInAsync(client, scenario);
        using var request = Request(operation, csrf);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", scenario.BrowserToken);
        request.Headers.Add("X-Employee-Id", "browser-actor");
        using var response = await client.SendAsync(request);
        Assert.Equal(operation == "linked-create" ? HttpStatusCode.Created : HttpStatusCode.OK, response.StatusCode);

        var sent = Assert.Single(scenario.Calls, call => call.Path.Contains("/qualification", StringComparison.Ordinal));
        Assert.False(sent.HasBrowserActorHeader);
        var body = await response.Content.ReadAsStringAsync();
        AssertNoTokens(body, scenario);
        if (operation == "update")
        {
            using var payload = JsonDocument.Parse(sent.Body);
            Assert.Equal(7, payload.RootElement.EnumerateObject().Count());
            Assert.Equal("stable-qualification-attempt", payload.RootElement.GetProperty("idempotencyKey").GetString());
            Assert.Equal(2, payload.RootElement.GetProperty("expectedVersion").GetInt32());
        }
        if (operation == "linked-create") AssertWorkloadCreation(scenario, linked: true);
        Assert.True(sent.Authorization == "Bearer " + scenario.EmployeeToken,
            "Qualification must use the employee bearer from the encrypted server ticket, not workload/browser credentials.");
    }

    [Fact]
    public async Task UnlinkedQuotationCreation_RetainsExistingWorkloadClientAndNeverReadsQualification()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await SignInAsync(client, scenario);
        using var request = Request("unlinked-create", csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.DoesNotContain(scenario.Calls, call => call.Path.Contains("/qualification", StringComparison.Ordinal));
        AssertWorkloadCreation(scenario, linked: false);
        AssertNoTokens(await response.Content.ReadAsStringAsync(), scenario);
    }

    [Fact]
    public async Task QualificationRefresh_RotatesServerTokenWithoutReturningCredentialsToBrowser()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await SignInAsync(client, scenario);
        scenario.Clock.Advance(TimeSpan.FromMinutes(13));
        using var response = await client.GetAsync("/bff/quotation-requests/84/qualification-receipt");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, scenario.RefreshCalls);
        var sent = Assert.Single(scenario.Calls, call => call.Path.Contains("/qualification", StringComparison.Ordinal));
        Assert.True(sent.Authorization == "Bearer " + scenario.RefreshedEmployeeToken,
            "A near-expiry ticket must forward the rotated employee token.");
        AssertNoTokens(await response.Content.ReadAsStringAsync(), scenario);
        AssertNoTokens(string.Join("\n", response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : []), scenario);
    }

    [Fact]
    public async Task TransientRefreshFailure_Returns503AndPreservesOpaqueSessionForRetry()
    {
        using var scenario = new Scenario { RefreshStatus = HttpStatusCode.ServiceUnavailable };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await SignInAsync(client, scenario);
        scenario.Clock.Advance(TimeSpan.FromMinutes(13));
        using var response = await client.GetAsync("/bff/quotation-requests/84/qualification-receipt");
        Assert.True(await IsAuthenticatedAsync(client), "Transient refresh failure must preserve the server ticket.");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain(scenario.Calls, call => call.Path.Contains("/qualification", StringComparison.Ordinal));
        AssertNoTokens(await response.Content.ReadAsStringAsync(), scenario);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokedOrWrongOwnerRefresh_Returns401ClearsSessionAndDoesNotForward(bool wrongOwner)
    {
        using var scenario = new Scenario { RefreshStatus = wrongOwner ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, RefreshWrongOwner = wrongOwner };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await SignInAsync(client, scenario);
        scenario.Clock.Advance(TimeSpan.FromMinutes(13));
        using var response = await client.GetAsync("/bff/quotation-requests/84/qualification-receipt");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(await IsAuthenticatedAsync(client));
        Assert.DoesNotContain(scenario.Calls, call => call.Path.Contains("/qualification", StringComparison.Ordinal));
        AssertNoTokens(await response.Content.ReadAsStringAsync(), scenario);
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("update")]
    [InlineData("linked-create")]
    public async Task BrowserBearerWithoutOpaqueCookie_CannotAuthorizeAnyQualificationCaller(string operation)
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        using var request = Request(operation, null);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", scenario.EmployeeToken);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(scenario.Calls);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("linked-create")]
    public async Task MissingAntiforgery_StopsQualificationAndCreationBeforeDownstream(string operation)
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await SignInAsync(client, scenario);
        using var request = Request(operation, null);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(scenario.Calls, call => !call.Path.StartsWith("/auth/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("receipt", LegacyEmployeePermissions.QuotationRequestsRead)]
    [InlineData("update", LegacyEmployeePermissions.QuotationRequestsUpdate)]
    [InlineData("linked-create", LegacyEmployeePermissions.QuotationRequestsRead)]
    public async Task MissingOperationPermission_StopsBeforeQualificationTransport(string operation, string missing)
    {
        using var scenario = new Scenario { MissingPermission = missing };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await SignInAsync(client, scenario);
        using var request = Request(operation, csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(scenario.Calls, call => !call.Path.StartsWith("/auth/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("wrong-signature")]
    [InlineData("expired")]
    public async Task InvalidLoginEmployeeToken_CannotCreateQualificationSession(string invalid)
    {
        using var scenario = new Scenario { InvalidLoginToken = invalid };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await CsrfAsync(client);
        using var request = LoginRequest(csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(await IsAuthenticatedAsync(client));
        Assert.DoesNotContain(scenario.Calls, call => !call.Path.StartsWith("/auth/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("receipt", LegacyEmployeePermissions.QuotationRequestsRead)]
    [InlineData("update", LegacyEmployeePermissions.QuotationRequestsUpdate)]
    [InlineData("linked-create", LegacyEmployeePermissions.QuotationRequestsRead)]
    public async Task RenewedPermissionRemoval_DeniesBeforeQualificationTransport(string operation, string removed)
    {
        using var scenario = new Scenario { RefreshMissingPermission = removed };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await SignInAsync(client, scenario);
        scenario.Clock.Advance(TimeSpan.FromMinutes(13));
        using var request = Request(operation, csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await IsAuthenticatedAsync(client));
        Assert.DoesNotContain(scenario.Calls, call => !call.Path.StartsWith("/auth/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("receipt", HttpStatusCode.Unauthorized)]
    [InlineData("receipt", HttpStatusCode.Forbidden)]
    [InlineData("receipt", HttpStatusCode.NotFound)]
    [InlineData("update", HttpStatusCode.Conflict)]
    [InlineData("receipt", HttpStatusCode.ServiceUnavailable)]
    [InlineData("linked-create", HttpStatusCode.Unauthorized)]
    [InlineData("linked-create", HttpStatusCode.Forbidden)]
    [InlineData("linked-create", HttpStatusCode.ServiceUnavailable)]
    public async Task QualificationFailure_PreservesBoundedStatusWithoutUpstreamBody(string operation, HttpStatusCode status)
    {
        using var scenario = new Scenario { QualificationStatus = status };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await SignInAsync(client, scenario);
        using var request = Request(operation, csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sensitive-upstream-detail", body, StringComparison.Ordinal);
        AssertNoTokens(body, scenario);
        Assert.DoesNotContain(scenario.Calls, call => call.Path == "/quotations");
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("update")]
    [InlineData("linked-create")]
    public async Task QualificationThrottle_Preserves429AndBoundedRetryAfter(string operation)
    {
        using var scenario = new Scenario { QualificationStatus = HttpStatusCode.TooManyRequests };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await SignInAsync(client, scenario);
        using var request = Request(operation, csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(7), response.Headers.RetryAfter?.Delta);
        Assert.DoesNotContain(scenario.Calls, call => call.Path == "/quotations");
    }

    [Theory]
    [InlineData("receipt", "not-json")]
    [InlineData("update", "not-json")]
    [InlineData("linked-create", "not-json")]
    [InlineData("receipt", "{\"requestId\":999}")]
    [InlineData("linked-create", "{\"requestId\":999}")]
    public async Task MalformedQualificationReceipt_Returns502WithoutForwardingBody(string operation, string malformed)
    {
        using var scenario = new Scenario { QualificationBody = malformed };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await SignInAsync(client, scenario);
        using var request = Request(operation, csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain(malformed, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(scenario.Calls, call => call.Path == "/quotations");
    }

    [Fact]
    public async Task AbortDuringRefresh_PropagatesCancellationAndPreservesTicketWithoutQualificationForward()
    {
        using var scenario = new Scenario { BlockRefresh = true };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await SignInAsync(client, scenario);
        scenario.Clock.Advance(TimeSpan.FromMinutes(13));
        using var abort = new CancellationTokenSource();
        var operation = client.GetAsync("/bff/quotation-requests/84/qualification-receipt", abort.Token);
        try
        {
            var first = await Task.WhenAny(scenario.RefreshEntered.Task, operation).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(first == scenario.RefreshEntered.Task, "Qualification must acquire the server employee credential before transport.");
            abort.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            Assert.True(await IsAuthenticatedAsync(client));
            Assert.DoesNotContain(scenario.Calls, call => call.Path.Contains("/qualification", StringComparison.Ordinal));
        }
        finally
        {
            abort.Cancel();
            scenario.ReleaseRefresh.TrySetResult();
            try { using var completed = await operation; }
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task AbortDuringQualificationTransport_PropagatesInsteadOfReturningInfrastructureFailure()
    {
        using var scenario = new Scenario { BlockQualification = true };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await SignInAsync(client, scenario);
        using var abort = new CancellationTokenSource();
        var operation = client.GetAsync("/bff/quotation-requests/84/qualification-receipt", abort.Token);
        try
        {
            await scenario.QualificationEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            abort.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            Assert.True(await IsAuthenticatedAsync(client));
        }
        finally
        {
            abort.Cancel();
            scenario.ReleaseQualification.TrySetResult();
            try { using var completed = await operation; }
            catch (OperationCanceledException) { }
        }
    }

    [Theory]
    [InlineData(LegacyEmployeePermissions.QuotationsCreate)]
    [InlineData(LegacyEmployeePermissions.QuotationLinesWrite)]
    [InlineData(LegacyEmployeePermissions.QuotationOrdersWrite)]
    [InlineData(LegacyEmployeePermissions.CustomersRead)]
    [InlineData(LegacyEmployeePermissions.EmployeesRead)]
    [InlineData(LegacyEmployeePermissions.CatalogCurrenciesRead)]
    [InlineData(LegacyEmployeePermissions.OrdersRead)]
    public async Task LinkedCreate_RenewedFullPolicyRemovalPreventsGatewayWrites(string removed)
    {
        using var scenario = new Scenario { RefreshMissingPermission = removed };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await SignInAsync(client, scenario);
        scenario.Clock.Advance(TimeSpan.FromMinutes(13));
        using var request = Request("linked-create", csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await IsAuthenticatedAsync(client));
        Assert.Equal(1, scenario.RefreshCalls);
        Assert.DoesNotContain(scenario.Calls, call => call.Path == "/quotations");
    }

    [Theory]
    [InlineData("receipt", 307)]
    [InlineData("update", 308)]
    public async Task ActualHttpRedirect_DoesNotReplayEmployeeQualificationOutsideOriginalRoute(string operation, int redirect)
    {
        await using var downstream = await LoopbackQuotation.StartAsync(redirect);
        using var scenario = new Scenario { LiveQuotationBase = downstream.BaseAddress };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await SignInAsync(client, scenario);
        using var request = Request(operation, csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, downstream.OriginalCalls);
        Assert.Equal(0, downstream.RedirectedCalls);
        Assert.True(downstream.OriginalAuthorization == "Bearer " + scenario.EmployeeToken,
            "The genuine HTTP first hop must carry the server-held employee bearer.");
        AssertNoTokens(await response.Content.ReadAsStringAsync(), scenario);
    }

    [Fact]
    public async Task ActualSlowBody_RemainsInsideOriginalTenSecondDownstreamDeadline()
    {
        await using var downstream = await LoopbackQuotation.StartAsync(slowBody: true);
        using var scenario = new Scenario { LiveQuotationBase = downstream.BaseAddress };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await SignInAsync(client, scenario);
        using var abort = new CancellationTokenSource();
        var operation = client.GetAsync("/bff/quotation-requests/84/qualification-receipt", abort.Token);
        try
        {
            await downstream.BodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var response = await operation.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.True(await IsAuthenticatedAsync(client));
        }
        finally
        {
            abort.Cancel();
            downstream.ReleaseBody.TrySetResult();
            try { using var completed = await operation; }
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task ActualSlowBody_CallerAbortPropagatesAndPreservesOpaqueSession()
    {
        await using var downstream = await LoopbackQuotation.StartAsync(slowBody: true);
        using var scenario = new Scenario { LiveQuotationBase = downstream.BaseAddress };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await SignInAsync(client, scenario);
        using var abort = new CancellationTokenSource();
        var operation = client.GetAsync("/bff/quotation-requests/84/qualification-receipt", abort.Token);
        try
        {
            await downstream.BodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            abort.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            Assert.True(await IsAuthenticatedAsync(client));
        }
        finally
        {
            abort.Cancel();
            downstream.ReleaseBody.TrySetResult();
            try { using var completed = await operation; }
            catch (OperationCanceledException) { }
        }
    }

    [Theory]
    [InlineData("receipt", HttpStatusCode.OK)]
    [InlineData("update", HttpStatusCode.OK)]
    [InlineData("linked-create", HttpStatusCode.OK)]
    [InlineData("receipt", HttpStatusCode.ServiceUnavailable)]
    [InlineData("update", HttpStatusCode.ServiceUnavailable)]
    [InlineData("linked-create", HttpStatusCode.ServiceUnavailable)]
    public async Task EnabledQualificationPath_MarksSuccessAndFailureAsNoStore(string operation, HttpStatusCode status)
    {
        using var scenario = new Scenario { QualificationStatus = status };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await SignInAsync(client, scenario);
        using var request = Request(operation, csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(status == HttpStatusCode.OK && operation == "linked-create" ? HttpStatusCode.Created : status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true, "Enabled employee credential paths must not be stored by intermediaries.");
    }

    [Fact]
    public async Task CookieCacheReadOutage_CharacterizesExistingGeneric400WithoutDownstreamAuthorityOrCredentialLeak()
    {
        using var scenario = new Scenario { ProbeCache = true };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await SignInAsync(client, scenario);
        scenario.FailCacheRead = true;
        using var response = await client.GetAsync("/bff/quotation-requests/84/qualification-receipt");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(scenario.Calls, call => !call.Path.StartsWith("/auth/", StringComparison.Ordinal));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("fixture-cache-unavailable", body, StringComparison.Ordinal);
        AssertNoTokens(body, scenario);
        scenario.FailCacheRead = false;
        Assert.True(await IsAuthenticatedAsync(client));
    }

    [Fact]
    public async Task ConcurrentRefreshLoser_MustReadWinningTicketInsteadOfDeletingItsSession()
    {
        using var scenario = new Scenario { RefreshRace = true };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await SignInAsync(client, scenario);
        scenario.Clock.Advance(TimeSpan.FromMinutes(13));
        var first = client.GetAsync("/bff/quotation-requests/84/qualification-receipt");
        var second = client.GetAsync("/bff/quotation-requests/84/qualification-receipt");
        try
        {
            await scenario.BothRefreshesEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            scenario.ReleaseRaceWinner.TrySetResult();
            var winnerTask = await Task.WhenAny(first, second).WaitAsync(TimeSpan.FromSeconds(15));
            using var winner = await winnerTask;
            Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
            Assert.True(await IsAuthenticatedAsync(client));
            scenario.ReleaseRaceLoser.TrySetResult();
            using var loser = await (winnerTask == first ? second : first);
            Assert.True(await IsAuthenticatedAsync(client), "Losing refresh must not delete the successfully rotated server session.");
            Assert.Equal(HttpStatusCode.OK, loser.StatusCode);
            Assert.Equal(2, scenario.RefreshCalls);
        }
        finally
        {
            scenario.ReleaseRaceWinner.TrySetResult();
            scenario.ReleaseRaceLoser.TrySetResult();
            using var a = await first;
            using var b = await second;
        }
    }

    [Theory]
    [InlineData("removed", HttpStatusCode.Unauthorized)]
    [InlineData("corrupt", HttpStatusCode.Unauthorized)]
    [InlineData("other-owner", HttpStatusCode.Unauthorized)]
    [InlineData("expired-ticket", HttpStatusCode.Unauthorized)]
    [InlineData("expired-access", HttpStatusCode.Unauthorized)]
    [InlineData("blank-access", HttpStatusCode.Unauthorized)]
    [InlineData("removed-permission", HttpStatusCode.Forbidden)]
    [InlineData("cache-outage", HttpStatusCode.ServiceUnavailable)]
    public async Task PeerRefresh_ReconcilesFreshEncryptedTicketWithoutResurrectionOrStalePermissions(string boundary, HttpStatusCode expected)
    {
        using var scenario = new Scenario
        {
            RefreshRace = true,
            ProbeCache = true,
            RefreshMissingPermission = boundary == "removed-permission" ? LegacyEmployeePermissions.QuotationRequestsRead : null
        };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await SignInAsync(client, scenario);
        scenario.Clock.Advance(TimeSpan.FromMinutes(13));
        var first = client.GetAsync("/bff/quotation-requests/84/qualification-receipt");
        var second = client.GetAsync("/bff/quotation-requests/84/qualification-receipt");
        var cache = factory.Services.GetRequiredService<IDistributedCache>();
        var store = factory.Services.GetRequiredService<DistributedTicketStore>();
        try
        {
            await scenario.BothRefreshesEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            scenario.ReleaseRaceWinner.TrySetResult();
            var winnerTask = await Task.WhenAny(first, second).WaitAsync(TimeSpan.FromSeconds(15));
            using var winner = await winnerTask;
            Assert.Equal(boundary == "removed-permission" ? HttpStatusCode.Forbidden : HttpStatusCode.OK, winner.StatusCode);
            var key = scenario.SessionKey!;
            Assert.False(string.IsNullOrWhiteSpace(key));
            var ticket = await store.RetrieveAsync(key);
            Assert.NotNull(ticket);
            if (boundary == "removed") await store.RemoveAsync(key);
            else if (boundary == "corrupt") await cache.SetAsync(key, [1, 2, 3], new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });
            else if (boundary is "other-owner" or "expired-ticket" or "expired-access" or "blank-access")
            {
                if (boundary == "other-owner")
                {
                    var identity = (ClaimsIdentity)ticket.Principal.Identity!;
                    identity.RemoveClaim(identity.FindFirst(ClaimTypes.NameIdentifier)!);
                    identity.AddClaim(new(ClaimTypes.NameIdentifier, "unrelated-peer-owner"));
                }
                if (boundary == "expired-ticket") ticket.Properties.ExpiresUtc = scenario.Clock.GetUtcNow().AddMinutes(-1);
                if (boundary == "expired-access") ticket.Properties.UpdateTokenValue("legacy_access_expires_at", scenario.Clock.GetUtcNow().AddMinutes(1).ToString("O"));
                if (boundary == "blank-access") ticket.Properties.UpdateTokenValue("legacy_access_token", "");
                // Adversarial peer-state seeding, not an authorized renewal: production CAS
                // correctly rejects unobserved/expired state. Keep the original reconciliation guards.
                var protector = factory.Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()
                    .CreateProtector("Legacy.Maliev.Intranet.AuthenticationTicketStore.v1");
                await cache.SetAsync(key, protector.Protect(TicketSerializer.Default.Serialize(ticket)),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });
            }
            var before = await cache.GetAsync(key);
            scenario.FailCacheRead = boundary == "cache-outage";
            scenario.ReleaseRaceLoser.TrySetResult();
            using var loser = await (winnerTask == first ? second : first);
            scenario.FailCacheRead = false;
            Assert.Equal(expected, loser.StatusCode);
            AssertNoTokens(await loser.Content.ReadAsStringAsync(), scenario);
            var after = await cache.GetAsync(key);
            if (boundary == "removed") Assert.Null(after);
            else Assert.True(before!.AsSpan().SequenceEqual(after), "A stale loser must not delete, renew or replace the fresh peer ticket.");
            Assert.Equal(boundary == "removed-permission" ? 0 : 1,
                scenario.Calls.Count(call => call.Path.Contains("/qualification-receipt", StringComparison.Ordinal)));
        }
        finally
        {
            scenario.FailCacheRead = false;
            scenario.ReleaseRaceWinner.TrySetResult();
            scenario.ReleaseRaceLoser.TrySetResult();
            using var a = await first;
            using var b = await second;
        }
    }

    [Fact]
    public async Task ActualCookieAndFreshReconciliation_UseTheSameRegisteredEncryptedStore()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await SignInAsync(client, scenario);
        Assert.Same(factory.Services.GetRequiredService<DistributedTicketStore>(),
            factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme).SessionStore);
    }

    private static void AssertWorkloadCreation(Scenario scenario, bool linked)
    {
        var root = Assert.Single(scenario.Calls, call => call.Path == "/quotations" && call.Method == HttpMethod.Post);
        Assert.True(root.Authorization == "Bearer " + scenario.ServiceToken, "Quotation root creation must keep its workload bearer.");
        using var payload = JsonDocument.Parse(root.Body);
        if (linked)
        {
            Assert.Equal(84, payload.RootElement.GetProperty("SourceRequestId").GetInt32());
            Assert.Equal("11111111-2222-3333-4444-555555555555", payload.RootElement.GetProperty("SourceJourneyId").GetString());
        }
        else Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("SourceRequestId").ValueKind);
        Assert.All(scenario.Calls.Where(call => !call.Path.StartsWith("/auth/", StringComparison.Ordinal) && !call.Path.Contains("/qualification", StringComparison.Ordinal)),
            call => Assert.True(call.Authorization == "Bearer " + scenario.ServiceToken, "Nonqualification clients must retain workload credentials."));
    }

    private static HttpClient Client(Factory factory) => factory.CreateClient(new()
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false,
    });

    private static HttpRequestMessage Request(string operation, string? csrf)
    {
        var request = operation switch
        {
            "receipt" => new HttpRequestMessage(HttpMethod.Get, "/bff/quotation-requests/84/qualification-receipt"),
            "update" => new HttpRequestMessage(HttpMethod.Put, "/bff/quotation-requests/84/qualification")
            {
                Content = JsonContent.Create(new QuotationQualificationUpdate("qualified", null, "complete", 0, null, "stable-qualification-attempt", 2)),
            },
            _ => new HttpRequestMessage(HttpMethod.Post, "/bff/quotations")
            {
                Content = JsonContent.Create(new QuotationCreateRequest(3, 2, 1, 30, "Courier", "Bangkok", "Net 30", "fixture", false,
                    [new(null, "Thai qualification fixture", 2, 50m, 0m)]) with
                { SourceRequestId = operation == "linked-create" ? 84 : null }),
            },
        };
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
        if (operation.EndsWith("create", StringComparison.Ordinal)) request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return request;
    }

    private static HttpRequestMessage LoginRequest(string csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "fixture-only-password", returnUrl = "/QuotationRequests/View?id=84" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return request;
    }

    private static async Task<string> SignInAsync(HttpClient client, Scenario scenario)
    {
        using var request = LoginRequest(await CsrfAsync(client));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoTokens(await response.Content.ReadAsStringAsync(), scenario);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-Legacy.Maliev.Intranet.Bff=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        AssertNoTokens(cookie, scenario);
        Assert.True(await IsAuthenticatedAsync(client));
        return await CsrfAsync(client);
    }

    private static async Task<string> CsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/bff/session");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("csrfToken").GetString()!;
    }

    private static async Task<bool> IsAuthenticatedAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/bff/session");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("isAuthenticated").GetBoolean();
    }

    private static void AssertNoTokens(string text, Scenario scenario)
    {
        foreach (var token in new[] { scenario.EmployeeToken, scenario.RefreshedEmployeeToken, scenario.ServiceToken, scenario.BrowserToken, Scenario.RefreshToken }.Where(value => value.Length > 0))
            Assert.False(text.Contains(token, StringComparison.Ordinal), "Browser-visible output must not contain a credential.");
    }

    private sealed class Factory(Scenario scenario) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestHostLifecycle.Configure(builder);
            builder.UseSetting("Jwt:Issuer", Scenario.Issuer);
            builder.UseSetting("Jwt:Audience", Scenario.Audience);
            builder.UseSetting("Jwt:PublicKeyPem", scenario.PublicKey);
            builder.UseSetting("Jwt:KeyId", "qualification-test-key");
            builder.UseSetting("ServiceAuthentication:ClientId", "legacy-intranet");
            builder.UseSetting("ServiceAuthentication:ClientSecret", "fixture-only-service-secret");
            // Proposed option for root design review. Current runtime ignores it, so the
            // positive forwarding/refresh cases remain genuine behavior RED.
            builder.UseSetting("QuotationQualification:ForwardEmployeeToken", "true");
            foreach (var name in new[] { "Auth", "Quotation", "Catalog", "Customer", "Employee", "Order", "File", "Notification", "Document", "Procurement", "Accounting" })
                builder.UseSetting("Services:" + name, "https://" + name.ToLowerInvariant() + ".test");
            if (scenario.LiveQuotationBase is not null) builder.UseSetting("Services:Quotation", scenario.LiveQuotationBase);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(scenario.Clock);
                services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options => options.TimeProvider = scenario.Clock);
                services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new TransportFilter(scenario));
                if (scenario.ProbeCache)
                {
                    services.RemoveAll<IDistributedCache>();
                    services.AddSingleton<IDistributedCache>(provider => new CacheProbe(
                        new MemoryDistributedCache(provider.GetRequiredService<IOptions<MemoryDistributedCacheOptions>>()), scenario));
                }
            });
        }
    }

    private sealed class TransportFilter(Scenario scenario) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            if (scenario.LiveQuotationBase is not null && builder.Name == "QuotationQualificationClient") return;
            builder.PrimaryHandler = new Transport(scenario);
        };
    }

    private sealed record Sent(HttpMethod Method, string Path, string? Authorization, string Body, bool HasBrowserActorHeader);

    private sealed class Scenario : IDisposable
    {
        public const string Issuer = "https://qualification-auth.test";
        public const string Audience = "legacy-qualification-test";
        public const string RefreshToken = "fixture-only-refresh-rotation-1";
        private readonly RSA rsa = RSA.Create(2048);
        public TicketClock Clock { get; } = new();
        public ConcurrentQueue<Sent> Calls { get; } = new();
        public string PublicKey => rsa.ExportSubjectPublicKeyInfoPem();
        public string EmployeeToken { get; }
        public string RefreshedEmployeeToken { get; private set; } = string.Empty;
        public string ServiceToken { get; }
        public string BrowserToken { get; }
        public HttpStatusCode RefreshStatus { get; set; } = HttpStatusCode.OK;
        public bool RefreshWrongOwner { get; set; }
        public string? MissingPermission { get; set; }
        public string? RefreshMissingPermission { get; set; }
        public string? InvalidLoginToken { get; set; }
        public HttpStatusCode QualificationStatus { get; set; } = HttpStatusCode.OK;
        public string? QualificationBody { get; set; }
        public string? LiveQuotationBase { get; set; }
        public bool BlockRefresh { get; set; }
        public bool BlockQualification { get; set; }
        public bool ProbeCache { get; set; }
        public bool FailCacheRead { get; set; }
        public bool RefreshRace { get; set; }
        public string? SessionKey { get; set; }
        public TaskCompletionSource RefreshEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRefresh { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource QualificationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseQualification { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BothRefreshesEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRaceWinner { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRaceLoser { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RefreshCalls;

        public Scenario()
        {
            EmployeeToken = Token("employee-ticket-owner", "employee");
            ServiceToken = Token("service:legacy-intranet", "service");
            BrowserToken = Token("employee-browser-override", "employee");
        }

        public string Token(string subject, string kind, RSA? key = null, bool expired = false, bool refreshed = false)
        {
            // Production JWT lifetime validation uses the actual wall clock. The controlled
            // clock moves ticket-envelope refresh skew only, not JWT not-before into the future.
            var now = DateTimeOffset.UtcNow;
            var claims = new List<Claim>
            {
                new("sub", subject), new("name", "employee@maliev.com"), new("email", "employee@maliev.com"),
                new("identity_kind", kind), new("legacy_database_id", "2"), new("sid", "fixture-session-binding"),
                new("jti", Guid.NewGuid().ToString("N")),
            };
            foreach (var permission in Permissions.Where(permission => permission != MissingPermission && (!refreshed || permission != RefreshMissingPermission))) claims.Add(new("permissions", permission));
            var token = new JwtSecurityToken(Issuer, Audience, claims, now.AddMinutes(-10).UtcDateTime,
                now.AddMinutes(expired ? -2 : 15).UtcDateTime,
                new SigningCredentials(new RsaSecurityKey(key ?? rsa) { KeyId = "qualification-test-key" }, SecurityAlgorithms.RsaSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public object LoginEnvelope()
        {
            var access = EmployeeToken;
            if (MissingPermission is not null) access = Token("employee-ticket-owner", "employee");
            if (InvalidLoginToken == "customer") access = Token("customer-fixture", "customer");
            if (InvalidLoginToken == "expired") access = Token("employee-ticket-owner", "employee", expired: true);
            if (InvalidLoginToken == "wrong-signature")
            {
                using var wrongKey = RSA.Create(2048);
                access = Token("employee-ticket-owner", "employee", wrongKey);
            }
            return Envelope(access, RefreshToken);
        }

        public object RefreshEnvelope()
        {
            RefreshedEmployeeToken = Token(RefreshWrongOwner ? "other-employee-owner" : "employee-ticket-owner", "employee", refreshed: true);
            return Envelope(RefreshedEmployeeToken, "fixture-only-refresh-rotation-2");
        }

        private object Envelope(string access, string refresh) => new
        {
            accessToken = access,
            refreshToken = refresh,
            tokenType = "Bearer",
            expiresIn = 900,
            refreshExpiresAt = Clock.GetUtcNow().AddDays(1),
        };

        public void Dispose() => rsa.Dispose();
    }

    private sealed class Transport(Scenario scenario) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content?.Headers.ContentType?.MediaType == "application/json"
                ? await request.Content.ReadAsStringAsync(cancellationToken) : string.Empty;
            scenario.Calls.Enqueue(new(request.Method, path, request.Headers.Authorization?.ToString(), body, request.Headers.Contains("X-Employee-Id")));
            switch (path)
            {
                case "/auth/v1/login": return Json(scenario.LoginEnvelope());
                case "/auth/v1/service/login": return Json(new { accessToken = scenario.ServiceToken, expiresIn = 900 });
                case "/auth/v1/refresh":
                    var refreshIndex = Interlocked.Increment(ref scenario.RefreshCalls);
                    if (scenario.RefreshRace)
                    {
                        if (refreshIndex == 2) scenario.BothRefreshesEntered.TrySetResult();
                        if (refreshIndex == 1)
                        {
                            await scenario.ReleaseRaceWinner.Task.WaitAsync(cancellationToken);
                            return Json(scenario.RefreshEnvelope());
                        }
                        await scenario.ReleaseRaceLoser.Task.WaitAsync(cancellationToken);
                        return new(HttpStatusCode.Unauthorized);
                    }
                    if (scenario.BlockRefresh)
                    {
                        scenario.RefreshEntered.TrySetResult();
                        await scenario.ReleaseRefresh.Task.WaitAsync(cancellationToken);
                    }
                    return scenario.RefreshStatus == HttpStatusCode.OK ? Json(scenario.RefreshEnvelope()) : new(scenario.RefreshStatus);
                case "/quotationrequests/84/qualification-receipt":
                case "/quotationrequests/84/qualification":
                    if (scenario.BlockQualification)
                    {
                        scenario.QualificationEntered.TrySetResult();
                        await scenario.ReleaseQualification.Task.WaitAsync(cancellationToken);
                    }
                    if (scenario.QualificationStatus != HttpStatusCode.OK)
                    {
                        var failure = new HttpResponseMessage(scenario.QualificationStatus) { Content = new StringContent("sensitive-upstream-detail") };
                        if (scenario.QualificationStatus == HttpStatusCode.TooManyRequests) failure.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                        return failure;
                    }
                    if (scenario.QualificationBody is not null) return Raw(scenario.QualificationBody);
                    return Json(new { requestId = 84, journeyId = "11111111-2222-3333-4444-555555555555", transactionId = "request-84", state = "qualified", stateChangedUtc = (DateTime?)null, version = 2, events = Array.Empty<object>() });
                case "/customers/3": return Raw(Customer);
                case "/employees/2": return Raw(Employee);
                case "/Currencies": return Raw("[{\"id\":1,\"shortName\":\"THB\",\"longName\":\"Thai Baht\"}]");
                case "/Countries": return Raw("[]");
                case "/quotations": return Json(new { Id = 77 });
                case "/quotations/orderitems": return Json(new { Id = 101 });
                case "/pdfs/quotation":
                    var pdf = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-fixture-only")) };
                    pdf.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                    return pdf;
                case "/Uploads": return Json(new { Object = new[] { new { Bucket = "fixture", ObjectName = "fixture.pdf" } } });
                case "/quotations/77/files":
                case "/Emails/manufacturing": return new(HttpStatusCode.NoContent);
                default: throw new InvalidOperationException("Unexpected controlled downstream route: " + path);
            }
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
        private static HttpResponseMessage Raw(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    }

    // Control ticket expiry without freezing production HTTP/Polly timeout and retry timers.
    private sealed class TicketClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class CacheProbe(IDistributedCache inner, Scenario scenario) : IDistributedCache
    {
        public byte[]? Get(string key) => scenario.FailCacheRead ? throw new InvalidOperationException("fixture-cache-unavailable") : inner.Get(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => scenario.FailCacheRead ? throw new InvalidOperationException("fixture-cache-unavailable") : inner.GetAsync(key, token);
        public void Refresh(string key) => inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);
        public void Remove(string key) => inner.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, value, options);
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            if (key.StartsWith("legacy-intranet:session:", StringComparison.Ordinal)) scenario.SessionKey = key;
            return inner.SetAsync(key, value, options, token);
        }
    }

    // Real sockets/automatic redirect/content buffering, owned loopback only. No TLS
    // bypass, external credentials or positive producer authority is supplied here.
    private sealed class LoopbackQuotation(WebApplication application) : IAsyncDisposable
    {
        public string BaseAddress { get; private set; } = string.Empty;
        public string? OriginalAuthorization { get; private set; }
        public int OriginalCalls;
        public int RedirectedCalls;
        public TaskCompletionSource BodyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseBody { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static async Task<LoopbackQuotation> StartAsync(int? redirect = null, bool slowBody = false)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            var fixture = new LoopbackQuotation(app);
            foreach (var route in new[] { "/quotationrequests/84/qualification-receipt", "/quotationrequests/84/qualification" })
            {
                app.MapMethods(route, ["GET", "PUT"], async (HttpContext context) =>
                {
                    Interlocked.Increment(ref fixture.OriginalCalls);
                    fixture.OriginalAuthorization = context.Request.Headers.Authorization.ToString();
                    if (redirect is not null)
                    {
                        context.Response.StatusCode = redirect.Value;
                        context.Response.Headers.Location = fixture.BaseAddress + "/redirect-target";
                        return;
                    }
                    if (slowBody)
                    {
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("{\"requestId\":", context.RequestAborted);
                        await context.Response.Body.FlushAsync(context.RequestAborted);
                        fixture.BodyStarted.TrySetResult();
                        await fixture.ReleaseBody.Task.WaitAsync(context.RequestAborted);
                    }
                });
            }
            app.MapMethods("/redirect-target", ["GET", "PUT"], async (HttpContext context) =>
            {
                Interlocked.Increment(ref fixture.RedirectedCalls);
                await context.Response.WriteAsJsonAsync(new { requestId = 84, journeyId = "11111111-2222-3333-4444-555555555555", transactionId = "request-84", state = "qualified", stateChangedUtc = (DateTime?)null, version = 2, events = Array.Empty<object>() }, context.RequestAborted);
            });
            await app.StartAsync();
            fixture.BaseAddress = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            ReleaseBody.TrySetResult();
            await application.DisposeAsync();
        }
    }

    private static readonly string[] Permissions =
    [
        LegacyEmployeePermissions.QuotationRequestsRead, LegacyEmployeePermissions.QuotationRequestsUpdate,
        LegacyEmployeePermissions.QuotationsCreate, LegacyEmployeePermissions.QuotationLinesWrite,
        LegacyEmployeePermissions.QuotationOrdersWrite, LegacyEmployeePermissions.CustomersRead,
        LegacyEmployeePermissions.EmployeesRead, LegacyEmployeePermissions.CatalogCurrenciesRead, LegacyEmployeePermissions.OrdersRead,
    ];
    private const string Customer = "{\"id\":3,\"firstName\":\"Thai\",\"lastName\":\"Fixture\",\"fullName\":\"Thai Fixture\",\"telephone\":null,\"mobile\":null,\"fax\":null,\"email\":\"customer@example.test\",\"dateOfBirth\":null,\"companyId\":null,\"billingAddressId\":null,\"shippingAddressId\":null,\"createdDate\":null,\"modifiedDate\":null,\"billingAddress\":null,\"company\":null,\"shippingAddress\":null}";
    private const string Employee = "{\"id\":2,\"roleId\":1,\"firstName\":\"Thai\",\"lastName\":\"Employee\",\"fullName\":\"Thai Employee\",\"phoneNumber\":null,\"email\":\"employee@maliev.com\",\"dateOfBirth\":null,\"homeAddressId\":null,\"createdDate\":null,\"modifiedDate\":null,\"homeAddress\":null,\"role\":null}";
}
