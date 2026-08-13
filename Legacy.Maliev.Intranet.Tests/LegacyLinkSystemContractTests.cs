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
            E("Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor", authorize, "Href=\"@order.NavigateTo\"", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@Text[\"OrderNumber\", order.Id]\"", "#@order.Id</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor", authorize, "Href=\"@quotation.NavigateTo\"", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@($\"{Text[\"Quote\"]} #{quotation.Id}\")\"", "#@quotation.Id</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor", authorize, "Href=\"@payment.NavigateTo\"", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@($\"{Text[\"Payment\"]} #{payment.Id}\")\"", "#@payment.Id</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor", authorize, "Href=\"@customer.NavigateTo\"", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@customer.FullName\"", "@customer.FullName</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor", authorize, "Href=\"@activity.NavigateTo\"", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@ActivityTitle(activity)\"", "@ActivityTitle(activity)</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client/Pages/Login.razor", anonymous, "Href=\"https://www.maliev.com\"", 1, "Role=\"LegacyLinkRole.External\"", "Target=\"_blank\"", "Rel=\"noreferrer\"", "www.maliev.com"),
            E("Legacy.Maliev.Intranet.Client/Components/Dashboard/DashboardPanel.razor", null, "Href=\"@NavigateTo\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@LinkText\"", "@LinkText"),
            E("Legacy.Maliev.Intranet.Client/Components/Dashboard/DashboardMetricCard.razor", null, "Href=\"@NavigateTo\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@LinkText\"", "dashboard-metric-link", "@LinkText"),

            E("Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.razor", authorize, "Href=\"/Customers/Index\"", 2, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"BackToCustomers\"]\"", "@Text[\"BackToCustomers\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Customers/Components/CustomerOverview.razor", null, "mailto:{Customer.Email}", 1, "Role=\"LegacyLinkRole.Inline\"", "@Customer.Email"),
            E("Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerCreate.razor", authorize, "Href=\"/Customers/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "Disabled=\"@submitting\"", "AriaLabel=\"@Text[\"Cancel\"]\"", "@Text[\"Cancel\"]</LegacyLink>"),

            E("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeView.razor", authorize, "Href=\"/Employees/Index\"", 2, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"BackToEmployees\"]\"", "@Text[\"BackToEmployees\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeCreate.razor", authorize, "Href=\"/Employees/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "Disabled=\"@submitting\"", "AriaLabel=\"@Text[\"Cancel\"]\"", "@Text[\"Cancel\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeForgotPassword.razor", anonymous, "Href=\"/Login\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "StartIcon=\"@Icons.Material.Filled.ArrowBack\"", "AriaLabel=\"@Text[\"BackToLogin\"]\"", "@Text[\"BackToLogin\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeEmailConfirmation.razor", anonymous, "Href=\"/Login\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "StartIcon=\"@Icons.Material.Filled.ArrowBack\"", "AriaLabel=\"@Text[\"BackToLogin\"]\"", "@Text[\"BackToLogin\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeResetPassword.razor", anonymous, "Href=\"/Employees/ForgotPassword\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"RequestNewLink\"]\"", "@Text[\"RequestNewLink\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeResetPassword.razor", anonymous, "Href=\"/Login\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"SignIn\"]\"", "@Text[\"SignIn\"]</LegacyLink>"),

            E("Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/Materials.razor", authorize, "/Materials/View?id={context.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@($\"{Text[\"Id\"]} {context.Id}\")\"", "@context.Id</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/MaterialCreate.razor", authorize, "Href=\"/Materials/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "Disabled=\"@submitting\"", "AriaLabel=\"@Text[\"Cancel\"]\"", "@Text[\"Cancel\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/MaterialDetail.razor", authorize, "Href=\"/Materials/Index\"", 2, "Role=\"LegacyLinkRole.Navigation\"", "StartIcon=\"@Icons.Material.Filled.ArrowBack\"", "AriaLabel=\"@Text[\"Back\"]\"", "@Text[\"Back\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/MaterialDetail.razor", authorize, "Href=\"/Materials/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "Disabled=\"@submitting\"", "AriaLabel=\"@Text[\"BackToMaterials\"]\"", "@Text[\"BackToMaterials\"]</LegacyLink>"),

            E("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderCreate.razor", authorize, "Href=\"/Orders/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"BackToOrders\"]\"", "@Text[\"BackToOrders\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderCreate.razor", authorize, "/Orders/View?id={orderId}", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@($\"{Text[\"ViewOrder\"]} {orderId}\")\"", "@Text[\"ViewOrder\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderDetail.razor", authorize, "Href=\"/Orders/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"BackToOrders\"]\"", "@Text[\"BackToOrders\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderDetail.razor", authorize, "Href=\"@file.Uri?.ToString()\"", 1, "Role=\"LegacyLinkRole.External\"", "Target=\"_blank\"", "Rel=\"noopener\"", "@file.ObjectName"),

            E("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/Suppliers.razor", authorize, "/Suppliers/View?id={context.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@($\"{Text[\"Id\"]} {context.Id}\")\"", "@context.Id</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/SupplierView.razor", authorize, "Href=\"/Suppliers/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"Back\"]\"", "@Text[\"Back\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/SupplierCreate.razor", authorize, "Href=\"/Suppliers/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "Disabled=\"@submitting\"", "AriaLabel=\"@Text[\"Cancel\"]\"", "@Text[\"Cancel\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrders.razor", authorize, "/PurchaseOrders/View?id={context.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@($\"{Text[\"Id\"]} {context.Id}\")\"", "@context.Id</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrderView.razor", authorize, "Href=\"/PurchaseOrders/Index\"", 2, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"Back\"]\"", "@Text[\"Back\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrderCreate.razor", authorize, "Href=\"/PurchaseOrders/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "Disabled=\"@submitting\"", "AriaLabel=\"@Text[\"Cancel\"]\"", "@Text[\"Cancel\"]</LegacyLink>"),

            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Invoices.razor", authorize, "/Invoices/View?id={invoice.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@($\"{Text[\"Id\"]} {invoice.Id}\")\"", "@invoice.Id</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Invoices.razor", authorize, "/Customers/View?id={invoice.CustomerId}", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@($\"{Text[\"CustomerId\"]} {invoice.CustomerId}\")\"", "@invoice.CustomerId</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceView.razor", authorize, "Href=\"/Invoices/Index\"", 2, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"Back\"]\"", "@Text[\"Back\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceView.razor", authorize, "/Customers/View?id={detailPage.Invoice.CustomerId}", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@($\"{Text[\"CustomerId\"]} {detailPage.Invoice.CustomerId}\")\"", "@detailPage.Invoice.CustomerId</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceView.razor", authorize, "Href=\"@file.Uri?.ToString()\"", 1, "Role=\"LegacyLinkRole.External\"", "Target=\"_blank\"", "Rel=\"noopener\"", "@file.ObjectName"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceCreate.razor", authorize, "Href=\"/Invoices/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"Back\"]\"", "@Text[\"Back\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Finances.razor", authorize, "/Finances/View?id={context.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@($\"{Text[\"Id\"]} {context.Id}\")\"", "@context.Id</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/FinanceView.razor", authorize, "Href=\"/Finances/Index\"", 2, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"Back\"]\"", "@Text[\"Back\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/FinanceView.razor", authorize, "Href=\"@file.Uri?.ToString()\"", 1, "Role=\"LegacyLinkRole.External\"", "Target=\"_blank\"", "Rel=\"noopener\"", "@file.ObjectName"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/NetProfitChart.razor", authorize, "Href=\"/Finances/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"Back\"]\"", "@Text[\"Back\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/YearlyActivityChart.razor", authorize, "Href=\"/Finances/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"Back\"]\"", "@Text[\"Back\"]</LegacyLink>"),

            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/View.razor", authorize, "Href=\"/QuotationRequests/Index\"", 2, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"Back\"]\"", "@Text[\"Back\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/View.razor", authorize, "Href=\"@file.Uri.ToString()\"", 1, "Role=\"LegacyLinkRole.External\"", "Target=\"_blank\"", "Rel=\"noopener\"", "@file.ObjectName"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/View.razor", authorize, "Href=\"/Quotations/Index\"", 2, "Role=\"LegacyLinkRole.Navigation\"", "AriaLabel=\"@Text[\"Back\"]\"", "@Text[\"Back\"]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/View.razor", authorize, "/Customers/View?id={page.Customer.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@(page.Customer.FullName)\"", "@(page.Customer.FullName)</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/View.razor", authorize, "/Invoices/View?id={page.Invoice.Id}", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@($\"{Text[\"Invoice\"]} {page.Invoice.Number}\")\"", "@(page.Invoice.Number)</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/View.razor", authorize, "/Orders/View?id={order.OrderId}", 1, "Role=\"LegacyLinkRole.Record\"", "AriaLabel=\"@Text[\"Order\", order.OrderId]\"", "@Text[\"Order\", order.OrderId]</LegacyLink>"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/View.razor", authorize, "Href=\"@file.Uri.ToString()\"", 1, "Role=\"LegacyLinkRole.External\"", "Target=\"_blank\"", "Rel=\"noopener\"", "@file.ObjectName"),
            E("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Create.razor", authorize, "Href=\"/Quotations/Index\"", 1, "Role=\"LegacyLinkRole.Navigation\"", "Disabled=\"submitting\"", "AriaLabel=\"@Text[\"Cancel\"]\"", "@Text[\"Cancel\"]</LegacyLink>"),
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

        var orders = ReadSource("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/Orders.razor");
        Assert.Contains("<OperationalTable", orders, StringComparison.Ordinal);
        Assert.Contains("DetailHref=\"@(order => $\"/Orders/View?id={order.Id}\")\"", orders, StringComparison.Ordinal);
        Assert.Contains("DetailAriaLabel=\"@(order => Text[\"ViewOrder\", order.Id])\"", orders, StringComparison.Ordinal);

        var extractedOperationalLinks = new[]
        {
            ("Legacy.Maliev.Intranet.Client.Features.Customers/Pages/Customers.razor", "/Customers/View?id={context.Id}", "ViewCustomer"),
            ("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/Employees.razor", "/Employees/View?id={context.Id}", "ViewEmployee"),
            ("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/Index.razor", "/QuotationRequests/View?id={context.Id}", "ViewQuotationRequest"),
            ("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Index.razor", "/Quotations/View?id={quotation.Id}", "ViewQuotation"),
        };
        foreach (var (path, href, label) in extractedOperationalLinks)
        {
            var source = ReadSource(path);
            Assert.Contains("<OperationalTable", source, StringComparison.Ordinal);
            Assert.Contains(href, source, StringComparison.Ordinal);
            Assert.Contains($"DetailAriaLabel=\"@(", source, StringComparison.Ordinal);
            Assert.Contains($"Text[\"{label}\"", source, StringComparison.Ordinal);
        }

        var quotationIndex = ReadSource("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Index.razor");
        Assert.True(LegacyLinkSourceContracts.MatchesExpectedBuilderLink(
            quotationIndex,
            "/Customers/View?id={id}",
            "nameof(LegacyLink.Role), LegacyLinkRole.Record",
            "nameof(LegacyLink.AriaLabel), $\"{Text[\"CustomerId\"]} {id}\"",
            "nameof(LegacyLink.ChildContent)",
            "content.AddContent(0, id)"));

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
