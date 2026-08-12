using System.Text.RegularExpressions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyLinkSystemContractTests
{
    [Fact]
    public void SharedLink_ExposesFourExplicitRoles()
    {
        var source = File.ReadAllText(Path.Combine(Root, "Legacy.Maliev.Intranet.Client.Shared", "Components", "LegacyLink.razor"));
        var role = File.ReadAllText(Path.Combine(Root, "Legacy.Maliev.Intranet.Client.Shared", "Components", "LegacyLinkRole.cs"));
        Assert.Contains("Inline", role);
        Assert.Contains("Record", role);
        Assert.Contains("Navigation", role);
        Assert.Contains("External", role);
        Assert.Contains("[Parameter] public LegacyLinkRole Role", source);
        Assert.Contains("[Parameter] public bool Disabled", source);
        Assert.Contains("aria-disabled", source);
    }

    [Fact]
    public void SharedLink_UsesCaseInsensitiveBlankTargetProtectionAndNamesDisabledLinks()
    {
        var source = File.ReadAllText(Path.Combine(Root, "Legacy.Maliev.Intranet.Client.Shared", "Components", "LegacyLink.razor"));

        Assert.Contains("string.Equals(Target, \"_blank\", StringComparison.OrdinalIgnoreCase)", source);
        Assert.Contains("aria-label=\"@AriaLabel\"", source);
    }

    [Fact]
    public void ProductionPages_UseSemanticLinksExceptForReviewedSpecializedOwners()
    {
        var documents = ProductionRazorFiles()
            .Select(file => new LegacyLinkSourceContracts.SourceDocument(
                Path.GetRelativePath(Root, file).Replace('\\', '/'),
                File.ReadAllText(file)))
            .ToArray();
        var violations = LegacyLinkSourceContracts.AuditInventory(documents);

        Assert.True(violations.Count == 0,
            $"Production links must use LegacyLink unless they have a reviewed specialized owner:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void RecordLinks_HaveRecordSpecificAccessibleNames()
    {
        var violations = new List<string>();
        var count = 0;

        foreach (var file in ProductionRazorFiles())
        {
            var relativePath = Path.GetRelativePath(Root, file).Replace('\\', '/');
            var source = File.ReadAllText(file);
            count += LegacyLinkSourceContracts.CountRecordLinks(source);
            violations.AddRange(LegacyLinkSourceContracts.FindRecordAccessibleNameViolations(relativePath, source));
        }

        Assert.True(count > 0, "The production link inventory must include record links.");
        Assert.True(violations.Count == 0,
            $"Record links need a record-specific accessible name, not a repeated bare View label:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void TaskTwoLinkRoutesAndBehavioralAttributes_RemainFrozen()
    {
        const string authorize = "@attribute [Authorize]";
        const string anonymous = "@attribute [AllowAnonymous]";
        LinkExpectation[] expectations =
        [
            E("Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor", authorize, "Href=\"@order.NavigateTo\"", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@Text[\"OrderNumber\", order.Id]\""),
            E("Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor", authorize, "Href=\"@quotation.NavigateTo\"", 1, "Role=\"LegacyLinkRole.Record\"", "quotation.Id"),
            E("Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor", authorize, "Href=\"@payment.NavigateTo\"", 1, "Role=\"LegacyLinkRole.Record\"", "payment.Id"),
            E("Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor", authorize, "Href=\"@customer.NavigateTo\"", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@customer.FullName\""),
            E("Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor", authorize, "Href=\"@activity.NavigateTo\"", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@ActivityTitle(activity)\""),
            E("Legacy.Maliev.Intranet.Client/Pages/Login.razor", anonymous, "Href=\"https://www.maliev.com\"", 1, "Role=\"LegacyLinkRole.External\"", "Target=\"_blank\"", "Rel=\"noreferrer\"", "www.maliev.com"),
            E("Legacy.Maliev.Intranet.Client/Components/Dashboard/DashboardPanel.razor", null, "Href=\"@NavigateTo\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@LinkText\"", "@LinkText"),
            E("Legacy.Maliev.Intranet.Client/Components/Dashboard/DashboardMetricCard.razor", null, "Href=\"@NavigateTo\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@LinkText\"", "dashboard-metric-link", "@LinkText"),

            E("Legacy.Maliev.Intranet.Client.Features.Customers/Pages/Customers.razor", authorize, "/Customers/View?id={context.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=", "context.Id", "@Text[\"View\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.razor", authorize, "Href=\"/Customers/Index\"", 2, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"BackToCustomers\"]\""),
            E("Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.razor", authorize, "mailto:{customer.Email}", 1, "Role=\"LegacyLinkRole.Inline\"", "@customer.Email"),
            E("Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerCreate.razor", authorize, "Href=\"/Customers/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "Disabled=\"@submitting\"", "@Text[\"Cancel\"]"),

            E("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/Employees.razor", authorize, "/Employees/View?id={context.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "context.Id", "@Text[\"View\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeView.razor", authorize, "Href=\"/Employees/Index\"", 2, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"BackToEmployees\"]\""),
            E("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeCreate.razor", authorize, "Href=\"/Employees/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "Disabled=\"@submitting\"", "@Text[\"Cancel\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeForgotPassword.razor", anonymous, "Href=\"/Login\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "StartIcon=\"@Icons.Material.Filled.ArrowBack\"", "@Text[\"BackToLogin\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeEmailConfirmation.razor", anonymous, "Href=\"/Login\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "StartIcon=\"@Icons.Material.Filled.ArrowBack\"", "@Text[\"BackToLogin\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeResetPassword.razor", anonymous, "Href=\"/Employees/ForgotPassword\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "@Text[\"RequestNewLink\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeResetPassword.razor", anonymous, "Href=\"/Login\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "@Text[\"SignIn\"]"),

            E("Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/Materials.razor", authorize, "/Materials/View?id={context.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "context.Id"),
            E("Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/MaterialCreate.razor", authorize, "Href=\"/Materials/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "Disabled=\"@submitting\"", "@Text[\"Cancel\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/MaterialDetail.razor", authorize, "Href=\"/Materials/Index\"", 3, "Role=\"LegacyLinkRole.Navigation\"", "StartIcon=\"@Icons.Material.Filled.ArrowBack\""),
            E("Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/MaterialDetail.razor", authorize, "Href=\"/Materials/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "Disabled=\"@submitting\"", "@Text[\"BackToMaterials\"]"),

            E("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/Orders.razor", authorize, "/Orders/View?id={order.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@Text[\"OrderNumber\", order.Id]\""),
            E("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderCreate.razor", authorize, "Href=\"/Orders/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "@Text[\"BackToOrders\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderCreate.razor", authorize, "/Orders/View?id={orderId}", 1, "Role=\"LegacyLinkRole.Record\"", "orderId", "@Text[\"ViewOrder\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderDetail.razor", authorize, "Href=\"/Orders/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "@Text[\"BackToOrders\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderDetail.razor", authorize, "Href=\"@file.Uri?.ToString()\"", 1, "Role=\"LegacyLinkRole.External\"", "Target=\"_blank\"", "Rel=\"noopener\"", "@file.ObjectName"),

            E("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/Suppliers.razor", authorize, "/Suppliers/View?id={context.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "context.Id"),
            E("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/SupplierView.razor", authorize, "Href=\"/Suppliers/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "@Text[\"Back\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/SupplierCreate.razor", authorize, "Href=\"/Suppliers/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "Disabled=\"@submitting\"", "@Text[\"Cancel\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrders.razor", authorize, "/PurchaseOrders/View?id={context.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "context.Id"),
            E("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrderView.razor", authorize, "Href=\"/PurchaseOrders/Index\"", 2, "Role=\"LegacyLinkRole.Navigation\"", "@Text[\"Back\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrderCreate.razor", authorize, "Href=\"/PurchaseOrders/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "Disabled=\"@submitting\"", "@Text[\"Cancel\"]"),

            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Invoices.razor", authorize, "/Invoices/View?id={invoice.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "invoice.Id"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Invoices.razor", authorize, "/Customers/View?id={invoice.CustomerId}", 1, "Role=\"LegacyLinkRole.Record\"", "invoice.CustomerId"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceView.razor", authorize, "Href=\"/Invoices/Index\"", 2, "Role=\"LegacyLinkRole.Navigation\"", "@Text[\"Back\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceView.razor", authorize, "/Customers/View?id={detailPage.Invoice.CustomerId}", 1, "Role=\"LegacyLinkRole.Record\"", "detailPage.Invoice.CustomerId"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceView.razor", authorize, "Href=\"@file.Uri?.ToString()\"", 1, "Role=\"LegacyLinkRole.External\"", "Target=\"_blank\"", "Rel=\"noopener\"", "@file.ObjectName"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceCreate.razor", authorize, "Href=\"/Invoices/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "@Text[\"Back\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Finances.razor", authorize, "/Finances/View?id={context.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "context.Id"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/FinanceView.razor", authorize, "Href=\"/Finances/Index\"", 2, "Role=\"LegacyLinkRole.Navigation\"", "@Text[\"Back\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/FinanceView.razor", authorize, "Href=\"@file.Uri?.ToString()\"", 1, "Role=\"LegacyLinkRole.External\"", "Target=\"_blank\"", "Rel=\"noopener\"", "@file.ObjectName"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/NetProfitChart.razor", authorize, "Href=\"/Finances/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "@Text[\"Back\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/YearlyActivityChart.razor", authorize, "Href=\"/Finances/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "@Text[\"Back\"]"),

            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/Index.razor", authorize, "/QuotationRequests/View?id={context.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "context.Id"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/View.razor", authorize, "Href=\"/QuotationRequests/Index\"", 2, "Role=\"LegacyLinkRole.Navigation\"", "@Text[\"Back\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/View.razor", authorize, "Href=\"@file.Uri.ToString()\"", 1, "Role=\"LegacyLinkRole.External\"", "Target=\"_blank\"", "Rel=\"noopener\"", "@file.ObjectName"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Index.razor", authorize, "/Quotations/View?id={quotation.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "quotation.Id"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/View.razor", authorize, "Href=\"/Quotations/Index\"", 2, "Role=\"LegacyLinkRole.Navigation\"", "@Text[\"Back\"]"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/View.razor", authorize, "/Customers/View?id={page.Customer.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "page.Customer.FullName"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/View.razor", authorize, "/Invoices/View?id={page.Invoice.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "page.Invoice.Number"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/View.razor", authorize, "/Orders/View?id={order.OrderId}", 1, "Role=\"LegacyLinkRole.Record\"", "order.OrderId"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/View.razor", authorize, "Href=\"@file.Uri.ToString()\"", 1, "Role=\"LegacyLinkRole.External\"", "Target=\"_blank\"", "Rel=\"noopener\"", "@file.ObjectName"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Create.razor", authorize, "Href=\"/Quotations/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "Disabled=\"submitting\"", "@Text[\"Cancel\"]"),
        ];

        foreach (var expectation in expectations)
        {
            var source = ReadSource(expectation.RelativePath);
            if (expectation.Authorization is not null)
            {
                Assert.Contains(expectation.Authorization, source, StringComparison.Ordinal);
            }

            Assert.Equal(
                expectation.Count,
                LegacyLinkSourceContracts.CountExpectedLinks(source, expectation.Href, expectation.LocalFragments));
        }

        var quotationIndex = ReadSource("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Index.razor");
        Assert.True(LegacyLinkSourceContracts.MatchesExpectedBuilderLink(
            quotationIndex,
            "/Customers/View?id={id}",
            "nameof(LegacyLink.Role), LegacyLinkRole.Record",
            "nameof(LegacyLink.AriaLabel)",
            "CustomerId",
            "{id}"));

        var suppliers = ReadSource("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/Suppliers.razor");
        Assert.True(LegacyLinkSourceContracts.MatchesExpectedContainer(
            suppliers,
            "span",
            "/Suppliers/View?id={context.Id}",
            "@onclick=\"@(() => OpenSupplier(context.Id))\"",
            "@onclick:preventDefault=\"true\""));
        var purchaseOrders = ReadSource("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrders.razor");
        Assert.True(LegacyLinkSourceContracts.MatchesExpectedContainer(
            purchaseOrders,
            "span",
            "/PurchaseOrders/View?id={context.Id}",
            "@onclick=\"@(() => OpenPurchaseOrder(context.Id))\"",
            "@onclick:preventDefault=\"true\""));

        var orderCreate = ReadSource("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderCreate.razor");
        Assert.True(LegacyLinkSourceContracts.MatchesExpectedConditionalLink(
            orderCreate,
            "@if (createdOrderId is int orderId)",
            "/Orders/View?id={orderId}",
            "Role=\"LegacyLinkRole.Record\""));
        var resetPassword = ReadSource("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeResetPassword.razor");
        Assert.True(LegacyLinkSourceContracts.MatchesExpectedConditionalLink(
            resetPassword,
            "@if (!HasValidAction)",
            "Href=\"/Employees/ForgotPassword\"",
            "Role=\"LegacyLinkRole.Navigation\""));
        Assert.True(LegacyLinkSourceContracts.MatchesExpectedConditionalLink(
            resetPassword,
            "@if (completed)",
            "Href=\"/Login\"",
            "Role=\"LegacyLinkRole.Navigation\""));
    }

    private static LinkExpectation E(
        string relativePath,
        string? authorization,
        string href,
        int count,
        params string[] localFragments) =>
        new(relativePath, authorization, href, count, localFragments);

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private sealed record LinkExpectation(
        string RelativePath,
        string? Authorization,
        string Href,
        int Count,
        string[] LocalFragments);

    private static IEnumerable<string> ProductionRazorFiles() =>
        Directory.EnumerateDirectories(Root, "Legacy.Maliev.Intranet.Client*")
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.razor", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static string Root => FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
