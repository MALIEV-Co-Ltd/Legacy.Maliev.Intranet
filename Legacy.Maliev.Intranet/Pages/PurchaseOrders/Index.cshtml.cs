using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Employees;
using Legacy.Maliev.Intranet.Suppliers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Intranet.Pages.PurchaseOrders;

/// <summary>Lists purchase orders through the migrated service boundaries.</summary>
public sealed class IndexModel(ILegacyProcurementClient procurement, ILegacyEmployeeClient employees, EmployeeSessionService sessions) : PageModel
{
    /// <summary>Purchase-order results.</summary>
    public Legacy.Maliev.Intranet.Suppliers.PaginatedResponse<PurchaseOrderResponse> Results { get; private set; } = new([], 1, 0, 0);
    /// <summary>Employee names keyed by identifier.</summary>
    public IReadOnlyDictionary<int, string> EmployeeNames { get; private set; } = new Dictionary<int, string>();
    /// <summary>Search text.</summary>
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    /// <summary>Sort order.</summary>
    [BindProperty(SupportsGet = true)] public PurchaseOrderSortType Sort { get; set; } = PurchaseOrderSortType.PurchaseOrderId_Descending;
    /// <summary>Page index.</summary>
    [BindProperty(SupportsGet = true)] public int Index { get; set; } = 1;
    /// <summary>Page size.</summary>
    [BindProperty(SupportsGet = true)] public int Size { get; set; } = 25;

    /// <summary>Loads purchase orders and employee display names.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Index = Math.Max(1, Index); Size = Math.Clamp(Size, 1, 250);
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken); if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        var ordersTask = procurement.GetPurchaseOrdersAsync(Sort, Search, Index, Size, token, cancellationToken);
        var employeesTask = employees.GetEmployeesAsync(EmployeeSortType.EmployeeId_Ascending, null, 1, 250, token, cancellationToken);
        await Task.WhenAll(ordersTask, employeesTask);
        Results = await ordersTask ?? new([], Index, 0, 0);
        EmployeeNames = (await employeesTask)?.Items.ToDictionary(value => value.Id, value => value.FullName) ?? new Dictionary<int, string>();
        return Page();
    }
}