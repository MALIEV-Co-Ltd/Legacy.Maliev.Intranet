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
            "/sales/orders",
            async (ct, state) => await CountAsync(
                "orders",
                "/sales/orders",
                () => orders.GetAsync(OrderListSort.OrderCreatedDate_Descending, null, 1, 6, ct),
                response => response.Content.ReadFromJsonAsync<OrderListPage>(ct),
                page => page?.TotalRecords ?? 0,
                state,
                (page, dashboard) => dashboard.SetRecentOrders(page?.Items ?? []))),
        new(
            LegacyEmployeePermissions.CustomersList,
            "customers",
            "/customers",
            async (ct, state) => await CountAsync(
                "customers",
                "/customers",
                () => customers.GetAsync(CustomerListSort.CustomerCreatedDate_Descending, null, 1, 6, ct),
                response => response.Content.ReadFromJsonAsync<CustomerListPage>(ct),
                page => page?.TotalRecords ?? 0,
                state,
                (page, dashboard) => dashboard.SetRecentCustomers(page?.Items ?? []))),
        new(
            LegacyEmployeePermissions.EmployeesList,
            "employees",
            "/Employees/Index",
            async (ct, state) => await CountAsync(
                "employees",
                "/Employees/Index",
                () => employees.GetAsync(EmployeeListSort.EmployeeId_Descending, null, 1, 1, ct),
                response => response.Content.ReadFromJsonAsync<EmployeeListPage>(ct),
                page => page?.TotalRecords ?? 0,
                state)),
        new(
            LegacyEmployeePermissions.AccountingRead,
            "invoices",
            "/accounting",
            async (ct, state) => await CountAsync(
                "invoices",
                "/accounting",
                () => invoices.GetPageAsync(InvoiceListSort.InvoiceCreatedDate_Descending, null, 1, 5, false, ct),
                response => response.Content.ReadFromJsonAsync<InvoiceListPage>(ct),
                page => page?.TotalRecords ?? 0,
                state,
                (page, dashboard) => dashboard.AddInvoiceActivity(page?.Items ?? []))),
        new(
            LegacyEmployeePermissions.QuotationRequestsRead,
            "quotation-requests",
            "/QuotationRequests/Index",
            async (ct, state) => await CountAsync(
                "quotation-requests",
                "/QuotationRequests/Index",
                () => quotationRequests.GetPageAsync(QuotationRequestSort.RequestCreatedDate_Descending, null, 1, 5, ct),
                response => response.Content.ReadFromJsonAsync<QuotationRequestPage>(ct),
                page => page?.TotalRecords ?? 0,
                state,
                (page, dashboard) => dashboard.AddQuotationRequestActivity(page?.Items ?? []))),
        new(
            LegacyEmployeePermissions.QuotationsRead,
            "quotations",
            "/Quotations/Index",
            async (ct, state) => await CountAsync(
                "quotations",
                "/Quotations/Index",
                () => quotations.GetPageAsync(QuotationListSort.QuotationCreatedDate_Descending, null, 1, 6, ct),
                response => response.Content.ReadFromJsonAsync<QuotationListPage>(ct),
                page => page?.TotalRecords ?? 0,
                state,
                (page, dashboard) => dashboard.SetRecentQuotations(page?.Items ?? []))),
        new(
            LegacyEmployeePermissions.SuppliersRead,
            "suppliers",
            "/purchasing/suppliers",
            async (ct, state) => await CountAsync(
                "suppliers",
                "/purchasing/suppliers",
                () => suppliers.GetAsync(SupplierListSort.SupplierId_Descending, null, 1, 1, ct),
                response => response.Content.ReadFromJsonAsync<SupplierListPage>(ct),
                page => page?.TotalRecords ?? 0,
                state)),
        new(
            LegacyEmployeePermissions.PurchaseOrdersRead,
            "purchase-orders",
            "/purchasing",
            async (ct, state) => await CountAsync(
                "purchase-orders",
                "/purchasing",
                () => purchaseOrders.GetAsync(PurchaseOrderListSort.PurchaseOrderCreatedDate_Descending, null, 1, 1, ct),
                response => response.Content.ReadFromJsonAsync<PurchaseOrderListPage>(ct),
                page => page?.TotalRecords ?? 0,
                state)),
        new(
            LegacyEmployeePermissions.CatalogMaterialsRead,
            "materials",
            "/mfg/materials",
            async (ct, state) => await CountAsync(
                "materials",
                "/mfg/materials",
                () => materials.GetAsync(CatalogMaterialSort.MaterialId_Descending, null, 1, 1, ct),
                response => response.Content.ReadFromJsonAsync<CatalogMaterialPage>(ct),
                page => page?.TotalRecords ?? 0,
                state)),
        new(
            LegacyEmployeePermissions.AccountingRead,
            "payments",
            "/Finances/Index",
            async (ct, state) => await CountAsync(
                "payments",
                "/Finances/Index",
                () => finances.GetPageAsync(FinancePaymentSort.PaymentCreatedDate_Descending, null, 1, 6, ct),
                response => response.Content.ReadFromJsonAsync<FinancePaymentPage>(ct),
                page => page?.TotalRecords ?? 0,
                state,
                (page, dashboard) => dashboard.SetRecentPayments(page?.Items ?? []))),
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
        var supplementalWork = new List<Task>();
        if (allowed.Contains(LegacyEmployeePermissions.QuotationsRead))
        {
            supplementalWork.Add(LoadQuotationSummaryAsync(cancellationToken, state));
        }

        if (allowed.Contains(LegacyEmployeePermissions.AccountingRead))
        {
            supplementalWork.Add(LoadMonthlyFinanceAsync(cancellationToken, state));
        }

        await Task.WhenAll(work.Cast<Task>().Concat(supplementalWork));

        var cards = work
            .Select(task => task.Result)
            .Where(result => result is not null)
            .Select(result => result!.ToCard())
            .ToArray();

        return new(timeProvider.GetUtcNow(), cards, state.DegradedSources.Order(StringComparer.Ordinal).ToArray())
        {
            RecentOrders = state.RecentOrders,
            RecentQuotations = state.RecentQuotations,
            RecentCustomers = state.RecentCustomers,
            RecentPayments = state.RecentPayments,
            RecentActivity = state.RecentActivity,
            QuotationSummary = state.QuotationSummary,
            MonthlyFinance = state.MonthlyFinance,
        };
    }

    private static async Task<DashboardCardResult?> CountAsync<TPage>(
        string key,
        string navigateTo,
        Func<Task<HttpResponseMessage>> send,
        Func<HttpResponseMessage, Task<TPage?>> deserialize,
        Func<TPage?, int> count,
        DashboardState state,
        Action<TPage?, DashboardState>? project = null)
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
            project?.Invoke(page, state);
            return page is null ? null : new DashboardCardResult(key, navigateTo, count(page));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            state.MarkDegraded("downstream");
            return null;
        }
    }

    private async Task LoadQuotationSummaryAsync(CancellationToken cancellationToken, DashboardState state)
    {
        try
        {
            using var response = await quotations.GetStatsAsync(cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                state.MarkDegraded("quotations-summary");
                return;
            }

            var summary = await response.Content.ReadFromJsonAsync<QuotationStats>(cancellationToken);
            if (summary is null || summary.Accepted < 0 || summary.Declined < 0 || summary.Open < 0)
            {
                state.MarkDegraded("quotations-summary");
                return;
            }

            state.SetQuotationSummary(summary);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            state.MarkDegraded("quotations-summary");
        }
    }

    private async Task LoadMonthlyFinanceAsync(CancellationToken cancellationToken, DashboardState state)
    {
        try
        {
            using var response = await finances.GetSummaryAsync("/payments/summaries/monthly", cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                state.MarkDegraded("finance-summary");
                return;
            }

            var summary = await response.Content.ReadFromJsonAsync<FinanceSummary>(cancellationToken);
            if (summary?.Details is null || summary.Details.Any(detail => string.IsNullOrWhiteSpace(detail.CurrencyId)))
            {
                state.MarkDegraded("finance-summary");
                return;
            }

            state.SetMonthlyFinance(summary.Details);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            state.MarkDegraded("finance-summary");
        }
    }

    private sealed record LegacyDashboardSource(
        string Permission,
        string Key,
        string NavigateTo,
        Func<CancellationToken, DashboardState, Task<DashboardCardResult?>> Fetch);

    private sealed record DashboardCardResult(string Key, string NavigateTo, int Count)
    {
        public LegacyDashboardCard ToCard() => new(Key, Count, NavigateTo);
    }

    private sealed class DashboardState
    {
        private readonly object gate = new();
        private readonly HashSet<string> degradedSources = new(StringComparer.Ordinal);
        private IReadOnlyList<LegacyDashboardOrder> recentOrders = [];
        private IReadOnlyList<LegacyDashboardQuotation> recentQuotations = [];
        private IReadOnlyList<LegacyDashboardCustomer> recentCustomers = [];
        private IReadOnlyList<LegacyDashboardPayment> recentPayments = [];
        private readonly List<LegacyDashboardActivity> recentActivity = [];
        private QuotationStats? quotationSummary;
        private IReadOnlyList<FinanceSummaryDetail> monthlyFinance = [];

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

        public IReadOnlyList<LegacyDashboardOrder> RecentOrders
        {
            get { lock (gate) { return recentOrders.ToArray(); } }
        }

        public IReadOnlyList<LegacyDashboardQuotation> RecentQuotations
        {
            get { lock (gate) { return recentQuotations.ToArray(); } }
        }

        public IReadOnlyList<LegacyDashboardCustomer> RecentCustomers
        {
            get { lock (gate) { return recentCustomers.ToArray(); } }
        }

        public IReadOnlyList<LegacyDashboardPayment> RecentPayments
        {
            get { lock (gate) { return recentPayments.ToArray(); } }
        }

        public IReadOnlyList<LegacyDashboardActivity> RecentActivity
        {
            get
            {
                lock (gate)
                {
                    return recentActivity
                        .OrderByDescending(activity => activity.OccurredAt)
                        .Take(6)
                        .ToArray();
                }
            }
        }

        public QuotationStats? QuotationSummary
        {
            get { lock (gate) { return quotationSummary; } }
        }

        public IReadOnlyList<FinanceSummaryDetail> MonthlyFinance
        {
            get { lock (gate) { return monthlyFinance.ToArray(); } }
        }

        public void MarkDegraded(string source)
        {
            lock (gate)
            {
                degradedSources.Add(source);
            }
        }

        public void SetRecentOrders(IReadOnlyList<OrderListItem> items)
        {
            lock (gate)
            {
                recentOrders = items.Take(6).Select(item => new LegacyDashboardOrder(
                    item.Id,
                    item.Name,
                    item.Quantity,
                    item.Manufactured,
                    item.Remaining,
                    item.PromisedDate,
                    $"/sales/orders/{item.Id}"))
                    .ToArray();
            }
        }

        public void SetRecentQuotations(IReadOnlyList<QuotationListItem> items)
        {
            lock (gate)
            {
                recentQuotations = items.Take(6).Select(item => new LegacyDashboardQuotation(
                    item.Id,
                    item.Total,
                    item.QuotedAmount,
                    item.CurrencyId,
                    item.ExpirationDate,
                    item.Accepted,
                    item.CreatedDate,
                    $"/Quotations/View?id={item.Id}"))
                    .ToArray();
            }
        }

        public void SetRecentCustomers(IReadOnlyList<CustomerListItem> items)
        {
            lock (gate)
            {
                recentCustomers = items.Take(6).Select(item => new LegacyDashboardCustomer(
                    item.Id,
                    item.FullName,
                    item.Email,
                    item.Company?.Name,
                    $"/Customers/View?id={item.Id}"))
                    .ToArray();
            }
        }

        public void SetRecentPayments(IReadOnlyList<FinancePaymentItem> items)
        {
            lock (gate)
            {
                recentPayments = items.Take(6).Select(item => new LegacyDashboardPayment(
                    item.Id,
                    item.Amount,
                    item.CurrencyId,
                    item.Recipient,
                    item.PaymentDate,
                    item.CreatedDate,
                    $"/Finances/View?id={item.Id}"))
                    .ToArray();
            }
        }

        public void AddInvoiceActivity(IReadOnlyList<InvoiceListItem> items)
        {
            lock (gate)
            {
                recentActivity.AddRange(items.Take(4).Select(item => new LegacyDashboardActivity(
                    "invoice",
                    item.Number,
                    item.IsPaid ? "paid" : "awaiting-payment",
                    item.CreatedDate,
                    $"/Invoices/View?id={item.Id}")));
            }
        }

        public void AddQuotationRequestActivity(IReadOnlyList<QuotationRequestItem> items)
        {
            lock (gate)
            {
                recentActivity.AddRange(items.Take(4).Select(item => new LegacyDashboardActivity(
                    "quotation-request",
                    ActivityTitle(item),
                    item.Done == true ? "completed" : "awaiting-review",
                    item.CreatedDate,
                    $"/QuotationRequests/View?id={item.Id}")));
            }
        }

        public void SetQuotationSummary(QuotationStats summary)
        {
            lock (gate) { quotationSummary = summary; }
        }

        public void SetMonthlyFinance(IReadOnlyList<FinanceSummaryDetail> details)
        {
            lock (gate) { monthlyFinance = details.ToArray(); }
        }

        private static string? ActivityTitle(QuotationRequestItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.CompanyName))
            {
                return item.CompanyName;
            }

            var name = $"{item.FirstName} {item.LastName}".Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
    }
}
