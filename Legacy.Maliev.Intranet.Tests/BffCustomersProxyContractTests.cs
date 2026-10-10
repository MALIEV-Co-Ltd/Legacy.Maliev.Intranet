extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Customers;
using Maliev.Aspire.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using BffProgram = Bff::Program;
using CustomersProxy = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomersProxy;
using RelationCountryClient = Bff::Legacy.Maliev.Intranet.Bff.Employees.EmployeeAdministrationCountryClient;
using CustomerRelationClient = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomerRelationClient;
using CustomerUpdateProxy = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomerUpdateProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffCustomersProxyContractTests
{
    [Fact]
    public async Task AuthorizedEmployee_ForwardsExactQueryAndServerOnlyBearerToken()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson);
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync(
            "/bff/customers?sort=CustomerEmail_Ascending&search=ada%20lovelace&index=2&size=25");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/customers?sort=CustomerEmail_Ascending&search=ada%20lovelace&index=2&size=25", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Contains("\"pageIndex\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"fullName\":\"Ada Lovelace\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only-access-token", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmployeeWithoutExactPermission_IsForbiddenBeforeCustomerServiceCall()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson);
        await using var factory = new CustomersBffFactory(downstream, hasPermission: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task AnonymousRequest_IsUnauthorizedBeforeCustomerServiceCall()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson);
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task InvalidPaging_IsClampedAndNotFoundBecomesAnEmptyPage()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.NotFound, "{}");
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers?index=-5&size=999");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/customers?sort=CustomerCreatedDate_Descending&search=&index=1&size=250", downstream.PathAndQuery);
        Assert.Contains("\"items\":[]", json, StringComparison.Ordinal);
        Assert.Contains("\"pageIndex\":1", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task DownstreamAuthorizationFailure_IsPreserved(HttpStatusCode statusCode)
    {
        var downstream = new RecordingCustomerHandler(statusCode, "{}");
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(statusCode, response.StatusCode);
    }

    [Fact]
    public async Task RateLimit_PreservesStatusAndBoundedRetryAfterWithoutRetry()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.TooManyRequests, "{}", retryAfterSeconds: 1);
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(1, downstream.RequestCount);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task InvalidPayload_IsMappedToBadGatewayWithoutLeakingPayload()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, "not-json");
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("not-json", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_StripsUnexpectedInternalRemarkFromDownstreamCustomerRows()
    {
        var downstream = new RecordingCustomerHandler(
            HttpStatusCode.OK,
            CustomerPageJson.Replace("\"Email\":\"ada@example.com\"", "\"Email\":\"ada@example.com\",\"InternalRemark\":\"private\"", StringComparison.Ordinal));
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("remark", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task TransportFailure_IsMappedToServiceUnavailable(Exception exception)
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, "{}", exception: exception);
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizedCookie_WithSignedCustomerListServiceToken_PassesCustomerPermissionPipeline()
    {
        using var signingKey = RSA.Create(2048);
        await using var customer = await StartCustomerPermissionPipelineAsync(signingKey);
        var serviceToken = CreateSignedToken(signingKey, includeCustomerListPermission: true);
        await using var factory = new CustomersBffFactory(
            customer.GetTestServer().CreateHandler(),
            hasPermission: true,
            serviceToken);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CallerCancellation_IsNotTranslatedIntoAServiceUnavailableResponse()
    {
        await using var factory = new CustomersBffFactory(new CallerCancellationHandler(), hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var exception = await Record.ExceptionAsync(() =>
            client.GetAsync("/bff/customers", cancellation.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task Detail_AuthorizedEmployee_ForwardsExactIdAndServerOnlyBearerToken()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerDetailJson);
        await using var factory = new CustomersBffFactory(
            downstream,
            hasPermission: true,
            hasReadPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/customers/42", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Contains("\"fullName\":\"Ada Lovelace\"", json, StringComparison.Ordinal);
        Assert.Contains("\"taxNumber\":\"TH-123\"", json, StringComparison.Ordinal);
        Assert.Contains("\"billingAddress\"", json, StringComparison.Ordinal);
        Assert.Contains("\"shippingAddress\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only-access-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("signed-service-token", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_EmployeeWithoutExactReadPermission_IsForbiddenBeforeDownstreamCall()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerDetailJson);
        await using var factory = new CustomersBffFactory(
            downstream,
            hasPermission: true,
            hasReadPermission: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task Detail_AnonymousRequest_IsUnauthorizedBeforeDownstreamCall()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerDetailJson);
        await using var factory = new CustomersBffFactory(
            downstream,
            hasPermission: true,
            hasReadPermission: true);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/customers/42");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Detail_ExpectedDownstreamStatus_IsPreserved(HttpStatusCode statusCode)
    {
        var downstream = new RecordingCustomerHandler(statusCode, "{}");
        await using var factory = new CustomersBffFactory(
            downstream,
            hasPermission: true,
            hasReadPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42");

        Assert.Equal(statusCode, response.StatusCode);
    }

    [Fact]
    public async Task Detail_RateLimit_PreservesBoundedRetryAfterWithoutRetry()
    {
        var downstream = new RecordingCustomerHandler(
            HttpStatusCode.TooManyRequests,
            "{}",
            retryAfterSeconds: 2);
        await using var factory = new CustomersBffFactory(
            downstream,
            hasPermission: true,
            hasReadPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(2), response.Headers.RetryAfter?.Delta);
        Assert.Equal(1, downstream.RequestCount);
    }

    [Fact]
    public async Task Detail_InvalidPayload_IsBadGatewayWithoutLeakingPayload()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, "customer-secret-not-json");
        await using var factory = new CustomersBffFactory(
            downstream,
            hasPermission: true,
            hasReadPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("customer-secret-not-json", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_MismatchedCustomerId_IsBadGateway()
    {
        var downstream = new RecordingCustomerHandler(
            HttpStatusCode.OK,
            CustomerDetailJson.Replace("\"Id\":42", "\"Id\":99", StringComparison.Ordinal));
        await using var factory = new CustomersBffFactory(
            downstream,
            hasPermission: true,
            hasReadPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Detail_StripsUnexpectedInternalRemarkFromTheOrdinaryCustomerProjection()
    {
        var downstream = new RecordingCustomerHandler(
            HttpStatusCode.OK,
            CustomerDetailJson.Insert(CustomerDetailJson.Length - 1, ",\"InternalRemark\":\"private\""));
        await using var factory = new CustomersBffFactory(
            downstream,
            hasPermission: true,
            hasReadPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("remark", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_MissingRequiredProfileFields_IsBadGateway()
    {
        var downstream = new RecordingCustomerHandler(
            HttpStatusCode.OK,
            CustomerDetailJson.Replace("\"FullName\":\"Ada Lovelace\",", string.Empty, StringComparison.Ordinal));
        await using var factory = new CustomersBffFactory(
            downstream,
            hasPermission: true,
            hasReadPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task Detail_TransportFailure_IsServiceUnavailable(Exception exception)
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, "{}", exception: exception);
        await using var factory = new CustomersBffFactory(
            downstream,
            hasPermission: true,
            hasReadPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Detail_SignedLeastPrivilegeServiceToken_PassesCustomerPermissionPipeline()
    {
        using var signingKey = RSA.Create(2048);
        await using var customer = await StartCustomerPermissionPipelineAsync(signingKey);
        var serviceToken = CreateSignedToken(signingKey, includeCustomerReadPermission: true);
        await using var factory = new CustomersBffFactory(
            customer.GetTestServer().CreateHandler(),
            hasPermission: true,
            serviceToken,
            hasReadPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Detail_ServiceTokenWithoutReadPermission_IsForbidden()
    {
        using var signingKey = RSA.Create(2048);
        await using var customer = await StartCustomerPermissionPipelineAsync(signingKey);
        var serviceToken = CreateSignedToken(signingKey);
        await using var factory = new CustomersBffFactory(
            customer.GetTestServer().CreateHandler(),
            hasPermission: true,
            serviceToken,
            hasReadPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidCsrfAndPermission_CreatesProfileAndIdentityWithServerToken()
    {
        var profile = new RecordingWorkflowHandler(
            (HttpStatusCode.Created, "{\"id\":42}"));
        var identity = new RecordingWorkflowHandler(
            (HttpStatusCode.Created, "{\"databaseID\":42}"),
            (HttpStatusCode.OK, "{\"accepted\":true,\"token\":\"setup-token\"}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: true,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/customers", profile.Requests.Single().PathAndQuery);
        Assert.Equal("Bearer signed-service-token", profile.Requests.Single().Authorization);
        Assert.Collection(
            identity.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/auth/v1/customer-identities/42", request.PathAndQuery);
                Assert.Equal("Bearer signed-service-token", request.Authorization);
                Assert.Contains("\"passwordSetupRequired\":true", request.Body, StringComparison.Ordinal);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/auth/v1/customer-identities/42/password-setup", request.PathAndQuery);
                Assert.Equal("Bearer signed-service-token", request.Authorization);
                Assert.Null(request.Body);
            });
        Assert.Contains("\"id\":42", body, StringComparison.Ordinal);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_MissingCsrf_IsRejectedBeforeAnyDownstreamCall()
    {
        var profile = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"id\":42}"));
        var identity = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: true,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(profile.Requests);
        Assert.Empty(identity.Requests);
    }

    [Fact]
    public async Task Create_InvalidOperationKey_IsRejectedBeforeAnyDownstreamCall()
    {
        var profile = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"id\":42}"));
        var identity = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: true,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/customers")
        {
            Content = JsonContent.Create(ValidCreateRequest()),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        request.Headers.Add("Idempotency-Key", "invalid");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(profile.Requests);
        Assert.Empty(identity.Requests);
    }

    [Fact]
    public async Task Create_WithoutExactPermission_IsForbiddenBeforeAnyDownstreamCall()
    {
        var profile = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"id\":42}"));
        var identity = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: false,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(profile.Requests);
        Assert.Empty(identity.Requests);
    }

    [Fact]
    public async Task Create_InvalidRequest_ReturnsValidationProblemBeforeAnyDownstreamCall()
    {
        var profile = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"id\":42}"));
        var identity = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: true,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        var invalid = ValidCreateRequest();
        invalid.Email = "not-an-email";
        invalid.Telephone = null;

        using var response = await SendCreateAsync(client, invalid, includeCsrf: true);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("email", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("telephone", body, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(profile.Requests);
        Assert.Empty(identity.Requests);
    }

    [Fact]
    public async Task Create_IdentityConflict_RetainsProfileAndPreservesConflict()
    {
        var profile = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"id\":42}"));
        var identity = new RecordingWorkflowHandler((HttpStatusCode.Conflict, "{}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: true,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpMethod.Post, Assert.Single(profile.Requests).Method);
    }

    [Fact]
    public async Task Create_RetryAfterUncertainIdentity_UsesSameProfileKeyWithoutDeletingProfile()
    {
        var operationId = Guid.Parse("1904e7f5-7223-45c9-8ea3-91c0aa498cf0");
        var profile = new RecordingWorkflowHandler(
            (HttpStatusCode.Created, "{\"id\":42}"),
            (HttpStatusCode.Created, "{\"id\":42}"));
        var identity = new RecordingWorkflowHandler(
            (HttpStatusCode.ServiceUnavailable, "{}"),
            (HttpStatusCode.Conflict, "{}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: true,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var first = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true, operationId: operationId);
        using var replay = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true, operationId: operationId);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(2, profile.Requests.Count);
        var downstreamKey = profile.Requests[0].IdempotencyKey;
        Assert.True(Guid.TryParse(downstreamKey, out var downstreamOperationId));
        Assert.NotEqual(operationId, downstreamOperationId);
        Assert.All(profile.Requests, request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(downstreamKey, request.IdempotencyKey);
        });
        Assert.Equal(2, identity.Requests.Count);
    }

    [Fact]
    public async Task Create_SameBrowserKeyForDifferentEmployees_UsesDistinctDownstreamKeys()
    {
        var operationId = Guid.Parse("1904e7f5-7223-45c9-8ea3-91c0aa498cf0");
        var profile = new RecordingWorkflowHandler(
            (HttpStatusCode.Created, "{\"id\":42}"),
            (HttpStatusCode.Created, "{\"id\":42}"));
        var identity = new RecordingWorkflowHandler(
            (HttpStatusCode.ServiceUnavailable, "{}"),
            (HttpStatusCode.ServiceUnavailable, "{}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: true,
            profileDownstream: profile,
            identityDownstream: identity);
        using var firstEmployee = CreateClient(factory);
        using var secondEmployee = CreateClient(factory);
        await SignInAsync(firstEmployee, "first.employee@maliev.com");
        await SignInAsync(secondEmployee, "second.employee@maliev.com");

        using var first = await SendCreateAsync(firstEmployee, ValidCreateRequest(), includeCsrf: true, operationId);
        using var second = await SendCreateAsync(secondEmployee, ValidCreateRequest(), includeCsrf: true, operationId);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal(2, profile.Requests.Count);
        Assert.True(Guid.TryParse(profile.Requests[0].IdempotencyKey, out _));
        Assert.True(Guid.TryParse(profile.Requests[1].IdempotencyKey, out _));
        Assert.NotEqual(profile.Requests[0].IdempotencyKey, profile.Requests[1].IdempotencyKey);
    }

    [Fact]
    public async Task Create_ProfileRateLimit_PreservesBoundedRetryAfterWithoutRetry()
    {
        var profile = new RecordingCustomerHandler(
            HttpStatusCode.TooManyRequests,
            "{}",
            retryAfterSeconds: 2);
        var identity = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: true,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(2), response.Headers.RetryAfter?.Delta);
        Assert.Equal(1, profile.RequestCount);
        Assert.Empty(identity.Requests);
    }

    [Fact]
    public async Task Create_AnonymousRequest_IsUnauthorizedBeforeAnyDownstreamCall()
    {
        var profile = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"id\":42}"));
        var identity = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: true,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(profile.Requests);
        Assert.Empty(identity.Requests);
    }

    [Fact]
    public async Task Create_SignedLeastPrivilegeServiceToken_PassesBothPermissionPipelines()
    {
        using var signingKey = RSA.Create(2048);
        await using var customer = await StartCustomerCreationPermissionPipelineAsync(signingKey);
        await using var auth = await StartIdentityCreationPermissionPipelineAsync(signingKey);
        var serviceToken = CreateSignedToken(
            signingKey,
            includeCustomerCreatePermission: true,
            includeCustomerDeletePermission: true,
            includeIdentityCreatePermission: true);
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            serviceToken,
            hasCreatePermission: true,
            profileDownstream: customer.GetTestServer().CreateHandler(),
            identityDownstream: auth.GetTestServer().CreateHandler());
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_ServiceTokenWithoutIdentityPermission_IsForbiddenWithoutProfileDelete()
    {
        using var signingKey = RSA.Create(2048);
        await using var customer = await StartCustomerCreationPermissionPipelineAsync(signingKey);
        await using var auth = await StartIdentityCreationPermissionPipelineAsync(signingKey);
        var serviceToken = CreateSignedToken(
            signingKey,
            includeCustomerCreatePermission: true,
            includeCustomerDeletePermission: true,
            includeIdentityCreatePermission: false);
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            serviceToken,
            hasCreatePermission: true,
            profileDownstream: customer.GetTestServer().CreateHandler(),
            identityDownstream: auth.GetTestServer().CreateHandler());
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_MissingCsrf_IsRejectedBeforeAnyCustomerWrite()
    {
        var profile = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerDetailJson);
        var update = new RecordingWorkflowHandler((HttpStatusCode.NoContent, string.Empty));
        await using var factory = new CustomersBffFactory(
            profile,
            hasPermission: true,
            hasReadPermission: true,
            hasUpdatePermission: true,
            updateDownstream: update);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.PutAsJsonAsync("/bff/customers/42", ValidUpdateRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, profile.RequestCount);
        Assert.Empty(update.Requests);
    }

    [Fact]
    public async Task Update_EmployeeWithoutExactPermission_IsForbiddenBeforeDownstreamCalls()
    {
        var profile = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerDetailJson);
        var update = new RecordingWorkflowHandler((HttpStatusCode.NoContent, string.Empty));
        await using var factory = new CustomersBffFactory(
            profile,
            hasPermission: true,
            hasReadPermission: true,
            hasUpdatePermission: false,
            updateDownstream: update);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendUpdateAsync(client, ValidUpdateRequest(), includeCsrf: true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, profile.RequestCount);
        Assert.Empty(update.Requests);
    }

    [Fact]
    public async Task Update_AuthorizedCsrfWrite_PreservesOwnedRelationsAndDoesNotRetry()
    {
        var profile = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerDetailJson);
        var update = new RecordingWorkflowHandler((HttpStatusCode.NoContent, string.Empty));
        await using var factory = new CustomersBffFactory(
            profile,
            hasPermission: true,
            hasReadPermission: true,
            hasUpdatePermission: true,
            updateDownstream: update);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendUpdateAsync(client, ValidUpdateRequest(), includeCsrf: true);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, profile.RequestCount);
        var request = Assert.Single(update.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/customers/42", request.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", request.Authorization);
        Assert.Contains("\"firstName\":\"Grace\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"companyId\":7", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"billingAddressId\":13", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"shippingAddressId\":14", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("remark", request.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server-only-access-token", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_InvalidInput_IsRejectedBeforeCustomerServiceRead()
    {
        var profile = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerDetailJson);
        var update = new RecordingWorkflowHandler((HttpStatusCode.NoContent, string.Empty));
        await using var factory = new CustomersBffFactory(
            profile,
            hasPermission: true,
            hasReadPermission: true,
            hasUpdatePermission: true,
            updateDownstream: update);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        var invalid = ValidUpdateRequest();
        invalid.Email = "not-an-email";
        invalid.FirstName = string.Empty;
        using var response = await SendUpdateAsync(client, invalid, includeCsrf: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, profile.RequestCount);
        Assert.Empty(update.Requests);
    }

    [Fact]
    public async Task Update_DownstreamConflict_IsPreservedWithoutRetry()
    {
        var profile = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerDetailJson);
        var update = new RecordingWorkflowHandler((HttpStatusCode.Conflict, string.Empty));
        await using var factory = new CustomersBffFactory(
            profile,
            hasPermission: true,
            hasReadPermission: true,
            hasUpdatePermission: true,
            updateDownstream: update);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendUpdateAsync(client, ValidUpdateRequest(), includeCsrf: true);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(update.Requests);
    }

    [Fact]
    public async Task Update_TwoSessionsWithStaleProfile_RejectsSecondWrite()
    {
        var downstream = new ConcurrentCustomerHandler();
        await using var factory = new CustomersBffFactory(
            downstream,
            hasPermission: true,
            hasReadPermission: true,
            hasUpdatePermission: true);
        using var firstClient = CreateClient(factory);
        using var secondClient = CreateClient(factory);
        await SignInAsync(firstClient, "first@maliev.com");
        await SignInAsync(secondClient, "second@maliev.com");

        using var firstRead = await firstClient.GetAsync("/bff/customers/42/versioned");
        using var secondRead = await secondClient.GetAsync("/bff/customers/42/versioned");
        firstRead.EnsureSuccessStatusCode();
        secondRead.EnsureSuccessStatusCode();
        Assert.Equal("\"00000001\"", firstRead.Headers.ETag?.ToString());
        Assert.Equal(firstRead.Headers.ETag, secondRead.Headers.ETag);
        Assert.Equal("no-store", firstRead.Headers.CacheControl?.ToString());
        var stale = ValidUpdateRequest();
        stale.FirstName = "Ada";
        var winner = ValidUpdateRequest();
        winner.FirstName = "First session";
        using var firstWrite = await SendUpdateAsync(firstClient, winner, includeCsrf: true, revision: firstRead.Headers.ETag!.ToString(), versioned: true);
        using var secondWrite = await SendUpdateAsync(secondClient, stale, includeCsrf: true, revision: secondRead.Headers.ETag!.ToString(), versioned: true);

        Assert.Equal(HttpStatusCode.NoContent, firstWrite.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, secondWrite.StatusCode);
        Assert.Equal("First session", downstream.FirstName);
        Assert.Equal(1, downstream.SuccessfulWrites);
        Assert.Equal("\"00000001\"", downstream.LastIfMatch);
        Assert.Equal(7, downstream.CompanyId);
        Assert.Equal(13, downstream.BillingAddressId);
        Assert.Equal(14, downstream.ShippingAddressId);
    }

    [Fact]
    public async Task UpdateVersioned_RequiresRevisionAfterAuthorizationAndCsrf()
    {
        var downstream = new ConcurrentCustomerHandler();
        await using var factory = new CustomersBffFactory(
            downstream, hasPermission: true, hasReadPermission: true, hasUpdatePermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var missing = await SendUpdateAsync(client, ValidUpdateRequest(), includeCsrf: true, versioned: true);
        using var malformed = await SendUpdateAsync(client, ValidUpdateRequest(), includeCsrf: true, "W/\"00000001\"", versioned: true);
        using var noCsrf = await SendUpdateAsync(client, ValidUpdateRequest(), includeCsrf: false, "\"00000001\"", versioned: true);

        Assert.Equal((HttpStatusCode)428, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Equal(0, downstream.RequestCount);
    }

    [Fact]
    public async Task VersionedReadAndWrite_RequireTheirExactEmployeePermissions()
    {
        var downstream = new ConcurrentCustomerHandler();
        await using var factory = new CustomersBffFactory(
            downstream, hasPermission: true, hasReadPermission: false, hasUpdatePermission: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var read = await client.GetAsync("/bff/customers/42/versioned");
        using var write = await SendUpdateAsync(client, ValidUpdateRequest(), includeCsrf: true, "\"00000001\"", versioned: true);

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(0, downstream.RequestCount);
    }

    [Fact]
    public async Task VersionedReadAndWrite_AnonymousCaller_IsRejectedBeforeDownstream()
    {
        var downstream = new ConcurrentCustomerHandler();
        await using var factory = new CustomersBffFactory(
            downstream, hasPermission: true, hasReadPermission: true, hasUpdatePermission: true);
        using var client = CreateClient(factory);

        using var read = await client.GetAsync("/bff/customers/42/versioned");
        using var write = await client.PutAsJsonAsync("/bff/customers/42/versioned", ValidUpdateRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
        Assert.Equal(0, downstream.RequestCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task VersionedRead_InvalidRevisionOrNestedProfile_FailsClosed(bool invalidNestedProfile, bool includeRevision)
    {
        var profile = invalidNestedProfile
            ? CustomerDetailJson.Replace("\"AddressLine1\":\"1 Logic Road\"", "\"AddressLine1\":\"\"", StringComparison.Ordinal)
            : CustomerDetailJson;
        var downstream = new ConcurrentCustomerHandler(profile, includeRevision);
        await using var factory = new CustomersBffFactory(
            downstream, hasPermission: true, hasReadPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/versioned");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Null(response.Headers.ETag);
        Assert.Equal(1, downstream.RequestCount);
    }

    [Fact]
    public async Task InternalRemark_AuthorizedEmployee_ReadsOnlyTheDedicatedPrivateContract()
    {
        var profile = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerDetailJson);
        var remarks = new RecordingWorkflowHandler((HttpStatusCode.OK, "{\"CustomerId\":42,\"InternalRemark\":\"Employee only\"}"));
        await using var factory = new CustomersBffFactory(
            profile,
            hasPermission: true,
            hasReadPermission: true,
            hasUpdatePermission: true,
            updateDownstream: remarks);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/internal-remark");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(remarks.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/customers/42/internal-remark", request.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", request.Authorization);
        Assert.Contains("\"internalRemark\":\"Employee only\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InternalRemark_MissingCsrf_IsRejectedBeforeCustomerServiceWrite()
    {
        var remarks = new RecordingWorkflowHandler((HttpStatusCode.NoContent, string.Empty));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerDetailJson),
            hasPermission: true,
            hasReadPermission: true,
            hasUpdatePermission: true,
            updateDownstream: remarks);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.PutAsJsonAsync(
            "/bff/customers/42/internal-remark",
            new { internalRemark = "Employee only" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(remarks.Requests);
    }

    [Fact]
    public async Task InternalRemark_EmployeeWithoutReadPermission_IsForbiddenBeforeCustomerServiceCall()
    {
        var remarks = new RecordingWorkflowHandler((HttpStatusCode.OK, "{\"CustomerId\":42,\"InternalRemark\":null}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerDetailJson),
            hasPermission: true,
            hasReadPermission: false,
            hasUpdatePermission: true,
            updateDownstream: remarks);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/internal-remark");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(remarks.Requests);
    }

    [Fact]
    public async Task InternalRemark_EmployeeWithoutUpdatePermission_IsForbiddenBeforeCustomerServiceWrite()
    {
        var remarks = new RecordingWorkflowHandler((HttpStatusCode.NoContent, string.Empty));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerDetailJson),
            hasPermission: true,
            hasReadPermission: true,
            hasUpdatePermission: false,
            updateDownstream: remarks);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/customers/42/internal-remark")
        {
            Content = JsonContent.Create(new { internalRemark = "Employee only" }),
        };
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(remarks.Requests);
    }

    [Fact]
    public async Task InternalRemark_OverLimitInput_IsRejectedBeforeCustomerServiceWrite()
    {
        var remarks = new RecordingWorkflowHandler((HttpStatusCode.NoContent, string.Empty));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerDetailJson),
            hasPermission: true,
            hasReadPermission: true,
            hasUpdatePermission: true,
            updateDownstream: remarks);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/customers/42/internal-remark")
        {
            Content = JsonContent.Create(new { internalRemark = new string('x', 4001) }),
        };
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(remarks.Requests);
    }

    [Fact]
    public async Task InternalRemark_AuthorizedCsrfWrite_ForwardsBoundedPrivatePayloadWithoutRetry()
    {
        var remarks = new RecordingWorkflowHandler((HttpStatusCode.NoContent, string.Empty));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerDetailJson),
            hasPermission: true,
            hasReadPermission: true,
            hasUpdatePermission: true,
            updateDownstream: remarks);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/customers/42/internal-remark")
        {
            Content = JsonContent.Create(new { internalRemark = " Employee only " }),
        };
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var downstream = Assert.Single(remarks.Requests);
        Assert.Equal(HttpMethod.Put, downstream.Method);
        Assert.Equal("/customers/42/internal-remark", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Contains("\"internalRemark\":\" Employee only \"", downstream.Body, StringComparison.Ordinal);
    }

    public static TheoryData<Exception> TransportFailures => new()
    {
        new HttpRequestException("customer unavailable"),
        new TaskCanceledException("customer timeout"),
    };

    [Theory]
    [InlineData("company", "77")]
    [InlineData("company", "new")]
    [InlineData("billing", "78")]
    [InlineData("billing", "new")]
    [InlineData("shipping", "79")]
    [InlineData("shipping", "new")]
    public async Task Relations_ExistingAndNewWritesKeepLiteralFieldsBothVersionsAndEmployeeCredential(string kind, string relation)
    {
        var downstream = new RelationHandler(kind, relation);
        await using var factory = RelationFactory(downstream);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var read = await client.GetAsync($"/bff/customers/42/relations/{kind}/{relation}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(RelationHandler.Version, read.Headers.ETag!.ToString());
        Assert.Equal("\"00000001\"", Assert.Single(read.Headers.GetValues("X-Customer-ETag")));
        Assert.Contains("no-store", read.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        using var write = await SendRelationAsync(client, kind, relation);
        Assert.Equal(HttpStatusCode.NoContent, write.StatusCode);
        Assert.Equal(RelationHandler.Version, downstream.IfMatch);
        Assert.Equal("\"00000001\"", downstream.CustomerIfMatch);
        Assert.Equal("Bearer server-only-access-token", downstream.Authorization);
        Assert.Equal(1, downstream.Writes);
        using var payload = System.Text.Json.JsonDocument.Parse(downstream.Payload!);
        Assert.Equal(kind == "company" ? 3 : 7, payload.RootElement.EnumerateObject().Count());
        Assert.Equal(" บริษัท ไทย ", payload.RootElement.GetProperty(kind == "company" ? "Name" : "AddressLine1").GetString());
        Assert.DoesNotContain("relationId", downstream.Payload!, StringComparison.Ordinal);
        Assert.DoesNotContain("modifiedDate", downstream.Payload!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("company", "77")]
    [InlineData("company", "new")]
    [InlineData("billing", "78")]
    [InlineData("shipping", "new")]
    public async Task Relations_MissingExactCurrentPermissionRejectsBeforeDownstream(string kind, string relation)
    {
        var downstream = new RelationHandler(kind, relation);
        await using var factory = RelationFactory(downstream, []);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var read = await client.GetAsync($"/bff/customers/42/relations/{kind}/{relation}");
        using var write = await SendRelationAsync(client, kind, relation);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(0, downstream.Reads + downstream.Writes);
    }

    [Theory]
    [InlineData(false, "valid", "valid", 400)]
    [InlineData(true, null, "valid", 428)]
    [InlineData(true, "valid", null, 428)]
    [InlineData(true, "W/\"bad\"", "valid", 400)]
    [InlineData(true, "valid", "*", 400)]
    public async Task Relations_CsrfAndBothCapturedValidatorsAreRequiredBeforeAnyDownstream(bool csrf, string? version, string? customerVersion, int expected)
    {
        var downstream = new RelationHandler("company", "77");
        await using var factory = RelationFactory(downstream);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var response = await SendRelationAsync(client, "company", "77", csrf, version, customerVersion);
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(0, downstream.Reads + downstream.Writes);
    }

    [Theory]
    [InlineData("owner", 404)]
    [InlineData("relation-version", 412)]
    [InlineData("customer-version", 412)]
    [InlineData("projection", 502)]
    public async Task Relations_BoundIdOrStaleEitherVersionNeverStartsWrite(string rejection, int expected)
    {
        var downstream = new RelationHandler("billing", "78") { Rejection = rejection };
        await using var factory = RelationFactory(downstream);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var response = await SendRelationAsync(client, "billing", "78");
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(0, downstream.Writes);
    }

    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(403)] [InlineData(404)]
    [InlineData(409)] [InlineData(412)] [InlineData(429)] [InlineData(503)]
    public async Task Relations_ProducerWriteFailureIsBoundedAndNotReplayed(int status)
    {
        var downstream = new RelationHandler("shipping", "79") { WriteStatus = status };
        await using var factory = RelationFactory(downstream);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var response = await SendRelationAsync(client, "shipping", "79");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(1, downstream.Writes);
    }

    [Theory]
    [InlineData("unknown", "77", 400)] [InlineData("company", "0", 400)] [InlineData("billing", "-1", 400)]
    [InlineData("company", "077", 400)]
    public async Task Relations_InvalidKindOrIdentifierIsRejectedBeforeAnyDownstream(string kind, string relation, int expected)
    {
        var downstream = new RelationHandler("company", "77");
        await using var factory = RelationFactory(downstream);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var response = await SendRelationAsync(client, kind, relation);
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(0, downstream.Reads + downstream.Writes);
    }

    [Fact]
    public async Task Relations_AnonymousRequestIsUnauthorizedBeforeDownstream()
    {
        var downstream = new RelationHandler("company", "77");
        await using var factory = RelationFactory(downstream);
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/bff/customers/42/relations/company/77");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, downstream.Reads + downstream.Writes);
    }

    [Theory]
    [InlineData("company", "{\"name\":\" \",\"taxNumber\":null,\"registrar\":null}")]
    [InlineData("company", "{\"name\":\"Valid\",\"relationId\":9}")]
    [InlineData("billing", "{\"addressLine1\":\"Valid\",\"countryId\":0}")]
    [InlineData("shipping", "{\"addressLine1\":\"Valid\",\"countryId\":1,\"customerId\":99}")]
    public async Task Relations_InvalidPayloadCannotMassAssignOrStartTransport(string kind, string payload)
    {
        var downstream = new RelationHandler(kind, "new");
        await using var factory = RelationFactory(downstream);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var session = await client.GetAsync("/bff/session");
        var state = await session.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/bff/customers/42/relations/{kind}/{(kind == "company" ? "" : "address/")}new")
        { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-CSRF-TOKEN", state.GetProperty("csrfToken").GetString());
        request.Headers.TryAddWithoutValidation("If-Match", RelationHandler.Version);
        request.Headers.TryAddWithoutValidation("X-Customer-If-Match", "\"00000001\"");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, downstream.Reads + downstream.Writes);
    }

    [Fact]
    public async Task Relations_MalformedCommittedAcknowledgementIsNotAcceptedOrReplayed()
    {
        var downstream = new RelationHandler("company", "new") { OmitSuccessVersions = true };
        await using var factory = RelationFactory(downstream);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var response = await SendRelationAsync(client, "company", "new");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(1, downstream.Writes);
    }

    [Fact]
    public async Task Relations_UnregisteredCountryRejectsBeforeCustomerTransport()
    {
        var downstream = new RelationHandler("shipping", "new");
        await using var factory = RelationFactory(downstream);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var session = await client.GetAsync("/bff/session");
        var state = await session.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/customers/42/relations/shipping/address/new")
        { Content = JsonContent.Create(new CustomerAddressEdit { AddressLine1 = "Literal", CountryId = 999 }) };
        request.Headers.Add("X-CSRF-TOKEN", state.GetProperty("csrfToken").GetString());
        request.Headers.TryAddWithoutValidation("If-Match", RelationHandler.Version);
        request.Headers.TryAddWithoutValidation("X-Customer-If-Match", "\"00000001\"");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, downstream.Reads + downstream.Writes);
    }

    [Fact]
    public async Task Relations_UpstreamCompanyWireUsesPascalCaseAndOmitsNullMetadata()
    {
        var downstream = new RelationHandler("company", "new");
        await using var factory = RelationFactory(downstream);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var response = await SendRelationAsync(client, "company", "new", omitOptional: true);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var payload = System.Text.Json.JsonDocument.Parse(downstream.Payload!);
        var field = Assert.Single(payload.RootElement.EnumerateObject());
        Assert.Equal("Name", field.Name);
        Assert.Equal(" บริษัท ไทย ", field.Value.GetString());
    }

    private static CustomersBffFactory RelationFactory(RelationHandler handler, string[]? permissions = null) =>
        new(handler, false, hasReadPermission: true, hasUpdatePermission: true, updateDownstream: handler,
            relationPermissions: permissions ?? ["legacy-customer.companies.read", "legacy-customer.companies.update", "legacy-customer.companies.create",
                "legacy-customer.addresses.read", "legacy-customer.addresses.update", "legacy-customer.addresses.create"]);

    private static async Task<HttpResponseMessage> SendRelationAsync(HttpClient client, string kind, string relation,
        bool csrf = true, string? version = "valid", string? customerVersion = "valid", bool omitOptional = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"/bff/customers/42/relations/{kind}/{(kind == "company" ? "" : "address/")}{relation}")
        {
            Content = kind == "company" ? JsonContent.Create(new CustomerCompanyEdit { Name = " บริษัท ไทย ", TaxNumber = omitOptional ? null : " 010 ", Registrar = omitOptional ? null : " กรุงเทพ " })
                : JsonContent.Create(new CustomerAddressEdit { Building = " อาคาร ", AddressLine1 = " บริษัท ไทย ", AddressLine2 = " ชั้น 2 ", City = " กรุงเทพ ", State = " กรุงเทพ ", PostalCode = " 10100 ", CountryId = 1 }),
        };
        if (csrf)
        {
            using var sessionResponse = await client.GetAsync("/bff/session");
            var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        }
        if (version is not null) request.Headers.TryAddWithoutValidation("If-Match", version == "valid" ? RelationHandler.Version : version);
        if (customerVersion is not null) request.Headers.TryAddWithoutValidation("X-Customer-If-Match", customerVersion == "valid" ? "\"00000001\"" : customerVersion);
        return await client.SendAsync(request);
    }

    private sealed class RelationHandler(string kind, string relation) : HttpMessageHandler
    {
        public static string Version => "\"" + new string('a', 64) + "\"";
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public string? IfMatch { get; private set; }
        public string? CustomerIfMatch { get; private set; }
        public string? Payload { get; private set; }
        public string? Authorization { get; private set; }
        public string? Rejection { get; init; }
        public int WriteStatus { get; init; } = 204;
        public bool OmitSuccessVersions { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Authorization = request.Headers.Authorization?.ToString();
            var id = relation == "new" ? (int?)null : int.Parse(relation, System.Globalization.CultureInfo.InvariantCulture);
            var value = new CustomerRelationDetail(Rejection == "projection" ? 99 : 42, id,
                kind == "company" && id is not null ? new(id.Value, "Original", null, null, null, null) : null,
                kind != "company" && id is not null ? new(id.Value, null, "Original", null, null, null, null, 1, null, null) : null);
            HttpResponseMessage response;
            if (request.Method == HttpMethod.Get)
            {
                Reads++;
                Assert.Equal($"/customers/42/relations/{kind}/{(kind == "company" ? "" : "address/")}{relation}{(relation == "new" ? "" : "/edit")}", request.RequestUri!.AbsolutePath);
                response = new(Rejection == "owner" ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = JsonContent.Create(value) };
            }
            else
            {
                Writes++;
                Assert.Equal(HttpMethod.Put, request.Method);
                Assert.Equal($"/customers/42/relations/{kind}/{(kind == "company" ? "" : "address/")}{relation}/versioned", request.RequestUri!.AbsolutePath);
                IfMatch = Assert.Single(request.Headers.GetValues("If-Match"));
                CustomerIfMatch = Assert.Single(request.Headers.GetValues("X-Customer-If-Match"));
                Payload = await request.Content!.ReadAsStringAsync(token);
                response = new((HttpStatusCode)WriteStatus);
                response.Headers.Add("X-Relation-Id", (id ?? 80).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (request.Method == HttpMethod.Put && OmitSuccessVersions) return response;
            response.Headers.ETag = new(Rejection == "relation-version" ? "\"" + new string('b', 64) + "\"" : Version);
            response.Headers.Add("X-Customer-ETag", Rejection == "customer-version" ? "\"00000002\"" : "\"00000001\"");
            return response;
        }
    }

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static async Task SignInAsync(HttpClient client, string email = "employee@maliev.com")
    {
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var csrf = session.GetProperty("csrfToken").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new
            {
                email,
                password = "password",
                returnUrl = "/Customers/Index",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> SendCreateAsync(
        HttpClient client,
        CreateCustomerAccountRequest requestBody,
        bool includeCsrf,
        Guid? operationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/customers")
        {
            Content = JsonContent.Create(requestBody),
        };
        if (includeCsrf)
        {
            using var sessionResponse = await client.GetAsync("/bff/session");
            var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        }

        if (operationId is { } key) request.Headers.Add("Idempotency-Key", key.ToString("D"));

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendUpdateAsync(
        HttpClient client,
        CustomerUpdateRequest requestBody,
        bool includeCsrf,
        string? revision = null,
        bool versioned = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, versioned ? "/bff/customers/42/versioned" : "/bff/customers/42")
        {
            Content = JsonContent.Create(requestBody),
        };
        if (revision is not null) request.Headers.TryAddWithoutValidation("If-Match", revision);
        if (includeCsrf)
        {
            using var sessionResponse = await client.GetAsync("/bff/session");
            var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        }

        return await client.SendAsync(request);
    }

    private static CreateCustomerAccountRequest ValidCreateRequest() => new()
    {
        FirstName = "Ada",
        LastName = "Lovelace",
        Email = "ada@example.com",
        Telephone = "+66 2 123 4567",
        Mobile = "+66 81 234 5678",
        Fax = "+66 2 765 4321",
        DateOfBirth = new DateTime(1815, 12, 10),
    };

    private static CustomerUpdateRequest ValidUpdateRequest() => new()
    {
        FirstName = "Grace",
        LastName = "Hopper",
        Email = "grace@example.com",
        Telephone = "+66 2 999 9999",
        Mobile = "+66 81 999 9999",
        Fax = "+66 2 888 8888",
        DateOfBirth = new DateTime(1906, 12, 9),
    };

    private sealed class CustomersBffFactory(
        HttpMessageHandler downstream,
        bool hasPermission,
        string serviceToken = "signed-service-token",
        bool hasCreatePermission = false,
        HttpMessageHandler? profileDownstream = null,
        HttpMessageHandler? identityDownstream = null,
        bool hasReadPermission = false,
        bool hasUpdatePermission = false,
        HttpMessageHandler? updateDownstream = null,
        string[]? relationPermissions = null)
        : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Services:Auth", "http://auth/");
            builder.UseSetting("Services:Catalog", "http://catalog/");
            builder.UseSetting("Services:Customer", "http://customer/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new CustomersAuthClient(hasPermission, hasCreatePermission, hasReadPermission, hasUpdatePermission, relationPermissions));
                services.RemoveAll<IServiceAccessTokenProvider>();
                services.AddSingleton<IServiceAccessTokenProvider>(new CustomersServiceTokenProvider(serviceToken));
                services.AddHttpClient<CustomersProxy>()
                    .ConfigurePrimaryHttpMessageHandler(() => downstream);
                services.AddHttpClient<CustomerUpdateProxy>()
                    .ConfigurePrimaryHttpMessageHandler(() => updateDownstream ?? downstream);
                services.AddHttpClient<RelationCountryClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new RecordingCustomerHandler(HttpStatusCode.OK, "[{\"id\":1,\"name\":\"Thailand\"}]"));
                services.AddHttpClient<CustomerRelationClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => updateDownstream ?? downstream);
                services.RemoveAll<ICustomerProfileCreationClient>();
                services.AddHttpClient<ICustomerProfileCreationClient, CustomerProfileCreationClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => profileDownstream ?? downstream);
                services.RemoveAll<ICustomerIdentityCreationClient>();
                services.AddHttpClient<ICustomerIdentityCreationClient, CustomerIdentityCreationClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => identityDownstream ?? downstream);
            });
        }
    }

    private sealed class CustomersServiceTokenProvider(string serviceToken) : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(serviceToken);

        public void Invalidate(string token)
        {
        }
    }

    private sealed class CustomersAuthClient(bool hasPermission, bool hasCreatePermission, bool hasReadPermission, bool hasUpdatePermission, string[]? relationPermissions = null) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
                new EmployeeIdentity(
                    email,
                    email,
                    email,
                    [
                        .. relationPermissions ?? Array.Empty<string>(),
                        .. hasPermission ? ["legacy-customer.customers.list"] : Array.Empty<string>(),
                        .. hasCreatePermission ? ["legacy-customer.customers.create"] : Array.Empty<string>(),
                        .. hasReadPermission ? ["legacy-customer.customers.read"] : Array.Empty<string>(),
                        .. hasUpdatePermission ? ["legacy-customer.customers.update"] : Array.Empty<string>(),
                    ])));

        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class RecordingCustomerHandler(
        HttpStatusCode statusCode,
        string body,
        int? retryAfterSeconds = null,
        Exception? exception = null) : HttpMessageHandler
    {
        public string? PathAndQuery { get; private set; }
        public string? Authorization { get; private set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
            if (exception is not null)
            {
                throw exception;
            }

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (retryAfterSeconds is not null)
            {
                response.Headers.RetryAfter = new(TimeSpan.FromSeconds(retryAfterSeconds.Value));
            }

            return Task.FromResult(response);
        }
    }

    private sealed class ConcurrentCustomerHandler(string? profile = null, bool includeRevision = true) : HttpMessageHandler
    {
        public string FirstName { get; private set; } = "Ada";
        public int RequestCount { get; private set; }
        public int SuccessfulWrites { get; private set; }
        public string? LastIfMatch { get; private set; }
        public int? CompanyId { get; private set; }
        public int? BillingAddressId { get; private set; }
        public int? ShippingAddressId { get; private set; }
        private uint revision = 1;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.Equal("/customers/42/versioned", request.RequestUri?.AbsolutePath);
            if (request.Method == HttpMethod.Get)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        (profile ?? CustomerDetailJson).Replace("\"FirstName\":\"Ada\"", $"\"FirstName\":\"{FirstName}\"", StringComparison.Ordinal),
                        Encoding.UTF8,
                        "application/json"),
                };
                if (includeRevision) response.Headers.ETag = new($"\"{revision:x8}\"");
                return response;
            }

            LastIfMatch = request.Headers.TryGetValues("If-Match", out var values) ? Assert.Single(values) : null;
            if (LastIfMatch != $"\"{revision:x8}\"") return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
            using var body = await System.Text.Json.JsonDocument.ParseAsync(
                await request.Content!.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            FirstName = body.RootElement.GetProperty("firstName").GetString()!;
            CompanyId = body.RootElement.GetProperty("companyId").GetInt32();
            BillingAddressId = body.RootElement.GetProperty("billingAddressId").GetInt32();
            ShippingAddressId = body.RootElement.GetProperty("shippingAddressId").GetInt32();
            SuccessfulWrites++;
            revision++;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed class CallerCancellationHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The caller cancellation should stop the request.");
        }
    }

    private sealed class RecordingWorkflowHandler(params (HttpStatusCode StatusCode, string Body)[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Body)> _responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.PathAndQuery,
                request.Headers.Authorization?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null));
            var response = _responses.Dequeue();
            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string? PathAndQuery, string? Authorization, string? Body, string? IdempotencyKey);

    private const string CustomerPageJson =
        """{"Items":[{"Id":42,"FirstName":"Ada","LastName":"Lovelace","FullName":"Ada Lovelace","Email":"ada@example.com","Company":{"Id":7,"Name":"Analytical Engines Ltd"}}],"PageIndex":2,"TotalPages":4,"TotalRecords":75,"HasNextPage":true,"HasPreviousPage":true}""";

    private const string CustomerDetailJson =
        """{"Id":42,"FirstName":"Ada","LastName":"Lovelace","FullName":"Ada Lovelace","Telephone":"+66 2 123 4567","Mobile":"+66 81 234 5678","Fax":"+66 2 765 4321","Email":"ada@example.com","DateOfBirth":"1815-12-10T00:00:00","CompanyId":7,"BillingAddressId":13,"ShippingAddressId":14,"CreatedDate":"2026-01-02T03:04:05Z","ModifiedDate":"2026-07-16T07:08:09Z","BillingAddress":{"Id":13,"Building":"A","AddressLine1":"1 Logic Road","AddressLine2":"Floor 2","City":"Bangkok","State":"Bangkok","PostalCode":"10110","CountryId":211,"CreatedDate":"2026-01-02T03:04:05Z","ModifiedDate":null},"Company":{"Id":7,"Name":"Analytical Engines Ltd","TaxNumber":"TH-123","Registrar":"Bangkok","CreatedDate":"2026-01-02T03:04:05Z","ModifiedDate":null},"ShippingAddress":{"Id":14,"Building":null,"AddressLine1":"2 Engine Road","AddressLine2":null,"City":"Bangkok","State":"Bangkok","PostalCode":"10120","CountryId":211,"CreatedDate":"2026-01-02T03:04:05Z","ModifiedDate":null}}""";

    private static async Task<WebApplication> StartCustomerPermissionPipelineAsync(RSA signingKey)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "https://auth.test",
            ["Jwt:Audience"] = "legacy-test",
            ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(signingKey.ExportSubjectPublicKeyInfoPem())),
        });
        builder.AddJwtAuthentication();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/customers", () => Results.Text(CustomerPageJson, "application/json"))
            .RequireAuthorization($"Permission:{LegacyEmployeePermissions.CustomersList}");
        app.MapGet("/customers/{id:int}", () => Results.Text(CustomerDetailJson, "application/json"))
            .RequireAuthorization("Permission:legacy-customer.customers.read");
        await app.StartAsync();
        return app;
    }

    private static string CreateSignedToken(
        RSA signingKey,
        bool includeCustomerListPermission = false,
        bool includeCustomerCreatePermission = false,
        bool includeCustomerDeletePermission = false,
        bool includeIdentityCreatePermission = false,
        bool includeCustomerReadPermission = false)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "legacy-intranet"),
            new("identity_kind", "service"),
        };
        if (includeCustomerListPermission)
        {
            claims.Add(new Claim("permissions", LegacyEmployeePermissions.CustomersList));
        }
        if (includeCustomerCreatePermission)
        {
            claims.Add(new Claim("permissions", LegacyEmployeePermissions.CustomersCreate));
        }
        if (includeCustomerDeletePermission)
        {
            claims.Add(new Claim("permissions", "legacy-customer.customers.delete"));
        }
        if (includeIdentityCreatePermission)
        {
            claims.Add(new Claim("permissions", "legacy-auth.customer-identities.create"));
        }
        if (includeCustomerReadPermission)
        {
            claims.Add(new Claim("permissions", "legacy-customer.customers.read"));
        }

        var key = new RsaSecurityKey(signingKey) { KeyId = "customer-contract-key" };
        var token = new JwtSecurityToken(
            "https://auth.test",
            "legacy-test",
            claims,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<WebApplication> StartCustomerCreationPermissionPipelineAsync(RSA signingKey)
    {
        var app = BuildPermissionPipeline(signingKey);
        app.MapPost("/customers", () => Results.Text("{\"id\":42}", "application/json", statusCode: StatusCodes.Status201Created))
            .RequireAuthorization($"Permission:{LegacyEmployeePermissions.CustomersCreate}");
        app.MapDelete("/customers/{id:int}", () => Results.NoContent())
            .RequireAuthorization("Permission:legacy-customer.customers.delete");
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartIdentityCreationPermissionPipelineAsync(RSA signingKey)
    {
        var app = BuildPermissionPipeline(signingKey);
        app.MapPost("/auth/v1/customer-identities/{id:int}", (int id) =>
                Results.Text($"{{\"databaseID\":{id}}}", "application/json", statusCode: StatusCodes.Status201Created))
            .RequireAuthorization("Permission:legacy-auth.customer-identities.create");
        app.MapPost("/auth/v1/customer-identities/{id:int}/password-setup", (int id) =>
                Results.Json(new { accepted = true, token = $"setup-token-{id}" }))
            .RequireAuthorization("Permission:legacy-auth.customer-identities.create");
        await app.StartAsync();
        return app;
    }

    private static WebApplication BuildPermissionPipeline(RSA signingKey)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "https://auth.test",
            ["Jwt:Audience"] = "legacy-test",
            ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(signingKey.ExportSubjectPublicKeyInfoPem())),
        });
        builder.AddJwtAuthentication();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
