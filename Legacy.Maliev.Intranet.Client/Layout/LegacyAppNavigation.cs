using Microsoft.AspNetCore.Components.Routing;
using global::Maliev.ShadcnBlazor.Components.Icons;
using global::Maliev.ShadcnBlazor.Icons.Lucide;

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
        new("Sales", Icon(LucideIconNames.BriefcaseBusiness),
        [
            new("Dashboard", "/Dashboard", Icon(LucideIconNames.LayoutDashboard), Description: "Workspace overview"),
            new("Customers", "/customers", Icon(LucideIconNames.Users), RequiredPermission: "legacy-customer.customers.list", Description: "Customer profiles"),
            new("New customer", "/customers/new", Icon(LucideIconNames.UserPlus), RequiredPermission: "legacy-customer.customers.create", Description: "Onboard a customer", Kind: LegacyNavItemKind.ChildAction, ParentHref: "/customers"),
            new("Orders", "/sales/orders", Icon(LucideIconNames.ClipboardList), RequiredPermission: "legacy.orders.read", Description: "Order queue and status"),
            new("New order", "/Orders/Create", Icon(LucideIconNames.ShoppingCart), RequiredPermission: "legacy.orders.create", Description: "Create an order", Kind: LegacyNavItemKind.ChildAction, ParentHref: "/sales/orders"),
            new("Quotation requests", "/QuotationRequests/Index", Icon(LucideIconNames.Mail), RequiredPermission: "legacy.quotation-requests.read", Description: "Website quotation queue"),
            new("Quotations", "/Quotations/Index", Icon(LucideIconNames.FileText), RequiredPermission: "legacy.quotations.read", Description: "Quotation lifecycle"),
            new("New quotation", "/Quotations/Create", Icon(LucideIconNames.FileText), RequiredPermission: "legacy.quotations.create", Description: "Create a quotation", Kind: LegacyNavItemKind.ChildAction, ParentHref: "/Quotations/Index"),
        ]),
        new("Finance", Icon(LucideIconNames.WalletCards),
        [
            new("Accounting", "/accounting", Icon(LucideIconNames.Landmark), RequiredPermission: "legacy.accounting.read", Description: "Finance records and reports"),
            new("Invoices", "/finance/invoices", Icon(LucideIconNames.Receipt), RequiredPermission: "legacy.accounting.read", Description: "Invoice list and lifecycle"),
            new("New invoice", "/accounting/new", Icon(LucideIconNames.ReceiptText), RequiredPermission: "legacy.accounting.create", Description: "Create an invoice", Kind: LegacyNavItemKind.ChildAction, ParentHref: "/finance/invoices"),
            new("Finance records", "/Finances/Index", Icon(LucideIconNames.CreditCard), RequiredPermission: "legacy.accounting.read", Description: "Payment records"),
            new("New finance record", "/Finances/Create", Icon(LucideIconNames.CreditCard), RequiredPermission: "legacy.accounting.create", Description: "Record a payment", Kind: LegacyNavItemKind.ChildAction, ParentHref: "/Finances/Index"),
            new("Net profit", "/Finances/NetProfitChart", Icon(LucideIconNames.ChartNoAxesCombined), RequiredPermission: "legacy.accounting.read", Description: "Net profit report"),
            new("Yearly activity", "/Finances/YearlyActivityChart", Icon(LucideIconNames.ChartSpline), RequiredPermission: "legacy.accounting.read", Description: "Yearly activity report"),
        ]),
        new("Manufacturing", Icon(LucideIconNames.Factory),
        [
            new("Materials", "/mfg/materials", Icon(LucideIconNames.Package), RequiredPermission: "legacy-catalog.materials.read", Description: "Material catalog and properties"),
            new("New material", "/Materials/Create", Icon(LucideIconNames.PackagePlus), RequiredPermission: "legacy-catalog.materials.create", Description: "Create a material", Kind: LegacyNavItemKind.ChildAction, ParentHref: "/mfg/materials"),
        ]),
        new("Purchasing", Icon(LucideIconNames.ShoppingBag),
        [
            new("Purchase orders", "/purchasing", Icon(LucideIconNames.ShoppingCart), RequiredPermission: "legacy-procurement.purchase-orders.read", Description: "Procurement queue"),
            new("New purchase order", "/purchasing/new", Icon(LucideIconNames.ShoppingCart), RequiredPermission: "legacy-procurement.purchase-orders.create", Description: "Create a purchase order", Kind: LegacyNavItemKind.ChildAction, ParentHref: "/purchasing"),
            new("Suppliers", "/purchasing/suppliers", Icon(LucideIconNames.Truck), RequiredPermission: "legacy-procurement.suppliers.read", Description: "Supplier profiles"),
            new("New supplier", "/Suppliers/Create", Icon(LucideIconNames.BriefcaseBusiness), RequiredPermission: "legacy-procurement.suppliers.create", Description: "Create a supplier", Kind: LegacyNavItemKind.ChildAction, ParentHref: "/purchasing/suppliers"),
        ]),
        new("People", Icon(LucideIconNames.Users),
        [
            new("Employees", "/Employees/Index", Icon(LucideIconNames.Badge), RequiredPermission: "legacy-employee.employees.list", Description: "Employee directory"),
            new("My profile", "/hr/profile", Icon(LucideIconNames.CircleUserRound), RequiredPermission: "legacy-employee.employees.read", Description: "Employee profile"),
            new("Server errors", "/Server/ErrorReport", Icon(LucideIconNames.Server), RequiredPermission: "legacy-intranet.diagnostics.read", Description: "Legacy diagnostics"),
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

    private static ShadcnIconData Icon(string name) => LucideIconCatalog.Instance.Get(name);
}

internal sealed record LegacyNavGroup(string Label, ShadcnIconData Icon, IReadOnlyList<LegacyNavItem> Items);

internal enum LegacyNavItemKind
{
    Primary,
    ChildAction,
}

internal sealed record LegacyNavItem(
    string Label,
    string Href,
    ShadcnIconData Icon,
    NavLinkMatch Match = NavLinkMatch.Prefix,
    string? RequiredPermission = null,
    string? Description = null,
    LegacyNavItemKind Kind = LegacyNavItemKind.Primary,
    string? ParentHref = null);
