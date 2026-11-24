extern alias Bff;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using BffAccounting = Bff::Legacy.Maliev.Intranet.Bff.Accounting;
using BffCatalog = Bff::Legacy.Maliev.Intranet.Bff.Catalog;
using BffCustomers = Bff::Legacy.Maliev.Intranet.Bff.Customers;
using BffDashboard = Bff::Legacy.Maliev.Intranet.Bff.Dashboard.LegacyDashboardAggregator;
using BffEmployees = Bff::Legacy.Maliev.Intranet.Bff.Employees;
using BffOrders = Bff::Legacy.Maliev.Intranet.Bff.Orders;
using BffProcurement = Bff::Legacy.Maliev.Intranet.Bff.Procurement;
using BffQuotations = Bff::Legacy.Maliev.Intranet.Bff.Quotations;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class DashboardCatalogCurrencyTests
{
    [Fact]
    public async Task NullCatalogCurrencyDegradesOnlyCurrencies()
    {
        using var handler = new CatalogDashboardHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://dashboard.example.test") };
        var aggregator = new BffDashboard(
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
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("permissions", LegacyEmployeePermissions.CustomersList),
            new Claim("permissions", LegacyEmployeePermissions.QuotationsRead),
        ], "Synthetic"));

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await aggregator.GetAsync(principal, deadline.Token);

        Assert.Equal(["currencies"], result.DegradedSources);
        Assert.Empty(result.CurrencyCodes);
        var customers = Assert.Single(result.Cards, card => card.Key == "customers");
        Assert.Equal(1, customers.Count);
        Assert.Equal("/customers", customers.NavigateTo);
        Assert.Single(result.RecentCustomers);
        Assert.Equal(new[]
        {
            "GET " + CatalogDashboardHandler.Currencies,
            "GET " + CatalogDashboardHandler.Customers,
            "GET " + CatalogDashboardHandler.Quotes,
            "GET " + CatalogDashboardHandler.QuoteCounts,
        }.Order(StringComparer.Ordinal).ToArray(), handler.Requests.Order(StringComparer.Ordinal).ToArray());
    }

    private sealed class CatalogDashboardHandler : HttpMessageHandler
    {
        public const string Currencies = "/Currencies";
        public const string Customers = "/customers?sort=CustomerCreatedDate_Descending&search=&index=1&size=6";
        public const string Quotes = "/quotations?sort=QuotationCreatedDate_Descending&search=&index=1&size=6";
        public const string QuoteCounts = "/quotations/stats";

        public ConcurrentQueue<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Enqueue($"{request.Method} {uri.PathAndQuery}");
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://dashboard.example.test", uri.GetLeftPart(UriPartial.Authority));
            HttpContent content = uri.PathAndQuery switch
            {
                Currencies => new StringContent("""[{"id":1,"shortName":"SYN"},null]""", Encoding.UTF8, "application/json"),
                Customers => JsonContent.Create(new CustomerListPage(
                    [new CustomerListItem(1, "Synthetic", "Customer", "Synthetic Customer", "", null)],
                    1, 1, 1, false, false)),
                Quotes => JsonContent.Create(new QuotationListPage([], 1, 1, 0, false, false)),
                QuoteCounts => JsonContent.Create(new QuotationStats(0, 0, 0)),
                _ => throw new InvalidOperationException($"Unexpected synthetic request: {uri.PathAndQuery}"),
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
