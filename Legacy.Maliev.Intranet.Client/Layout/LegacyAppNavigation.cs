using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace Legacy.Maliev.Intranet.Client.Layout;

/// <summary>Navigation groups that are backed by the migrated legacy workflows.</summary>
internal static class LegacyAppNavigation
{
    public static IReadOnlyList<LegacyNavGroup> Groups { get; } =
    [
        new("Sales & CRM", Icons.Material.Outlined.Business,
        [
            new("Dashboard", "/Dashboard", Icons.Material.Outlined.Dashboard),
            new("Customers", "/customers", Icons.Material.Outlined.People, RequiredPermission: "legacy-customer.customers.list"),
            new("New customer", "/customers/new", Icons.Material.Outlined.PersonAdd, RequiredPermission: "legacy-customer.customers.create"),
            new("Orders", "/sales/orders", Icons.Material.Outlined.ReceiptLong, RequiredPermission: "legacy.orders.read"),
            new("New order", "/Orders/Create", Icons.Material.Outlined.AddShoppingCart, RequiredPermission: "legacy.orders.create"),
            new("Quotation requests", "/QuotationRequests/Index", Icons.Material.Outlined.ContactMail, RequiredPermission: "legacy.quotation-requests.read"),
            new("Quotations", "/Quotations/Index", Icons.Material.Outlined.RequestQuote, RequiredPermission: "legacy.quotations.read"),
            new("New quotation", "/Quotations/Create", Icons.Material.Outlined.RequestQuote, RequiredPermission: "legacy.quotations.create"),
        ]),
        new("Finance", Icons.Material.Outlined.AccountBalanceWallet,
        [
            new("Accounting", "/accounting", Icons.Material.Outlined.AccountBalance, RequiredPermission: "legacy.accounting.read"),
            new("Invoices", "/finance/invoices", Icons.Material.Outlined.Receipt, RequiredPermission: "legacy.accounting.read"),
            new("New invoice", "/accounting/new", Icons.Material.Outlined.ReceiptLong, RequiredPermission: "legacy.accounting.create"),
            new("Finance records", "/Finances/Index", Icons.Material.Outlined.Payments, RequiredPermission: "legacy.accounting.read"),
            new("New finance record", "/Finances/Create", Icons.Material.Outlined.AddCard, RequiredPermission: "legacy.accounting.create"),
            new("Net profit", "/Finances/NetProfitChart", Icons.Material.Outlined.ShowChart, RequiredPermission: "legacy.accounting.read"),
            new("Yearly activity", "/Finances/YearlyActivityChart", Icons.Material.Outlined.Timeline, RequiredPermission: "legacy.accounting.read"),
        ]),
        new("Purchasing", Icons.Material.Outlined.ShoppingBag,
        [
            new("Purchase orders", "/purchasing", Icons.Material.Outlined.ShoppingCart, RequiredPermission: "legacy-procurement.purchase-orders.read"),
            new("Suppliers", "/purchasing/suppliers", Icons.Material.Outlined.LocalShipping, RequiredPermission: "legacy-procurement.suppliers.read"),
            new("New purchase order", "/purchasing/new", Icons.Material.Outlined.AddShoppingCart, RequiredPermission: "legacy-procurement.purchase-orders.create"),
            new("New supplier", "/Suppliers/Create", Icons.Material.Outlined.AddBusiness, RequiredPermission: "legacy-procurement.suppliers.create"),
        ]),
        new("Manufacturing", Icons.Material.Outlined.PrecisionManufacturing,
        [
            new("Materials", "/mfg/materials", Icons.Material.Outlined.Inventory, RequiredPermission: "legacy-catalog.materials.read"),
            new("New material", "/Materials/Create", Icons.Material.Outlined.AddBox, RequiredPermission: "legacy-catalog.materials.create"),
        ]),
        new("People & operations", Icons.Material.Outlined.Groups,
        [
            new("Employees", "/Employees/Index", Icons.Material.Outlined.Badge, RequiredPermission: "legacy-employee.employees.list"),
            new("My profile", "/hr/profile", Icons.Material.Outlined.AccountCircle, RequiredPermission: "legacy-employee.employees.read"),
            new("Server errors", "/Server/ErrorReport", Icons.Material.Outlined.Dns),
        ]),
    ];
}

internal sealed record LegacyNavGroup(string Label, string Icon, IReadOnlyList<LegacyNavItem> Items);

internal sealed record LegacyNavItem(
    string Label,
    string Href,
    string Icon,
    NavLinkMatch Match = NavLinkMatch.Prefix,
    string? RequiredPermission = null);
