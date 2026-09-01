extern alias Bff;

using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffHttpMethodSurfaceTests
{
    private static readonly string[] RequestedMethods =
    [
        "GET",
        "POST",
        "PUT",
        "HEAD",
        "DELETE",
        "PATCH",
        "OPTIONS",
        "CONNECT",
        "TRACE",
    ];

    public static TheoryData<string, string[], string[]> FeatureMethodContracts => new()
    {
        { "Workspace authentication", ["/bff/session", "/bff/login", "/bff/logout", "/bff/google"], ["GET", "POST"] },
        { "Dashboard", ["/bff/dashboard"], ["GET"] },
        { "Diagnostics", ["/bff/diagnostics"], ["GET"] },
        { "Employee management and recovery", ["/bff/employees", "/bff/profile", "/bff/employee-recovery"], ["GET", "POST", "PUT"] },
        { "Customers", ["/bff/customers"], ["GET", "POST", "PUT"] },
        { "Catalog", ["/bff/catalog"], ["DELETE", "GET", "POST", "PUT"] },
        { "Orders", ["/bff/orders", "/bff/order-processes"], ["DELETE", "GET", "POST", "PUT"] },
        { "Quotations and requests", ["/bff/quotations", "/bff/quotation-requests"], ["GET", "POST", "PUT"] },
        { "Accounting", ["/bff/finances", "/bff/invoices"], ["DELETE", "GET", "POST", "PUT"] },
        { "Procurement", ["/bff/suppliers", "/bff/purchase-orders"], ["DELETE", "GET", "POST", "PUT"] },
        { "Address configuration", ["/bff/address"], ["GET"] },
    };

    [Fact]
    public void RuntimeEndpointSurface_EveryBffMethodAndRouteMatchesTheReviewedContract()
    {
        using var factory = new AnonymousBffFactory();
        using var client = CreateClient(factory);

        var actual = GetBffEndpoints(factory)
            .SelectMany(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods
                .Select(method => $"{method.ToUpperInvariant()} {endpoint.RoutePattern.RawText}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedEndpointSurface, actual);
        Assert.Equal(95, actual.Length);
        Assert.Equal(53, actual.Count(value => value.StartsWith("GET ", StringComparison.Ordinal)));
        Assert.Equal(23, actual.Count(value => value.StartsWith("POST ", StringComparison.Ordinal)));
        Assert.Equal(10, actual.Count(value => value.StartsWith("PUT ", StringComparison.Ordinal)));
        Assert.Equal(9, actual.Count(value => value.StartsWith("DELETE ", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnonymousEndpointSurface_IsLimitedToSessionAndCredentialBootstrapFlows()
    {
        using var factory = new AnonymousBffFactory();
        using var client = CreateClient(factory);

        var actual = GetBffEndpoints(factory)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .SelectMany(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods
                .Select(method => $"{method.ToUpperInvariant()} {endpoint.RoutePattern.RawText}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
        [
            "GET /bff/session",
            "POST /bff/employee-recovery/email-confirmation/complete",
            "POST /bff/employee-recovery/password-reset/complete",
            "POST /bff/employee-recovery/password-reset/request",
            "POST /bff/google",
            "POST /bff/google/nonce",
            "POST /bff/login",
        ],
        actual);
    }

    [Theory]
    [MemberData(nameof(FeatureMethodContracts))]
    public void FeatureArea_ExposesItsCompleteReviewedHttpMethodSet(
        string feature,
        string[] routePrefixes,
        string[] expectedMethods)
    {
        using var factory = new AnonymousBffFactory();
        using var client = CreateClient(factory);

        var endpoints = GetBffEndpoints(factory)
            .Where(endpoint => routePrefixes.Any(prefix =>
                endpoint.RoutePattern.RawText!.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();
        var actualMethods = endpoints
            .SelectMany(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(endpoints.Length > 0, $"{feature} has no runtime BFF endpoints.");
        Assert.Equal(expectedMethods.Order(StringComparer.Ordinal), actualMethods);
    }

    [Fact]
    public async Task EveryProtectedBffEndpoint_SupportedMethodRejectsAnonymousRequests()
    {
        await using var factory = new AnonymousBffFactory();
        using var client = CreateClient(factory);

        foreach (var endpoint in GetBffEndpoints(factory)
                     .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null))
        {
            var route = MaterializeRoute(endpoint.RoutePattern.RawText!);
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods;
            foreach (var method in methods)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), route);
                using var response = await client.SendAsync(request);

                Assert.True(
                    response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                    $"{method} {route} returned {(int)response.StatusCode} instead of rejecting an anonymous request.");
            }
        }
    }

    [Fact]
    public async Task EveryBffRoute_UnsupportedRequestedMethodsFailClosedWithoutServingTheSpa()
    {
        await using var factory = new AuthenticatedBffFactory();
        using var client = CreateClient(factory);
        var routes = GetBffEndpoints(factory)
            .GroupBy(endpoint => endpoint.RoutePattern.RawText!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var routeGroup in routes)
        {
            var allowed = routeGroup
                .SelectMany(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var route = MaterializeRoute(routeGroup.Key);
            foreach (var method in RequestedMethods.Where(method => !allowed.Contains(method)))
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), route);
                using var response = await client.SendAsync(request);

                Assert.True(
                    response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound,
                    $"{method} {route} returned {(int)response.StatusCode} instead of failing closed.");
                Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
                if (response.StatusCode == HttpStatusCode.MethodNotAllowed)
                {
                    Assert.NotEmpty(response.Content.Headers.Allow);
                    Assert.All(allowed, value => Assert.Contains(value, response.Content.Headers.Allow));
                }
            }
        }
    }

    [Theory]
    [InlineData("GET", HttpStatusCode.OK)]
    [InlineData("POST", HttpStatusCode.MethodNotAllowed)]
    [InlineData("PUT", HttpStatusCode.MethodNotAllowed)]
    [InlineData("HEAD", HttpStatusCode.NotFound)]
    [InlineData("DELETE", HttpStatusCode.MethodNotAllowed)]
    [InlineData("PATCH", HttpStatusCode.MethodNotAllowed)]
    [InlineData("OPTIONS", HttpStatusCode.MethodNotAllowed)]
    [InlineData("CONNECT", HttpStatusCode.MethodNotAllowed)]
    [InlineData("TRACE", HttpStatusCode.MethodNotAllowed)]
    public async Task SessionEndpoint_EachRequestedHttpMethodHasAnExplicitOutcome(
        string method,
        HttpStatusCode expectedStatus)
    {
        await using var factory = new AuthenticatedBffFactory();
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(new HttpMethod(method), "/bff/session");

        using var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task MethodOverrideHeader_CannotTurnPostIntoADelete()
    {
        await using var factory = new AuthenticatedBffFactory();
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/session");
        request.Headers.Add("X-HTTP-Method-Override", "DELETE");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task UnknownBffReadRoute_ReturnsNotFoundInsteadOfTheBlazorShell(string method)
    {
        await using var factory = new AuthenticatedBffFactory();
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(new HttpMethod(method), "/bff/not-a-real-route");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static IReadOnlyList<RouteEndpoint> GetBffEndpoints(WebApplicationFactory<BffProgram> factory) =>
        factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/bff/", StringComparison.Ordinal) == true)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>() is not null)
            .OrderBy(endpoint => endpoint.RoutePattern.RawText, StringComparer.Ordinal)
            .ToArray();

    private static string MaterializeRoute(string routeTemplate) =>
        Regex.Replace(routeTemplate, "\\{[^}]+\\}", "1", RegexOptions.CultureInvariant);

    private static string[] ExpectedEndpointSurface =>
        """
        GET /bff/address/google-config
        GET /bff/catalog/colors
        GET /bff/catalog/currencies
        GET /bff/catalog/material-groups
        GET /bff/catalog/materials
        GET /bff/catalog/materials/{id:int}
        GET /bff/catalog/materials/{id:int}/colors
        GET /bff/catalog/materials/{id:int}/surface-finishes
        GET /bff/catalog/surface-finishes
        GET /bff/customers
        GET /bff/customers/{customerId:int}/activity
        GET /bff/customers/{customerId:int}/invoices
        GET /bff/customers/{customerId:int}/orders
        GET /bff/customers/{customerId:int}/quotations
        GET /bff/customers/{id:int}
        GET /bff/customers/{id:int}/internal-remark
        GET /bff/dashboard
        GET /bff/diagnostics/events
        GET /bff/employees
        GET /bff/employees/{id:int}
        GET /bff/finances
        GET /bff/finances/create
        GET /bff/finances/summaries/monthly
        GET /bff/finances/summaries/monthly-job-income
        GET /bff/finances/summaries/weekly
        GET /bff/finances/summaries/yearly
        GET /bff/finances/trends/yearly-expense
        GET /bff/finances/trends/yearly-income
        GET /bff/finances/{id:int}
        GET /bff/invoices
        GET /bff/invoices/from-quotation/{quotationId:int}/preview
        GET /bff/invoices/{id:int}
        GET /bff/order-processes
        GET /bff/orders
        GET /bff/orders/create
        GET /bff/orders/create/materials/{materialId:int}
        GET /bff/orders/pending
        GET /bff/orders/{id:int}
        GET /bff/orders/{id:int}/label
        GET /bff/profile
        GET /bff/purchase-orders
        GET /bff/purchase-orders/create-options
        GET /bff/purchase-orders/{id:int}
        GET /bff/quotation-requests
        GET /bff/quotation-requests/{id:int}
        GET /bff/quotations
        GET /bff/quotations/create
        GET /bff/quotations/create/orders
        GET /bff/quotations/stats
        GET /bff/quotations/{id:int}
        GET /bff/session
        GET /bff/suppliers
        GET /bff/suppliers/{id:int}
        POST /bff/catalog/materials
        POST /bff/catalog/materials/{id:int}/colors/{colorId:int}
        POST /bff/catalog/materials/{id:int}/surface-finishes/{surfaceFinishId:int}
        POST /bff/customers
        POST /bff/employee-recovery/email-confirmation/complete
        POST /bff/employee-recovery/password-reset/complete
        POST /bff/employee-recovery/password-reset/request
        POST /bff/employees
        POST /bff/finances
        POST /bff/finances/{id:int}/files
        POST /bff/google
        POST /bff/google/nonce
        POST /bff/invoices/from-quotation/{quotationId:int}
        POST /bff/invoices/{id:int}/receipt
        POST /bff/invoices/{id:int}/receipt/email
        POST /bff/login
        POST /bff/logout
        POST /bff/orders
        POST /bff/orders/{id:int}/files
        POST /bff/orders/{id:int}/status/{statusId:int}
        POST /bff/purchase-orders
        POST /bff/quotations
        POST /bff/suppliers
        PUT /bff/catalog/materials/{id:int}
        PUT /bff/customers/{id:int}
        PUT /bff/customers/{id:int}/internal-remark
        PUT /bff/finances/{id:int}
        PUT /bff/invoices/{id:int}
        PUT /bff/orders/{id:int}
        PUT /bff/profile
        PUT /bff/quotation-requests/{id:int}
        PUT /bff/quotations/{id:int}/decision
        PUT /bff/suppliers/{id:int}
        DELETE /bff/catalog/materials/{id:int}/colors/{colorId:int}
        DELETE /bff/catalog/materials/{id:int}/surface-finishes/{surfaceFinishId:int}
        DELETE /bff/finances/{id:int}
        DELETE /bff/finances/{id:int}/files/{fileId:int}
        DELETE /bff/invoices/{id:int}
        DELETE /bff/invoices/{id:int}/receipt
        DELETE /bff/orders/{id:int}/files/{fileId:int}
        DELETE /bff/purchase-orders/{id:int}
        DELETE /bff/suppliers/{id:int}
        """
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private sealed class AnonymousBffFactory : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
        }
    }

    private sealed class AuthenticatedBffFactory : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPolicyEvaluator>();
                services.AddSingleton<IPolicyEvaluator, AllowAllPolicyEvaluator>();
            });
        }
    }

    private sealed class AllowAllPolicyEvaluator : IPolicyEvaluator
    {
        private static readonly ClaimsPrincipal Principal = new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "http-method-contract-employee"),
            new Claim(ClaimTypes.Name, "HTTP method contract employee"),
            new Claim(ClaimTypes.Role, "Employee"),
        ], "HttpMethodContract"));

        public Task<AuthenticateResult> AuthenticateAsync(AuthorizationPolicy policy, HttpContext context)
        {
            context.User = Principal;
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(Principal, "HttpMethodContract")));
        }

        public Task<PolicyAuthorizationResult> AuthorizeAsync(
            AuthorizationPolicy policy,
            AuthenticateResult authenticationResult,
            HttpContext context,
            object? resource) => Task.FromResult(PolicyAuthorizationResult.Success());
    }
}
