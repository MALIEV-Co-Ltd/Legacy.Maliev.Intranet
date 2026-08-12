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
        var violations = new List<string>();

        foreach (var file in ProductionRazorFiles())
        {
            var relativePath = Path.GetRelativePath(Root, file).Replace('\\', '/');
            var source = File.ReadAllText(file);

            if (!relativePath.EndsWith("/Components/LegacyLink.razor", StringComparison.Ordinal))
            {
                foreach (Match match in Regex.Matches(source, @"<a\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    if (!Regex.IsMatch(match.Value,
                            """class\s*=\s*"[^"]*\b(?:legacy-skip-link|legacy-topbar-logo|legacy-rail-logo|legacy-rail-link|legacy-profile-action|legacy-login-brand|legacy-signin-link|legacy-quick-action)\b""",
                            RegexOptions.IgnoreCase | RegexOptions.Singleline))
                    {
                        violations.Add($"{relativePath}: raw anchor {SingleLine(match.Value)}");
                    }
                }
            }

            foreach (Match match in Regex.Matches(source, @"<MudLink\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                violations.Add($"{relativePath}: MudLink {SingleLine(match.Value)}");
            }

            if (Regex.IsMatch(source, @"OpenComponent\s*<\s*MudLink\s*>", RegexOptions.IgnoreCase))
            {
                violations.Add($"{relativePath}: builder-created MudLink");
            }

            foreach (Match match in Regex.Matches(source, @"<MudButton\b(?=[^>]*\bHref\s*=)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var reviewedCtaOrDownload = Regex.IsMatch(match.Value, "Variant\\s*=\\s*\"Variant\\.Filled\"", RegexOptions.IgnoreCase) ||
                    IsReviewedOutlinedCtaOrDownload(relativePath, match.Value);
                if (!reviewedCtaOrDownload)
                {
                    violations.Add($"{relativePath}: text-button navigation {SingleLine(match.Value)}");
                }
            }
        }

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
            foreach (Match match in Regex.Matches(
                         source,
                         """<LegacyLink\b(?=[^>]*Role\s*=\s*"LegacyLinkRole\.Record")[^>]*>(.*?)</LegacyLink>""",
                         RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                count++;
                var hasSpecificName = Regex.IsMatch(match.Value, @"\bAriaLabel\s*=", RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(match.Value, "AriaLabel\\s*=\\s*\"@Text\\[\"View\"\\]\"", RegexOptions.IgnoreCase);
                if (!hasSpecificName)
                {
                    violations.Add($"{relativePath}: {SingleLine(match.Value)}");
                }
            }

            if (Regex.IsMatch(source, @"OpenComponent\s*<\s*LegacyLink\s*>", RegexOptions.IgnoreCase) &&
                source.Contains("LegacyLinkRole.Record", StringComparison.Ordinal))
            {
                count++;
                if (!source.Contains(nameof(Legacy.Maliev.Intranet.Client.Shared.Components.LegacyLink.AriaLabel), StringComparison.Ordinal))
                {
                    violations.Add($"{relativePath}: builder-created record link has no AriaLabel");
                }
            }
        }

        Assert.True(count > 0, "The production link inventory must include record links.");
        Assert.True(violations.Count == 0,
            $"Record links need a record-specific accessible name, not a repeated bare View label:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void TaskTwoLinkRoutesAndBehavioralAttributes_RemainFrozen()
    {
        var expectations = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor"] = ["@order.NavigateTo", "@quotation.NavigateTo", "@payment.NavigateTo", "@customer.NavigateTo", "@activity.NavigateTo"],
            ["Legacy.Maliev.Intranet.Client/Pages/Login.razor"] = ["href=\"/\"", "https://www.maliev.com", "_blank", "noreferrer"],
            ["Legacy.Maliev.Intranet.Client/Components/Dashboard/DashboardPanel.razor"] = ["@NavigateTo", "@LinkText"],
            ["Legacy.Maliev.Intranet.Client/Components/Dashboard/DashboardMetricCard.razor"] = ["@NavigateTo", "@LinkText", "dashboard-metric-link"],
            ["Legacy.Maliev.Intranet.Client.Features.Customers/Pages/Customers.razor"] = ["/Customers/Create", "/Customers/View?id={context.Id}"],
            ["Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.razor"] = ["/Customers/Index", "mailto:{customer.Email}"],
            ["Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerCreate.razor"] = ["/Customers/Index", "Disabled=\"@submitting\""],
            ["Legacy.Maliev.Intranet.Client.Features.Employees/Pages/Employees.razor"] = ["/Employees/Create", "/Employees/View?id={context.Id}"],
            ["Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeView.razor"] = ["/Employees/Index"],
            ["Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeCreate.razor"] = ["/Employees/Index", "Disabled=\"@submitting\""],
            ["Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeForgotPassword.razor"] = ["/Login"],
            ["Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeEmailConfirmation.razor"] = ["/Login"],
            ["Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeResetPassword.razor"] = ["/Employees/ForgotPassword", "/Login"],
            ["Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/Materials.razor"] = ["/Materials/Create", "/Materials/View?id={context.Id}"],
            ["Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/MaterialCreate.razor"] = ["/Materials/Index", "Disabled=\"@submitting\""],
            ["Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/MaterialDetail.razor"] = ["/Materials/Index", "Disabled=\"@submitting\""],
            ["Legacy.Maliev.Intranet.Client.Features.Orders/Pages/Orders.razor"] = ["/Orders/Create", "/Orders/View?id={order.Id}"],
            ["Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderCreate.razor"] = ["/Orders/Index", "/Orders/View?id={orderId}"],
            ["Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderDetail.razor"] = ["/Orders/Index", "/bff/orders/{page.Order.Id}/label", "/Customers/View?id={customerId}", "@file.Uri?.ToString()", "_blank", "noopener"],
            ["Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/Suppliers.razor"] = ["/Suppliers/View?id={context.Id}", "OpenSupplier(context.Id)", "@onclick:preventDefault=\"true\""],
            ["Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/SupplierView.razor"] = ["/Suppliers/Index"],
            ["Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/SupplierCreate.razor"] = ["/Suppliers/Index", "Disabled=\"@submitting\""],
            ["Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrders.razor"] = ["/PurchaseOrders/View?id={context.Id}", "OpenPurchaseOrder(context.Id)", "@onclick:preventDefault=\"true\""],
            ["Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrderView.razor"] = ["/PurchaseOrders/Index", "@download.Url.AbsoluteUri", "_blank", "noopener"],
            ["Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrderCreate.razor"] = ["/PurchaseOrders/Index", "Disabled=\"@submitting\""],
            ["Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Invoices.razor"] = ["/Invoices/Create", "/Invoices/View?id={invoice.Id}", "/Customers/View?id={invoice.CustomerId}"],
            ["Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceView.razor"] = ["/Invoices/Index", "/Customers/View?id={detailPage.Invoice.CustomerId}", "@file.Uri?.ToString()", "_blank", "noopener"],
            ["Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceCreate.razor"] = ["/Invoices/Index"],
            ["Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Finances.razor"] = ["/Finances/Create", "/Finances/YearlyActivityChart", "/Finances/NetProfitChart?year={BangkokYear}", "/Finances/View?id={context.Id}"],
            ["Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/FinanceView.razor"] = ["/Finances/Index", "@file.Uri?.ToString()", "_blank", "noopener"],
            ["Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/NetProfitChart.razor"] = ["/Finances/Index"],
            ["Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/YearlyActivityChart.razor"] = ["/Finances/Index"],
            ["Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/Index.razor"] = ["/QuotationRequests/View?id={context.Id}"],
            ["Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/View.razor"] = ["/QuotationRequests/Index", "@file.Uri.ToString()", "_blank", "noopener"],
            ["Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Index.razor"] = ["/Quotations/Create", "/Quotations/Estimate", "/Quotations/View?id={quotation.Id}", "/Customers/View?id={id}"],
            ["Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/View.razor"] = ["/Quotations/Index", "@quotationUri.ToString()", "/Customers/View?id={page.Customer.Id}", "/Invoices/View?id={page.Invoice.Id}", "/Orders/View?id={order.OrderId}", "@file.Uri.ToString()", "_blank", "noopener"],
            ["Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Create.razor"] = ["/Quotations/Index", "Disabled=\"submitting\""],
        };

        foreach (var (relativePath, fragments) in expectations)
        {
            var source = File.ReadAllText(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            foreach (var fragment in fragments)
            {
                Assert.Contains(fragment, source, StringComparison.Ordinal);
            }
        }
    }

    private static IEnumerable<string> ProductionRazorFiles() =>
        Directory.EnumerateDirectories(Root, "Legacy.Maliev.Intranet.Client*")
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.razor", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static string SingleLine(string value) => Regex.Replace(value, @"\s+", " ").Trim();

    private static bool IsReviewedOutlinedCtaOrDownload(string relativePath, string markup) =>
        (relativePath.EndsWith("Client.Features.Orders/Pages/OrderDetail.razor", StringComparison.Ordinal) &&
         (markup.Contains("/label", StringComparison.Ordinal) || markup.Contains("/Customers/View", StringComparison.Ordinal))) ||
        relativePath.EndsWith("Client.Features.Orders/Components/Shared/SecondaryButton.razor", StringComparison.Ordinal) ||
        (relativePath.EndsWith("Client.Features.Procurement/Pages/PurchaseOrderView.razor", StringComparison.Ordinal) &&
         markup.Contains("download.Url", StringComparison.Ordinal)) ||
        (relativePath.EndsWith("Client.Features.Accounting/Pages/Finances.razor", StringComparison.Ordinal) &&
         (markup.Contains("YearlyActivity", StringComparison.Ordinal) || markup.Contains("NetProfit", StringComparison.Ordinal))) ||
        (relativePath.EndsWith("Client.Features.Quotations/Pages/Quotations/Index.razor", StringComparison.Ordinal) &&
         markup.Contains("Estimate", StringComparison.Ordinal));

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
