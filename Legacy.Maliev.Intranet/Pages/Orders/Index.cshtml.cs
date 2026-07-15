using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Employees;
using Legacy.Maliev.Intranet.Orders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using OrderPageResponse = Legacy.Maliev.Intranet.Orders.PaginatedResponse<Legacy.Maliev.Intranet.Orders.OrderResponse>;

namespace Legacy.Maliev.Intranet.Pages.Orders;

/// <summary>Lists legacy customer orders through migrated service boundaries.</summary>
public sealed class IndexModel(
    ILegacyOrderClient orders,
    ILegacyEmployeeClient employees,
    EmployeeSessionService sessions) : PageModel
{
    private const int PendingWorkingSetSize = 1000;

    /// <summary>Current filtered order page.</summary>
    public OrderPageResponse Results { get; private set; } = new([], 1, 0, 0);
    /// <summary>Pending orders assigned to the signed-in employee.</summary>
    public IReadOnlyList<OrderResponse> AssignedOrders { get; private set; } = [];
    /// <summary>Pending orders that have not been assigned.</summary>
    public IReadOnlyList<OrderResponse> UnassignedOrders { get; private set; } = [];
    /// <summary>Employee names keyed by database identifier.</summary>
    public IReadOnlyDictionary<int, string> EmployeeNames { get; private set; } = new Dictionary<int, string>();
    /// <summary>Process names keyed by identifier.</summary>
    public IReadOnlyDictionary<int, string> ProcessNames { get; private set; } = new Dictionary<int, string>();
    /// <summary>Assigned order table with display labels.</summary>
    public OrderTableModel AssignedTable => new("Orders assigned to the signed-in employee", AssignedOrders, EmployeeNames, ProcessNames);
    /// <summary>Unassigned order table with display labels.</summary>
    public OrderTableModel UnassignedTable => new("Pending orders awaiting assignment", UnassignedOrders, EmployeeNames, ProcessNames);
    /// <summary>Filtered order table with display labels.</summary>
    public OrderTableModel ResultsTable => new("Filtered legacy orders", Results.Items, EmployeeNames, ProcessNames);
    /// <summary>Search text.</summary>
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    /// <summary>Sort order.</summary>
    [BindProperty(SupportsGet = true)] public OrderSortType Sort { get; set; } = OrderSortType.OrderCreatedDate_Descending;
    /// <summary>One-based page index.</summary>
    [BindProperty(SupportsGet = true)] public int Index { get; set; } = 1;
    /// <summary>Bounded page size.</summary>
    [BindProperty(SupportsGet = true)] public int Size { get; set; } = 25;

    /// <summary>Loads orders, pending work and display labels with one employee token.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Index = Math.Max(1, Index);
        Size = Math.Clamp(Size, 1, 250);
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");

        var resultsTask = orders.GetOrdersAsync(Sort, Search, Index, Size, token, cancellationToken);
        var pendingTask = orders.GetPendingOrdersAsync(PendingWorkingSetSize, token, cancellationToken);
        var processesTask = orders.GetProcessesAsync(token, cancellationToken);
        var employeesTask = employees.GetEmployeesAsync(EmployeeSortType.EmployeeId_Ascending, null, 1, 250, token, cancellationToken);
        await Task.WhenAll(resultsTask, pendingTask, processesTask, employeesTask);

        Results = await resultsTask ?? new([], Index, 0, 0);
        var pending = (await pendingTask)?.Items ?? [];
        var employeeItems = (await employeesTask)?.Items ?? [];
        EmployeeNames = employeeItems.ToDictionary(value => value.Id, value => value.FullName);
        ProcessNames = (await processesTask).ToDictionary(value => value.Id, value => value.Name);

        var signedInEmail = User.FindFirstValue(ClaimTypes.Email);
        var employeeId = employeeItems.FirstOrDefault(value =>
            string.Equals(value.Email, signedInEmail, StringComparison.OrdinalIgnoreCase))?.Id;
        AssignedOrders = employeeId is null
            ? []
            : pending.Where(value => value.EmployeeId == employeeId).ToArray();
        UnassignedOrders = pending.Where(value => value.EmployeeId is null).ToArray();
        return Page();
    }
}
