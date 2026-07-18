namespace Legacy.Maliev.Intranet;

/// <summary>Canonical route inventory preserved from the private legacy Intranet.</summary>
public static class LegacyRoutes
{
    /// <summary>Gets every historical Razor Page route, including anonymous routes.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "/AccessDenied",
        "/Customers/Create",
        "/Customers/Index",
        "/Customers/View",
        "/Dashboard",
        "/Employees/Create",
        "/Employees/EmailConfirmation",
        "/Employees/ForgotPassword",
        "/Employees/Index",
        "/Employees/ResetPassword",
        "/Employees/View",
        "/Finances/Create",
        "/Finances/Index",
        "/Finances/NetProfitChart",
        "/Finances/View",
        "/Finances/YearlyActivityChart",
        "/Index",
        "/Invoices/Create",
        "/Invoices/Index",
        "/Invoices/View",
        "/Login",
        "/Materials/Create",
        "/Materials/Index",
        "/Materials/View",
        "/Orders/Create",
        "/Orders/Index",
        "/Orders/View",
        "/PurchaseOrders/Create",
        "/PurchaseOrders/Index",
        "/PurchaseOrders/View",
        "/QuotationRequests/Index",
        "/QuotationRequests/View",
        "/Quotations/Create",
        "/Quotations/Estimate",
        "/Quotations/Index",
        "/Quotations/View",
        "/Suppliers/Create",
        "/Suppliers/Index",
        "/Suppliers/View",
        "/Travelers/Create",
        "/Travelers/Index",
    ];

    /// <summary>Routes that can be reached before an employee session exists.</summary>
    public static IReadOnlySet<string> Anonymous { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "/Index",
        "/Login",
        "/Employees/EmailConfirmation",
        "/Employees/ForgotPassword",
        "/Employees/ResetPassword",
    };

    /// <summary>Historical routes proven to have no implementation or owning service contract.</summary>
    public static IReadOnlySet<string> Retired { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "/Travelers/Create",
        "/Travelers/Index",
    };

    /// <summary>Historical routes that still have an implementation or a migration candidate contract.</summary>
    public static IReadOnlyList<string> ActiveMigrationCandidates { get; } = All.Where(route => !Retired.Contains(route)).ToArray();
}
