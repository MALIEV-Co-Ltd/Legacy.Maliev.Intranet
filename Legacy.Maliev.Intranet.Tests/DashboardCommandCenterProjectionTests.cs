extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Xml.Linq;
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

public sealed class DashboardCommandCenterProjectionTests
{
    [Fact]
    public async Task GetAsync_OrderPermissionProjectsBoundedRecentProductionRows()
    {
        var handler = new CommandCenterHandler();
        var result = await CreateAggregator(handler).GetAsync(
            Employee(LegacyEmployeePermissions.OrdersRead),
            CancellationToken.None);

        Assert.Equal(2, Assert.Single(result.Cards).Count);
        var first = Assert.Single(result.RecentOrders);
        Assert.Equal(84, first.Id);
        Assert.Equal("Pump bracket", first.Name);
        Assert.Equal(3, first.Remaining);
        Assert.Equal("/sales/orders/84", first.NavigateTo);
        Assert.Empty(result.RecentQuotations);
        Assert.Empty(result.MonthlyFinance);
        Assert.Contains(handler.Paths, path => path.Contains("size=6", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Paths, path => path.StartsWith("/quotations", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(handler.Paths, path => path.StartsWith("/payments", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetAsync_QuotationPermissionProjectsRowsAndServiceOwnedDecisionSummary()
    {
        var handler = new CommandCenterHandler();
        var result = await CreateAggregator(handler).GetAsync(
            Employee(LegacyEmployeePermissions.QuotationsRead),
            CancellationToken.None);

        var quotation = Assert.Single(result.RecentQuotations);
        Assert.Equal(71, quotation.Id);
        Assert.Equal(1070.25m, quotation.Total);
        Assert.Equal("/Quotations/View?id=71", quotation.NavigateTo);
        var summary = Assert.IsType<QuotationStats>(result.QuotationSummary);
        Assert.Equal(4, summary.Accepted);
        Assert.Equal(2, summary.Declined);
        Assert.Equal(3, summary.Open);
        Assert.Contains("/quotations/stats", handler.Paths, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAsync_AccountingPermissionProjectsRealMonthlySummaryAndInvoiceActivity()
    {
        var handler = new CommandCenterHandler();
        var result = await CreateAggregator(handler).GetAsync(
            Employee(LegacyEmployeePermissions.AccountingRead),
            CancellationToken.None);

        var detail = Assert.Single(result.MonthlyFinance);
        Assert.Equal("1", detail.CurrencyId);
        Assert.Equal(125000m, detail.CurrentAmount);
        var activity = Assert.Single(result.RecentActivity);
        Assert.Equal("INV-2030-007", activity.Title);
        Assert.Equal("awaiting-payment", activity.State);
        Assert.Equal("/Invoices/View?id=7", activity.NavigateTo);
        var payment = Assert.Single(result.RecentPayments);
        Assert.Equal(19, payment.Id);
        Assert.Equal(8500m, payment.Amount);
        Assert.Equal("/Finances/View?id=19", payment.NavigateTo);
        Assert.Contains("/payments/summaries/monthly", handler.Paths, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAsync_CustomerPermissionRetainsOriginalRecentCustomerPanelData()
    {
        var handler = new CommandCenterHandler();
        var result = await CreateAggregator(handler).GetAsync(
            Employee(LegacyEmployeePermissions.CustomersList),
            CancellationToken.None);

        var customer = Assert.Single(result.RecentCustomers);
        Assert.Equal(42, customer.Id);
        Assert.Equal("บริษัท ทดสอบ จำกัด", customer.Company);
        Assert.Equal("/Customers/View?id=42", customer.NavigateTo);
        Assert.Contains(handler.Paths, path => path.StartsWith("/Customers?", StringComparison.OrdinalIgnoreCase) && path.Contains("size=6", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_SupplementalFailurePreservesUsableQuotationRowsAndMarksOnlySafeSourceName()
    {
        var handler = new CommandCenterHandler { FailQuotationSummary = true };
        var result = await CreateAggregator(handler).GetAsync(
            Employee(LegacyEmployeePermissions.QuotationsRead),
            CancellationToken.None);

        Assert.Single(result.Cards);
        Assert.Single(result.RecentQuotations);
        Assert.Null(result.QuotationSummary);
        Assert.Equal(["quotations-summary"], result.DegradedSources);
        Assert.DoesNotContain("exception", string.Join('|', result.DegradedSources), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DashboardMarkup_ProvidesOperationalStatesAndAccessibleTables()
    {
        var root = FindRepositoryRoot();
        var dashboard = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Dashboard.razor"));
        var styles = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Dashboard.razor.css"));

        Assert.Contains("IStringLocalizer<Dashboard>", dashboard, StringComparison.Ordinal);
        Assert.Contains("Text[\"ProductionPipeline\"]", dashboard, StringComparison.Ordinal);
        Assert.Contains("Text[\"RecentQuotations\"]", dashboard, StringComparison.Ordinal);
        Assert.Contains("Text[\"FinancialSnapshot\"]", dashboard, StringComparison.Ordinal);
        Assert.Contains("Text[\"QuotationDecisions\"]", dashboard, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"assertive\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("<caption class=\"sr-only\">", dashboard, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 760px)", styles, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("dashboard-eyebrow", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("sparkline", dashboard, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("revenue", dashboard, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DashboardResources_HaveExactEnglishThaiKeyParityAndNaturalThaiCoreCopy()
    {
        var root = FindRepositoryRoot();
        var english = ReadResources(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Dashboard.resx"));
        var thai = ReadResources(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Dashboard.th.resx"));

        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), thai.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Operations command center", english["Heading"]);
        Assert.Equal("ศูนย์ควบคุมการปฏิบัติงาน", thai["Heading"]);
        Assert.Equal("คำขอใบเสนอราคาอยู่ระหว่างรอตรวจสอบ", thai["Activity_quotation-request_awaiting-review"]);
        Assert.All(thai.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    [Fact]
    public void DashboardAggregator_UsesOnlyCultureNeutralWireKeysAndStates()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Dashboard", "LegacyDashboardAggregator.cs"));

        Assert.Contains("\"paid\" : \"awaiting-payment\"", source, StringComparison.Ordinal);
        Assert.Contains("\"completed\" : \"awaiting-review\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoice paid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoice awaiting payment", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Quotation request completed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Quotation request awaiting review", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Order #{item.Id}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Request #{item.Id}", source, StringComparison.Ordinal);
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.Client")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static IReadOnlyDictionary<string, string> ReadResources(string path) =>
        XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")!.Value,
                StringComparer.Ordinal);

    private sealed class CommandCenterHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public bool FailQuotationSummary { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            Paths.Add(path);
            if (FailQuotationSummary && path.Equals("/quotations/stats", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    RequestMessage = request,
                });
            }

            object payload = path switch
            {
                var value when value.StartsWith("/Orders?", StringComparison.Ordinal) => new OrderListPage(
                    [new(84, 10, 3, "Pump bracket", 2, 8, 5, 3, 8500m, new DateTime(2030, 8, 7), false)],
                    1, 1, 2, false, false),
                var value when value.StartsWith("/Customers?", StringComparison.OrdinalIgnoreCase) => new CustomerListPage(
                    [new(42, "สมชาย", "ทดสอบ", "สมชาย ทดสอบ", "customer@example.test", new CustomerCompanyListItem(8, "บริษัท ทดสอบ จำกัด"))],
                    1, 1, 1, false, false),
                var value when value.StartsWith("/quotations?", StringComparison.OrdinalIgnoreCase) => new QuotationListPage(
                    [new(71, 10, 3, null, 14, new DateTime(2030, 8, 14), 1000m, 70.25m, 1070.25m, null, null, 1, null, null, null, null, null, new DateTime(2030, 8, 1), null)],
                    1, 1, 1, false, false),
                "/quotations/stats" => new QuotationStats(4, 2, 3),
                var value when value.StartsWith("/invoices?", StringComparison.OrdinalIgnoreCase) => new InvoiceListPage(
                    [new(7, 10, "INV-2030-007", "THB", null, 1000m, 70m, 1070m, null, 1070m, false, null, null, new DateTime(2030, 8, 1))],
                    1, 1, 1, false, false),
                var value when value.StartsWith("/payments?", StringComparison.OrdinalIgnoreCase) => new FinancePaymentPage(
                    [new(19, 3, 1, 2, "Tooling deposit", 1, 8500m, 1, "MALIEV", "TX-19", new DateTime(2030, 8, 2), new DateTime(2030, 8, 1), null)],
                    1, 1, 5, false, false),
                "/payments/summaries/monthly" => new FinanceSummary([new("1", 125000m, 100000m, 25000m, 25m)]),
                _ => new { items = Array.Empty<object>(), pageIndex = 1, totalPages = 1, totalRecords = 0, hasNextPage = false, hasPreviousPage = false },
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = JsonContent.Create(payload),
            });
        }
    }
}
