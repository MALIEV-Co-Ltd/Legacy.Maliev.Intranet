# Customer History and Site Link System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish explicit Shadcn-style link roles across the Intranet and turn the customer detail route into a permission-scoped operational workspace with Overview, Activity, Orders, Quotations, and Invoices tabs backed by real customer-owned service queries.

**Architecture:** A small shared `LegacyLink` primitive owns inline, record, navigation-action, and external link semantics without globally restyling every anchor. Customer history uses existing customer-filtered OrderService, QuotationService, and AccountingService routes through typed BFF proxies; a focused BFF aggregator composes a bounded activity feed while family endpoints retain independent pagination and failure states. The Blazor feature splits tab content into focused components and keeps the selected tab in the URL.

**Tech Stack:** .NET 10, Blazor WebAssembly, MudBlazor 9.7, `Maliev.ShadcnBlazor` tokens and adapter CSS, ASP.NET Core minimal APIs, typed `HttpClient` proxies, xUnit, `WebApplicationFactory`, Playwright.

## Global Constraints

- Preserve all existing routes, query strings, click handlers, authorization conditions, localized strings, and save/cancel behavior unless this plan explicitly extends them.
- All browser reads use same-origin `/bff/*` endpoints; service URLs and credentials remain server-side.
- Customer ownership is enforced by explicit downstream customer routes, never by search text or client-side filtering.
- Unauthorized record families are absent from the UI; they are not represented as empty data.
- Each history family has independent loading, success, empty, error, retry, and pagination state.
- All new user-facing and accessible text is localized in English and Thai.
- Use existing Bangkok-aware date helpers and record-owned currency values.
- Desktop controls retain 36px Shadcn density; narrow/coarse-pointer targets are at least 44 by 44 CSS pixels.
- No horizontal document overflow at 1280, 768, 390, or 320 CSS pixels; verify 200 percent zoom.
- WCAG 2.2 AA, visible `focus-visible`, keyboard tab semantics, reduced motion, forced colors, English, and Thai are acceptance requirements.
- Do not deploy. Commit each independently validated slice; do not push without separate authorization.
- Preserve the pre-existing untracked `.impeccable/critique/2026-08-11T09-28-51Z__et-client-features-customers-pages-customers-razor.md` file.

---

## File and responsibility map

### Shared link system

- Create `Legacy.Maliev.Intranet.Client.Shared/Components/LegacyLink.razor` — typed semantic link primitive.
- Create `Legacy.Maliev.Intranet.Client.Shared/Components/LegacyLink.razor.css` — role variants, hover, focus-visible, truncation, responsive target sizes, forced-colors behavior.
- Create `Legacy.Maliev.Intranet.Client.Shared/Components/LegacyLinkRole.cs` — `Inline`, `Record`, `Navigation`, and `External` enum.
- Modify production Razor files returned by the site-wide anchor inventory — migrate raw content links, `MudLink`, and text-variant `MudButton Href` navigation while retaining specialized brand, skip, rail, menu, and primary CTA components.
- Create `Legacy.Maliev.Intranet.Tests/LegacyLinkSystemContractTests.cs` — component API, allowlist, route preservation, and raw-link migration contract.
- Create `Maliev.ShadcnBlazor.BrowserTests/LegacyLinkBrowserTests.cs` — rendered role, focus, target, truncation, and destination checks.

### Customer history boundary

- Modify `Legacy.Maliev.Intranet.Contracts/OrderListContracts.cs` — retain order creation/modification timestamps required by customer activity.
- Create `Legacy.Maliev.Intranet.Contracts/CustomerHistoryContracts.cs` — browser-safe event, source-state, counts, and activity contracts.
- Modify `Legacy.Maliev.Intranet.Bff/Orders/OrdersProxy.cs` — expose the existing customer-owned order query to customer history endpoints.
- Modify `Legacy.Maliev.Intranet.Bff/Quotations/QuotationsProxy.cs` — add customer-owned quotation page query.
- Modify `Legacy.Maliev.Intranet.Bff/Accounting/InvoicesProxy.cs` — add customer-owned invoice page query without forcing a paid filter.
- Create `Legacy.Maliev.Intranet.Bff/Customers/CustomerHistoryEndpointMapper.cs` — normalize the three paged family endpoints.
- Create `Legacy.Maliev.Intranet.Bff/Customers/CustomerActivityAggregator.cs` — permission-aware bounded composition and deterministic ordering.
- Modify `Legacy.Maliev.Intranet.Bff/Program.cs` — map customer-scoped history endpoints and register the aggregator.
- Create `Legacy.Maliev.Intranet.Tests/BffCustomerHistoryContractTests.cs` — exact downstream paths, auth, projection, partial failure, and secret-boundary tests.

### Customer workspace

- Create `Legacy.Maliev.Intranet.Client.Features.Customers/Components/CustomerOverview.razor` and `.razor.css` — existing contact/company/address/record sections and edit form.
- Create `Legacy.Maliev.Intranet.Client.Features.Customers/Components/CustomerActivity.razor` and `.razor.css` — bounded newest-first timeline.
- Create `Legacy.Maliev.Intranet.Client.Features.Customers/Components/CustomerHistoryTable.razor` and `.razor.css` — typed family table/card renderer, retry, empty state, and pager.
- Modify `Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.razor` and `.razor.css` — identity/actions, URL-backed tabs, independent history loading, permission gating, and composition.
- Modify `Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.resx` and `CustomerView.th.resx` — tab, activity, table, state, pagination, and record-specific accessible names.
- Modify `Legacy.Maliev.Intranet.Tests/CustomersViewWasmMigrationContractTests.cs` — route/tab/query/localization/state source contract.
- Modify `Maliev.ShadcnBlazor.BrowserTests/CustomerDetailBrowserTests.cs` — production-client contract stubs and live responsive/keyboard/error coverage.

---

### Task 1: Add the shared semantic link primitive

**Files:**
- Create: `Legacy.Maliev.Intranet.Client.Shared/Components/LegacyLinkRole.cs`
- Create: `Legacy.Maliev.Intranet.Client.Shared/Components/LegacyLink.razor`
- Create: `Legacy.Maliev.Intranet.Client.Shared/Components/LegacyLink.razor.css`
- Create: `Legacy.Maliev.Intranet.Tests/LegacyLinkSystemContractTests.cs`
- Create: `Maliev.ShadcnBlazor.BrowserTests/LegacyLinkBrowserTests.cs`

**Interfaces:**
- Consumes: existing `--shadcn-foreground`, `--shadcn-muted-foreground`, `--shadcn-accent`, `--shadcn-accent-foreground`, `--shadcn-ring`, `--shadcn-radius-md`, and `--shadcn-control-height` tokens.
- Produces: `LegacyLinkRole` and `<LegacyLink Href Role StartIcon Target Rel AriaLabel Disabled Class>` for later migration tasks.

- [ ] **Step 1: Write failing source and browser contracts**

Add tests that require the enum and component parameters, render each role, and verify navigation has no resting border while `focus-visible` has a semantic ring:

```csharp
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
```

```csharp
[Theory]
[InlineData(1280, 36)]
[InlineData(390, 44)]
public async Task NavigationLinkUsesShadcnGeometryAndFocusVisible(int width, double minimumHeight)
{
    await page.SetViewportSizeAsync(width, 800);
    var link = page.Locator("[data-link-role=navigation]").First;
    Assert.Equal("0px", await link.EvaluateAsync<string>("e => getComputedStyle(e).borderTopWidth"));
    await link.FocusAsync();
    Assert.True(await link.EvaluateAsync<double>("e => e.getBoundingClientRect().height") >= minimumHeight);
    Assert.NotEqual("none", await link.EvaluateAsync<string>("e => getComputedStyle(e).boxShadow"));
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter FullyQualifiedName~LegacyLinkSystemContractTests
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --filter FullyQualifiedName~LegacyLinkBrowserTests
```

Expected: source contract fails because the component files do not exist; browser contract fails because no rendered role markers exist.

- [ ] **Step 3: Implement the typed primitive**

Use an anchor only when enabled; use a non-interactive span when disabled so the DOM cannot navigate:

```razor
@namespace Legacy.Maliev.Intranet.Client.Shared.Components

@if (Disabled)
{
    <span class="@CssClass" aria-disabled="true" @attributes="AdditionalAttributes">
        @IconMarkup
        <span class="legacy-link__label">@ChildContent</span>
    </span>
}
else
{
    <a href="@Href" class="@CssClass" target="@Target" rel="@EffectiveRel"
       aria-label="@AriaLabel" data-link-role="@RoleName" @attributes="AdditionalAttributes">
        @IconMarkup
        <span class="legacy-link__label">@ChildContent</span>
    </a>
}

@code {
    [Parameter, EditorRequired] public string Href { get; set; } = string.Empty;
    [Parameter] public LegacyLinkRole Role { get; set; } = LegacyLinkRole.Inline;
    [Parameter] public string? StartIcon { get; set; }
    [Parameter] public string? Target { get; set; }
    [Parameter] public string? Rel { get; set; }
    [Parameter] public string? AriaLabel { get; set; }
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public string? Class { get; set; }
    [Parameter, EditorRequired] public RenderFragment? ChildContent { get; set; }
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private string RoleName => Role.ToString().ToLowerInvariant();
    private string CssClass => $"legacy-link legacy-link--{RoleName} {Class}".Trim();
    private string? EffectiveRel => Target == "_blank" ? string.Join(' ', new[] { Rel, "noopener", "noreferrer" }.Where(value => !string.IsNullOrWhiteSpace(value)).SelectMany(value => value!.Split(' ')).Distinct()) : Rel;
    private RenderFragment? IconMarkup => string.IsNullOrWhiteSpace(StartIcon) ? null : builder =>
    {
        builder.OpenComponent<MudIcon>(0);
        builder.AddAttribute(1, nameof(MudIcon.Icon), StartIcon);
        builder.AddAttribute(2, nameof(MudIcon.Size), Size.Small);
        builder.AddAttribute(3, "aria-hidden", "true");
        builder.CloseComponent();
    };
}
```

Implement role CSS with `:focus-visible`, a 3px ring, underline offset, record truncation, navigation hover background, 36/44px sizing, reduced-motion, and forced-colors overrides. Do not add a global `a { ... }` selector.

- [ ] **Step 4: Run GREEN tests and build the shared project**

```powershell
dotnet build .\Legacy.Maliev.Intranet.Client.Shared\Legacy.Maliev.Intranet.Client.Shared.csproj -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter FullyQualifiedName~LegacyLinkSystemContractTests
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --filter FullyQualifiedName~LegacyLinkBrowserTests
```

Expected: build has zero warnings/errors; focused tests pass.

- [ ] **Step 5: Commit the primitive**

```powershell
git add -- Legacy.Maliev.Intranet.Client.Shared/Components/LegacyLinkRole.cs Legacy.Maliev.Intranet.Client.Shared/Components/LegacyLink.razor Legacy.Maliev.Intranet.Client.Shared/Components/LegacyLink.razor.css Legacy.Maliev.Intranet.Tests/LegacyLinkSystemContractTests.cs Maliev.ShadcnBlazor.BrowserTests/LegacyLinkBrowserTests.cs
git diff --cached --check
git commit -m "feat: add semantic Shadcn link roles"
```

### Task 2: Migrate production content and navigation links

**Files:**
- Modify: `Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Pages/Login.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Components/Dashboard/DashboardPanel.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Components/Dashboard/DashboardMetricCard.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Customers/Pages/Customers.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerCreate.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Employees/Pages/Employees.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeView.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeCreate.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeForgotPassword.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeEmailConfirmation.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Employees/Pages/EmployeeResetPassword.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/Materials.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/MaterialCreate.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/MaterialDetail.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Pages/Orders.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderCreate.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderDetail.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/Suppliers.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/SupplierView.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/SupplierCreate.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrders.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrderView.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrderCreate.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Invoices.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceView.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceCreate.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Finances.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/FinanceView.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/NetProfitChart.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/YearlyActivityChart.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/Index.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/View.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Index.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/View.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Create.razor`
- Modify: `Legacy.Maliev.Intranet.Tests/LegacyLinkSystemContractTests.cs`
- Modify: `Maliev.ShadcnBlazor.BrowserTests/LegacyLinkBrowserTests.cs`

**Interfaces:**
- Consumes: `LegacyLink` from Task 1.
- Produces: explicit link roles throughout production content; specialized brand, skip, rail, profile-menu, download-button, and primary CTA links remain allowlisted.

- [ ] **Step 1: Freeze the inventory and allowlist in a failing test**

Enumerate all production `.razor` files and fail for raw `<a>`, `<MudLink>`, or `MudButton Href` used only as a text-style link, except these specialized owners: `legacy-skip-link`, `legacy-topbar-logo`, `legacy-rail-logo`, `legacy-profile-action`, `legacy-login-brand`, primary/outlined CTA buttons, and download/print actions. Assert that every record link has a record-specific accessible name rather than a bare repeated `View` name.

- [ ] **Step 2: Run the inventory test and verify RED**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter FullyQualifiedName~LegacyLinkSystemContractTests
```

Expected: failures name the existing raw/Mud content links and text-button navigation actions.

- [ ] **Step 3: Migrate by behavior role**

Use these exact patterns:

```razor
<LegacyLink Href="@($"/Orders/View?id={order.Id}")"
            Role="LegacyLinkRole.Record"
            AriaLabel="@Text["ViewOrderAccessible", order.Id]">
    #@order.Id
</LegacyLink>
```

```razor
<LegacyLink Href="/Customers/Index"
            Role="LegacyLinkRole.Navigation"
            StartIcon="@Icons.Material.Filled.ArrowBack"
            AriaLabel="@Text["BackToCustomers"]">
    @Text["BackToCustomers"]
</LegacyLink>
```

```razor
<LegacyLink Href="@file.Uri.ToString()" Role="LegacyLinkRole.External" Target="_blank">
    @file.ObjectName
</LegacyLink>
```

Do not convert create/sign-in primary CTAs, download/label buttons, brand links, skip links, rail links, or menu items.

- [ ] **Step 4: Verify routes, accessible names, and representative browser states**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~LegacyLinkSystemContractTests|FullyQualifiedName~PresentationBoundaryTests"
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --filter FullyQualifiedName~LegacyLinkBrowserTests
```

Expected: all original `href`, target, rel, query, localization, and authorization conditions remain; keyboard focus is visible without a resting border.

- [ ] **Step 5: Commit the migration**

Stage only the inventoried production pages and link tests, run `git diff --cached --check`, and commit:

```powershell
git commit -m "style: standardize Intranet link roles"
```

### Task 3: Add customer-scoped history proxy endpoints

**Files:**
- Modify: `Legacy.Maliev.Intranet.Contracts/OrderListContracts.cs`
- Modify: `Legacy.Maliev.Intranet.Bff/Orders/OrdersProxy.cs`
- Modify: `Legacy.Maliev.Intranet.Bff/Quotations/QuotationsProxy.cs`
- Modify: `Legacy.Maliev.Intranet.Bff/Accounting/InvoicesProxy.cs`
- Create: `Legacy.Maliev.Intranet.Bff/Customers/CustomerHistoryEndpointMapper.cs`
- Modify: `Legacy.Maliev.Intranet.Bff/Program.cs`
- Create: `Legacy.Maliev.Intranet.Tests/BffCustomerHistoryContractTests.cs`

**Interfaces:**
- Consumes: existing `OrderListPage`, `QuotationListPage`, and `InvoiceListPage` projections and service routes `/Orders/customers/{customerId}`, `/quotations/customers/{customerId}`, `/invoices/customers/{customerId}`.
- Produces: `/bff/customers/{customerId}/orders`, `/quotations`, and `/invoices` with bounded `index`/`size`, existing sort enums, and existing family permissions.

- [ ] **Step 1: Write failing BFF boundary tests**

For each family, authenticate with only the exact read permission and assert the downstream path includes the customer route:

```csharp
[Theory]
[InlineData("orders", "/Orders/customers/42?sort=OrderCreatedDate_Descending&search=&index=1&size=100")]
[InlineData("quotations", "/quotations/customers/42?sort=QuotationCreatedDate_Descending&search=&index=1&size=100")]
[InlineData("invoices", "/invoices/customers/42?sort=InvoiceCreatedDate_Descending&search=&index=1&size=100")]
public async Task CustomerFamily_ForwardsExplicitOwnerRoute(string family, string expected)
{
    using var response = await client.GetAsync($"/bff/customers/42/{family}?index=-3&size=999");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(expected, downstream.PathAndQuery);
}
```

Also assert anonymous is 401, missing family permission is 403 before downstream, mismatched `CustomerId` in any returned item is 502, invalid JSON is 502 without payload leakage, 429 preserves bounded Retry-After, timeout/transport becomes 503, and downstream 404 becomes an empty page for the requested index.

- [ ] **Step 2: Run the BFF tests and verify RED**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter FullyQualifiedName~BffCustomerHistoryContractTests
```

Expected: all new routes return 404 because they are not mapped.

- [ ] **Step 3: Add exact customer proxy methods**

```csharp
public Task<HttpResponseMessage> GetCustomerPageAsync(int customerId, QuotationListSort sort, string? search, int index, int size, CancellationToken token) =>
    SendAsync($"/quotations/customers/{customerId}?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}", token);
```

```csharp
public Task<HttpResponseMessage> GetCustomerPageAsync(int customerId, InvoiceListSort sort, string? search, int index, int size, CancellationToken token) =>
    SendAsync($"/invoices/customers/{customerId}?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}", token);
```

Reuse `OrdersProxy.GetCustomerAsync`. Add `DateTime? CreatedDate` and `DateTime? ModifiedDate` at the end of `OrderListItem`, updating its construction sites and contracts so OrderService timestamps survive the BFF projection. The endpoint mapper validates `customerId > 0`, clamps index to at least 1 and size to 1..100, projects only existing browser-safe list contracts, and rejects any item whose `CustomerId` differs from the route.

- [ ] **Step 4: Map independently authorized family endpoints**

```csharp
app.MapGet("/bff/customers/{customerId:int}/orders", CustomerHistoryEndpointMapper.OrdersAsync)
   .RequireAuthorization(LegacyEmployeePermissions.OrdersRead);
app.MapGet("/bff/customers/{customerId:int}/quotations", CustomerHistoryEndpointMapper.QuotationsAsync)
   .RequireAuthorization(LegacyEmployeePermissions.QuotationsRead);
app.MapGet("/bff/customers/{customerId:int}/invoices", CustomerHistoryEndpointMapper.InvoicesAsync)
   .RequireAuthorization(LegacyEmployeePermissions.AccountingRead);
```

- [ ] **Step 5: Build and run GREEN boundary tests**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~BffCustomerHistoryContractTests|FullyQualifiedName~BffOrdersProxyContractTests|FullyQualifiedName~BffQuotationsProxyContractTests|FullyQualifiedName~BffInvoicesProxyContractTests"
```

Expected: zero build warnings/errors and all focused proxy tests pass.

- [ ] **Step 6: Commit the family endpoints**

```powershell
git add -- Legacy.Maliev.Intranet.Contracts/OrderListContracts.cs Legacy.Maliev.Intranet.Bff/Orders/OrdersProxy.cs Legacy.Maliev.Intranet.Bff/Quotations/QuotationsProxy.cs Legacy.Maliev.Intranet.Bff/Accounting/InvoicesProxy.cs Legacy.Maliev.Intranet.Bff/Customers/CustomerHistoryEndpointMapper.cs Legacy.Maliev.Intranet.Bff/Program.cs Legacy.Maliev.Intranet.Tests/BffCustomerHistoryContractTests.cs
git diff --cached --check
git commit -m "feat: expose customer-owned history pages"
```

### Task 4: Compose the permission-aware customer activity feed

**Files:**
- Create: `Legacy.Maliev.Intranet.Contracts/CustomerHistoryContracts.cs`
- Create: `Legacy.Maliev.Intranet.Bff/Customers/CustomerActivityAggregator.cs`
- Modify: `Legacy.Maliev.Intranet.Bff/Program.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/BffCustomerHistoryContractTests.cs`

**Interfaces:**
- Consumes: customer family proxy methods from Task 3 and `HttpContext.User` permission claims.
- Produces: `CustomerActivityPage(IReadOnlyList<CustomerActivityItem> Items, CustomerHistorySourceSummary Orders, CustomerHistorySourceSummary Quotations, CustomerHistorySourceSummary Invoices)` from `GET /bff/customers/{customerId}/activity?size=20`.

- [ ] **Step 1: Define and test the exact activity contract**

```csharp
public enum CustomerHistoryKind { Order, Quotation, Invoice }
public enum CustomerHistorySourceState { Available, Forbidden, RateLimited, Unavailable, InvalidResponse }
public enum CustomerActivityStatus { InProgress, Complete, Open, Accepted, Declined, Paid, Outstanding }
public sealed record CustomerHistorySourceSummary(CustomerHistorySourceState State, int? TotalRecords);
public sealed record CustomerActivityItem(
    CustomerHistoryKind Kind,
    int Id,
    string? Label,
    CustomerActivityStatus Status,
    int? CompletedUnits,
    int? TotalUnits,
    decimal? Amount,
    string? Currency,
    DateTime Timestamp);
public sealed record CustomerActivityPage(
    IReadOnlyList<CustomerActivityItem> Items,
    CustomerHistorySourceSummary Orders,
    CustomerHistorySourceSummary Quotations,
    CustomerHistorySourceSummary Invoices);
```

Tests require newest-first ordering with `Kind` then `Id` tie-breaking, no event for a missing timestamp, only authorized downstream calls, nullable currency where the source projection does not own a display currency, and partial success when one authorized service is unavailable or rate-limited.

- [ ] **Step 2: Run activity tests and verify RED**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter FullyQualifiedName~BffCustomerHistoryContractTests.Activity
```

Expected: missing contract/aggregator failures.

- [ ] **Step 3: Implement bounded deterministic composition**

Fetch at most `size` records from each authorized source concurrently. Map order `ModifiedDate ?? CreatedDate` and `InProgress`/`Complete` with manufactured and total units; quotation `ModifiedDate ?? CreatedDate` and `Open`/`Accepted`/`Declined`; invoice `PaymentDate ?? CreatedDate` and `Paid`/`Outstanding`. Discard missing timestamps, order descending, tie-break by kind and descending identifier, and return at most the clamped 1..50 requested items. Do not localize event text in the BFF.

- [ ] **Step 4: Map and authorize the activity endpoint**

Require authenticated customer read access at the endpoint; the aggregator checks the three family permissions and never calls a forbidden source. Return 401 only for the employee session boundary; encode source-specific failures in `CustomerHistorySourceSummary`.

- [ ] **Step 5: Build, test, and commit**

```powershell
dotnet build .\Legacy.Maliev.Intranet.Bff\Legacy.Maliev.Intranet.Bff.csproj -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter FullyQualifiedName~BffCustomerHistoryContractTests
git add -- Legacy.Maliev.Intranet.Contracts/CustomerHistoryContracts.cs Legacy.Maliev.Intranet.Bff/Customers/CustomerActivityAggregator.cs Legacy.Maliev.Intranet.Bff/Program.cs Legacy.Maliev.Intranet.Tests/BffCustomerHistoryContractTests.cs
git diff --cached --check
git commit -m "feat: compose customer activity history"
```

### Task 5: Build focused customer history components

**Files:**
- Create: `Legacy.Maliev.Intranet.Client.Features.Customers/Components/CustomerOverview.razor`
- Create: `Legacy.Maliev.Intranet.Client.Features.Customers/Components/CustomerOverview.razor.css`
- Create: `Legacy.Maliev.Intranet.Client.Features.Customers/Components/CustomerActivity.razor`
- Create: `Legacy.Maliev.Intranet.Client.Features.Customers/Components/CustomerActivity.razor.css`
- Create: `Legacy.Maliev.Intranet.Client.Features.Customers/Components/CustomerHistoryTable.razor`
- Create: `Legacy.Maliev.Intranet.Client.Features.Customers/Components/CustomerHistoryTable.razor.css`
- Modify: `Legacy.Maliev.Intranet.Tests/CustomersViewWasmMigrationContractTests.cs`

**Interfaces:**
- Consumes: `CustomerDetail`, `CustomerActivityPage`, and existing order/quotation/invoice list contracts.
- Produces: overview, activity, and reusable history-table renderers with typed callbacks; no component performs cross-family fetching.

- [ ] **Step 1: Write failing component source contracts**

Require semantic `section`/heading structure, `MudAlert` errors, `MudProgressLinear` loading, explicit empty state, record-specific `LegacyLink` accessible names, bounded page callbacks, and no raw anchor/MudLink.

- [ ] **Step 2: Run source tests and verify RED**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter FullyQualifiedName~CustomersViewWasmMigrationContractTests
```

- [ ] **Step 3: Extract Overview without behavioral changes**

Move the current contact/company/address/record/edit markup into `CustomerOverview`. Pass `Customer`, `CanEdit`, `Editing`, `Submitting`, localized display delegates, and edit/save/cancel callbacks explicitly. Preserve the existing `MudForm`, validation model, field bindings, and presentation helpers in the owning page.

- [ ] **Step 4: Implement activity and typed family table rendering**

`CustomerActivity` formats each kind with localized text and a record-specific `LegacyLink`. Order status is localized from `Manufactured` and `Quantity`; quotation status is Open, Accepted, or Declined from `Accepted`; invoice status is Paid or Outstanding from `IsPaid`. `CustomerHistoryTable` accepts `CustomerHistoryKind Kind`, exactly one of `OrderListPage? Orders`, `QuotationListPage? Quotations`, or `InvoiceListPage? Invoices`, plus `Loading`, `Error`, `Retry`, and `PageChanged` callbacks; validate that the page matching `Kind` is the only non-null page, then render only fields owned by that contract. Keep amounts/currency absent when the projection does not provide a display currency.

- [ ] **Step 5: Build and run focused tests**

```powershell
dotnet build .\Legacy.Maliev.Intranet.Client.Features.Customers\Legacy.Maliev.Intranet.Client.Features.Customers.csproj -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter FullyQualifiedName~CustomersViewWasmMigrationContractTests
```

- [ ] **Step 6: Commit the components**

```powershell
git add -- Legacy.Maliev.Intranet.Client.Features.Customers/Components Legacy.Maliev.Intranet.Tests/CustomersViewWasmMigrationContractTests.cs
git diff --cached --check
git commit -m "refactor: split customer workspace components"
```

### Task 6: Integrate URL-backed, permission-scoped customer tabs

**Files:**
- Modify: `Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.resx`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.th.resx`
- Modify: `Legacy.Maliev.Intranet.Tests/CustomersViewWasmMigrationContractTests.cs`
- Modify: `Maliev.ShadcnBlazor.BrowserTests/CustomerDetailBrowserTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 3, 4, and 5.
- Produces: `/Customers/View?id={id}&tab={overview|activity|orders|quotations|invoices}` with permission-gated tabs and independent lazy-loaded family state.

- [ ] **Step 1: Add failing production-browser scenarios**

Extend the real-client fixture to intercept `/bff/customers/69738/activity`, `/orders`, `/quotations`, and `/invoices`. Test:

- default Overview and deep-linked tab selection;
- Back/forward history preserving `tab`;
- unauthorized family tabs absent from role queries and tab order;
- authorized tabs lazy-load once, retry independently, and paginate without reloading customer Overview;
- Activity partial failure retains successful events and exposes localized source warning;
- each table uses record-specific link names and correct destinations;
- English/Thai strings, dark mode, reduced motion, forced colors;
- 1280/768/390/320 geometry and 200 percent zoom;
- no document overflow and 44px narrow tab/pager/link targets.

- [ ] **Step 2: Run browser and source tests and verify RED**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter FullyQualifiedName~CustomersViewWasmMigrationContractTests
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --filter FullyQualifiedName~CustomerDetailBrowserTests
```

- [ ] **Step 3: Add URL-backed tab state**

Add `[SupplyParameterFromQuery(Name = "tab")] public string? Tab { get; set; }`, normalize unknown/missing values to `overview`, and update the URL with `Navigation.GetUriWithQueryParameter("tab", value)` using history-friendly navigation. Render `MudTabs`/`MudTabPanel` with accessible labels and panels only for permissions present in the session projection.

- [ ] **Step 4: Add independent lazy-loading state**

Keep separate cancellation-safe loaders and state fields for activity, orders, quotations, and invoices. A tab loader handles 401 by returning to Login; 403 removes/blocks that family; 429 renders localized rate-limit state; 502/503 renders localized retry state; success validates every returned `CustomerId == Id` before display.

- [ ] **Step 5: Implement responsive hierarchy and localization**

Use a contained tab list and card/table patterns. Add exact English/Thai resource keys for `Overview`, `Activity`, `Orders`, `Quotations`, `Invoices`, result counts, statuses, empty/error/retry copy, pagination labels, source warnings, and record-specific accessible names. Keep the Back action in the header/footer as a navigation-role `LegacyLink` with `ArrowBack`.

- [ ] **Step 6: Run GREEN focused validation**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~CustomersViewWasmMigrationContractTests|FullyQualifiedName~BffCustomerHistoryContractTests|FullyQualifiedName~LegacyLinkSystemContractTests"
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --filter "FullyQualifiedName~CustomerDetailBrowserTests|FullyQualifiedName~LegacyLinkBrowserTests"
```

- [ ] **Step 7: Commit the integrated workspace**

```powershell
git add -- Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.razor Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.razor.css Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.resx Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.th.resx Legacy.Maliev.Intranet.Tests/CustomersViewWasmMigrationContractTests.cs Maliev.ShadcnBlazor.BrowserTests/CustomerDetailBrowserTests.cs
git diff --cached --check
git commit -m "feat: add customer operational history tabs"
```

### Task 7: Run the final quality and integration gate

**Files:**
- Modify only if a gate exposes an in-scope defect; add regression coverage before each correction.
- Do not edit or commit the existing `.impeccable/critique/*` artifact.

**Interfaces:**
- Consumes: all prior task commits.
- Produces: evidence that the link system and customer workspace satisfy build, contract, browser, localization, accessibility, responsive, and packaging boundaries.

- [ ] **Step 1: Confirm repository scope and clean diffs**

```powershell
git status --short --branch
git diff --check
git log --oneline -8
```

Record the pre-existing untracked critique file and ensure no temp screenshots, secrets, logs, or build artifacts are staged.

- [ ] **Step 2: Run build-first Release validation**

```powershell
dotnet restore .\Legacy.Maliev.Intranet.slnx
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release --no-restore
```

Expected: zero warnings and zero errors. If live Aspire locks normal outputs, use one exact isolated `--artifacts-path` for restore and build; do not stop the user's Aspire instance without authorization.

- [ ] **Step 3: Run focused and full automated suites**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~CustomerHistory|FullyQualifiedName~CustomerView|FullyQualifiedName~LegacyLink"
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --no-build
dotnet format .\Legacy.Maliev.Intranet.slnx --verify-no-changes --no-restore
```

Report exact pass/fail/skip counts. Do not treat an isolated-output source-root failure as a product result; rerun source-reading tests from the repository-local build lane.

- [ ] **Step 4: Run exactly one final Impeccable detector pass**

```powershell
node C:\Users\natth\.agents\skills\impeccable\scripts\detect.mjs --json Legacy.Maliev.Intranet.Client.Shared\Components\LegacyLink.razor Legacy.Maliev.Intranet.Client.Features.Customers\Pages\CustomerView.razor Legacy.Maliev.Intranet.Client.Features.Customers\Components\CustomerActivity.razor Legacy.Maliev.Intranet.Client.Features.Customers\Components\CustomerHistoryTable.razor
```

Record exact JSON output. Run this command once only, after all UI corrections.

- [ ] **Step 5: Inspect the real production-client route**

Use the real standalone production Client or healthy Aspire route with only external same-origin BFF boundaries controlled in the test fixture. Capture and read back Overview, Activity, Orders, Quotations, and Invoices at 1280, 768, 390, and 320; repeat 320 in Thai, one dark-mode pass, forced colors, reduced motion, and modeled 200 percent zoom. Verify tab keyboard order, Enter/Space activation, focus-visible rings, Back destination, record destinations, independent retry, pagination, partial failures, and zero console/page errors.

- [ ] **Step 6: Audit the final staged scope and commit any gate fixes**

```powershell
git diff --check
git status --short
git diff --name-only HEAD~7..HEAD
```

If the gate required fixes, each fix must have a preceding failing regression test and its own scoped commit. Otherwise create no empty commit. Confirm no deployment or push occurred.
