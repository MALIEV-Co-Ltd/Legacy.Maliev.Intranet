# Native Maliev.ShadcnBlazor Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace every direct MudBlazor UI integration in the Legacy MALIEV Intranet with released Maliev.ShadcnBlazor 1.2.2 components and prove zero direct consumer MudBlazor usage.

**Architecture:** All ten Blazor consumer projects reference the released package rather than the embedded RCL. Migration proceeds through independently buildable component-family and feature waves; application state, DTOs, routes, validation, localization, and BFF contracts remain unchanged. Native HTML is used for non-behavioral layout, while interactive UI uses public Shadcn components and Lucide catalog icons.

**Tech Stack:** .NET 10, Blazor WebAssembly, Maliev.ShadcnBlazor 1.2.2, Maliev.ShadcnBlazor.Icons.Lucide 1.2.2, xUnit 2.9, bUnit, Microsoft Playwright, CSS semantic tokens.

**Spec:** `docs/superpowers/specs/2026-08-29-native-maliev-shadcnblazor-migration-design.md`

## Global Constraints

- Final production consumer code contains zero `<Mud*>` elements, `using MudBlazor`, `@using MudBlazor`, or MudBlazor public types.
- Final Intranet consumer projects contain no direct `MudBlazor` package references; the package's transitive MudBlazor 9.7.0 dependency and documented static assets remain allowed.
- Use exactly `Maliev.ShadcnBlazor` 1.2.2 and `Maliev.ShadcnBlazor.Icons.Lucide` 1.2.2.
- Confirm every component parameter from the installed 1.2.2 API; do not infer MudBlazor or React APIs.
- Preserve routes, API/DTO wire shapes, authorization, bindings, validation timing, localization, money/date semantics, focus behavior, and user-visible states.
- Use package semantic tokens and public attributes only; do not target generated IDs or private package markup.
- Build before tests. Every source wave runs focused tests, the full 1,053-test Intranet suite, applicable browser tests, and `git diff --check` before commit.
- Keep `DOTNET_PROCESSOR_COUNT=1` for the full suite and browser tests. The verified baseline suite takes approximately 9.54 minutes.
- Preserve the pre-existing untracked `.impeccable/critique/...` and `.superpowers/` files and never stage them.
- For a verified package limitation, search upstream issues and either link the existing issue or open a new issue containing version, environment, minimal reproduction, steps, actual behavior, expected behavior, impact, and workaround.
- Do not push, deploy, publish a package, or change the upstream library repository.

---

### Task 1: Released Package Foundation and Fail-Closed Migration Inventory

**Files:**
- Create: `docs/native-shadcn-migration-ledger.json`
- Create: `Legacy.Maliev.Intranet.Tests/NativeShadcnMigrationContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Client/Legacy.Maliev.Intranet.Client.csproj`
- Modify: `Legacy.Maliev.Intranet.Client.Shared/Legacy.Maliev.Intranet.Client.Shared.csproj`
- Modify: all eight `Legacy.Maliev.Intranet.Client.Features.*/Legacy.Maliev.Intranet.Client.Features.*.csproj` files
- Modify: the ten consumer `_Imports.razor` files
- Modify: `Legacy.Maliev.Intranet.Client/wwwroot/index.html`
- Modify: `Legacy.Maliev.Intranet.Tests/ShadcnPackageIntegrationContractTests.cs`

**Interfaces:**
- Consumes: NuGet packages `Maliev.ShadcnBlazor` 1.2.2 and `Maliev.ShadcnBlazor.Icons.Lucide` 1.2.2.
- Produces: one installed public component API across all consumer assemblies, ordered package assets, and a machine-readable baseline inventory keyed by relative file and Mud component type.

- [ ] **Step 1: Write package and inventory tests that fail against the embedded adapter**

Create `NativeShadcnMigrationContractTests.cs` with tests that enumerate only directories whose names equal `Legacy.Maliev.Intranet.Client`, `Legacy.Maliev.Intranet.Client.Shared`, or start with `Legacy.Maliev.Intranet.Client.Features.`. Assert that every project references `Maliev.ShadcnBlazor` version `1.2.2`, no project references `..\Maliev.ShadcnBlazor\Maliev.ShadcnBlazor.csproj`, and the ledger baseline totals are `1485` render sites, `36` types, and `60` Razor files.

```csharp
[Fact]
public void ConsumerProjectsUseTheReleasedPackageAtOneExactVersion()
{
    foreach (var project in ConsumerProjects())
    {
        var xml = XDocument.Load(project);
        var references = xml.Descendants("PackageReference").ToArray();
        Assert.Contains(references, item =>
            (string?)item.Attribute("Include") == "Maliev.ShadcnBlazor" &&
            (string?)item.Attribute("Version") == "1.2.2");
        Assert.DoesNotContain(xml.Descendants("ProjectReference"), item =>
            ((string?)item.Attribute("Include"))?.Contains("Maliev.ShadcnBlazor.csproj", StringComparison.OrdinalIgnoreCase) == true);
    }
}
```

- [ ] **Step 2: Run the focused package tests and verify the expected failure**

Run:

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter "FullyQualifiedName~NativeShadcnMigrationContractTests|FullyQualifiedName~ShadcnPackageIntegrationContractTests"
```

Expected: failure because consumer projects still reference MudBlazor and the embedded RCL.

- [ ] **Step 3: Replace the embedded RCL reference and direct Mud package references**

In every consumer `.csproj`, remove `<PackageReference Include="MudBlazor" Version="9.7.0" />` and embedded `Maliev.ShadcnBlazor.csproj` references. Add:

```xml
<PackageReference Include="Maliev.ShadcnBlazor" Version="1.2.2" />
```

Add the Lucide package to Client, Client.Shared, Orders, Procurement, and Quotations because those projects render icon-bearing shell or action components:

```xml
<PackageReference Include="Maliev.ShadcnBlazor.Icons.Lucide" Version="1.2.2" />
```

- [ ] **Step 4: Add the installed component namespaces without removing temporary Mud imports**

Add the applicable subset of these namespaces to each consumer `_Imports.razor`; retain `@using MudBlazor` only until that project's source wave reaches zero usages:

```razor
@using global::Maliev.ShadcnBlazor.Components.Actions
@using global::Maliev.ShadcnBlazor.Components.Content
@using global::Maliev.ShadcnBlazor.Components.DataDisplay
@using global::Maliev.ShadcnBlazor.Components.Disclosure
@using global::Maliev.ShadcnBlazor.Components.Feedback
@using global::Maliev.ShadcnBlazor.Components.Forms
@using global::Maliev.ShadcnBlazor.Components.Icons
@using global::Maliev.ShadcnBlazor.Components.Navigation
@using global::Maliev.ShadcnBlazor.Components.Selection
@using global::Maliev.ShadcnBlazor.Components.Typography
@using global::Maliev.ShadcnBlazor.Icons.Lucide
```

- [ ] **Step 5: Replace the old two-layer asset setup with the documented 1.2.2 order**

In `index.html`, keep MudBlazor CSS first and its script before Blazor startup, then load `shadcn-base.css`, `shadcn-semantic-foundations.css`, `shadcn-layout.css`, `shadcn-actions.css`, `shadcn-data-display.css`, `shadcn-disclosure-navigation.css`, `shadcn-forms.css`, `shadcn-feedback-content.css`, `shadcn-overlays-menus.css`, `shadcn-conversation.css`, and `shadcn-mudblazor.css` in that order before application styles.

- [ ] **Step 6: Generate the exact baseline ledger**

Write `docs/native-shadcn-migration-ledger.json` with schema version `1`, package version `1.2.2`, baseline totals, and one entry for each of the 60 affected files. Each entry records `project`, `path`, sorted `mudTypes`, `status: "pending"`, `replacementFamilies: []`, and `issues: []`. Generate it from source with a deterministic PowerShell command embedded in the test helper rather than hand-editing counts.

- [ ] **Step 7: Build and pass the foundation tests**

Run:

```powershell
$env:DOTNET_PROCESSOR_COUNT='1'
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~NativeShadcnMigrationContractTests|FullyQualifiedName~ShadcnPackageIntegrationContractTests"
git diff --check
```

Expected: build has zero warnings/errors; focused tests pass; ledger still reports 1,485 remaining render sites.

- [ ] **Step 8: Commit the package foundation**

```powershell
git add docs/native-shadcn-migration-ledger.json Legacy.Maliev.Intranet.Tests/NativeShadcnMigrationContractTests.cs Legacy.Maliev.Intranet.Client*/*.csproj Legacy.Maliev.Intranet.Client*/_Imports.razor Legacy.Maliev.Intranet.Client/wwwroot/index.html Legacy.Maliev.Intranet.Tests/ShadcnPackageIntegrationContractTests.cs
git commit -m "build: adopt released ShadcnBlazor package"
```

### Task 2: Shared Shell, Navigation, Icons, and Layout

**Files:**
- Modify: `Legacy.Maliev.Intranet.Client/Layout/MainLayout.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Layout/LegacyTopBar.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Layout/LegacyTopBar.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client/Components/Shell/LegacyGlobalSearch.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Components/Shell/LegacyLanguageSelector.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Components/Shell/LegacyNavigationRail.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Components/Shell/LegacyQuickActions.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Components/Dashboard/DashboardMetricCard.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Layout/LegacyAppNavigation.cs`
- Modify: `Legacy.Maliev.Intranet.Client.Shared/Components/LegacyLink.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Shared/Components/ListToolbar.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Shared/Components/OperationalTable.razor`
- Modify: `Legacy.Maliev.Intranet.Tests/OperationsShellContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/LegacyLinkSystemContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/ListToolbarBehaviorTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/OperationalTableBehaviorTests.cs`
- Modify: `Maliev.ShadcnBlazor.BrowserTests/OperationalShellBrowserTests.cs`

**Interfaces:**
- Consumes: `ShadcnButton`, `ShadcnInput<string>`, `ShadcnNativeSelect<string>`, `ShadcnIcon`, and `LucideIconCatalog.Instance.Get(name)`.
- Produces: a Mud-free shared shell and reusable application composites that all feature waves depend on.

- [ ] **Step 1: Change shell contract tests to require native Shadcn markup**

Replace Mud-specific assertions with exact checks for one `ShadcnThemeProvider`, native `<div class="legacy-layout">`, `<main id="main-content">`, Shadcn action/input components, and Lucide icon data. Add a project-level assertion that Client.Shared contains neither `<Mud` nor `@using MudBlazor`.

- [ ] **Step 2: Run shell-focused tests and verify they fail**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter "FullyQualifiedName~OperationsShellContractTests|FullyQualifiedName~LegacyLinkSystemContractTests|FullyQualifiedName~ListToolbar|FullyQualifiedName~OperationalTable"
```

- [ ] **Step 3: Replace layout-only Mud components with semantic HTML**

Use this exact structure in `MainLayout.razor`, retaining the existing attributes and event handlers:

```razor
<ShadcnThemeProvider IsDarkMode="@ThemeService.IsDarkMode" Direction="ShadcnDirection.LeftToRight" Class="legacy-provider-root">
    <a class="legacy-skip-link" href="#main-content">@Text["Skip to content"]</a>
    <div class="legacy-layout">
        <LegacyNavigationRail Session="session" />
        <div class="legacy-workspace-frame" @attributes="WorkspaceAccessibilityAttributes">
            <LegacyTopBar Session="session" NavigationOpen="_navigationOpen" OnToggleNavigation="ToggleNavigationAsync" OnCloseNavigation="CloseNavigationAsync" OnSignOut="SignOutAsync" />
            <div class="legacy-workspace-shell">
                <main id="main-content" class="legacy-main-content legacy-page-container" tabindex="-1">@Body</main>
            </div>
        </div>
    </div>
</ShadcnThemeProvider>
```

- [ ] **Step 4: Replace shell controls and icon data**

Use `ShadcnButton` with `Size="ShadcnButtonSize.Icon"` for icon-only actions, `ShadcnInput<string>` for search, and `ShadcnNativeSelect<string>` with `ShadcnNativeSelectOption` children for language. Resolve icons once in `LegacyAppNavigation.cs` or component code:

```csharp
private static readonly ShadcnIconData SearchIcon =
    LucideIconCatalog.Instance.Get(LucideIconNames.Search);
```

Render decorative icons inside an already named button without a label:

```razor
<ShadcnIcon Icon="SearchIcon" Size="20" />
```

- [ ] **Step 5: Migrate shared toolbar, link, and operational-table actions**

Preserve `ListToolbarState`, debounce, refresh, row expansion, route generation, and accessible names. Replace Mud controls only; do not change their public parameters or callbacks.

- [ ] **Step 6: Remove Client.Shared's Mud import and direct type usage**

Search and require no matches:

```powershell
rg -n "<Mud|@using MudBlazor|using MudBlazor" Legacy.Maliev.Intranet.Client.Shared
```

- [ ] **Step 7: Build, run focused/full/browser tests, and commit**

```powershell
$env:DOTNET_PROCESSOR_COUNT='1'
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~OperationsShellContractTests|FullyQualifiedName~LegacyLinkSystemContractTests|FullyQualifiedName~ListToolbar|FullyQualifiedName~OperationalTable"
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --no-build --filter "FullyQualifiedName~OperationalShellBrowserTests"
git diff --check
git add Legacy.Maliev.Intranet.Client Legacy.Maliev.Intranet.Client.Shared Legacy.Maliev.Intranet.Tests Maliev.ShadcnBlazor.BrowserTests docs/native-shadcn-migration-ledger.json
git commit -m "feat: migrate shared shell to Shadcn components"
```

### Task 3: Core Pages, Actions, Typography, Feedback, and Loading States

**Files:**
- Modify: `Legacy.Maliev.Intranet.Client/LoginRedirect.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Pages/AccessDenied.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Pages/CompatibilityDetailRedirect.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Pages/Foundation.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Pages/Home.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Pages/NotFound.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Diagnostics/Pages/ErrorReport.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Components/Shared/PrimaryButton.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Components/Shared/SecondaryButton.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Components/Shared/ProgressiveSkeleton.razor`
- Modify: relevant `.razor.css`, `app.css`, `module-pages.css`, and `operations-pages.css`
- Modify: `Legacy.Maliev.Intranet.Tests/MudBlazorComponentConformanceTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/DashboardCommandCenterProjectionTests.cs`

**Interfaces:**
- Consumes: `ShadcnButton`, `ShadcnTypography`, `ShadcnAlert` with title/description children, `ShadcnProgress`, `ShadcnSpinner`, `ShadcnSkeleton`, `ShadcnCard`, and semantic HTML tables.
- Produces: canonical application patterns for non-form pages and shared action wrappers.

- [ ] **Step 1: Replace the old conformance test with a Shadcn-first rule**

Rename the test class to `NativeShadcnComponentConformanceTests`. Reject `<Mud`, Mud namespaces, and native interactive elements outside the reviewed semantic table/layout allowlist. Require `ShadcnButton` for actions and `ShadcnAlert` for alert states.

- [ ] **Step 2: Run the focused tests and verify failure on current markup**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter "FullyQualifiedName~NativeShadcnComponentConformanceTests|FullyQualifiedName~DashboardCommandCenterProjectionTests"
```

- [ ] **Step 3: Apply the canonical feedback and loading compositions**

Use status alerts for non-destructive information and destructive alerts for errors:

```razor
<ShadcnAlert Variant="ShadcnAlertVariant.Destructive" AlertRole="ShadcnAlertRole.Alert">
    <ShadcnAlertTitle>@Text["Unable to load"]</ShadcnAlertTitle>
    <ShadcnAlertDescription>@error</ShadcnAlertDescription>
</ShadcnAlert>
```

Use `ShadcnProgress Value="null" Label="@Text["Loading"]"` for indeterminate page progress, `ShadcnSpinner` for compact button-adjacent activity, and `ShadcnSkeleton` with existing geometry classes for reserved layouts.

- [ ] **Step 4: Replace typography, cards, actions, and dashboard tables**

Map heading typography to `ShadcnTypographyVariant.H1` through `H4`, muted supporting copy to `Muted`, MudPaper to `ShadcnCard`, and MudSimpleTable to the full `ShadcnTable` composition. Keep `PageTitle`, resource keys, `aria-*`, routes, and row data unchanged.

- [ ] **Step 5: Remove obsolete generic `.mud-*` styling touched by these pages**

Delete selectors only after their migrated owners no longer render the runtime class. Replace page-specific geometry selectors with named application classes and Shadcn token values.

- [ ] **Step 6: Build, validate, update ledger, and commit**

Run the Release build, focused tests, full Intranet suite, Dashboard and diagnostics browser cases, `git diff --check`, then commit:

```powershell
git commit -m "feat: migrate core feedback to Shadcn components"
```

### Task 4: Forms and Input Composition Foundation

**Files:**
- Create: `Legacy.Maliev.Intranet.Client.Shared/Components/ShadcnFormField.razor`
- Create: `Legacy.Maliev.Intranet.Client.Shared/Components/ShadcnFormActions.razor`
- Create: `Legacy.Maliev.Intranet.Tests/ShadcnFormCompositionTests.cs`
- Modify: `Legacy.Maliev.Intranet.Client/Pages/Login.razor`
- Modify: `Legacy.Maliev.Intranet.Tests/LoginFormContractTests.cs`
- Modify: `Maliev.ShadcnBlazor.BrowserTests/LoginBrowserTests.cs`

**Interfaces:**
- Consumes: `EditForm`, `DataAnnotationsValidator`, `ShadcnField`, `ShadcnFieldLabel`, `ShadcnFieldDescription`, `ShadcnFieldError`, `ShadcnInput<TValue>`, `ShadcnCheckbox`, `ShadcnSelect<TValue>`, `ShadcnNativeSelect<TValue>`, `ShadcnDatePicker`, and `ShadcnButton`.
- Produces: application-only field and action compositions that preserve existing model expressions without copying library behavior.

- [ ] **Step 1: Write bUnit tests for labels, validation, disabled state, and submit behavior**

Assert that `ShadcnFormField` associates label, description, and error IDs with the provided input render fragment, and `ShadcnFormActions` renders submit/cancel actions without double submission.

- [ ] **Step 2: Run the form tests and verify missing components fail**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter "FullyQualifiedName~ShadcnFormCompositionTests|FullyQualifiedName~LoginFormContractTests"
```

- [ ] **Step 3: Implement the two application compositions**

`ShadcnFormField` accepts `Id`, `Label`, `Description`, `Error`, `Required`, and `RenderFragment Control`; it renders package field children and no Mud API. `ShadcnFormActions` accepts `SubmitText`, `CancelText`, `IsBusy`, `OnCancel`, and an optional leading action; its submit button uses `ButtonType="ShadcnButtonType.Submit"`, `Disabled="IsBusy"`, and localized busy text.

- [ ] **Step 4: Migrate Login without changing authentication behavior**

Replace `MudForm` with `EditForm Model="model" OnValidSubmit="SignInAsync"`, preserve the email regex, password input type, remember-me binding, Google button, theme toggle, error alert, return URL, and CSRF/BFF calls. Use `Value`, `ValueChanged`, and `ValueExpression` on typed inputs.

- [ ] **Step 5: Run browser interaction coverage**

Verify keyboard tab order, invalid email announcement, password secrecy, remember-me toggling, busy state, dark theme toggle, and narrow layout. If the Shadcn input or checkbox cannot preserve one of these behaviors through its public API, follow the issue protocol before adding a workaround.

- [ ] **Step 6: Build, run full validation, and commit**

```powershell
git commit -m "feat: establish Shadcn form compositions"
```

### Task 5: Customer, Employee, and Catalog Features

**Files:**
- Modify: all seven Razor files under `Legacy.Maliev.Intranet.Client.Features.Customers/Components` and `Pages` listed in the migration ledger
- Modify: all seven affected pages under `Legacy.Maliev.Intranet.Client.Features.Employees/Pages`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/MaterialCreate.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/MaterialDetail.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/Materials.razor`
- Modify: the corresponding feature `_Imports.razor` files and feature CSS
- Modify: Customer, Employee, Material, localization, accessibility, and responsive contract tests
- Modify: `Maliev.ShadcnBlazor.BrowserTests/CustomerDetailBrowserTests.cs`
- Modify: `Maliev.ShadcnBlazor.BrowserTests/CustomerResponsiveBrowserTests.cs`
- Modify: `Maliev.ShadcnBlazor.BrowserTests/OperationalTableMigrationBrowserTests.cs`

**Interfaces:**
- Consumes: Task 2 shell/table components and Task 4 form compositions.
- Produces: three complete feature assemblies with no direct Mud source usage.

- [ ] **Step 1: Update feature contract tests to assert Shadcn ownership and preserved workflows**

Keep existing DTO, route, permission, concurrency, and localization assertions. Replace checks such as `MudForm`, `MudTable`, and `MudProgressLinear` with the corresponding Shadcn field, table, tabs, alert, progress, and button contracts.

- [ ] **Step 2: Migrate Customer list, overview, activity, history, create, and view flows**

Use `ShadcnTabs Value="@activeTab" ValueChanged="SetActiveTab"` with explicit `ShadcnTabsList`, `ShadcnTabsTrigger`, and `ShadcnTabsContent`. Preserve URL-backed history selection, permissions, page clamps, latest-request cancellation, and responsive record destinations.

- [ ] **Step 3: Migrate Employee create, profile, recovery, list, and detail flows**

Use `EditForm` plus Task 4 fields, date picker for date-only values, numeric input via `ShadcnInput<int?> Type="number"`, and package feedback components. Preserve password reset secrecy, confirmation action, profile ownership, and Bangkok date formatting.

- [ ] **Step 4: Migrate Material list, create, and detail flows**

Build `IReadOnlyList<ShadcnSelectOption<int?>>` values for groups/currencies and bind with typed `Value`, `ValueChanged`, and `ValueExpression`. Preserve color and surface-finish checkbox synchronization, decimal parsing, form validation, and CSRF writes.

- [ ] **Step 5: Remove Mud imports and verify each feature directory is clean**

```powershell
rg -n "<Mud|@using MudBlazor|using MudBlazor" Legacy.Maliev.Intranet.Client.Features.Customers Legacy.Maliev.Intranet.Client.Features.Employees Legacy.Maliev.Intranet.Client.Features.Catalog
```

Expected: no matches.

- [ ] **Step 6: Build, run focused/full/browser suites, update ledger, and commit**

```powershell
git commit -m "feat: migrate people and catalog features to Shadcn"
```

### Task 6: Orders and Procurement Features

**Files:**
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderCreate.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderDetail.razor`
- Modify: all six affected pages under `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages`
- Modify: corresponding `_Imports.razor`, `.razor.css`, and module CSS files
- Modify: Orders and Procurement contract/behavior tests
- Modify: `Maliev.ShadcnBlazor.BrowserTests/OrdersOperationalTableBrowserTests.cs`
- Modify: `Maliev.ShadcnBlazor.BrowserTests/OperationalTableMigrationBrowserTests.cs`

**Interfaces:**
- Consumes: Task 2 operational shell/table, Task 4 forms, `ShadcnAccordion`, `ShadcnTable`, and Lucide action icons.
- Produces: Mud-free order and procurement feature assemblies with unchanged compensated write workflows.

- [ ] **Step 1: Freeze order and procurement behavior in failing Shadcn-oriented tests**

Retain checks for multipart upload, idempotency, status transitions, concurrency, compensation, supplier/address ownership, PDF isolation, page-size defaults, mobile table behavior, and destructive confirmation focus.

- [ ] **Step 2: Migrate OrderCreate and OrderDetail**

Replace expansion panels with `ShadcnAccordion`/item/trigger/content, lists with `ShadcnItemGroup` and `ShadcnItem`, field controls with Task 4 compositions, and icon buttons with named Shadcn buttons and Lucide icons. Preserve persistent save, bounded status history, upload/removal, status transition, and cancellation gates.

- [ ] **Step 3: Migrate PurchaseOrder create/list/view**

Use Shadcn forms and tables for line items, native CSS grid for responsive field groups, and destructive Shadcn buttons inside the existing focus-managed confirmation composition. Keep stable operation keys and reverse compensation order unchanged.

- [ ] **Step 4: Migrate Supplier create/list/view**

Preserve supplier/address ownership, server-side validation, delete confirmation, localized labels, and no-retry writes.

- [ ] **Step 5: Remove Mud imports, run zero-match search, validate, and commit**

Run Release build, relevant order/procurement tests, full suite, both browser classes, and commit:

```powershell
git commit -m "feat: migrate order and procurement features to Shadcn"
```

### Task 7: Accounting and Quotation Features, Tables, Tabs, and Charts

**Files:**
- Modify: all eight affected Accounting pages listed in the migration ledger
- Modify: all seven affected Quotation pages listed in the migration ledger
- Modify: corresponding `_Imports.razor`, `.razor.css`, and module CSS files
- Modify: `Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/NetProfitChart.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/YearlyActivityChart.razor`
- Modify: Accounting, Quotation, chart, accessibility, operations, and responsive tests
- Modify: relevant Playwright tests for finance, invoice, quotation, and operational tables

**Interfaces:**
- Consumes: Task 4 forms, `ShadcnTable` composition, `ShadcnTabs`, `ShadcnBreadcrumb`, `ShadcnBadge`, `ShadcnSeparator`, and native `ShadcnChart` data types.
- Produces: final feature migrations and direct proof of the package's data-display behavior in realistic THB/date workflows.

- [ ] **Step 1: Update chart/table/tab contract tests to the installed Shadcn API**

Preserve exact data projections, sorting, THB formatting, cumulative totals, date-only semantics, invoice/quotation calculations, row completeness, and accessible table fallback. Add assertions that every chart series key has a matching `ShadcnChartConfigItem`.

- [ ] **Step 2: Migrate Accounting CRUD and list surfaces**

Replace MudChip status output with `ShadcnBadge`, Mud lists with item compositions, all forms with Task 4 fields, and raw Mud tables with `ShadcnTable` header/body/row/cell components. Keep invoice and finance concurrency headers, receipt/file ownership, and authoritative previews unchanged.

- [ ] **Step 3: Convert both accounting charts to native ShadcnChart**

Create stable category strings, `ShadcnChartSeries` values, and config entries from the existing view model. Provide localized `Title`, `Description`, `ValueFormatter`, and a semantic table fallback containing the same values. Remove `ShadcnMudChartOptions` usage.

- [ ] **Step 4: Migrate Quotation request, create, estimate, list, and view surfaces**

Replace breadcrumbs with the full package breadcrumb composition, tabs with controlled Shadcn tabs, divider with `ShadcnSeparator`, responsive line-item tables with Shadcn tables, and the quotation decision chart with native ShadcnChart. Preserve order search, pricing, VAT/withholding, rollback, upload, PDF, expiration, and decision semantics.

- [ ] **Step 5: Exercise package gaps before workarounds**

For typed nullable decimal input, date-only binding, select option rendering, table column semantics, tabs keyboard behavior, and chart formatting, reproduce any mismatch against a minimal 1.2.2 consumer. Search `gh issue list -R MALIEV-Co-Ltd/Maliev.ShadcnBlazor --state all`; create a detailed issue only for confirmed package behavior.

- [ ] **Step 6: Remove Mud imports, run focused/full/browser validation, update ledger, and commit**

```powershell
git commit -m "feat: migrate accounting and quotations to Shadcn"
```

### Task 8: Remove the Embedded Adapter and Enforce Zero Direct Mud Usage

**Files:**
- Delete: `Maliev.ShadcnBlazor/`
- Delete: `Maliev.ShadcnBlazor.Tests/`
- Delete: `Maliev.ShadcnBlazor.Showcase/`
- Delete: `Maliev.ShadcnBlazor.BrowserTests/` after moving consumer Playwright coverage to `Legacy.Maliev.Intranet.BrowserTests/`
- Create: `Legacy.Maliev.Intranet.BrowserTests/Legacy.Maliev.Intranet.BrowserTests.csproj`
- Move and update: consumer-relevant Playwright fixtures/tests from the old browser project
- Modify: `Legacy.Maliev.Intranet.slnx`
- Modify: `.github/workflows/_build-and-test.yml`
- Modify: `scripts/install-shadcn-browser.ps1`
- Modify: `Legacy.Maliev.Intranet.Tests/NativeShadcnMigrationContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/ShadcnPackageIntegrationContractTests.cs`
- Modify: styling ownership tests and CSS files
- Modify: `docs/native-shadcn-migration-ledger.json`

**Interfaces:**
- Consumes: all completed migration waves and released package assets.
- Produces: an application-only repository, consumer browser suite, and fail-closed zero-Mud gate.

- [ ] **Step 1: Add final zero-Mud tests before deleting anything**

Require the following searches to return no matches in consumer production and test source, excluding generated `bin`/`obj` files and the text of the migration documentation itself:

```csharp
Assert.Empty(FindConsumerMatches(@"<\s*Mud[A-Za-z0-9_]+"));
Assert.Empty(FindConsumerMatches(@"(?:@using|using)\s+MudBlazor"));
Assert.Empty(FindConsumerProjectReferences("MudBlazor"));
Assert.All(LedgerEntries(), entry => Assert.Equal("migrated", entry.Status));
```

- [ ] **Step 2: Run final contract tests and verify they expose any remaining usage**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter "FullyQualifiedName~NativeShadcnMigrationContractTests|FullyQualifiedName~ShadcnPackageIntegrationContractTests"
```

- [ ] **Step 3: Move consumer Playwright infrastructure out of the obsolete package test project**

The new browser project references the Intranet Client/BFF as required, retains the pinned Microsoft.Playwright package version and fixture lifecycle, and contains only Intranet routes and package-consumer behavior. Remove Showcase-only catalog/reference tests because upstream owns those tests.

- [ ] **Step 4: Remove embedded package projects and update solution/CI/scripts**

Delete the four obsolete directories only after confirming no remaining project reference. Update the solution and workflow to restore/build/install Playwright from `Legacy.Maliev.Intranet.BrowserTests` and run that project serially.

- [ ] **Step 5: Remove obsolete adapter CSS and tests**

Delete `mudblazor-overrides.css` and remove its link. Reduce `shadcn.css`, `operations-pages.css`, and `module-pages.css` to named application layout only. Delete tests whose only purpose was to validate the embedded package's private CSS; replace them with consumer-facing asset-order, semantic-token, rendered-behavior, and zero-private-selector tests.

- [ ] **Step 6: Mark every ledger entry migrated and record issue URLs**

Each ledger entry contains non-empty `replacementFamilies`, `status: "migrated"`, and any upstream issue URLs. Totals become `remainingRenderSites: 0`, `remainingTypes: 0`, and `remainingFiles: 0` while retaining baseline totals for audit history.

- [ ] **Step 7: Build, run final contract checks, and commit removal**

```powershell
$env:DOTNET_PROCESSOR_COUNT='1'
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~NativeShadcnMigrationContractTests|FullyQualifiedName~ShadcnPackageIntegrationContractTests"
git diff --check
git add -A -- Legacy.Maliev.Intranet.slnx .github scripts Legacy.Maliev.Intranet.Client* Legacy.Maliev.Intranet.Tests Legacy.Maliev.Intranet.BrowserTests docs/native-shadcn-migration-ledger.json Maliev.ShadcnBlazor Maliev.ShadcnBlazor.Tests Maliev.ShadcnBlazor.Showcase Maliev.ShadcnBlazor.BrowserTests
git commit -m "build: remove embedded Shadcn adapter"
```

### Task 9: Full Completion Audit

**Files:**
- Modify only if evidence reveals an incomplete migration: the affected consumer, test, ledger, or workflow file
- Read: `docs/native-shadcn-migration-ledger.json`
- Read: upstream issues linked by the ledger

**Interfaces:**
- Consumes: the entire migrated repository and all linked upstream issue evidence.
- Produces: command output proving every requirement in the design completion boundary.

- [ ] **Step 1: Verify repository status and exact dependency graph**

```powershell
git status --short --branch
dotnet list .\Legacy.Maliev.Intranet.Client\Legacy.Maliev.Intranet.Client.csproj package --include-transitive
rg -n "<Mud|@using MudBlazor|using MudBlazor|PackageReference Include=\"MudBlazor\"|Maliev.ShadcnBlazor.csproj" --glob "Legacy.Maliev.Intranet.Client/**" --glob "Legacy.Maliev.Intranet.Client.Shared/**" --glob "Legacy.Maliev.Intranet.Client.Features.*/**" --glob "Legacy.Maliev.Intranet.Tests/**" --glob "Legacy.Maliev.Intranet.BrowserTests/**" .
```

Expected: no source/project matches; dependency output shows Maliev.ShadcnBlazor 1.2.2 and MudBlazor only transitively.

- [ ] **Step 2: Run the clean Release build**

```powershell
$env:DOTNET_PROCESSOR_COUNT='1'
dotnet clean .\Legacy.Maliev.Intranet.slnx -c Release
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
```

Expected: zero warnings and zero errors.

- [ ] **Step 3: Run the complete Intranet suite**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --logger "console;verbosity=normal"
```

Expected: every test passes; report the exact count and duration.

- [ ] **Step 4: Run all consumer browser tests**

```powershell
dotnet build .\Legacy.Maliev.Intranet.BrowserTests\Legacy.Maliev.Intranet.BrowserTests.csproj -c Release
pwsh -NoProfile -File .\Legacy.Maliev.Intranet.BrowserTests\bin\Release\net10.0\playwright.ps1 install chromium
dotnet test .\Legacy.Maliev.Intranet.BrowserTests\Legacy.Maliev.Intranet.BrowserTests.csproj -c Release --no-build
```

Expected: every browser test passes at the specified desktop/mobile/theme/keyboard states.

- [ ] **Step 5: Run formatting, diff, ledger, and issue audit**

```powershell
dotnet format .\Legacy.Maliev.Intranet.slnx --verify-no-changes
git diff --check
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~NativeShadcnMigrationContractTests"
```

For every ledger issue URL, read the live GitHub issue and verify that it contains the package version, reproduction, actual behavior, expected behavior, impact, and workaround. Do not claim an unverified issue was reported.

- [ ] **Step 6: Inspect rendered artifacts and repository diff**

Open representative desktop and mobile screenshots for Login, Dashboard, a list, a form, a detail page, a table, tabs, an overlay, and each chart. Inspect at original resolution for clipping, focus visibility, theme errors, Thai wrapping, and unexpected layout shifts. Review `git diff origin/main...HEAD` and confirm only migration work and its documentation are present.

- [ ] **Step 7: Commit any evidence-driven corrections separately**

If the audit reveals a defect, add the smallest regression test, fix it, rerun the affected and full gates, and commit with an observable outcome message. If no correction is needed, create no artificial final commit.

- [ ] **Step 8: Report completion without pushing or deploying**

Report package versions, projects and component families migrated, zero-Mud inventory result, build/test/browser counts, commit hashes, upstream issue links, skipped checks with blockers, and residual risk. Mark the active goal complete only when all evidence above passes.
