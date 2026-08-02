using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace Legacy.Maliev.Intranet.Client.Layout;

/// <summary>
/// Navigation groups that are backed by the migrated legacy workflows.
///
/// The primary/overflow split intentionally follows the current workspace
/// shell. It only contains routes with a proven legacy service contract.
/// </summary>
internal static class LegacyAppNavigation
{
    public static IReadOnlyList<LegacyNavGroup> Groups { get; } =
    [
        new("Sales", Icons.Material.Outlined.Business,
        [
            new("Dashboard", "/Dashboard", Icons.Material.Outlined.Dashboard, Description: "Workspace overview"),
            new("Customers", "/customers", Icons.Material.Outlined.People, RequiredPermission: "legacy-customer.customers.list", Description: "Customer profiles"),
            new("New customer", "/customers/new", Icons.Material.Outlined.PersonAdd, RequiredPermission: "legacy-customer.customers.create", Description: "Onboard a customer"),
            new("Orders", "/sales/orders", Icons.Material.Outlined.ReceiptLong, RequiredPermission: "legacy.orders.read", Description: "Order queue and status"),
            new("New order", "/Orders/Create", Icons.Material.Outlined.AddShoppingCart, RequiredPermission: "legacy.orders.create", Description: "Create an order"),
            new("Quotation requests", "/QuotationRequests/Index", Icons.Material.Outlined.ContactMail, RequiredPermission: "legacy.quotation-requests.read", Description: "Website quotation queue"),
            new("Quotations", "/Quotations/Index", Icons.Material.Outlined.RequestQuote, RequiredPermission: "legacy.quotations.read", Description: "Quotation lifecycle"),
            new("New quotation", "/Quotations/Create", Icons.Material.Outlined.RequestQuote, RequiredPermission: "legacy.quotations.create", Description: "Create a quotation"),
        ]),
        new("Finance", Icons.Material.Outlined.AccountBalanceWallet,
        [
            new("Accounting", "/accounting", Icons.Material.Outlined.AccountBalance, RequiredPermission: "legacy.accounting.read", Description: "Finance records and reports"),
            new("Invoices", "/finance/invoices", Icons.Material.Outlined.Receipt, RequiredPermission: "legacy.accounting.read", Description: "Invoice list and lifecycle"),
            new("New invoice", "/accounting/new", Icons.Material.Outlined.ReceiptLong, RequiredPermission: "legacy.accounting.create", Description: "Create an invoice"),
            new("Finance records", "/Finances/Index", Icons.Material.Outlined.Payments, RequiredPermission: "legacy.accounting.read", Description: "Payment records"),
            new("New finance record", "/Finances/Create", Icons.Material.Outlined.AddCard, RequiredPermission: "legacy.accounting.create", Description: "Record a payment"),
            new("Net profit", "/Finances/NetProfitChart", Icons.Material.Outlined.ShowChart, RequiredPermission: "legacy.accounting.read", Description: "Net profit report"),
            new("Yearly activity", "/Finances/YearlyActivityChart", Icons.Material.Outlined.Timeline, RequiredPermission: "legacy.accounting.read", Description: "Yearly activity report"),
        ]),
        new("Manufacturing", Icons.Material.Outlined.PrecisionManufacturing,
        [
            new("Materials", "/mfg/materials", Icons.Material.Outlined.Inventory, RequiredPermission: "legacy-catalog.materials.read", Description: "Material catalog and properties"),
            new("New material", "/Materials/Create", Icons.Material.Outlined.AddBox, RequiredPermission: "legacy-catalog.materials.create", Description: "Create a material"),
        ]),
        new("Purchasing", Icons.Material.Outlined.ShoppingBag,
        [
            new("Purchase orders", "/purchasing", Icons.Material.Outlined.ShoppingCart, RequiredPermission: "legacy-procurement.purchase-orders.read", Description: "Procurement queue"),
            new("New purchase order", "/purchasing/new", Icons.Material.Outlined.AddShoppingCart, RequiredPermission: "legacy-procurement.purchase-orders.create", Description: "Create a purchase order"),
            new("Suppliers", "/purchasing/suppliers", Icons.Material.Outlined.LocalShipping, RequiredPermission: "legacy-procurement.suppliers.read", Description: "Supplier profiles"),
            new("New supplier", "/Suppliers/Create", Icons.Material.Outlined.AddBusiness, RequiredPermission: "legacy-procurement.suppliers.create", Description: "Create a supplier"),
        ]),
        new("People", Icons.Material.Outlined.Groups,
        [
            new("Employees", "/Employees/Index", Icons.Material.Outlined.Badge, RequiredPermission: "legacy-employee.employees.list", Description: "Employee directory"),
            new("My profile", "/hr/profile", Icons.Material.Outlined.AccountCircle, RequiredPermission: "legacy-employee.employees.read", Description: "Employee profile"),
            new("Server errors", "/Server/ErrorReport", Icons.Material.Outlined.Dns, RequiredPermission: "legacy-intranet.diagnostics.read", Description: "Legacy diagnostics"),
        ]),
    ];

    /// <summary>Primary desktop groups in the same order as the current shell.</summary>
    public static IReadOnlyList<LegacyNavGroup> DesktopGroups { get; } =
    [
        Groups[0],
        Groups[1],
        Groups[2],
    ];

    /// <summary>Groups shown under the current shell's desktop overflow menu.</summary>
    public static IReadOnlyList<LegacyNavGroup> DesktopOverflowGroups { get; } =
    [
        Groups[3],
        Groups[4],
    ];

    public static LegacyNavItem FindByHref(string href) => Groups
        .SelectMany(group => group.Items)
        .Single(item => string.Equals(item.Href, href, StringComparison.OrdinalIgnoreCase));
}

internal sealed record LegacyNavGroup(string Label, string Icon, IReadOnlyList<LegacyNavItem> Items);

internal sealed record LegacyNavItem(
    string Label,
    string Href,
    string Icon,
    NavLinkMatch Match = NavLinkMatch.Prefix,
    string? RequiredPermission = null,
    string? Description = null);
