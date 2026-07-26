extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using BffDashboard = Bff::Legacy.Maliev.Intranet.Bff.Dashboard.LegacyDashboardAggregator;
using BffAccounting = Bff::Legacy.Maliev.Intranet.Bff.Accounting;
using BffCatalog = Bff::Legacy.Maliev.Intranet.Bff.Catalog;
using BffCustomers = Bff::Legacy.Maliev.Intranet.Bff.Customers;
using BffEmployees = Bff::Legacy.Maliev.Intranet.Bff.Employees;
using BffOrders = Bff::Legacy.Maliev.Intranet.Bff.Orders;
using BffProcurement = Bff::Legacy.Maliev.Intranet.Bff.Procurement;
using BffQuotations = Bff::Legacy.Maliev.Intranet.Bff.Quotations;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class DashboardAggregatorTests
{
    [Fact]
    public async Task GetAsync_ProjectsOnlyPermissionScopedCounts()
    {
        var handler = new DashboardHandler();
        var aggregator = CreateAggregator(handler);
        var principal = Employee(LegacyEmployeePermissions.OrdersRead);

        var result = await aggregator.GetAsync(principal, CancellationToken.None);

        var card = Assert.Single(result.Cards);
        Assert.Equal("orders", card.Key);
        Assert.Equal(12, card.Count);
        Assert.Equal("/sales/orders", card.NavigateTo);
        Assert.Empty(result.DegradedSources);
        Assert.Single(handler.Paths);
        Assert.StartsWith("/Orders", handler.Paths[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAsync_IsolatesDownstreamFailureAndDoesNotLeakRawResponse()
    {
        var handler = new DashboardHandler { FailOrders = true };
        var aggregator = CreateAggregator(handler);
        var principal = Employee(LegacyEmployeePermissions.OrdersRead);

        var result = await aggregator.GetAsync(principal, CancellationToken.None);

        Assert.Empty(result.Cards);
        Assert.NotEmpty(result.DegradedSources);
        Assert.DoesNotContain("status", string.Join('|', result.DegradedSources), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", string.Join('|', result.DegradedSources), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAsync_AnonymousSessionReturnsNoCards()
    {
        var aggregator = CreateAggregator(new DashboardHandler());

        var result = await aggregator.GetAsync(new ClaimsPrincipal(new ClaimsIdentity()), CancellationToken.None);

        Assert.Empty(result.Cards);
        Assert.Equal(["session"], result.DegradedSources);
    }

    private static ClaimsPrincipal Employee(params string[] permissions) =>
        new(new ClaimsIdentity(
            permissions.Select(permission => new Claim("permissions", permission)),
            "Cookies",
            ClaimTypes.Name,
            ClaimTypes.Role));

    private static BffDashboard CreateAggregator(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://legacy.test") };
        return new(
            new BffOrders.OrdersProxy(http),
            new BffCustomers.CustomersProxy(http),
            new BffEmployees.EmployeesProxy(http),
            new BffAccounting.InvoicesProxy(http),
            new BffQuotations.QuotationRequestsProxy(http),
            new BffQuotations.QuotationsProxy(http),
            new BffProcurement.SuppliersProxy(http),
            new BffProcurement.PurchaseOrdersProxy(http),
            new BffCatalog.CatalogMaterialsProxy(http),
            new BffAccounting.FinancesProxy(http),
            TimeProvider.System);
    }

    private sealed class DashboardHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public bool FailOrders { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            Paths.Add(path);
            if (FailOrders && path.StartsWith("/Orders", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    RequestMessage = request,
                });
            }

            var page = new
            {
                items = Array.Empty<object>(),
                pageIndex = 1,
                totalPages = 1,
                totalRecords = 12,
                hasNextPage = false,
                hasPreviousPage = false,
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = JsonContent.Create(page),
            });
        }
    }
}
