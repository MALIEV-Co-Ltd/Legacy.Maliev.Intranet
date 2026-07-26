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
            new("Customers", "/customers", Icons.Material.Outlined.People),
            new("New customer", "/customers/new", Icons.Material.Outlined.PersonAdd),
            new("Orders", "/sales/orders", Icons.Material.Outlined.ReceiptLong),
            new("New order", "/Orders/Create", Icons.Material.Outlined.AddShoppingCart),
            new("Quotation requests", "/QuotationRequests/Index", Icons.Material.Outlined.ContactMail),
            new("Quotations", "/Quotations/Index", Icons.Material.Outlined.RequestQuote),
            new("New quotation", "/Quotations/Create", Icons.Material.Outlined.RequestQuote),
        ]),
        new("Finance", Icons.Material.Outlined.AccountBalanceWallet,
        [
            new("Accounting", "/accounting", Icons.Material.Outlined.AccountBalance),
            new("Invoices", "/finance/invoices", Icons.Material.Outlined.Receipt),
            new("New invoice", "/accounting/new", Icons.Material.Outlined.ReceiptLong),
            new("Finance records", "/Finances/Index", Icons.Material.Outlined.Payments),
            new("New finance record", "/Finances/Create", Icons.Material.Outlined.AddCard),
            new("Net profit", "/Finances/NetProfitChart", Icons.Material.Outlined.ShowChart),
            new("Yearly activity", "/Finances/YearlyActivityChart", Icons.Material.Outlined.Timeline),
        ]),
        new("Purchasing", Icons.Material.Outlined.ShoppingBag,
        [
            new("Purchase orders", "/purchasing", Icons.Material.Outlined.ShoppingCart),
            new("Suppliers", "/purchasing/suppliers", Icons.Material.Outlined.LocalShipping),
            new("New purchase order", "/purchasing/new", Icons.Material.Outlined.AddShoppingCart),
            new("New supplier", "/Suppliers/Create", Icons.Material.Outlined.AddBusiness),
        ]),
        new("Manufacturing", Icons.Material.Outlined.PrecisionManufacturing,
        [
            new("Materials", "/mfg/materials", Icons.Material.Outlined.Inventory),
            new("New material", "/Materials/Create", Icons.Material.Outlined.AddBox),
        ]),
        new("People & operations", Icons.Material.Outlined.Groups,
        [
            new("Employees", "/Employees/Index", Icons.Material.Outlined.Badge),
            new("My profile", "/hr/profile", Icons.Material.Outlined.AccountCircle),
            new("Server errors", "/Server/ErrorReport", Icons.Material.Outlined.Dns),
        ]),
    ];
}

internal sealed record LegacyNavGroup(string Label, string Icon, IReadOnlyList<LegacyNavItem> Items);

internal sealed record LegacyNavItem(string Label, string Href, string Icon, NavLinkMatch Match = NavLinkMatch.Prefix);
