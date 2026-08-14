# Operational Tables, Shell Navigation, and Material Icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a coherent, responsive MALIEV operational-table and workspace-shell system with separate detail and single-row quick-view actions, icon-only refresh, hierarchical navigation, breadcrumbs, aligned top-bar zones, and one Google Material icon vocabulary.

**Architecture:** Shared components own reusable behavior and responsive/accessibility mechanics; feature pages supply typed business render fragments, existing routes, authorization, localization, formatting, and data. Orders is the reference adapter, followed by bounded migration waves and an explicit exception ledger. Every slice is test-first, independently buildable, and committed before the next slice begins.

**Tech Stack:** .NET 10, Blazor WebAssembly, Razor Class Libraries, MudBlazor 9.7, Maliev.ShadcnBlazor CSS tokens, xUnit 2.9, Microsoft Playwright 1.61, RESX localization.

## Global Constraints

- Work in an isolated worktree created with `superpowers:using-git-worktrees`; inspect status before every task and preserve unrelated changes.
- Do not change backend endpoints, DTO wire shapes, database models, authentication, authorization semantics, deployment configuration, or existing route/query contracts.
- Keep feature data loading, sorting, filtering, paging, formatting, permissions, and destinations in the owning feature; shared components must not infer business meaning through reflection or dictionaries.
- Preserve English and Thai localization parity; never hard-code user-facing text in Razor or C#.
- Retain semantic tables at all widths. Table overflow is contained locally; document horizontal overflow is forbidden.
- Show separate detail and quick-view icon actions where applicable; at most one row may be expanded per table.
- Use MudBlazor embedded Google Material SVG paths first. Outlined is the default, filled is selected/active only, dense icons are 20px, standard icons are 24px, and no runtime Google Fonts dependency may be added.
- Desktop controls may remain 36px; narrow or coarse-pointer interactive roots must be at least 44 by 44 CSS pixels.
- Preserve WCAG 2.2 AA, keyboard operation, record-specific accessible names, visible focus, forced-colors support, reduced motion, dark mode, and modeled 200% zoom.
- Run the Impeccable detector exactly once, only in the final gate after all implementation and review fixes.
- Do not stop or restart the user's Aspire instance. Use the production Client browser fixture and intercept only external BFF boundaries needed by a test.
- Before every commit, run `dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build` after focused tests. For every task that changes rendered markup or CSS, also run `dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --no-build`. Record exact counts.
- If the live Aspire BFF locks normal Release outputs, do not stop it. Prove the full solution with a task-specific external `--artifacts-path`, build each changed frontend dependency into normal Release output, build `Legacy.Maliev.Intranet.Tests` with `--no-dependencies`, then run repository-local tests with `--no-build` so source-contract root discovery remains valid.
- Each task ends with an independently useful commit containing only its owned files.

---

## File and responsibility map

### Shared component project

- Create `Legacy.Maliev.Intranet.Client.Shared/Components/OperationalTable.razor`: semantic table shell, typed row context, expansion row, and detail/quick-view action rendering.
- Create `Legacy.Maliev.Intranet.Client.Shared/Components/OperationalTable.razor.css`: local overflow, sticky regions, priority visibility, focus, forced-colors, reduced-motion, and touch geometry.
- Create `Legacy.Maliev.Intranet.Client.Shared/Components/OperationalTableState.cs`: single-expanded-record state for `OperationalTable<TItem,TKey>`.
- Create `Legacy.Maliev.Intranet.Client.Shared/Components/PageBreadcrumbs.razor`: localized semantic breadcrumb navigation.
- Create `Legacy.Maliev.Intranet.Client.Shared/Components/PageBreadcrumbs.razor.css`: responsive breadcrumb layout and focus treatment.
- Create `Legacy.Maliev.Intranet.Client.Shared/Components/PageBreadcrumbsResources.resx` and `.th.resx`: landmark and collapse copy.
- Modify `Legacy.Maliev.Intranet.Client.Shared/Components/ListToolbar.razor`: icon-only refresh without changing the callback API.
- Modify `Legacy.Maliev.Intranet.Client.Shared/Components/ListToolbar.razor.css`: aligned action cluster and icon-button geometry.

### Shell project

- Modify `Legacy.Maliev.Intranet.Client/Layout/LegacyAppNavigation.cs`: explicit parent/child navigation metadata.
- Modify `Legacy.Maliev.Intranet.Client/Components/Shell/LegacyNavigationRail.razor` and `.razor.css`: structural child rendering and visual hierarchy.
- Modify `Legacy.Maliev.Intranet.Client/Layout/LegacyTopBar.razor` and `.razor.css`: four-zone grid and responsive utility ownership.
- Modify shell RESX only when a new accessible name is required; keep key parity between English and Thai.

### Feature adapters

- Modify each list page Razor/CSS/RESX in the exact migration wave that owns it. Feature adapters declare visible columns, quick-view content, detail route, record-specific action names, and localized breadcrumbs.
- Do not migrate form-entry tables or analytical/chart tables to `OperationalTable`; record and justify those cases in the exception ledger.

### Tests and evidence

- Create `Legacy.Maliev.Intranet.Tests/OperationalTableBehaviorTests.cs`: executable expansion-state tests and component contract tests.
- Create `Legacy.Maliev.Intranet.Tests/OperationalTableAdoptionContractTests.cs`: exact adoption/exception ledger and route/localization ownership.
- Modify `Legacy.Maliev.Intranet.Tests/ListToolbarAdoptionContractTests.cs`, `OperationsShellContractTests.cs`, and feature migration contract tests as their owners change.
- Create `Maliev.ShadcnBlazor.BrowserTests/OperationalShellBrowserTests.cs`: production Client shell/breadcrumb/rail/top-bar coverage.
- Create `Maliev.ShadcnBlazor.BrowserTests/OrdersOperationalTableBrowserTests.cs`: Orders reference behavior at all required widths and modes.
- Create `Maliev.ShadcnBlazor.BrowserTests/OperationalTableMigrationBrowserTests.cs`: representative coverage for each migration wave.

---

### Task 1: Shared operational-table state and semantic component

**Files:**
- Create: `Legacy.Maliev.Intranet.Client.Shared/Components/OperationalTableState.cs`
- Create: `Legacy.Maliev.Intranet.Client.Shared/Components/OperationalTable.razor`
- Create: `Legacy.Maliev.Intranet.Client.Shared/Components/OperationalTable.razor.css`
- Create: `Legacy.Maliev.Intranet.Tests/OperationalTableBehaviorTests.cs`

**Interfaces:**
- Produces: `sealed class OperationalTableState<TKey> where TKey : notnull` with `bool HasExpandedKey`, `TKey ExpandedKey`, `bool IsExpanded(TKey key)`, `void Toggle(TKey key)`, and `void Clear()`.
- Produces: `OperationalTable<TItem,TKey>` parameters `IReadOnlyList<TItem> Items`, `Func<TItem,TKey> KeySelector`, `RenderFragment HeaderContent`, `RenderFragment<TItem> RowContent`, `RenderFragment<TItem> QuickViewContent`, `Func<TItem,string?> DetailHref`, `Func<TItem,string> DetailAriaLabel`, `Func<TItem,string> ExpandAriaLabel`, `Func<TItem,string> CollapseAriaLabel`, `string TableLabel`, `RenderFragment? EmptyContent`, `int ColumnCount`, and `OperationalTableState<TKey> State`.
- Later tasks must use the component's own `operational-table`, `operational-table__scroll`, `operational-table__row`, `operational-table__identity`, `operational-table__actions`, `operational-table__detail`, `operational-table__toggle`, and `operational-table__quick-view` selectors rather than creating parallel table behavior.

- [ ] **Step 1: Add failing executable state tests**

Create `OperationalTableBehaviorTests.cs` with these exact state transitions:

```csharp
[Fact]
public void Toggle_keeps_only_one_expanded_record()
{
    var state = new OperationalTableState<int>();
    state.Toggle(41);
    Assert.True(state.IsExpanded(41));
    state.Toggle(84);
    Assert.False(state.IsExpanded(41));
    Assert.True(state.IsExpanded(84));
}

[Fact]
public void Toggle_current_record_collapses_and_clear_resets()
{
    var state = new OperationalTableState<int>();
    state.Toggle(41);
    state.Toggle(41);
    Assert.False(state.HasExpandedKey);
    state.Toggle(84);
    state.Clear();
    Assert.False(state.HasExpandedKey);
}
```

Also add source-contract assertions for semantic `<table>`, a single colspan expansion row, separate detail/toggle controls, `aria-expanded`, `aria-controls`, record-specific labels, contained-scroll selector, forced-colors, reduced-motion, and no mobile `display:block` table/card conversion.

- [ ] **Step 2: Run the focused tests to verify RED**

Run:

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter "FullyQualifiedName~OperationalTableBehaviorTests"
```

Expected: compilation fails because `OperationalTableState<TKey>` and the component files do not exist.

- [ ] **Step 3: Implement the minimal state type and component API**

Implement state without reflection or equality shortcuts:

```csharp
public sealed class OperationalTableState<TKey> where TKey : notnull
{
    public bool HasExpandedKey { get; private set; }
    public TKey ExpandedKey { get; private set; } = default!;
    public bool IsExpanded(TKey key) =>
        HasExpandedKey && EqualityComparer<TKey>.Default.Equals(ExpandedKey, key);
    public void Toggle(TKey key)
    {
        if (IsExpanded(key)) { Clear(); return; }
        ExpandedKey = key;
        HasExpandedKey = true;
    }
    public void Clear()
    {
        ExpandedKey = default!;
        HasExpandedKey = false;
    }
}
```

Render a native `<table>` inside `.operational-table__scroll`. Render the page-owned header and cells through typed `RenderFragment` parameters. Put the two action buttons in the shared final cell. Use `Icons.Material.Outlined.OpenInNew`, `Icons.Material.Outlined.ExpandMore`, and `Icons.Material.Outlined.ExpandLess`. Omit the detail action when `DetailHref(item)` is null. Render quick view as the immediately following `<tr>` with a stable id and `colspan="@ColumnCount"`; reject `ColumnCount < 1` in `OnParametersSet`.

- [ ] **Step 4: Implement shared CSS with explicit responsive contracts**

Add:

```css
.operational-table__scroll { max-width: 100%; overflow-x: auto; overscroll-behavior-inline: contain; }
.operational-table { width: 100%; min-width: var(--operational-table-min-width, 48rem); border-collapse: separate; border-spacing: 0; }
.operational-table__identity { position: sticky; inset-inline-start: 0; background: var(--shadcn-background); }
.operational-table__actions { position: sticky; inset-inline-end: 0; background: var(--shadcn-background); }
.operational-table__detail, .operational-table__toggle { width: 2.25rem; height: 2.25rem; }
.operational-table__quick-view > td { padding: 1rem; background: var(--shadcn-muted); }
@media (max-width: 720px) { .operational-table [data-priority="supporting"] { display: none; } }
@media (max-width: 720px), (pointer: coarse) { .operational-table__detail, .operational-table__toggle { width: 2.75rem; height: 2.75rem; } }
@media (prefers-reduced-motion: reduce) { .operational-table__scroll { scroll-behavior: auto; } }
@media (forced-colors: active) { .operational-table__actions, .operational-table__quick-view > td { border: 1px solid CanvasText; } }
```

Use semantic Shadcn variables already owned by the shared adapter. Do not add `.mud-*` appearance selectors.

- [ ] **Step 5: Build first, then run focused tests GREEN**

Run:

```powershell
dotnet build .\Legacy.Maliev.Intranet.Client.Shared\Legacy.Maliev.Intranet.Client.Shared.csproj -c Release
dotnet build .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-dependencies
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~OperationalTableBehaviorTests"
```

Expected: build has 0 warnings/0 errors; focused tests pass.

- [ ] **Step 6: Commit the shared table primitive**

```powershell
git add Legacy.Maliev.Intranet.Client.Shared/Components/OperationalTable* Legacy.Maliev.Intranet.Tests/OperationalTableBehaviorTests.cs
git diff --cached --check
git commit -m "feat: add shared operational table primitive"
```

---

### Task 2: Shared breadcrumbs, icon-only refresh, and Material icon contract

**Files:**
- Create: `Legacy.Maliev.Intranet.Client.Shared/Components/PageBreadcrumbs.razor`
- Create: `Legacy.Maliev.Intranet.Client.Shared/Components/PageBreadcrumbs.razor.css`
- Create: `Legacy.Maliev.Intranet.Client.Shared/Components/PageBreadcrumbsResources.cs`
- Create: `Legacy.Maliev.Intranet.Client.Shared/Components/PageBreadcrumbsResources.resx`
- Create: `Legacy.Maliev.Intranet.Client.Shared/Components/PageBreadcrumbsResources.th.resx`
- Create: `Legacy.Maliev.Intranet.Client.Shared/Components/PageBreadcrumbItem.cs`
- Modify: `Legacy.Maliev.Intranet.Client.Shared/Components/ListToolbar.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Shared/Components/ListToolbar.razor.css`
- Modify: `Legacy.Maliev.Intranet.Tests/ListToolbarAdoptionContractTests.cs`
- Create: `Legacy.Maliev.Intranet.Tests/BreadcrumbAndMaterialIconContractTests.cs`

**Interfaces:**
- Produces: `sealed record PageBreadcrumbItem(string Label, string? Href = null)`.
- Produces: `PageBreadcrumbs` parameter `IReadOnlyList<PageBreadcrumbItem> Items`.
- Preserves: `ListToolbar<TSort>` parameter and callback API exactly.
- Later pages pass explicit localized labels and routes; `PageBreadcrumbs` must not inspect `NavigationManager.Uri`.

- [ ] **Step 1: Add RED contracts for breadcrumbs and refresh**

Assert that the breadcrumb component renders `<nav aria-label>`, intermediate `LegacyLink` instances, and a final `aria-current="page"` text node. Assert English/Thai RESX key parity for `BreadcrumbLabel` and `More`. Update toolbar tests to require one `MudIconButton` with `Icons.Material.Outlined.Refresh`, `aria-label`, and `title`, and to reject visible `@Text["Refresh"]` content inside the button.

Add an inventory assertion that production Razor/C# icon references use `Icons.Material.` or an explicit self-hosted Material SVG allowlist; reject Font Awesome package references, `fa-` classes, and Google Fonts Material icon stylesheets.

- [ ] **Step 2: Run RED**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter "FullyQualifiedName~BreadcrumbAndMaterialIconContractTests|FullyQualifiedName~ListToolbarAdoptionContractTests"
```

Expected: missing breadcrumb files and visible refresh-label assertions fail.

- [ ] **Step 3: Implement breadcrumbs and icon-only refresh**

Use this breadcrumb structure:

```razor
<nav class="page-breadcrumbs" aria-label="@Text["BreadcrumbLabel"]">
    <ol>
        @for (var index = 0; index < Items.Count - 1; index++)
        {
            var item = Items[index];
            <li><LegacyLink Href="@item.Href" Role="LegacyLinkRole.Navigation">@item.Label</LegacyLink></li>
        }
        <li aria-current="page">@Items[^1].Label</li>
    </ol>
</nav>
```

Guard zero items by rendering nothing; reject a non-final item without `Href` with `InvalidOperationException`. Convert Refresh to:

```razor
<MudIconButton ButtonType="ButtonType.Button"
               Class="list-toolbar__refresh"
               Icon="@Icons.Material.Outlined.Refresh"
               Disabled="@IsBusy"
               aria-label="@Text["Refresh"]"
               title="@Text["Refresh"]"
               OnClick="RefreshAsync" />
```

Do not change `RefreshAsync`, request reasons, or controller behavior.

- [ ] **Step 4: Build and run GREEN**

```powershell
dotnet build .\Legacy.Maliev.Intranet.Client.Shared\Legacy.Maliev.Intranet.Client.Shared.csproj -c Release
dotnet build .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-dependencies
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~BreadcrumbAndMaterialIconContractTests|FullyQualifiedName~ListToolbarAdoptionContractTests|FullyQualifiedName~ListToolbarBehaviorTests"
```

Expected: build 0/0; breadcrumb, icon inventory, toolbar source, and toolbar behavior tests pass.

- [ ] **Step 5: Commit**

```powershell
git add Legacy.Maliev.Intranet.Client.Shared/Components/PageBreadcrumb* Legacy.Maliev.Intranet.Client.Shared/Components/ListToolbar.razor Legacy.Maliev.Intranet.Client.Shared/Components/ListToolbar.razor.css Legacy.Maliev.Intranet.Tests/BreadcrumbAndMaterialIconContractTests.cs Legacy.Maliev.Intranet.Tests/ListToolbarAdoptionContractTests.cs
git diff --cached --check
git commit -m "feat: add breadcrumbs and compact refresh action"
```

---

### Task 3: Hierarchical navigation rail and aligned top bar

**Files:**
- Modify: `Legacy.Maliev.Intranet.Client/Layout/LegacyAppNavigation.cs`
- Modify: `Legacy.Maliev.Intranet.Client/Components/Shell/LegacyNavigationRail.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Components/Shell/LegacyNavigationRail.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client/Layout/LegacyTopBar.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Layout/LegacyTopBar.razor.css`
- Modify: `Legacy.Maliev.Intranet.Tests/OperationsShellContractTests.cs`
- Create: `Maliev.ShadcnBlazor.BrowserTests/OperationalShellBrowserTests.cs`

**Interfaces:**
- Changes `LegacyNavItem` to include `LegacyNavItemKind Kind = LegacyNavItemKind.Primary` and `string? ParentHref = null`.
- Produces `enum LegacyNavItemKind { Primary, ChildAction }`.
- Keeps all current navigation labels, hrefs, icons, permissions, descriptions, and match behavior unchanged.
- Keeps `LegacyTopBar` public parameters unchanged.

- [ ] **Step 1: Add RED shell contracts and production-browser geometry**

Update source tests to require explicit `ChildAction` metadata for `/customers/new`, `/Orders/Create`, `/Quotations/Create`, `/accounting/new`, `/Finances/Create`, `/Materials/Create`, `/purchasing/new`, and `/Suppliers/Create`. Require parent hrefs that identify their primary owner.

Create a production Client Playwright test that stubs `/bff/session`, opens `/sales/orders`, and at 1280/768/390/320 asserts:

- Child create links follow their parent in DOM order and have `.legacy-rail-link--child`.
- Current primary destination retains `aria-current="page"`.
- Top bar zones have `.legacy-topbar__brand`, `__search`, `__actions`, and `__utilities`.
- Zone rectangles share a vertical center within 2px at 1280.
- Document `scrollWidth == clientWidth` at all widths.
- Drawer focus trapping and focus restoration still work.

- [ ] **Step 2: Run RED**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter "FullyQualifiedName~OperationsShellContractTests"
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --filter "FullyQualifiedName~OperationalShellBrowserTests"
```

Expected: metadata, CSS classes, top-bar zones, and geometry assertions fail.

- [ ] **Step 3: Implement explicit navigation hierarchy**

Add `Kind` and `ParentHref` to create-action entries. Render primary and child actions in the same authorized traversal, but add:

```razor
class="legacy-rail-link @(item.Kind == LegacyNavItemKind.ChildAction ? "legacy-rail-link--child" : string.Empty) @(IsItemActive(item) ? "active" : string.Empty)"
```

Add a decorative connector and quieter typography without reducing the 44px target. Do not infer child links from labels beginning with “New”.

- [ ] **Step 4: Replace top-bar flex/negative margins with grid zones**

Wrap existing children in four named zones and use:

```css
.legacy-topbar {
  display: grid;
  grid-template-columns: var(--legacy-rail-width) minmax(18rem, 1fr) auto auto;
  align-items: center;
  width: 100%;
  margin: 0;
}
```

At `max-width:1180px`, replace the rail track with `auto`; at `max-width:960px`, keep quick actions usable through their existing compact behavior; at `max-width:720px`, put search in a full second row. Do not hide the only route to quick-create actions.

- [ ] **Step 5: Build and run GREEN shell checks**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~OperationsShellContractTests|FullyQualifiedName~StaticShellWasmMigrationContractTests|FullyQualifiedName~ShadcnStyleSystemContractTests"
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --no-build --filter "FullyQualifiedName~OperationalShellBrowserTests"
```

Expected: solution build 0/0; focused source and production-browser shell tests pass.

- [ ] **Step 6: Commit**

```powershell
git add Legacy.Maliev.Intranet.Client/Layout/LegacyAppNavigation.cs Legacy.Maliev.Intranet.Client/Components/Shell/LegacyNavigationRail.razor* Legacy.Maliev.Intranet.Client/Layout/LegacyTopBar.razor* Legacy.Maliev.Intranet.Tests/OperationsShellContractTests.cs Maliev.ShadcnBlazor.BrowserTests/OperationalShellBrowserTests.cs
git diff --cached --check
git commit -m "fix: align workspace shell navigation hierarchy"
```

---

### Task 4: Orders reference adapter

**Files:**
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Pages/Orders.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Pages/Orders.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Pages/Orders.resx`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Pages/Orders.th.resx`
- Modify: `Legacy.Maliev.Intranet.Tests/OrdersIndexWasmMigrationContractTests.cs`
- Create: `Maliev.ShadcnBlazor.BrowserTests/OrdersOperationalTableBrowserTests.cs`

**Interfaces:**
- Consumes `OperationalTable<OrderListItem,int>`, `OperationalTableState<int>`, `PageBreadcrumbs`, and the unchanged `ListToolbar<OrderListSort>`.
- Primary columns: ID, Customer, Name, Remaining, Promised, Actions.
- Supporting desktop/tablet columns: Process, Quantity, Manufactured, Employee, Subtotal.
- Quick view contains all supporting values plus confidentiality state and existing record context.
- Detail href remains `/Orders/View?id={id}`; create route remains `/Orders/Create`.

- [ ] **Step 1: Freeze current Orders behavior and write failing production-browser tests**

Extend the source contract to require exact routes, the five existing BFF requests, all ten current data fields, toolbar query serialization, pager behavior, and error mapping while requiring `OperationalTable`, `PageBreadcrumbs`, two row action labels, and rejecting the mobile block/card CSS selectors.

Create a real production Client fixture response with at least three orders, long Thai/English names, confidential and public records, nullable employee/promised values, and different amounts. At 1280/768/390/320 assert semantic table containment, primary column visibility, supporting-column priority, atomic IDs/money/dates, 44px narrow actions, and zero document overflow.

Assert open-detail `href` and accessible name. Expand record A, then B, then B again; require A closed when B opens and no expansion after the second B activation. Trigger sort, page, search, and icon refresh and require expansion cleared each time.

- [ ] **Step 2: Run RED**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter "FullyQualifiedName~OrdersIndexWasmMigrationContractTests"
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --filter "FullyQualifiedName~OrdersOperationalTableBrowserTests"
```

Expected: component/breadcrumb/action/quick-view assertions fail.

- [ ] **Step 3: Replace the three Orders table copies with the shared adapter**

Keep `Sections`, the five concurrent requests, result partitioning, query navigation, and paging. Use one page-level `OperationalTableState<int>` shared by all three Orders sections so only one Orders row is expanded anywhere on the page. Clear that state at the start of the current `LoadAsync` lease and before query navigation.

Render all current secondary values in quick view. Do not fetch additional data. Use localized `ViewOrder`, `ExpandOrder`, and `CollapseOrder` keys containing the record ID.

- [ ] **Step 4: Remove Orders mobile-card CSS and add page-owned width map**

Delete the `display:block` table conversion and `data-label` pseudo-label layout. Define only page variables/classes such as `--operational-table-min-width`, numeric alignment, confidential badge, working-set height, and section headings. Leave shared action/focus/priority styling to `OperationalTable.razor.css`.

- [ ] **Step 5: Build and run Orders GREEN**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~OrdersIndexWasmMigrationContractTests|FullyQualifiedName~ListToolbarAdoptionContractTests|FullyQualifiedName~PaginationQueryDefaultsTests|FullyQualifiedName~RoutedPageLocalizationParityTests"
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --no-build --filter "FullyQualifiedName~OrdersOperationalTableBrowserTests|FullyQualifiedName~OperationalShellBrowserTests"
```

Expected: build 0/0; all focused source and production-browser tests pass.

- [ ] **Step 6: Commit**

```powershell
git add Legacy.Maliev.Intranet.Client.Features.Orders/Pages/Orders.* Legacy.Maliev.Intranet.Tests/OrdersIndexWasmMigrationContractTests.cs Maliev.ShadcnBlazor.BrowserTests/OrdersOperationalTableBrowserTests.cs
git diff --cached --check
git commit -m "feat: add responsive order quick views"
```

---

### Task 5: Sales and customer list migration wave

**Files:**
- Modify: `Legacy.Maliev.Intranet.Client.Features.Customers/Pages/Customers.razor` and `.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Employees/Pages/Employees.razor` and `.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/Index.razor` and `.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Index.razor` and `.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Customers/Pages/Customers.resx` and `Customers.th.resx`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Employees/Pages/Employees.resx` and `Employees.th.resx`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/Index.resx` and `Index.th.resx`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Index.resx` and `Index.th.resx`
- Create: `Legacy.Maliev.Intranet.Tests/OperationalTableAdoptionContractTests.cs`
- Modify: existing Customers, Employees, Quotation Requests, and Quotations migration contract tests
- Create: `Maliev.ShadcnBlazor.BrowserTests/OperationalTableMigrationBrowserTests.cs`

**Interfaces:**
- Consumes the Task 1 and Task 2 primitives without modifying their API.
- Every page supplies an `OperationalTableState<TKey>` and clears it on current data replacement/query navigation.
- Existing detail destinations remain exact: Customers `/Customers/View?id={id}`, Employees `/Employees/View?id={id}`, Quotation Requests `/QuotationRequests/View?id={id}`, and Quotations `/Quotations/View?id={id}`.

- [ ] **Step 1: Inventory and freeze every field before editing pages**

In `OperationalTableAdoptionContractTests`, encode exact page rows with project path, page path, key type, detail route fragment, expected field/resource keys, and whether quick view is supported. For this wave, require all four pages to adopt `OperationalTable` and `PageBreadcrumbs` and prohibit page-local mobile card conversion.

Define priority maps in the test data:

- Customers essential: ID, Name, Email, Actions; supporting: Company.
- Employees essential: ID, Name, Role, Actions; supporting: Email.
- Quotation Requests essential: ID, Customer, Status, Created, Actions; supporting: Company.
- Quotations essential: ID, Customer, Total/Quoted Amount, Decision, Actions; supporting: Employee, Period, Expiration, Subtotal, VAT, withholding, FOB, shipped via, terms.

- [ ] **Step 2: Run the adoption/source tests RED**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter "FullyQualifiedName~OperationalTableAdoptionContractTests|FullyQualifiedName~CustomersWasmMigrationContractTests|FullyQualifiedName~EmployeesWasmMigrationContractTests|FullyQualifiedName~QuotationRequestsWasmMigrationContractTests|FullyQualifiedName~QuotationsIndexWasmMigrationContractTests"
```

Expected: missing adoption, breadcrumb, quick-view, and action contracts fail.

- [ ] **Step 3: Migrate Customers and Employees**

Replace existing row markup with typed shared tables. Preserve customer sort buttons and `aria-sort`; do not place hidden sortable controls in the narrow accessibility tree. Move full long values to quick view and retain accessible disclosure behavior. Preserve Customers search-clear and pagination semantics. Add record-specific open/expand/collapse labels in both locales. The adapter pattern is:

```razor
<OperationalTable TItem="CustomerListItem" TKey="int"
                  Items="@Page.Items"
                  KeySelector="@(item => item.Id)"
                  DetailHref="@(item => $"/Customers/View?id={item.Id}")"
                  DetailAriaLabel="@(item => Text["ViewCustomer", item.Id])"
                  ExpandAriaLabel="@(item => Text["ExpandCustomer", item.Id])"
                  CollapseAriaLabel="@(item => Text["CollapseCustomer", item.Id])"
                  State="customerTableState"
                  ColumnCount="5">
    <HeaderContent>
        <th scope="col">@Text["Id"]</th>
        <th scope="col">@Text["Name"]</th>
        <th scope="col">@Text["Email"]</th>
        <th scope="col" data-priority="supporting">@Text["Company"]</th>
        <th scope="col"><span class="visually-hidden">@Text["Actions"]</span></th>
    </HeaderContent>
    <RowContent Context="item">
        <td class="customer-id-cell">@item.Id</td>
        <td class="customer-name-cell">@item.FullName</td>
        <td class="customer-email-cell">@item.Email</td>
        <td class="customer-company-cell" data-priority="supporting">@item.Company?.Name</td>
    </RowContent>
    <QuickViewContent Context="item">
        <dl class="customer-quick-view">
            <dt>@Text["Name"]</dt><dd>@item.FullName</dd>
            <dt>@Text["Email"]</dt><dd>@item.Email</dd>
            <dt>@Text["Company"]</dt><dd>@item.Company?.Name</dd>
        </dl>
    </QuickViewContent>
</OperationalTable>
```

- [ ] **Step 4: Migrate Quotation Requests and Quotations**

Preserve current BFF paths, permissions, query sorts, paging, create/view routes, builder links, and decision formatting. Put secondary financial/logistics values in quick view rather than removing them. Use atomic money/date cells. The Quotation Request adapter uses `/QuotationRequests/View?id={id}` and the Quotation adapter uses `/Quotations/View?id={id}`; both localized action names contain the record ID.

- [ ] **Step 5: Add representative production-browser assertions**

Extend `OperationalTableMigrationBrowserTests` with one test per page at 1280/768/390/320. For Customers also retain the full-value disclosure and sort/deep-link tests. For Quotations use long Thai labels and assert the action column remains reachable. For every page assert one expansion, correct detail href/name, refresh clears expansion, 44px narrow actions, contained table overflow, and no console/page errors.

- [ ] **Step 6: Build and run wave GREEN**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~OperationalTableAdoptionContractTests|FullyQualifiedName~CustomersWasmMigrationContractTests|FullyQualifiedName~EmployeesWasmMigrationContractTests|FullyQualifiedName~QuotationRequestsWasmMigrationContractTests|FullyQualifiedName~QuotationsIndexWasmMigrationContractTests|FullyQualifiedName~LegacyLinkSystemContractTests"
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --no-build --filter "FullyQualifiedName~OperationalTableMigrationBrowserTests|FullyQualifiedName~CustomerResponsiveBrowserTests"
```

Expected: build 0/0; source/link/adoption and production-browser tests pass.

- [ ] **Step 7: Commit**

```powershell
git add Legacy.Maliev.Intranet.Client.Features.Customers/Pages/Customers.* Legacy.Maliev.Intranet.Client.Features.Employees/Pages/Employees.* Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/Index.* Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Index.* Legacy.Maliev.Intranet.Tests/OperationalTableAdoptionContractTests.cs Legacy.Maliev.Intranet.Tests/CustomersWasmMigrationContractTests.cs Legacy.Maliev.Intranet.Tests/EmployeesWasmMigrationContractTests.cs Legacy.Maliev.Intranet.Tests/QuotationRequestsWasmMigrationContractTests.cs Legacy.Maliev.Intranet.Tests/QuotationsIndexWasmMigrationContractTests.cs Maliev.ShadcnBlazor.BrowserTests/OperationalTableMigrationBrowserTests.cs
git diff --cached --check
git commit -m "feat: unify sales operational tables"
```

Before committing, inspect `git diff --cached --name-only` and verify it contains only the exact paths listed in this task.

---

### Task 6: Accounting, procurement, catalog, employee, and diagnostics migration wave

**Files:**
- Modify: `Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Invoices.razor` and `.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Finances.razor` and `.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrders.razor` and `.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/Suppliers.razor` and `.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/Materials.razor` and `.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Diagnostics/Pages/ErrorReport.razor` and `.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Invoices.resx`, `Invoices.th.resx`, `Finances.resx`, and `Finances.th.resx`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrders.resx`, `PurchaseOrders.th.resx`, `Suppliers.resx`, and `Suppliers.th.resx`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/Materials.resx` and `Materials.th.resx`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Diagnostics/Pages/ErrorReport.resx` and `ErrorReport.th.resx`
- Modify: `Legacy.Maliev.Intranet.Tests/OperationalTableAdoptionContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/InvoicesIndexWasmMigrationContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/FinancesIndexWasmMigrationContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/PurchaseOrdersIndexWasmMigrationContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/SuppliersIndexWasmMigrationContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/MaterialsWasmMigrationContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/ServerErrorReportWasmMigrationContractTests.cs`
- Modify: `Maliev.ShadcnBlazor.BrowserTests/OperationalTableMigrationBrowserTests.cs`

**Interfaces:**
- Consumes shared APIs unchanged.
- Detail actions appear only when the existing feature has a stable detail destination and permission. Exact existing routes are Invoices `/Invoices/View?id={id}`, Finances `/Finances/View?id={id}`, Purchase Orders `/PurchaseOrders/View?id={id}`, Suppliers `/Suppliers/View?id={id}`, and Materials `/Materials/View?id={id}`. Error Report has no stable record-detail route, so it renders quick view only; do not invent a route.

- [ ] **Step 1: Extend the exact adoption/exception ledger and run RED**

Add these priority maps:

- Invoices essential: ID/number, customer, paid/outstanding, total, actions; supporting: receipt, PO, subtotal, VAT, withholding, payment/created dates.
- Finances essential: ID, direction/type, amount, payment date, actions; supporting: description, method, recipient, transaction number. Keep the monthly summary table as an explicit analytical exception.
- Purchase Orders essential: ID, employee/supplier identity available in the current model, total/FOB, created, actions; supporting: terms and shipping.
- Suppliers essential: ID, name, email, actions; supporting: telephone.
- Materials essential: ID/number, name, group, actions; supporting: density, machinable, printable.
- Error Report essential: timestamp, level, code, category, actions when a detail destination exists; supporting: path and correlation ID.

Run the focused adoption plus six existing migration test classes and confirm RED.

- [ ] **Step 2: Migrate accounting pages**

Preserve finance direction/status formatting, currency/number formatting, invoice paid/outstanding semantics, all query/sort/page contracts, and existing detail links. Quick view owns secondary financial fields. Do not migrate `NetProfitChart`, `YearlyActivityChart`, `InvoiceCreate`, or `InvoiceView` line-item tables; add explicit exception entries because they are analytical or form/detail sub-tables rather than list records.

- [ ] **Step 3: Migrate procurement, catalog, and diagnostics pages**

Preserve row click/handler semantics by moving navigation into explicit detail icon actions; do not leave a clickable entire row and a nested action button competing. Keep authorization and localization exact. Where no existing detail route exists, the ledger must state `QuickViewOnly` and the shared component must omit the detail button.

- [ ] **Step 4: Extend production-browser coverage**

Use complete fixture shapes for one representative page from accounting, procurement, catalog, and diagnostics. Test all four widths; use Thai at 320 for the widest labels. Assert atomic money/date/ID values, one expansion, exact existing detail route where supported, no fake route where unsupported, and zero document overflow/errors.

- [ ] **Step 5: Build and run wave GREEN**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~OperationalTableAdoptionContractTests|FullyQualifiedName~InvoicesIndexWasmMigrationContractTests|FullyQualifiedName~FinancesIndexWasmMigrationContractTests|FullyQualifiedName~PurchaseOrdersIndexWasmMigrationContractTests|FullyQualifiedName~SuppliersIndexWasmMigrationContractTests|FullyQualifiedName~MaterialsWasmMigrationContractTests|FullyQualifiedName~ServerErrorReportWasmMigrationContractTests|FullyQualifiedName~LegacyLinkSystemContractTests"
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --no-build --filter "FullyQualifiedName~OperationalTableMigrationBrowserTests"
```

Expected: build 0/0; adoption, feature, link, and production-browser tests pass.

- [ ] **Step 6: Commit**

Stage only the six feature families, their exact RESX files, the adoption test, exact feature contract tests, and the migration browser test. Audit `git diff --cached --name-only`, run `git diff --cached --check`, then:

```powershell
git commit -m "feat: unify operational data tables"
```

---

### Task 7: Dashboard, customer-history, and remaining-table exception closure

**Files:**
- Modify: `Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor`, `Dashboard.resx`, and `Dashboard.th.resx`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.razor`, `CustomerView.resx`, and `CustomerView.th.resx`
- Inspect without changing: `Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor.css`
- Inspect without changing: `Legacy.Maliev.Intranet.Client.Features.Customers/Components/CustomerHistoryTable.razor` and `.razor.css`
- Modify: `Legacy.Maliev.Intranet.Tests/OperationalTableAdoptionContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/OperationsPageVisualSystemContractTests.cs`
- Modify: `Maliev.ShadcnBlazor.BrowserTests/OperationalTableMigrationBrowserTests.cs`

**Interfaces:**
- No new shared API.
- Produces a complete repository-wide ledger in which every production `<MudTable>`, `<MudSimpleTable>`, or native operational `<table>` is either adopted or explicitly classified as `Analytical`, `FormEntry`, `DetailLineItems`, `CompactDashboard`, or `LegacyFallback`.

- [ ] **Step 1: Add a repository scanner test and confirm RED**

Scan production Razor files, excluding tests/showcase/compatibility Razor fallbacks, for table markup. Compare exact normalized paths with the adoption/exception ledger. Fail on missing entries, duplicate entries, or an exception without a reason. Require every operational-list exception to be eliminated.

- [ ] **Step 2: Prove the two retained specialized-table exceptions**

Encode Dashboard summary tables as `CompactDashboard`: each table is short, comparison-oriented, already has a record-specific detail link, and has no omitted secondary record payload requiring quick view. Encode `CustomerHistoryTable` as `SpecializedHistory`: each row already exposes the complete browser-safe history projection plus its exact detail destination, so a duplicate quick view is not applicable. Add source assertions that fail if either table gains an unrepresented secondary field, loses its detail link, introduces mobile card conversion, or allows document overflow.

- [ ] **Step 3: Implement the minimal closure and browser evidence**

Add `PageBreadcrumbs` to Dashboard with one current crumb and to Customer View with explicit Customers `/customers` plus the localized customer identity as the current crumb. For retained exceptions, add browser assertions proving containment, atomic values, keyboard navigation, 44px coarse/narrow record links, and absence of duplicated quick-view actions.

- [ ] **Step 4: Build and run GREEN ledger checks**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~OperationalTableAdoptionContractTests|FullyQualifiedName~OperationsPageVisualSystemContractTests|FullyQualifiedName~CustomersViewWasmMigrationContractTests"
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --no-build --filter "FullyQualifiedName~OperationalTableMigrationBrowserTests|FullyQualifiedName~CustomerDetailBrowserTests"
```

Expected: exact ledger, representative exception behavior, and all browser checks pass.

- [ ] **Step 5: Commit**

```powershell
git add Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor Legacy.Maliev.Intranet.Client/Pages/Dashboard.resx Legacy.Maliev.Intranet.Client/Pages/Dashboard.th.resx Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.razor Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.resx Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.th.resx Legacy.Maliev.Intranet.Tests/OperationalTableAdoptionContractTests.cs Legacy.Maliev.Intranet.Tests/OperationsPageVisualSystemContractTests.cs Maliev.ShadcnBlazor.BrowserTests/OperationalTableMigrationBrowserTests.cs
git diff --cached --check
git commit -m "test: close operational table migration inventory"
```

Verify the staged list contains no `CustomerHistoryTable` production change; this task proves and records that specialized exception rather than rewriting it.

---

### Task 8: Full application verification and final Impeccable critique

**Files:**
- Modify only files required to correct defects discovered by this gate.
- Create ignored report: `.superpowers/sdd/2026-08-13-operational-table-shell-navigation-plan/task-8-report.md`

**Interfaces:**
- Consumes the completed shared system and migration ledger.
- Produces no new feature contract; only closes verified shared-owner defects.

- [ ] **Step 1: Verify repository scope before executing gates**

Run:

```powershell
git status --short
git log --oneline --decorate -12
git diff --check de24b00..HEAD
```

Confirm only the planned commits/files are present. Preserve the existing untracked brainstorming workspace.

- [ ] **Step 2: Run the mandatory Release build first**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
```

Expected: 0 warnings and 0 errors. Stop and fix compilation/analyzer failures before tests.

- [ ] **Step 3: Run focused and full suites serially**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~OperationalTable|FullyQualifiedName~ListToolbar|FullyQualifiedName~OperationsShell|FullyQualifiedName~ShadcnStyleSystem|FullyQualifiedName~LegacyLink"
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --no-build
dotnet format .\Legacy.Maliev.Intranet.slnx --verify-no-changes --no-restore
```

Record exact pass/fail/skip counts and elapsed times. If a wrapper times out but a child remains active, do not launch a duplicate; wait for or safely identify the exact task-owned process before retrying once with a longer bound.

- [ ] **Step 4: Run the exhaustive production-client visual matrix**

Using `IntranetClientServerFixture`, capture representative Orders, Customers, Quotations, Invoices, Purchase Orders, Materials, Diagnostics, Dashboard, and Customer Detail pages at 1280, 768, 390, and 320. Include English and Thai at 320, isolated dark mode, forced colors, reduced motion, coarse pointer, and modeled 200% zoom.

For every captured list assert:

- `document.documentElement.scrollWidth == document.documentElement.clientWidth`.
- Table scroll container owns any overflow.
- Essential values and both applicable actions are visible.
- At most one quick-view row is expanded.
- Keyboard Enter/Space and Escape behavior is correct.
- Refresh/sort/search/page changes clear expansion.
- Touch roots meet 44px geometry.
- No captured console error or page error.

Save evidence under the task's visualization directory, visually read every retained image, and do not commit screenshots.

- [ ] **Step 5: Run Impeccable detector exactly once**

After all source/browser corrections are complete, run one detector command over the shared toolbar/table/breadcrumb/shell components and representative migrated pages. Record exact JSON and exit code. Do not rerun it, even if it returns `[]`; any later fix is validated through build/tests/browser evidence.

- [ ] **Step 6: Perform final critique and correct shared-owner defects**

Use `$impeccable critique` against fresh production-client pages. Review layout hierarchy, interaction states, atomic values, expansion, toolbar alignment, rail hierarchy, breadcrumbs, top bar, English/Thai, contrast, and emotional/cognitive clarity. Correct each confirmed defect at its actual shared or feature owner, add a regression test first, then rerun the affected focused gates and the complete suites from Steps 2–4.

- [ ] **Step 7: Final audit and commit any gate fix**

```powershell
git diff --check
git status --short
```

If Task 8 required code changes, stage only those defect fixes and their tests, audit with `git diff --cached --check`, and commit:

```powershell
git commit -m "fix: close operational UI verification gaps"
```

If no changes were required, do not create an empty commit. Write the ignored task report with build/test/browser/detector evidence, remaining environment concerns, and exact commit range. Do not push or deploy without a new explicit user instruction.
