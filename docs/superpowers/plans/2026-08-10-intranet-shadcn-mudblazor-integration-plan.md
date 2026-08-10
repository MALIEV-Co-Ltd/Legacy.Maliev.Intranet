# Intranet Shadcn MudBlazor Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate `Maliev.ShadcnBlazor` into `Legacy.Maliev.Intranet.Client` and give all 41 currently rendered MudBlazor component types the approved Shadcn Base UI, Vega, Neutral appearance without changing application behavior.

**Architecture:** MudBlazor 9.7 remains the behavior and DOM foundation. The reusable RCL owns canonical tokens, provider behavior, and a new scoped `shadcn-mudblazor.css`; the Intranet owns only shell, layout, and named business compositions. `LegacyThemeService` drives both layouts and the login page, while deterministic showcase fixtures and actual Intranet flows prove package and production behavior separately.

**Tech Stack:** .NET 10, Blazor WebAssembly, Razor Class Library static web assets, MudBlazor 9.7.0, bUnit 2.9.0, xUnit 2.9.3, Microsoft Playwright 1.61.0, CSS custom properties, PowerShell 7.

## Global Constraints

- Authoritative Shadcn source is commit `6261bd89f72d794aea491482cc2acfd8dc3d63e2`, Base UI registry, Vega geometry, Neutral palette.
- Default controls are exactly 36 CSS pixels high; small controls are exactly 32 CSS pixels; standard component icons are 16 CSS pixels.
- Coarse-pointer and mobile interactive targets are at least 44 by 44 CSS pixels.
- The package default font remains its upstream sans stack; Intranet overrides `--shadcn-font-sans` with self-hosted IBM Plex Sans Thai.
- Both supported cultures, `en-TH` and `th-TH`, are explicitly LTR.
- Preserve all Mud bindings, callbacks, `For`, validation, disabled/read-only distinctions, navigation, authentication/session state, lazy feature assemblies, and portal behavior.
- Preserve resource-backed Thai and English copy. Money remains THB with current formatting. Display time remains Asia/Bangkok and stored timestamps remain UTC.
- Do not change API, DTO, JSON, cookie, storage, message, database, business workflow, or compatibility-host contracts.
- The non-Mud Razor compatibility host is outside scope.
- Build affected projects before tests. Every build must finish with zero warnings and zero errors.
- Use `apply_patch` for edits. Preserve unrelated work. Stage only task-owned files.
- Do not push without a fresh explicit instruction.

## File responsibility map

- `Maliev.ShadcnBlazor/wwwroot/css/shadcn-base.css` — canonical tokens and media primitives only.
- `Maliev.ShadcnBlazor/wwwroot/css/shadcn-mudblazor.css` — sole reusable owner of Mud component appearance and states.
- `Maliev.ShadcnBlazor/Components/ShadcnThemeProvider.razor` — root theme, direction, provider, and overlay scope.
- `Legacy.Maliev.Intranet.Client/LegacyThemeService.cs` — sole persisted interactive theme authority.
- `Legacy.Maliev.Intranet.Client/Layout/MainLayout.razor` and `Layout/EmptyLayout.razor` — package-provider consumers; no local Mud themes.
- `Legacy.Maliev.Intranet.Client/wwwroot/css/design-tokens.css` — MALIEV/legacy aliases to package tokens.
- `Legacy.Maliev.Intranet.Client/wwwroot/css/app.css` — document, login, and shell layout.
- `Legacy.Maliev.Intranet.Client/wwwroot/css/module-pages.css` — `.mlv-*` module layout.
- `Legacy.Maliev.Intranet.Client/wwwroot/css/operations-pages.css` — operations geometry and responsive composition only.
- `Legacy.Maliev.Intranet.Client/wwwroot/css/mudblazor-overrides.css` — narrowly documented Mud 9 DOM compatibility only.
- `Legacy.Maliev.Intranet.Client/wwwroot/css/shadcn.css` — named product shell/business compositions only; no generic Mud appearance.
- `Maliev.ShadcnBlazor.Showcase/Pages/MudInventory.razor` — deterministic 41-type state fixture.
- `Maliev.ShadcnBlazor.Tests/Contracts/MudAdapterContractTests.cs` — reusable adapter inventory, selector, token, and package-asset contract.
- `Legacy.Maliev.Intranet.Tests/ShadcnPackageIntegrationContractTests.cs` — production reference, registration, provider, theme, and load-order contract.
- `Legacy.Maliev.Intranet.Tests/ShadcnCssOwnershipContractTests.cs` — prevents competing product appearance rules.
- `Maliev.ShadcnBlazor.BrowserTests/MudInventoryBrowserTests.cs` — computed-style, interaction, responsive, accessibility-media, and console evidence.

---

### Task 1: Establish the reusable Mud adapter asset and inventory contract

**Files:**
- Modify: `Maliev.ShadcnBlazor/wwwroot/css/shadcn-base.css`
- Create: `Maliev.ShadcnBlazor/wwwroot/css/shadcn-mudblazor.css`
- Create: `Maliev.ShadcnBlazor.Tests/Contracts/MudAdapterContractTests.cs`
- Modify: `Maliev.ShadcnBlazor.Tests/Contracts/TokenContractTests.cs`
- Modify: `Maliev.ShadcnBlazor.Tests/Contracts/PackageContractTests.cs`
- Modify: `Maliev.ShadcnBlazor.Showcase/wwwroot/index.html`
- Modify: `Maliev.ShadcnBlazor/README.md`

**Interfaces:**
- Consumes: `ShadcnCss.ScopeClass`, `ShadcnCss.OverlayScopeClass`, and tokens from `shadcn-base.css`.
- Produces: `_content/Maliev.ShadcnBlazor/css/shadcn-mudblazor.css`, scoped beneath `.shadcn-scope` or `.shadcn-overlay-scope`; a frozen 41-type test inventory used by later tasks.

- [ ] **Step 1: Write the failing adapter and package tests**

Create `MudAdapterContractTests.cs` with a root finder and the exact production type list:

```csharp
namespace Maliev.ShadcnBlazor.Tests.Contracts;

public sealed class MudAdapterContractTests
{
    internal static readonly string[] ProductionTypes =
    [
        "MudAlert", "MudBreadcrumbs", "MudButton", "MudChart", "MudCheckBox", "MudChip",
        "MudContainer", "MudDatePicker", "MudDialogProvider", "MudDivider", "MudExpansionPanel",
        "MudExpansionPanels", "MudForm", "MudGrid", "MudIcon", "MudIconButton", "MudItem",
        "MudLayout", "MudLink", "MudList", "MudListItem", "MudMainContent", "MudNumericField",
        "MudPaper", "MudPopoverProvider", "MudProgressCircular", "MudProgressLinear", "MudSelect",
        "MudSelectItem", "MudSimpleTable", "MudSkeleton", "MudSnackbarProvider", "MudStack",
        "MudTable", "MudTabPanel", "MudTabs", "MudTd", "MudText", "MudTextField", "MudTh",
        "MudThemeProvider"
    ];

    [Fact]
    public void ProductionInventoryIsFrozenAtFortyOneUniqueTypes()
    {
        Assert.Equal(41, ProductionTypes.Length);
        Assert.Equal(41, ProductionTypes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void BaseOwnsPrimitivesAndAdapterConsumesThem()
    {
        var foundation = ReadFoundation();
        var css = ReadAdapter();
        Assert.Contains(":where(.shadcn-scope, .shadcn-overlay-scope)", css, StringComparison.Ordinal);
        Assert.Contains("height: var(--shadcn-control-height)", css, StringComparison.Ordinal);
        Assert.DoesNotContain("--shadcn-control-height:", css, StringComparison.Ordinal);
        Assert.Contains("--shadcn-control-height: 2.25rem", foundation, StringComparison.Ordinal);
        Assert.Contains("--shadcn-control-height-sm: 2rem", foundation, StringComparison.Ordinal);
        Assert.Contains("@media (pointer: coarse)", foundation, StringComparison.Ordinal);
        Assert.Contains("min-width: 2.75rem", foundation, StringComparison.Ordinal);
        Assert.Contains("min-height: 2.75rem", foundation, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", foundation, StringComparison.Ordinal);
        Assert.Contains("@media (forced-colors: active)", foundation, StringComparison.Ordinal);
    }

    internal static string ReadAdapter() => File.ReadAllText(Path.Combine(
        FindRoot(), "Maliev.ShadcnBlazor", "wwwroot", "css", "shadcn-mudblazor.css"));

    private static string ReadFoundation() => File.ReadAllText(Path.Combine(
        FindRoot(), "Maliev.ShadcnBlazor", "wwwroot", "css", "shadcn-base.css"));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
```

Extend `PackageContractTests` so the freshly built nupkg must contain exactly one `staticwebassets/css/shadcn-base.css` and exactly one `staticwebassets/css/shadcn-mudblazor.css` entry.

- [ ] **Step 2: Run RED tests**

Run:

```powershell
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --filter "FullyQualifiedName~MudAdapterContractTests|FullyQualifiedName~PackageContractTests"
```

Expected: failure because `shadcn-mudblazor.css` does not exist and the packed asset assertion is unmet.

- [ ] **Step 3: Create the foundational scoped adapter**

Refine the existing coarse-pointer block in `shadcn-base.css` so button-like controls have square-safe targets while text controls keep fluid width:

```css
@media (pointer: coarse) {
    .shadcn-scope :where(button, [role="button"]),
    .shadcn-overlay-scope :where(button, [role="button"]) {
        min-width: 2.75rem;
        min-height: 2.75rem;
    }

    .shadcn-scope :where(input, select, textarea),
    .shadcn-overlay-scope :where(input, select, textarea) {
        min-height: 2.75rem;
    }
}
```

Extend `TokenContractTests` to assert the exact split. Create the adapter stylesheet with scoped Mud foundations that consume, but never redefine, base tokens:

```css
:where(.shadcn-scope, .shadcn-overlay-scope) .mud-button-root {
    height: var(--shadcn-control-height);
}

:where(.shadcn-scope, .shadcn-overlay-scope) :where(button, input, textarea, select, [tabindex]):focus-visible {
    outline: none;
}

:where(.shadcn-scope, .shadcn-overlay-scope) :where(.mud-button-root, .mud-icon-button-root, .mud-input-control):focus-visible,
:where(.shadcn-scope, .shadcn-overlay-scope) :where(.mud-input-control):focus-within {
    box-shadow: 0 0 0 3px color-mix(in oklab, var(--shadcn-ring) 50%, transparent);
}

:where(.shadcn-scope, .shadcn-overlay-scope) :where([disabled], .mud-disabled) {
    cursor: not-allowed;
    opacity: 0.5;
}
```

Do not add `width: 100%` to `.mud-button-root`; checkbox internals must remain square.

- [ ] **Step 4: Wire the asset into the showcase and package documentation**

Load the adapter immediately after `shadcn-base.css` in the showcase:

```html
<link href="_content/Maliev.ShadcnBlazor/css/shadcn-base.css" rel="stylesheet" />
<link href="_content/Maliev.ShadcnBlazor/css/shadcn-mudblazor.css" rel="stylesheet" />
```

Update the consumer setup in `README.md` with the same two ordered links.

- [ ] **Step 5: Build first, then run focused and package suites**

Run:

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~MudAdapterContractTests|FullyQualifiedName~PackageContractTests"
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build --no-restore
```

Expected: build reports zero warnings/errors; focused and full package suites pass.

- [ ] **Step 6: Commit the reusable adapter foundation**

```powershell
git add -- Maliev.ShadcnBlazor/wwwroot/css/shadcn-base.css Maliev.ShadcnBlazor/wwwroot/css/shadcn-mudblazor.css Maliev.ShadcnBlazor.Tests/Contracts/MudAdapterContractTests.cs Maliev.ShadcnBlazor.Tests/Contracts/TokenContractTests.cs Maliev.ShadcnBlazor.Tests/Contracts/PackageContractTests.cs Maliev.ShadcnBlazor.Showcase/wwwroot/index.html Maliev.ShadcnBlazor/README.md
git commit -m "feat: establish reusable Mud shadcn adapter"
```

---

### Task 2: Integrate package registration, providers, and persisted theme flow

**Files:**
- Create: `Legacy.Maliev.Intranet.Tests/ShadcnPackageIntegrationContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Client/Legacy.Maliev.Intranet.Client.csproj`
- Modify: `Legacy.Maliev.Intranet.Client/Program.cs`
- Modify: `Legacy.Maliev.Intranet.Client/_Imports.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Layout/MainLayout.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Layout/EmptyLayout.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Pages/Login.razor`
- Modify: `Legacy.Maliev.Intranet.Client/wwwroot/index.html`
- Modify: `Legacy.Maliev.Intranet.Client/wwwroot/css/app.css`
- Modify: `Legacy.Maliev.Intranet.Tests/LegacyThemeContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/ShadcnStyleSystemContractTests.cs`

**Interfaces:**
- Consumes: `IServiceCollection.AddMalievShadcn(Action<ShadcnOptions>?)`, `ShadcnThemeProvider`, `ShadcnDirection.LeftToRight`, both package CSS assets.
- Produces: one client-wide provider/theme contract; `LegacyThemeService.IsDarkMode` drives authenticated and anonymous layouts and Login.

- [ ] **Step 1: Write failing production integration contracts**

Create source contracts that assert the observable wiring, not handwritten palette literals:

```csharp
[Fact]
public void ClientReferencesAndRegistersTheReusablePackageOnce()
{
    var project = Read("Legacy.Maliev.Intranet.Client", "Legacy.Maliev.Intranet.Client.csproj");
    var program = Read("Legacy.Maliev.Intranet.Client", "Program.cs");
    Assert.Contains("Maliev.ShadcnBlazor.csproj", project, StringComparison.Ordinal);
    Assert.Contains("AddMalievShadcn", program, StringComparison.Ordinal);
    Assert.DoesNotContain("AddMudServices", program, StringComparison.Ordinal);
    Assert.Contains("IBM Plex Sans Thai", program, StringComparison.Ordinal);
}

[Theory]
[InlineData("MainLayout.razor")]
[InlineData("EmptyLayout.razor")]
public void LayoutUsesOneShadcnProviderAndNoLocalMudTheme(string file)
{
    var layout = Read("Legacy.Maliev.Intranet.Client", "Layout", file);
    Assert.Equal(1, Count(layout, "<ShadcnThemeProvider"));
    Assert.Contains("IsDarkMode=\"@ThemeService.IsDarkMode\"", layout, StringComparison.Ordinal);
    Assert.Contains("Direction=\"ShadcnDirection.LeftToRight\"", layout, StringComparison.Ordinal);
    Assert.DoesNotContain("<MudThemeProvider", layout, StringComparison.Ordinal);
    Assert.DoesNotContain("<MudPopoverProvider", layout, StringComparison.Ordinal);
    Assert.DoesNotContain("<MudDialogProvider", layout, StringComparison.Ordinal);
    Assert.DoesNotContain("<MudSnackbarProvider", layout, StringComparison.Ordinal);
    Assert.DoesNotContain("new MudTheme", layout, StringComparison.Ordinal);
}

[Fact]
public void BootstrapAndStylesSynchronizeBothThemeContractsInExactOrder()
{
    var index = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "index.html");
    Assert.Contains("root.dataset.shadcnTheme = theme", index, StringComparison.Ordinal);
    AssertOrder(index,
        "_content/MudBlazor/MudBlazor.min.css",
        "css/ibm-plex-sans-thai.css",
        "_content/Maliev.ShadcnBlazor/css/shadcn-base.css",
        "css/design-tokens.css",
        "_content/Maliev.ShadcnBlazor/css/shadcn-mudblazor.css",
        "css/app.css",
        "css/module-pages.css",
        "css/utilities.css",
        "css/mudblazor-overrides.css",
        "css/operations-pages.css",
        "css/shadcn.css",
        "Legacy.Maliev.Intranet.Client.styles.css",
        "css/loading-shell.css");
}

[Fact]
public void LoginUsesLegacyThemeServiceAndDoesNotCallThemeInteropDirectly()
{
    var login = Read("Legacy.Maliev.Intranet.Client", "Pages", "Login.razor");
    Assert.Contains("@inject LegacyThemeService ThemeService", login, StringComparison.Ordinal);
    Assert.Contains("ThemeService.ToggleAsync", login, StringComparison.Ordinal);
    Assert.DoesNotContain("malievTheme.isDark", login, StringComparison.Ordinal);
    Assert.DoesNotContain("malievTheme.toggle", login, StringComparison.Ordinal);
}
```

Implement `Read`, `Count`, `AssertOrder`, and repository-root lookup directly in the test file. Rewrite old `LegacyThemeContractTests` and `ShadcnStyleSystemContractTests` assertions so they verify the package/provider contract instead of `PaletteDark` and literal local colors.

- [ ] **Step 2: Run RED integration tests**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter "FullyQualifiedName~ShadcnPackageIntegrationContractTests|FullyQualifiedName~LegacyThemeContractTests|FullyQualifiedName~ShadcnStyleSystemContractTests"
```

Expected: failures for missing project reference, registration, providers, synchronized theme attribute, adapter link, and Login service usage.

- [ ] **Step 3: Add the package reference and registration**

Add to the client project references:

```xml
<ProjectReference Include="..\Maliev.ShadcnBlazor\Maliev.ShadcnBlazor.csproj" />
```

Replace the Mud registration in `Program.cs`:

```csharp
using Maliev.ShadcnBlazor;

builder.Services.AddMalievShadcn(options =>
    options.FontFamily = "'IBM Plex Sans Thai', sans-serif");
```

Add component/theming namespaces to `_Imports.razor`:

```razor
@using Maliev.ShadcnBlazor.Components
@using Maliev.ShadcnBlazor.Theming
```

- [ ] **Step 4: Replace both provider stacks**

Wrap the complete existing layout markup without changing its children. In `MainLayout.razor`, put the opening `ShadcnThemeProvider` immediately before `<a class="legacy-skip-link"` and the closing tag immediately after `</MudLayout>`. In `EmptyLayout.razor`, put the opening tag immediately before `<main class="legacy-empty-layout"` and the closing tag immediately after that `</main>`. Use these exact parameters on both opening tags: `IsDarkMode="@ThemeService.IsDarkMode"`, `Direction="ShadcnDirection.LeftToRight"`, and `Class="legacy-provider-root"`.

Inject `LegacyThemeService`, subscribe to `Changed`, initialize it after first render, rerender on change, and unsubscribe in both layouts. Delete both local `MudTheme` definitions and four direct provider components.

Add the exact shell geometry:

```css
.legacy-provider-root {
    min-height: 100dvh;
}
```

- [ ] **Step 5: Converge Login on `LegacyThemeService`**

Keep `IJSRuntime` for Google Identity. Replace `_isDarkMode`, `ReadThemeAsync`, and direct theme toggle interop with:

```razor
@inject LegacyThemeService ThemeService

<div class="legacy-login-page @(ThemeService.IsDarkMode ? "dark-theme" : string.Empty)">
```

```csharp
private string ThemeToggleLabel => ThemeService.IsDarkMode
    ? Text["Switch to light mode"].Value
    : Text["Switch to dark mode"].Value;

private Task ToggleThemeAsync() => ThemeService.ToggleAsync();
```

Subscribe/unsubscribe to `ThemeService.Changed` through the component's existing async-disposal path and initialize once without disturbing Google Identity initialization.

- [ ] **Step 6: Synchronize the bootstrap and exact stylesheet order**

Inside `apply(preference)` add:

```javascript
root.dataset.malievTheme = theme;
root.dataset.shadcnTheme = theme;
root.style.colorScheme = theme;
```

Order links as Mud, font, package base, client aliases, package adapter, product layout sheets, CSS isolation, then loading shell. Move the existing inline Mud typography variables into package options or client token aliases; remove the inline `body !important` style block.

- [ ] **Step 7: Build first and run focused plus complete Intranet suite**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~ShadcnPackageIntegrationContractTests|FullyQualifiedName~LegacyThemeContractTests|FullyQualifiedName~ShadcnStyleSystemContractTests|FullyQualifiedName~LegacyLoginExperienceContractTests|FullyQualifiedName~WasmShellAssetsContractTests"
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore
```

Expected: zero-warning build and all tests pass.

- [ ] **Step 8: Commit production provider integration**

```powershell
git add -- Legacy.Maliev.Intranet.Client/Legacy.Maliev.Intranet.Client.csproj Legacy.Maliev.Intranet.Client/Program.cs Legacy.Maliev.Intranet.Client/_Imports.razor Legacy.Maliev.Intranet.Client/Layout/MainLayout.razor Legacy.Maliev.Intranet.Client/Layout/EmptyLayout.razor Legacy.Maliev.Intranet.Client/Pages/Login.razor Legacy.Maliev.Intranet.Client/wwwroot/index.html Legacy.Maliev.Intranet.Client/wwwroot/css/app.css Legacy.Maliev.Intranet.Tests/ShadcnPackageIntegrationContractTests.cs Legacy.Maliev.Intranet.Tests/LegacyThemeContractTests.cs Legacy.Maliev.Intranet.Tests/ShadcnStyleSystemContractTests.cs
git commit -m "feat: integrate shadcn provider into Intranet"
```

---

### Task 3: Restyle actions, typography, and form controls

**Files:**
- Modify: `Maliev.ShadcnBlazor/wwwroot/css/shadcn-mudblazor.css`
- Modify: `Maliev.ShadcnBlazor.Tests/Contracts/MudAdapterContractTests.cs`
- Create: `Maliev.ShadcnBlazor.Showcase/Pages/MudInventory.razor`
- Modify: `Maliev.ShadcnBlazor.Showcase/wwwroot/css/showcase.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Components/Shared/PrimaryButton.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Components/Shared/SecondaryButton.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Shared/Components/ListToolbar.razor.css`

**Interfaces:**
- Consumes: provider scope and universal density/focus/disabled primitives from Task 1.
- Produces: complete appearance contract for `MudText`, `MudIcon`, `MudLink`, `MudButton`, `MudIconButton`, `MudForm`, `MudTextField`, `MudNumericField`, `MudSelect`, `MudSelectItem`, `MudDatePicker`, and `MudCheckBox`; deterministic fixture IDs `mud-actions`, `mud-typography`, and `mud-forms`.

- [ ] **Step 1: Add failing family selector/state tests**

Extend `MudAdapterContractTests`:

```csharp
[Theory]
[InlineData(".mud-button-root", "height: var(--shadcn-control-height)")]
[InlineData(".mud-button-filled", "background: var(--shadcn-primary)")]
[InlineData(".mud-button-outlined", "border: 1px solid var(--shadcn-border)")]
[InlineData(".mud-button-text", "background: transparent")]
[InlineData(".mud-icon-button-root", "width: var(--shadcn-control-height)")]
[InlineData(".mud-input-control", "min-height: var(--shadcn-control-height)")]
[InlineData(".mud-input-error", "var(--shadcn-destructive)")]
[InlineData(".mud-select-input", "var(--shadcn-foreground)")]
[InlineData(".mud-list-item-selected", "background: var(--shadcn-accent)")]
[InlineData(".mud-picker", "background: var(--shadcn-popover)")]
[InlineData(".mud-checkbox", "var(--shadcn-primary)")]
public void ActionsTypographyAndFormsExposeCanonicalContracts(string selector, string declaration)
{
    var css = ReadAdapter();
    Assert.Contains(selector, css, StringComparison.Ordinal);
    Assert.Contains(declaration, css, StringComparison.Ordinal);
}
```

Add assertions for `:hover`, `:active`, `:focus-visible`, disabled, read-only, invalid, checked, indeterminate, selected, open, and dark-theme selectors. Add a regression assertion that no coarse-pointer rule makes `.mud-checkbox .mud-button-root` full width.

- [ ] **Step 2: Run RED package tests**

```powershell
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --filter "FullyQualifiedName~MudAdapterContractTests"
```

Expected: failures for missing family selectors and states.

- [ ] **Step 3: Implement typography and action rules**

Add focused sections using scoped selectors. The button contract starts with:

```css
:where(.shadcn-scope, .shadcn-overlay-scope) .mud-button-root {
    min-width: 0;
    height: var(--shadcn-control-height);
    min-height: var(--shadcn-control-height);
    padding-inline: 0.625rem;
    gap: 0.375rem;
    border-radius: var(--shadcn-radius-md);
    font-size: 0.875rem;
    font-weight: 500;
    line-height: 1.25rem;
    letter-spacing: 0;
    text-transform: none;
    box-shadow: none;
    transition: color 100ms, background-color 100ms, border-color 100ms, box-shadow 100ms;
}

:where(.shadcn-scope, .shadcn-overlay-scope) .mud-button-filled {
    background: var(--shadcn-primary);
    color: var(--shadcn-primary-foreground);
}

:where(.shadcn-scope, .shadcn-overlay-scope) .mud-button-outlined {
    border: 1px solid var(--shadcn-border);
    background: var(--shadcn-background);
    color: var(--shadcn-foreground);
}
```

Map Mud `Primary`, `Secondary`, `Error`, `Success`, `Warning`, `Default`, and `Inherit` states to semantic package pairs without literal product colors. Give icon buttons exact square sizes. Map all used `MudText` roles to the existing package typography scale while preserving semantic `HtmlTag` output.

- [ ] **Step 4: Implement form and selection rules**

Style outlined, filled, and underline DOM variants; label, slot, helper, adornment, clear button, textarea, numeric, select trigger/items, picker/calendar, and checkbox. Use the exact invalid focus rule:

```css
:where(.shadcn-scope, .shadcn-overlay-scope) .mud-input-error:focus-within {
    border-color: var(--shadcn-destructive);
    box-shadow: 0 0 0 3px color-mix(in oklab, var(--shadcn-destructive) 20%, transparent);
}

[data-shadcn-theme="dark"] :where(.mud-input-error:focus-within) {
    box-shadow: 0 0 0 3px color-mix(in oklab, var(--shadcn-destructive) 40%, transparent);
}
```

Do not change bindings or component parameters. Disabled uses opacity `0.5` and cursor suppression; read-only remains focusable. Calendar day rules explicitly cover hover, today, selected, range, outside-month, and disabled states.

- [ ] **Step 5: Add deterministic showcase fixtures**

Create `/components/mud-inventory` with `data-testid="mud-inventory-fixture"`. Add named sections and fixed fixtures:

```razor
<section data-testid="mud-actions">
    <MudButton Variant="Variant.Filled">Primary</MudButton>
    <MudButton Variant="Variant.Outlined">Outline</MudButton>
    <MudButton Variant="Variant.Text">Ghost</MudButton>
    <MudButton Disabled>Disabled</MudButton>
    <MudIconButton Icon="@Icons.Material.Outlined.Settings" aria-label="Settings" />
</section>

<section data-testid="mud-forms">
    <MudTextField Label="Email" Value="ada@maliev.com" Variant="Variant.Outlined" />
    <MudTextField Label="Invalid" Error ErrorText="Required" Variant="Variant.Outlined" />
    <MudNumericField T="decimal" Label="Quantity" Value="12.5m" />
    <MudSelect T="string" Label="Material" Value="Steel"><MudSelectItem Value="@("Steel")">Steel</MudSelectItem></MudSelect>
    <MudDatePicker Label="Delivery date" Date="new DateTime(2026, 8, 10)" />
    <MudCheckBox T="bool" Value="true" Label="Approved" />
</section>
```

Add disabled, read-only, invalid, clearable, multiline, adornment, selected, indeterminate, and Thai-label fixtures. Showcase CSS may arrange fixtures but cannot restyle `.mud-*` appearance.

- [ ] **Step 6: Reduce wrapper CSS to application composition**

Keep `PrimaryButton` busy behavior and `SecondaryButton` convenience behavior. Remove their canonical color/radius/hover/focus rules. In `ListToolbar.razor.css`, retain grid and responsive layout but remove control height, border, background, font, and state appearance.

- [ ] **Step 7: Build and run package plus focused Intranet tests**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build --no-restore
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~MudBlazorComponentConformanceTests|FullyQualifiedName~ListToolbar|FullyQualifiedName~LoginFormContractTests|FullyQualifiedName~AccountingUiAccessibilityContractTests"
```

Expected: zero-warning build and all selected suites pass.

- [ ] **Step 8: Commit action and form styling**

```powershell
git add -- Maliev.ShadcnBlazor/wwwroot/css/shadcn-mudblazor.css Maliev.ShadcnBlazor.Tests/Contracts/MudAdapterContractTests.cs Maliev.ShadcnBlazor.Showcase/Pages/MudInventory.razor Maliev.ShadcnBlazor.Showcase/wwwroot/css/showcase.css Legacy.Maliev.Intranet.Client.Features.Orders/Components/Shared/PrimaryButton.razor.css Legacy.Maliev.Intranet.Client.Features.Orders/Components/Shared/SecondaryButton.razor.css Legacy.Maliev.Intranet.Client.Shared/Components/ListToolbar.razor.css
git commit -m "style: apply shadcn actions and form controls"
```

---

### Task 4: Restyle surfaces, navigation, and portal-rendered overlays

**Files:**
- Modify: `Maliev.ShadcnBlazor/wwwroot/css/shadcn-mudblazor.css`
- Modify: `Maliev.ShadcnBlazor.Tests/Contracts/MudAdapterContractTests.cs`
- Modify: `Maliev.ShadcnBlazor.Tests/Components/ShadcnThemeProviderTests.cs`
- Modify: `Maliev.ShadcnBlazor.Showcase/Pages/MudInventory.razor`

**Interfaces:**
- Consumes: production provider and form/action styling.
- Produces: complete contracts for `MudContainer`, `MudGrid`, `MudItem`, `MudStack`, `MudPaper`, `MudDivider`, `MudExpansionPanels`, `MudExpansionPanel`, `MudTabs`, `MudTabPanel`, `MudBreadcrumbs`, `MudList`, `MudListItem`, `MudChip`, `MudLayout`, `MudMainContent`, popover/dialog/snackbar providers, menus, selects, and pickers.

- [ ] **Step 1: Write failing surface, state, and portal tests**

Add data-driven CSS assertions for:

```csharp
var contracts = new Dictionary<string, string>
{
    [".mud-paper"] = "background: var(--shadcn-card)",
    [".mud-divider"] = "border-color: var(--shadcn-border)",
    [".mud-expand-panel"] = "border: 1px solid var(--shadcn-border)",
    [".mud-expand-panel-header"] = "min-height: var(--shadcn-control-height)",
    [".mud-tab.mud-tab-active"] = "background: var(--shadcn-background)",
    [".mud-list-item-selected"] = "color: var(--shadcn-accent-foreground)",
    [".mud-chip"] = "border-radius: var(--shadcn-radius-md)",
    [".mud-popover"] = "background: var(--shadcn-popover)",
    [".mud-dialog"] = "background: var(--shadcn-background)",
    [".mud-snackbar"] = "background: var(--shadcn-foreground)"
};
```

Assert hover/focus/disabled/selected/expanded/open selectors and overlay `z-index: 50`. Extend provider bUnit tests to prove dialog background and popover container carry `shadcn-overlay-scope`; render snackbar content and assert it inherits the current direction/theme context.

- [ ] **Step 2: Run RED tests**

```powershell
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --filter "FullyQualifiedName~MudAdapterContractTests|FullyQualifiedName~ShadcnThemeProviderTests"
```

Expected: failures for missing surface/overlay selectors and portal evidence.

- [ ] **Step 3: Implement layout and surface styling**

Use canonical card/popover pairs, one-pixel borders, derived radii, restrained shadows, and 14px chrome. Expansion headers cover hover, focus, disabled, expanded content, and indicator rotation. Tabs cover hover, focus, disabled, active, indicator, overflow, and panel spacing. Lists and chips cover selected/active and status variants.

Grid/container/stack rules must not replace Mud breakpoint calculations. Only remove visual drift and set tokenized gutters/surface behavior.

- [ ] **Step 4: Implement overlay styling and scope propagation**

Apply the shared overlay selector prefix:

```css
:where(.shadcn-scope, .shadcn-overlay-scope) :where(.mud-popover, .mud-menu, .mud-picker, .mud-dialog) {
    z-index: 50;
    border: 1px solid var(--shadcn-border);
    background: var(--shadcn-popover);
    color: var(--shadcn-popover-foreground);
    border-radius: var(--shadcn-radius-md);
    box-shadow: var(--shadcn-shadow-md);
}
```

Dialogs use the background/foreground pair, rounded large geometry, overlay opacity, focus trap, restore, 100ms fade/zoom. Anchored surfaces use side-aware 0.5rem slide and 100ms transitions. Snackbars include severity, action, close, multiline, desktop and mobile placement. Do not change Mud open/close or focus behavior.

- [ ] **Step 5: Extend the deterministic fixture**

Add fixed paper, grid, divider, expansion, tab, breadcrumb, list, selected item, chip, dialog trigger, popover/select, date picker, and snackbar fixtures under `data-testid="mud-surfaces-overlays"`. Add controls with stable test IDs that open each portal surface.

- [ ] **Step 6: Build and run affected suites**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build --no-restore
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~OperationsShellContractTests|FullyQualifiedName~LegacyNavigationContractTests|FullyQualifiedName~OrdersProcurementResponsiveContractTests"
```

Expected: zero-warning build and all tests pass.

- [ ] **Step 7: Commit surface and overlay styling**

```powershell
git add -- Maliev.ShadcnBlazor/wwwroot/css/shadcn-mudblazor.css Maliev.ShadcnBlazor.Tests/Contracts/MudAdapterContractTests.cs Maliev.ShadcnBlazor.Tests/Components/ShadcnThemeProviderTests.cs Maliev.ShadcnBlazor.Showcase/Pages/MudInventory.razor
git commit -m "style: apply shadcn surfaces and overlays"
```

---

### Task 5: Restyle tables, charts, alerts, progress, and skeletons

**Files:**
- Modify: `Maliev.ShadcnBlazor/wwwroot/css/shadcn-mudblazor.css`
- Modify: `Maliev.ShadcnBlazor.Tests/Contracts/MudAdapterContractTests.cs`
- Modify: `Maliev.ShadcnBlazor.Showcase/Pages/MudInventory.razor`
- Modify: `Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client/Components/Dashboard/DashboardPanel.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Components/Shared/ProgressiveSkeleton.razor.css`

**Interfaces:**
- Consumes: canonical semantic and chart tokens from package base CSS.
- Produces: complete contracts for `MudTable`, `MudSimpleTable`, `MudTh`, `MudTd`, `MudChart`, `MudAlert`, `MudProgressLinear`, `MudProgressCircular`, and `MudSkeleton`.

- [ ] **Step 1: Write failing data and feedback tests**

Add exact adapter assertions:

```csharp
[Theory]
[InlineData(".mud-table-root", "border: 1px solid var(--shadcn-border)")]
[InlineData(".mud-table-head", "color: var(--shadcn-muted-foreground)")]
[InlineData(".mud-table-row:hover", "background: var(--shadcn-muted)")]
[InlineData(".mud-table-row-selected", "background: var(--shadcn-accent)")]
[InlineData(".mud-chart", "--mud-palette-lines-default: var(--shadcn-border)")]
[InlineData(".mud-alert", "border-radius: var(--shadcn-radius-lg)")]
[InlineData(".mud-progress-linear", "background: var(--shadcn-secondary)")]
[InlineData(".mud-progress-circular", "color: var(--shadcn-primary)")]
[InlineData(".mud-skeleton", "background: var(--shadcn-muted)")]
public void DataAndFeedbackUseSemanticContracts(string selector, string declaration)
{
    var css = ReadAdapter();
    Assert.Contains(selector, css, StringComparison.Ordinal);
    Assert.Contains(declaration, css, StringComparison.Ordinal);
}
```

Assert responsive `data-label` behavior, dense rows, selected rows, focus-within, chart 1–5 consumption, alert severities, indeterminate animation, and reduced-motion overrides.

- [ ] **Step 2: Run RED tests**

```powershell
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --filter "FullyQualifiedName~MudAdapterContractTests"
```

Expected: failures for missing table/chart/feedback contracts.

- [ ] **Step 3: Implement tables and charts**

Style table container, head, cells, hover, selected, focus-within, dense, pagination, loading/empty, and small-screen `DataLabel` cards. Do not change `Items`, row templates, sorting, paging, or callbacks.

Bind Mud chart CSS variables and SVG selectors to `--shadcn-chart-1` through `--shadcn-chart-5`; style axes, grid, labels, legend, Bar, Donut, and Line fixtures. Remove fixed light dashboard chart/table colors and replace them with semantic package variables.

- [ ] **Step 4: Implement feedback primitives**

Alerts cover default/information/success/warning/destructive surfaces, icons, close action, live-region content, and outlined compatibility. Linear and circular progress cover determinate and indeterminate states. Skeletons cover text, rectangle, circle, wave, dark mode, and reduced motion.

- [ ] **Step 5: Extend deterministic fixtures**

Add fixed responsive/hover/selected tables, simple table, Bar/Donut/Line charts, each alert severity, linear/circular determinate and indeterminate progress, and all skeleton variants under `data-testid="mud-data-feedback"`.

- [ ] **Step 6: Build and run affected suites**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build --no-restore
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~FinanceChartsWasmMigrationContractTests|FullyQualifiedName~Dashboard|FullyQualifiedName~AccountingUiAccessibilityContractTests"
```

Expected: zero-warning build and all tests pass.

- [ ] **Step 7: Commit data and feedback styling**

```powershell
git add -- Maliev.ShadcnBlazor/wwwroot/css/shadcn-mudblazor.css Maliev.ShadcnBlazor.Tests/Contracts/MudAdapterContractTests.cs Maliev.ShadcnBlazor.Showcase/Pages/MudInventory.razor Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor.css Legacy.Maliev.Intranet.Client/Components/Dashboard/DashboardPanel.razor.css Legacy.Maliev.Intranet.Client.Features.Orders/Components/Shared/ProgressiveSkeleton.razor.css
git commit -m "style: apply shadcn data and feedback components"
```

---

### Task 6: Consolidate client CSS ownership and remove page-specific conflicts

**Files:**
- Create: `Legacy.Maliev.Intranet.Tests/ShadcnCssOwnershipContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Client/wwwroot/css/design-tokens.css`
- Modify: `Legacy.Maliev.Intranet.Client/wwwroot/css/app.css`
- Modify: `Legacy.Maliev.Intranet.Client/wwwroot/css/mudblazor-overrides.css`
- Modify: `Legacy.Maliev.Intranet.Client/wwwroot/css/shadcn.css`
- Modify: `Legacy.Maliev.Intranet.Client/wwwroot/css/operations-pages.css`
- Modify: `Legacy.Maliev.Intranet.Client/Components/Shell/LegacyGlobalSearch.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client/Components/Shell/LegacyLanguageSelector.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client/Components/Shell/LegacyNavigationRail.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client/Layout/LegacyTopBar.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Shared/Components/ListToolbar.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Customers/Pages/Customers.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderDetail.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrderCreate.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrders.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrderView.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/SupplierCreate.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/Suppliers.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/SupplierView.razor.css`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Create.razor.css`
- Modify: `Legacy.Maliev.Intranet.Tests/ShadcnStyleSystemContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/OperationsPageVisualSystemContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/TypographySystemContractTests.cs`

**Interfaces:**
- Consumes: complete package adapter from Tasks 3–5.
- Produces: one-owner cascade; product files may control geometry through named wrappers but cannot redefine canonical Mud appearance.

- [ ] **Step 1: Write failing CSS ownership tests**

Create tests with a small brace-aware rule scanner returning `CssRule(string Selector, string Declarations)`. Enforce:

```csharp
private static readonly string[] AppearanceProperties =
[
    "background", "background-color", "color", "border", "border-color", "border-width",
    "border-radius", "box-shadow", "outline", "fill", "stroke", "font-family", "font-size",
    "font-weight", "letter-spacing", "opacity", "text-transform"
];

private static readonly string[] ApprovedMudSelectorHooks =
[
    ".legacy-", ".mlv-", ".operations-", ".list-toolbar", ".dashboard-",
    ".customer-", ".order-", ".purchase-", ".supplier-", ".quotation-"
];

[Fact]
public void ProductSemanticLayerContainsNoGenericMudAppearance()
{
    var rules = ReadRules("Legacy.Maliev.Intranet.Client", "wwwroot", "css", "shadcn.css");
    Assert.DoesNotContain(rules, rule =>
        rule.Selector.Contains(".mud-", StringComparison.Ordinal) &&
        !rule.Selector.Contains(".legacy-", StringComparison.Ordinal) &&
        !rule.Selector.Contains(".mlv-", StringComparison.Ordinal));
}

[Fact]
public void OperationsAndScopedStylesUseMudSelectorsForGeometryOnly()
{
    foreach (var rule in ReadAllProductMudRules())
    {
        if (!rule.Selector.Contains(".mud-", StringComparison.Ordinal)) continue;
        Assert.Contains(ApprovedMudSelectorHooks,
            prefix => rule.Selector.Contains(prefix, StringComparison.Ordinal));
        Assert.DoesNotContain(AppearanceProperties,
            property => Regex.IsMatch(rule.Declarations, $@"(^|;)\s*{Regex.Escape(property)}\s*:", RegexOptions.IgnoreCase));
    }
}
```

The rule scanner must reject retained Mud selectors that lack one of those exact semantic prefixes.

Implement the brace-aware scanner in the same test file so nested media rules are inspected rather than skipped:

```csharp
private sealed record CssRule(string Selector, string Declarations);

private static IEnumerable<CssRule> ScanRules(string css, int start = 0, int? limit = null)
{
    var end = limit ?? css.Length;
    var cursor = start;
    while (cursor < end)
    {
        var open = css.IndexOf('{', cursor);
        if (open < 0 || open >= end) yield break;
        var selector = css[cursor..open].Trim();
        var depth = 1;
        var close = open + 1;
        while (close < end && depth > 0)
        {
            if (css[close] == '{') depth++;
            else if (css[close] == '}') depth--;
            close++;
        }
        if (depth != 0) throw new InvalidDataException($"Unbalanced CSS block: {selector}");
        if (selector.StartsWith('@'))
        {
            foreach (var nested in ScanRules(css, open + 1, close - 1)) yield return nested;
        }
        else
        {
            yield return new CssRule(selector, css[(open + 1)..(close - 1)]);
        }
        cursor = close;
    }
}
```

Strip `/* ... */` comments before scanning. `ReadAllProductMudRules()` must recursively enumerate `*.css` beneath these exact production roots: `Legacy.Maliev.Intranet.Client`, `Legacy.Maliev.Intranet.Client.Shared`, and each of the eight `Legacy.Maliev.Intranet.Client.Features.*` directories. Exclude `bin` and `obj`. Do not scan the package or showcase as product CSS.

Add assertions that `design-tokens.css` defines no `--shadcn-*:` values and that its MALIEV/legacy aliases resolve to package variables.

- [ ] **Step 2: Run RED ownership tests**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter "FullyQualifiedName~ShadcnCssOwnershipContractTests"
```

Expected: failures identifying duplicated token definitions, generic Mud appearance in `shadcn.css`, universal 44px desktop controls, and page-specific color/border/font overrides.

- [ ] **Step 3: Reduce global client files to their assigned responsibilities**

In `design-tokens.css`, replace Shadcn value definitions with aliases such as:

```css
:root {
    --maliev-surface-page: var(--shadcn-background);
    --maliev-surface-card: var(--shadcn-card);
    --maliev-text-primary: var(--shadcn-foreground);
    --maliev-text-secondary: var(--shadcn-muted-foreground);
    --maliev-action-primary: var(--shadcn-primary);
    --maliev-action-primary-text: var(--shadcn-primary-foreground);
    --maliev-focus-color: var(--shadcn-ring);
    --legacy-background: var(--shadcn-background);
    --legacy-surface: var(--shadcn-card);
    --legacy-primary: var(--shadcn-primary);
}
```

Delete generic component appearance from `shadcn.css`. Retain named shell/navigation/dashboard/login compositions. Reduce `mudblazor-overrides.css` to documented Mud DOM fixes and put a comment above each retained rule naming the Mud 9.7 behavior being corrected. Remove universal 44px desktop heights from `operations-pages.css`; retain grid, wrapping, overflow, and responsive geometry.

- [ ] **Step 4: Clean isolated page conflicts**

For every scoped file in the task file list, retain only layout properties such as `display`, `grid-*`, `flex-*`, `gap`, `margin`, `padding`, `width`, `max-width`, overflow, position, and responsive rearrangement. Remove component colors, borders, radii, shadows, font styling, state opacity, and control-height duplication. If a business composition truly requires a variation, add a named semantic wrapper class to its Razor file and use package variables without redefining the underlying Mud state contract.

Specifically verify the checkbox mobile rule no longer matches its nested `.mud-button-root` with `width: 100%`.

- [ ] **Step 5: Update old style contracts to ownership assertions**

Remove literal local token and last-loaded-override expectations from `ShadcnStyleSystemContractTests`, `OperationsPageVisualSystemContractTests`, and `TypographySystemContractTests`. Assert package assets, aliasing, allowed geometry, IBM Plex self-hosting, and absence of duplicate appearance instead.

- [ ] **Step 6: Build and run focused plus complete Intranet suite**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~ShadcnCssOwnershipContractTests|FullyQualifiedName~ShadcnStyleSystemContractTests|FullyQualifiedName~OperationsPageVisualSystemContractTests|FullyQualifiedName~TypographySystemContractTests|FullyQualifiedName~MudBlazorComponentConformanceTests"
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore
```

Expected: zero-warning build and all Intranet tests pass.

- [ ] **Step 7: Commit CSS ownership consolidation**

Stage only the files listed in this task and inspect `git diff --cached --name-only`:

```powershell
git add -- Legacy.Maliev.Intranet.Tests/ShadcnCssOwnershipContractTests.cs Legacy.Maliev.Intranet.Client/wwwroot/css/design-tokens.css Legacy.Maliev.Intranet.Client/wwwroot/css/app.css Legacy.Maliev.Intranet.Client/wwwroot/css/mudblazor-overrides.css Legacy.Maliev.Intranet.Client/wwwroot/css/shadcn.css Legacy.Maliev.Intranet.Client/wwwroot/css/operations-pages.css Legacy.Maliev.Intranet.Client/Components/Shell/LegacyGlobalSearch.razor.css Legacy.Maliev.Intranet.Client/Components/Shell/LegacyLanguageSelector.razor.css Legacy.Maliev.Intranet.Client/Components/Shell/LegacyNavigationRail.razor.css Legacy.Maliev.Intranet.Client/Layout/LegacyTopBar.razor.css Legacy.Maliev.Intranet.Client.Shared/Components/ListToolbar.razor.css Legacy.Maliev.Intranet.Client.Features.Customers/Pages/Customers.razor.css Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderDetail.razor.css Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrderCreate.razor.css Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrders.razor.css Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrderView.razor.css Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/SupplierCreate.razor.css Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/Suppliers.razor.css Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/SupplierView.razor.css Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Create.razor.css Legacy.Maliev.Intranet.Tests/ShadcnStyleSystemContractTests.cs Legacy.Maliev.Intranet.Tests/OperationsPageVisualSystemContractTests.cs Legacy.Maliev.Intranet.Tests/TypographySystemContractTests.cs
git diff --cached --name-only
git commit -m "refactor: consolidate Intranet shadcn style ownership"
```

---

### Task 7: Add exhaustive browser interaction, responsive, and accessibility evidence

**Files:**
- Create: `Maliev.ShadcnBlazor.BrowserTests/MudInventoryBrowserTests.cs`
- Modify: `Maliev.ShadcnBlazor.Showcase/Pages/MudInventory.razor`
- Modify: `Maliev.ShadcnBlazor.Showcase/wwwroot/css/showcase.css`
- Modify: `Maliev.ShadcnBlazor.Tests/Showcase/ShowcaseStateTests.cs`
- Modify: `Maliev.ShadcnBlazor.BrowserTests/FoundationSmokeTests.cs`

**Interfaces:**
- Consumes: complete 41-type fixture and consolidated adapter.
- Produces: automated computed-style, interaction, mobile, dark, RTL, reduced-motion, and console evidence; actual Intranet browser checklist.

- [ ] **Step 1: Write failing fixture-completeness tests**

In `ShowcaseStateTests`, read `MudInventory.razor` and assert one `data-mud-type` fixture for each `MudAdapterContractTests.ProductionTypes` entry. Assert unique IDs and exact section IDs. Link the inventory source file into the test project if required, just as `ShowcaseState.cs` is linked.

Expected contract:

```csharp
var fixtures = Regex.Matches(source, "data-mud-type=\\\"(?<type>Mud[A-Za-z]+)\\\"")
    .Select(match => match.Groups["type"].Value)
    .ToArray();
Assert.Equal(MudAdapterContractTests.ProductionTypes.Order(), fixtures.Distinct().Order());
```

- [ ] **Step 2: Write failing browser tests**

Create `MudInventoryBrowserTests` using the existing bounded server/browser fixtures. Include separate tests for desktop states, mobile/coarse pointer, dark mode, RTL propagation, reduced motion, and portals. The desktop test begins:

```csharp
[Fact]
public async Task InventoryUsesVegaGeometryAndHealthyInteractions()
{
    var errors = new List<string>();
    await using var context = await playwright.Browser.NewContextAsync(new()
    {
        ViewportSize = new() { Width = 1440, Height = 1000 },
        DeviceScaleFactor = 1,
        ReducedMotion = ReducedMotion.NoPreference
    });
    var page = await context.NewPageAsync();
    page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(new Uri(server.BaseUri, "/components/mud-inventory").ToString());
    await page.GetByTestId("mud-inventory-fixture").WaitForAsync();

    Assert.Equal("36px", await page.GetByTestId("button-default")
        .EvaluateAsync<string>("element => getComputedStyle(element).height"));
    Assert.Equal("14px", await page.GetByTestId("button-default")
        .EvaluateAsync<string>("element => getComputedStyle(element).fontSize"));
    Assert.Empty(errors);
}
```

Portal tests click stable triggers, wait for dialog/popover/select/picker/snackbar, and assert nontransparent background, semantic color, radius, focus containment/restoration, and `[data-shadcn-theme]`/direction behavior.

- [ ] **Step 3: Run RED showcase and browser tests**

```powershell
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --filter "FullyQualifiedName~ShowcaseStateTests"
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --filter "FullyQualifiedName~MudInventoryBrowserTests"
```

Expected: fixture completeness or browser-state assertions fail until all IDs and states are present.

- [ ] **Step 4: Complete the 41-type deterministic fixture**

Add `data-mud-type="MudTypeName"` once for every inventory type. Provider types are represented by observable provider-owned outputs: theme root, dialog, popover, snackbar, and RTL context. Fix dates, chart data, table rows, labels, and animation states. Add Thai strings long enough to exercise label and button geometry. Do not fetch network data.

- [ ] **Step 5: Complete interaction and media tests**

Assert:

- 36px default and 32px small desktop controls;
- at least 44px hit areas in a context with `HasTouch = true` and mobile viewport;
- hover background/border changes;
- Tab focus ring and Escape/focus restoration;
- disabled callbacks do not change fixture counters;
- invalid destructive rings;
- selected checkbox/select/tab/row states;
- expansion open/closed and indicator transform;
- progress/skeleton reduced-motion durations;
- dark semantic surfaces;
- RTL root and overlay direction;
- table/card reflow without horizontal page overflow at 390px;
- chart series consume five semantic colors;
- no captured page or console errors.

Save fresh full-page desktop and 390x844 mobile screenshots to unique temp paths and assert nonzero file length. Do not commit screenshots generated from temp paths.

- [ ] **Step 6: Run build, package, and full browser suite**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build --no-restore
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --no-build --no-restore
```

Expected: zero-warning build; package and browser suites pass; no showcase/headless-browser processes remain after disposal.

- [ ] **Step 7: Exercise actual Intranet flows in a real browser**

Start the sibling AppHost without changing it:

```powershell
dotnet run --project B:\maliev-legacy\Legacy.Maliev.AppHost\Legacy.Maliev.AppHost\Legacy.Maliev.AppHost.csproj --launch-profile http
```

Use the dynamic Intranet endpoint from Aspire. Inspect anonymous Login and an authenticated dashboard/list/form/detail flow in light/dark, English/Thai, desktop/tablet/mobile. Verify theme persistence across reload, navigation, validation, select/date overlays, dialog/snackbar, table/chart, loading/empty/error states, network CSS status, console, and responsive overflow. Record exact URLs, viewport sizes, screenshots, and any unavailable credential/environment blocker in the task report. Automated showcase evidence remains mandatory even if authenticated local credentials are unavailable.

- [ ] **Step 8: Commit browser evidence harness**

```powershell
git add -- Maliev.ShadcnBlazor.BrowserTests/MudInventoryBrowserTests.cs Maliev.ShadcnBlazor.Showcase/Pages/MudInventory.razor Maliev.ShadcnBlazor.Showcase/wwwroot/css/showcase.css Maliev.ShadcnBlazor.Tests/Showcase/ShowcaseStateTests.cs Maliev.ShadcnBlazor.BrowserTests/FoundationSmokeTests.cs
git commit -m "test: verify Intranet Mud shadcn states in browser"
```

---

### Task 8: Run the completion audit and freeze delivery evidence

**Files:**
- Create: `docs/intranet-shadcn-mudblazor-completion-ledger.md`
- Modify: `Maliev.ShadcnBlazor.Tests/Contracts/MudAdapterContractTests.cs`
- Modify: `Maliev.ShadcnBlazor/README.md`
- Modify: `docs/superpowers/specs/2026-08-10-intranet-shadcn-mudblazor-integration-design.md` only if implementation evidence exposes a factual mismatch

**Interfaces:**
- Consumes: every prior commit and test suite.
- Produces: requirement-by-requirement evidence for all 41 types, all behavior states, package boundary, and production integration.

- [ ] **Step 1: Create the failing completion ledger gate**

Add an Intranet contract test or extend `MudAdapterContractTests` to parse `docs/intranet-shadcn-mudblazor-completion-ledger.md`. Require exactly 41 unique rows and nonempty columns for:

```text
Mud type | Package selector evidence | State evidence | Showcase fixture | Browser evidence | Intranet usage evidence | Deviations
```

Require `Deviations` to be `None` or a linked approved exception. Run it before creating the ledger and observe failure for the missing file.

- [ ] **Step 2: Populate the exact evidence ledger and consumer documentation**

Create one row for each frozen production type. Use actual test method names and actual selectors/fixtures; do not write generic “covered” claims. Update README consumer setup with registration, provider, both CSS links, font override, and the supported MudBlazor 9.7 boundary.

- [ ] **Step 3: Verify repository and pinned-source hygiene**

```powershell
git status --short
git diff --check
pwsh .\scripts\verify-shadcn-reference.ps1
```

Expected: only Task 8 files are modified before its commit; diff check passes; verifier confirms all 61 Base files and Vega style at the pinned full commit.

- [ ] **Step 4: Run the final build-first automated gate**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build --no-restore
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --no-build --no-restore
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore
```

Expected: zero warnings/errors and every test in all three suites passes. Record fresh pass counts rather than copying historical counts.

- [ ] **Step 5: Pack and inspect the reusable package boundary**

Use a unique output directory under `.artifacts`, pack from fresh Release source, hash the nupkg, and inspect archive entries:

```powershell
$packageOutput = Join-Path (Resolve-Path .artifacts) ("shadcn-intranet-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $packageOutput | Out-Null
dotnet pack .\Maliev.ShadcnBlazor\Maliev.ShadcnBlazor.csproj -c Release --no-restore -o $packageOutput
$package = Get-ChildItem -LiteralPath $packageOutput -Filter *.nupkg -File | Select-Object -Single
Get-FileHash -Algorithm SHA256 -LiteralPath $package.FullName
tar -tf $package.FullName
```

Require one package DLL, README/license assets, one reference manifest, one `shadcn-base.css`, one `shadcn-mudblazor.css`, and zero `Legacy.Maliev.Intranet.*`, test, showcase, browser, credential, or temporary content paths. Delete only the verified unique package-output directory after recording evidence.

- [ ] **Step 6: Confirm browser/server cleanup and inspect final screenshots**

Check exact command lines for residual showcase, AppHost, dotnet test, Chromium, Playwright, or headless browser processes. Stop only processes launched by this task. Visually inspect the fresh desktop/mobile screenshots for clipping, unstyled native fallbacks, broken Thai text, missing backgrounds, inconsistent radii, and overflow.

- [ ] **Step 7: Commit the completion ledger**

```powershell
git add -- docs/intranet-shadcn-mudblazor-completion-ledger.md Maliev.ShadcnBlazor.Tests/Contracts/MudAdapterContractTests.cs Maliev.ShadcnBlazor/README.md
git diff --cached --check
git commit -m "docs: certify Intranet shadcn Mud migration"
```

If the design spec required a factual evidence correction, stage that exact file too and explain the correction in the commit body.

- [ ] **Step 8: Perform the final completion audit**

```powershell
git status --short --branch
git diff --check
git log -8 --oneline
```

For each specification outcome, point to authoritative current evidence: source file, focused test, suite result, browser state, package entry, or ledger row. Any missing, indirect, or contradictory evidence keeps the goal active. Mark the goal complete only when all 41 types, all applicable states, production flows, and delivery boundaries are proven with fresh evidence.
