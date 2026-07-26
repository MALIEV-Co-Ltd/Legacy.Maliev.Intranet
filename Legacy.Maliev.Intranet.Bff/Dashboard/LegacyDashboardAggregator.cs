using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Bff.Accounting;
using Legacy.Maliev.Intranet.Bff.Catalog;
using Legacy.Maliev.Intranet.Bff.Customers;
using Legacy.Maliev.Intranet.Bff.Employees;
using Legacy.Maliev.Intranet.Bff.Orders;
using Legacy.Maliev.Intranet.Bff.Procurement;
using Legacy.Maliev.Intranet.Bff.Quotations;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Dashboard;

/// <summary>
/// Builds the employee dashboard from bounded legacy service projections.
/// This class contains orchestration only: all business data remains owned by
/// the downstream legacy services.
/// </summary>
public sealed class LegacyDashboardAggregator
{
    private readonly OrdersProxy orders;
    private readonly CustomersProxy customers;
    private readonly EmployeesProxy employees;
    private readonly InvoicesProxy invoices;
    private readonly QuotationRequestsProxy quotationRequests;
    private readonly QuotationsProxy quotations;
    private readonly SuppliersProxy suppliers;
    private readonly PurchaseOrdersProxy purchaseOrders;
    private readonly CatalogMaterialsProxy materials;
    private readonly FinancesProxy finances;
    private readonly TimeProvider timeProvider;
    private readonly LegacyDashboardSource[] sources;

    /// <summary>Creates a dashboard aggregator from the server-side legacy service proxies.</summary>
    public LegacyDashboardAggregator(
        OrdersProxy orders,
        CustomersProxy customers,
        EmployeesProxy employees,
        InvoicesProxy invoices,
        QuotationRequestsProxy quotationRequests,
        QuotationsProxy quotations,
        SuppliersProxy suppliers,
        PurchaseOrdersProxy purchaseOrders,
        CatalogMaterialsProxy materials,
        FinancesProxy finances,
        TimeProvider timeProvider)
    {
        this.orders = orders;
        this.customers = customers;
        this.employees = employees;
        this.invoices = invoices;
        this.quotationRequests = quotationRequests;
        this.quotations = quotations;
        this.suppliers = suppliers;
        this.purchaseOrders = purchaseOrders;
        this.materials = materials;
        this.finances = finances;
        this.timeProvider = timeProvider;

        sources =
        [
        new(
            LegacyEmployeePermissions.OrdersRead,
            "orders",
            "Orders",
            "/sales/orders",
            async (ct, state) => await CountAsync(
                "orders",
                "Orders",
                "/sales/orders",
                () => orders.GetAsync(OrderListSort.OrderCreatedDate_Descending, null, 1, 1, ct),
                response => response.Content.ReadFromJsonAsync<OrderListPage>(ct),
                page => page?.TotalRecords ?? 0,
                state)),
        new(
            LegacyEmployeePermissions.CustomersList,
            "customers",
            "Customers",
            "/customers",
            async (ct, state) => await CountAsync(
                "customers",
                "Customers",
                "/customers",
                () => customers.GetAsync(CustomerListSort.CustomerCreatedDate_Descending, null, 1, 1, ct),
                response => response.Content.ReadFromJsonAsync<CustomerListPage>(ct),
                page => page?.TotalRecords ?? 0,
                state)),
        new(
            LegacyEmployeePermissions.EmployeesList,
            "employees",
            "Employees",
            "/Employees/Index",
            async (ct, state) => await CountAsync(
                "employees",
                "Employees",
                "/Employees/Index",
                () => employees.GetAsync(EmployeeListSort.EmployeeId_Descending, null, 1, 1, ct),
                response => response.Content.ReadFromJsonAsync<EmployeeListPage>(ct),
                page => page?.TotalRecords ?? 0,
                state)),
        new(
            LegacyEmployeePermissions.AccountingRead,
            "invoices",
            "Invoices",
            "/accounting",
            async (ct, state) => await CountAsync(
                "invoices",
                "Invoices",
                "/accounting",
                () => invoices.GetPageAsync(InvoiceListSort.InvoiceCreatedDate_Descending, null, 1, 1, false, ct),
                response => response.Content.ReadFromJsonAsync<InvoiceListPage>(ct),
                page => page?.TotalRecords ?? 0,
                state)),
        new(
            LegacyEmployeePermissions.QuotationRequestsRead,
            "quotation-requests",
            "Quotation requests",
            "/QuotationRequests/Index",
            async (ct, state) => await CountAsync(
                "quotation-requests",
                "Quotation requests",
                "/QuotationRequests/Index",
                () => quotationRequests.GetPageAsync(QuotationRequestSort.RequestCreatedDate_Descending, null, 1, 1, ct),
                response => response.Content.ReadFromJsonAsync<QuotationRequestPage>(ct),
                page => page?.TotalRecords ?? 0,
                state)),
        new(
            LegacyEmployeePermissions.QuotationsRead,
            "quotations",
            "Quotations",
            "/Quotations/Index",
            async (ct, state) => await CountAsync(
                "quotations",
                "Quotations",
                "/Quotations/Index",
                () => quotations.GetPageAsync(QuotationListSort.QuotationCreatedDate_Descending, null, 1, 1, ct),
                response => response.Content.ReadFromJsonAsync<QuotationListPage>(ct),
                page => page?.TotalRecords ?? 0,
                state)),
        new(
            LegacyEmployeePermissions.SuppliersRead,
            "suppliers",
            "Suppliers",
            "/purchasing/suppliers",
            async (ct, state) => await CountAsync(
                "suppliers",
                "Suppliers",
                "/purchasing/suppliers",
                () => suppliers.GetAsync(SupplierListSort.SupplierId_Descending, null, 1, 1, ct),
                response => response.Content.ReadFromJsonAsync<SupplierListPage>(ct),
                page => page?.TotalRecords ?? 0,
                state)),
        new(
            LegacyEmployeePermissions.PurchaseOrdersRead,
            "purchase-orders",
            "Purchase orders",
            "/purchasing",
            async (ct, state) => await CountAsync(
                "purchase-orders",
                "Purchase orders",
                "/purchasing",
                () => purchaseOrders.GetAsync(PurchaseOrderListSort.PurchaseOrderCreatedDate_Descending, null, 1, 1, ct),
                response => response.Content.ReadFromJsonAsync<PurchaseOrderListPage>(ct),
                page => page?.TotalRecords ?? 0,
                state)),
        new(
            LegacyEmployeePermissions.CatalogMaterialsRead,
            "materials",
            "Materials",
            "/mfg/materials",
            async (ct, state) => await CountAsync(
                "materials",
                "Materials",
                "/mfg/materials",
                () => materials.GetAsync(CatalogMaterialSort.MaterialId_Descending, null, 1, 1, ct),
                response => response.Content.ReadFromJsonAsync<CatalogMaterialPage>(ct),
                page => page?.TotalRecords ?? 0,
                state)),
        new(
            LegacyEmployeePermissions.AccountingRead,
            "payments",
            "Finance records",
            "/Finances/Index",
            async (ct, state) => await CountAsync(
                "payments",
                "Finance records",
                "/Finances/Index",
                () => finances.GetPageAsync(FinancePaymentSort.PaymentCreatedDate_Descending, null, 1, 1, ct),
                response => response.Content.ReadFromJsonAsync<FinancePaymentPage>(ct),
                page => page?.TotalRecords ?? 0,
                state)),
        ];
    }

    /// <summary>Returns only cards for permissions present on the current employee session.</summary>
    public async Task<LegacyDashboardSnapshot> GetAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return new(timeProvider.GetUtcNow(), [], ["session"]);
        }

        var allowed = user.FindAll("permissions")
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);
        var state = new DashboardState();
        var work = sources
            .Where(source => allowed.Contains(source.Permission))
            .Select(source => source.Fetch(cancellationToken, state))
            .ToArray();

        await Task.WhenAll(work);

        var cards = work
            .Select(task => task.Result)
            .Where(result => result is not null)
            .Select(result => result!.ToCard())
            .ToArray();

        return new(timeProvider.GetUtcNow(), cards, state.DegradedSources.Order(StringComparer.Ordinal).ToArray());
    }

    private static async Task<DashboardCardResult?> CountAsync<TPage>(
        string key,
        string label,
        string navigateTo,
        Func<Task<HttpResponseMessage>> send,
        Func<HttpResponseMessage, Task<TPage?>> deserialize,
        Func<TPage?, int> count,
        DashboardState state)
    {
        try
        {
            using var response = await send();
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                state.MarkDegraded(response.RequestMessage?.RequestUri?.AbsolutePath ?? "downstream");
                return null;
            }

            var page = await deserialize(response);
            return page is null ? null : new DashboardCardResult(key, label, navigateTo, count(page));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            state.MarkDegraded("downstream");
            return null;
        }
    }

    private sealed record LegacyDashboardSource(
        string Permission,
        string Key,
        string Label,
        string NavigateTo,
        Func<CancellationToken, DashboardState, Task<DashboardCardResult?>> Fetch);

    private sealed record DashboardCardResult(string Key, string Label, string NavigateTo, int Count)
    {
        public LegacyDashboardCard ToCard() => new(Key, Label, Count, NavigateTo);
    }

    private sealed class DashboardState
    {
        private readonly object gate = new();
        private readonly HashSet<string> degradedSources = new(StringComparer.Ordinal);

        public IReadOnlySet<string> DegradedSources
        {
            get
            {
                lock (gate)
                {
                    return degradedSources.ToHashSet(StringComparer.Ordinal);
                }
            }
        }

        public void MarkDegraded(string source)
        {
            lock (gate)
            {
                degradedSources.Add(source);
            }
        }
    }
}
