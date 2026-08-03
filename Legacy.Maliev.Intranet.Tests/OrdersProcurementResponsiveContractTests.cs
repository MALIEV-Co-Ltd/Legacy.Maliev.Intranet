namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrdersProcurementResponsiveContractTests
{
    [Fact]
    public void OrdersQueue_BoundsWorkingSetsAndKeepsResponsiveRecordsAccessible()
    {
        var root = FindRoot();
        var page = Read(root, "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "Orders.razor");
        var styles = Read(root, "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "Orders.razor.css");

        Assert.Contains("orders-working-set", page, StringComparison.Ordinal);
        Assert.Contains("max-height:", styles, StringComparison.Ordinal);
        Assert.Contains("overflow: auto", styles, StringComparison.Ordinal);
        Assert.Contains("position: sticky", styles, StringComparison.Ordinal);
        Assert.Contains("data-label=", page, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 720px)", styles, StringComparison.Ordinal);
        Assert.Contains("public int Size { get; set; } = 10", page, StringComparison.Ordinal);
        Assert.Contains("fallback: 10", page, StringComparison.Ordinal);
        Assert.Contains(".orders-module-shell .mlv-button", styles, StringComparison.Ordinal);
        Assert.Contains(".orders-table a", styles, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", styles, StringComparison.Ordinal);
        Assert.Contains("LegacyPresentation.FormatCalendarDate", page, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderDetail_UsesProgressiveSectionsPersistentSaveAndBoundedHistory()
    {
        var root = FindRoot();
        var page = Read(root, "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "OrderDetail.razor");
        var styles = Read(root, "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "OrderDetail.razor.css");

        Assert.Equal(5, CountOccurrences(page, "class=\"order-edit-section\""));
        Assert.Contains("class=\"order-save-bar\"", page, StringComparison.Ordinal);
        Assert.Contains("class=\"order-history\" tabindex=\"0\"", page, StringComparison.Ordinal);
        Assert.Contains("position: sticky", styles, StringComparison.Ordinal);
        Assert.Contains("max-height:", styles, StringComparison.Ordinal);
        Assert.Contains("@media (pointer: coarse)", styles, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", styles, StringComparison.Ordinal);

        // Presentation grouping must not alter any write endpoint or DTO projection.
        Assert.Contains("JsonContent.Create(edit.ToRequest())", page, StringComparison.Ordinal);
        Assert.Contains("/status/{selectedStatus.Value}", page, StringComparison.Ordinal);
        Assert.Contains("multipart/form-data", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("LegacyPresentation.FormatUtcDateTime", page, StringComparison.Ordinal);
        Assert.Contains("StatusLabel(page.CurrentStatus?.Name)", page, StringComparison.Ordinal);
        Assert.Contains("StatusLabel(item.Name)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcurementLists_AdaptAtTabletAndUsePracticalDefaultPageSizes()
    {
        var root = FindRoot();
        var purchaseOrders = Read(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "PurchaseOrders.razor");
        var suppliers = Read(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "Suppliers.razor");
        var purchaseStyles = Read(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "PurchaseOrders.razor.css");
        var supplierStyles = Read(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "Suppliers.razor.css");

        Assert.Contains("Breakpoint=\"Breakpoint.Md\"", purchaseOrders, StringComparison.Ordinal);
        Assert.Contains("Breakpoint=\"Breakpoint.Md\"", suppliers, StringComparison.Ordinal);
        Assert.Contains("public int Size { get; set; } = 25", suppliers, StringComparison.Ordinal);
        Assert.Contains("fallback: 25", suppliers, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 1fr", purchaseStyles, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 1fr", supplierStyles, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", purchaseStyles, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", supplierStyles, StringComparison.Ordinal);
        Assert.Contains("LegacyPresentation.FormatUtcDateTime", purchaseOrders, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcurementForms_ProgressivelyGroupFieldsAvoidZeroDefaultsAndIsolateDeletion()
    {
        var root = FindRoot();
        var create = Read(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "PurchaseOrderCreate.razor");
        var createStyles = Read(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "PurchaseOrderCreate.razor.css");
        var supplierCreate = Read(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "SupplierCreate.razor");
        var supplierView = Read(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "SupplierView.razor");
        var purchaseView = Read(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "PurchaseOrderView.razor");

        Assert.Contains("purchase-order-section", create, StringComparison.Ordinal);
        Assert.Contains("purchase-order-line", create, StringComparison.Ordinal);
        Assert.Contains("purchase-order-form-actions", create, StringComparison.Ordinal);
        Assert.Contains("ApplyInitialSelections", create, StringComparison.Ordinal);
        Assert.Contains("model.SupplierId <= 0", create, StringComparison.Ordinal);
        Assert.Contains("model.ShippingAddressId <= 0", create, StringComparison.Ordinal);
        Assert.Contains("model.BillingAddressId <= 0", create, StringComparison.Ordinal);
        Assert.Contains("model.EmployeeId <= 0", create, StringComparison.Ordinal);
        Assert.Contains("position: sticky", createStyles, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", createStyles, StringComparison.Ordinal);
        Assert.Contains(".purchase-order-addresses", createStyles, StringComparison.Ordinal);
        Assert.Contains("margin: 0", createStyles, StringComparison.Ordinal);

        Assert.Contains("MudNumericField T=\"int?\" @bind-Value=\"countryId\"", supplierCreate, StringComparison.Ordinal);
        Assert.Contains("model.CountryId = countryId.Value", supplierCreate, StringComparison.Ordinal);
        Assert.Contains("supplier-danger-zone", supplierView, StringComparison.Ordinal);
        Assert.Contains("purchase-order-danger-zone", purchaseView, StringComparison.Ordinal);

        // Auth, CSRF, idempotency and downstream write contracts remain intact.
        Assert.Contains("X-CSRF-TOKEN", create, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", create, StringComparison.Ordinal);
        Assert.Contains("JsonContent.Create(model)", create, StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] path) => File.ReadAllText(Path.Combine([root, .. path]));

    private static int CountOccurrences(string value, string search) =>
        value.Split(search, StringSplitOptions.None).Length - 1;

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
