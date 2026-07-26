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
            new("Orders", "/sales/orders", Icons.Material.Outlined.ReceiptLong),
            new("Quotation requests", "/QuotationRequests/Index", Icons.Material.Outlined.ContactMail),
            new("Quotations", "/Quotations/Index", Icons.Material.Outlined.RequestQuote),
        ]),
        new("Finance", Icons.Material.Outlined.AccountBalanceWallet,
        [
            new("Invoices", "/accounting", Icons.Material.Outlined.Receipt),
            new("Finance records", "/Finances/Index", Icons.Material.Outlined.Payments),
        ]),
        new("Purchasing", Icons.Material.Outlined.ShoppingBag,
        [
            new("Purchase orders", "/purchasing", Icons.Material.Outlined.ShoppingCart),
            new("Suppliers", "/purchasing/suppliers", Icons.Material.Outlined.LocalShipping),
        ]),
        new("Manufacturing", Icons.Material.Outlined.PrecisionManufacturing,
        [
            new("Materials", "/mfg/materials", Icons.Material.Outlined.Inventory),
        ]),
        new("People & operations", Icons.Material.Outlined.Groups,
        [
            new("Employees", "/Employees/Index", Icons.Material.Outlined.Badge),
            new("Server errors", "/Server/ErrorReport", Icons.Material.Outlined.Dns),
        ]),
    ];
}

internal sealed record LegacyNavGroup(string Label, string Icon, IReadOnlyList<LegacyNavItem> Items);

internal sealed record LegacyNavItem(string Label, string Href, string Icon, NavLinkMatch Match = NavLinkMatch.Prefix);
