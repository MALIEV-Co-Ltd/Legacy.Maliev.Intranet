namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomersViewWasmMigrationContractTests
{
    [Fact]
    public void CustomerOverview_ExposesTypedEditContractWithoutOwningCustomerRequests()
    {
        var source = ReadCustomerComponent("CustomerOverview.razor");

        Assert.Contains("<section", source, StringComparison.Ordinal);
        Assert.Contains("HtmlTag=\"h2\"", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter, EditorRequired] public CustomerDetail Customer", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter, EditorRequired] public CustomerUpdateRequest EditModel", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public bool CanEdit", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public bool Editing", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public bool Submitting", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public EventCallback BeginEdit", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public EventCallback Save", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public EventCallback CancelEdit", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter, EditorRequired] public Func<string, string> Localize", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter, EditorRequired] public Func<DateTime?, string> DisplayDate", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter, EditorRequired] public Func<DateTime?, string> DisplayDateTime", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter, EditorRequired] public Func<CustomerAddressDetail?, string> DisplayAddress", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<MudForm", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MudForm", source, StringComparison.Ordinal);
        Assert.Contains("@bind-Value=\"EditModel.FirstName\"", source, StringComparison.Ordinal);
        AssertPresentationalOnly(source);
    }

    [Fact]
    public void CustomerActivity_RepresentsIndependentSourceStatesAndRecordDestinations()
    {
        var source = ReadCustomerComponent("CustomerActivity.razor");

        Assert.Contains("<section", source, StringComparison.Ordinal);
        Assert.Contains("HtmlTag=\"h2\"", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public CustomerActivityPage? Page", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public bool Loading", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public string? Error", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public EventCallback Retry", source, StringComparison.Ordinal);
        Assert.Contains("<MudProgressLinear", source, StringComparison.Ordinal);
        Assert.Contains("<MudAlert", source, StringComparison.Ordinal);
        Assert.Contains("Page.Items.Count == 0", source, StringComparison.Ordinal);
        Assert.Contains("Page.Orders", source, StringComparison.Ordinal);
        Assert.Contains("Page.Quotations", source, StringComparison.Ordinal);
        Assert.Contains("Page.Invoices", source, StringComparison.Ordinal);
        Assert.Contains("CustomerHistorySourceState.Available", source, StringComparison.Ordinal);
        Assert.Contains("Href=\"@RecordHref(item)\"", source, StringComparison.Ordinal);
        Assert.Contains("AriaLabel=\"@ActivityTitle(item)\"", source, StringComparison.Ordinal);
        Assert.Contains("\"/Orders/View?id=", source, StringComparison.Ordinal);
        Assert.Contains("\"/Quotations/View?id=", source, StringComparison.Ordinal);
        Assert.Contains("\"/Invoices/View?id=", source, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(item.Currency)", source, StringComparison.Ordinal);
        AssertPresentationalOnly(source);
    }

    [Fact]
    public void CustomerHistoryTable_ValidatesOneMatchingPageAndBoundsPageChanges()
    {
        var source = ReadCustomerComponent("CustomerHistoryTable.razor");

        Assert.Contains("<section", source, StringComparison.Ordinal);
        Assert.Contains("HtmlTag=\"h2\"", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter, EditorRequired] public CustomerHistoryKind Kind", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public OrderListPage? Orders", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public QuotationListPage? Quotations", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public InvoiceListPage? Invoices", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public bool Loading", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public string? Error", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public EventCallback Retry", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public EventCallback<int> PageChanged", source, StringComparison.Ordinal);
        Assert.Contains("protected override void OnParametersSet()", source, StringComparison.Ordinal);
        Assert.Contains("ValidatePageContract", source, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp", source, StringComparison.Ordinal);
        Assert.Contains("<MudProgressLinear", source, StringComparison.Ordinal);
        Assert.Contains("<MudAlert", source, StringComparison.Ordinal);
        Assert.Contains("Items.Count == 0", source, StringComparison.Ordinal);
        Assert.Contains("Href=\"@($\"/Orders/View?id={order.Id}\")\"", source, StringComparison.Ordinal);
        Assert.Contains("Href=\"@($\"/Quotations/View?id={quotation.Id}\")\"", source, StringComparison.Ordinal);
        Assert.Contains("Href=\"@($\"/Invoices/View?id={invoice.Id}\")\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("order.Subtotal", source, StringComparison.Ordinal);
        Assert.DoesNotContain("quotation.Subtotal", source, StringComparison.Ordinal);
        Assert.DoesNotContain("quotation.Total", source, StringComparison.Ordinal);
        Assert.Contains("invoice.Currency", source, StringComparison.Ordinal);
        AssertPresentationalOnly(source);
    }

    [Fact]
    public void CustomerViewSlice_PreservesRouteAuthorizationDtoAndRollbackContracts()
    {
        var root = FindRoot();
        var featurePage = Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Customers",
            "Pages",
            "CustomerView.razor");
        var featureStyles = Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Customers",
            "Pages",
            "CustomerView.razor.css");
        var contracts = Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Contracts",
            "CustomerDetailContracts.cs");
        var proxy = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Bff",
            "Customers",
            "CustomersProxy.cs"));
        var bffProgram = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Bff",
            "Program.cs"));

        Assert.True(File.Exists(featurePage), "The lazy WASM customer detail route is missing.");
        var page = File.ReadAllText(featurePage);
        Assert.Contains("@page \"/Customers/View\"", page, StringComparison.Ordinal);
        Assert.Contains("[Authorize", page, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery(Name = \"id\")]", page, StringComparison.Ordinal);
        Assert.Contains("/bff/customers/{Id}", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Unauthorized", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Forbidden", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.NotFound", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.TooManyRequests", page, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", page, StringComparison.Ordinal);
        Assert.Contains("Href=\"/Customers/Index\"", page, StringComparison.Ordinal);
        var styles = File.ReadAllText(featureStyles);
        Assert.Contains("a[href^=\"mailto:\"]", styles, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", styles, StringComparison.Ordinal);

        Assert.True(File.Exists(contracts), "The browser-safe full customer detail DTO is missing.");
        var contractSource = File.ReadAllText(contracts);
        Assert.Contains("CustomerDetail", contractSource, StringComparison.Ordinal);
        Assert.Contains("CustomerCompanyDetail", contractSource, StringComparison.Ordinal);
        Assert.Contains("CustomerAddressDetail", contractSource, StringComparison.Ordinal);

        Assert.Contains("GetByIdAsync", proxy, StringComparison.Ordinal);
        Assert.Contains("$\"/customers/{id}\"", proxy, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/customers/{id:int}\"", bffProgram, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.CustomersRead", bffProgram, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Customers", "View.cshtml")),
            "The Razor customer detail rollback page must remain in this slice.");
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Customers", "View.cshtml.cs")),
            "The Razor customer detail rollback PageModel must remain in this slice.");
    }

    [Fact]
    public void CustomerView_IntegratesPermissionScopedUrlBackedHistoryWithoutReloadingOverview()
    {
        var root = FindRoot();
        var page = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Customers",
            "Pages",
            "CustomerView.razor"));
        var styles = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Customers",
            "Pages",
            "CustomerView.razor.css"));

        Assert.Contains("[SupplyParameterFromQuery(Name = \"tab\")]", page, StringComparison.Ordinal);
        Assert.Contains("Navigation.GetUriWithQueryParameter(\"tab\"", page, StringComparison.Ordinal);
        Assert.Contains("<MudTabs", page, StringComparison.Ordinal);
        Assert.Contains("<CustomerOverview", page, StringComparison.Ordinal);
        Assert.Contains("<CustomerActivity", page, StringComparison.Ordinal);
        Assert.Contains("<CustomerHistoryTable", page, StringComparison.Ordinal);
        Assert.Contains("legacy.orders.read", page, StringComparison.Ordinal);
        Assert.Contains("legacy.quotations.read", page, StringComparison.Ordinal);
        Assert.Contains("legacy.accounting.read", page, StringComparison.Ordinal);
        Assert.Contains("/bff/customers/{Id}/activity", page, StringComparison.Ordinal);
        Assert.Contains("/bff/customers/{Id}/orders", page, StringComparison.Ordinal);
        Assert.Contains("/bff/customers/{Id}/quotations", page, StringComparison.Ordinal);
        Assert.Contains("/bff/customers/{Id}/invoices", page, StringComparison.Ordinal);
        Assert.Contains("item.CustomerId != Id", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.ServiceUnavailable", page, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource", page, StringComparison.Ordinal);
        Assert.Contains("<MudForm", page, StringComparison.Ordinal);

        Assert.Contains("overflow-x: auto", styles, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", styles, StringComparison.Ordinal);
        Assert.Contains("forced-colors: active", styles, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion: reduce", styles, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static string ReadCustomerComponent(string fileName)
    {
        var path = Path.Combine(
            FindRoot(),
            "Legacy.Maliev.Intranet.Client.Features.Customers",
            "Components",
            fileName);

        Assert.True(File.Exists(path), $"The Task 5 customer component '{fileName}' is missing.");
        return File.ReadAllText(path);
    }

    private static void AssertPresentationalOnly(string source)
    {
        Assert.DoesNotContain("@inject HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Http.Get", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<a ", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<MudLink", source, StringComparison.Ordinal);
    }
}
