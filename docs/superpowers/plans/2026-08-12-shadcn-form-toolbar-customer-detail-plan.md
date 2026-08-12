# Shadcn Form, Toolbar, and Customer Detail Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a consistent Shadcn-quality form system, shared list toolbar, and responsive customer-detail experience without changing functional contracts.

**Architecture:** Package CSS owns MudBlazor form appearance; the shared RCL owns list-toolbar composition; the Customers feature owns record-page hierarchy. Browser tests exercise the production Client with only BFF boundaries controlled.

**Tech Stack:** .NET 10, Blazor WebAssembly, MudBlazor 9.7, scoped CSS, xUnit, Microsoft Playwright.

## Global Constraints

- Preserve routes, query values, callbacks, permissions, DTOs, BFF endpoints, localization, and save behavior.
- Desktop controls remain 36px; touch and narrow controls remain at least 44px.
- Use Shadcn semantic tokens only; no page-level generic `.mud-*` appearance overrides.
- Keep English and Thai behavior and information order identical.
- Preserve the existing unrelated `.impeccable` artifact.

---

### Task 1: Freeze the shared form contract

**Files:**
- Modify: `Maliev.ShadcnBlazor.Tests/ShadcnMudBlazorCssContractTests.cs`
- Modify: `Maliev.ShadcnBlazor.BrowserTests/MudInventoryBrowserTests.cs`
- Modify: `Maliev.ShadcnBlazor/wwwroot/css/shadcn-mudblazor.css`

**Interfaces:**
- Consumes: existing `.shadcn-scope` and `--shadcn-*` tokens.
- Produces: one stable form-control visual contract for all scoped Mud fields.

- [ ] Add package assertions for static external labels, one visible 1px border, compact padding, adornment alignment, helper spacing, invalid borders, and focus ring.
- [ ] Run focused package/browser tests and verify RED against the current date/input geometry.
- [ ] Update only package-owned selectors for text, numeric, select, date, textarea, readonly, disabled, and invalid states.
- [ ] Rerun focused tests and verify GREEN in light/dark and desktop/mobile fixtures.
- [ ] Commit the independently validated shared adapter slice.

### Task 2: Polish the shared list command strip

**Files:**
- Modify: `Legacy.Maliev.Intranet.Client.Shared/Components/ListToolbar.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Shared/Components/ListToolbar.razor.css`
- Modify: `Legacy.Maliev.Intranet.Tests/OrdersProcurementResponsiveContractTests.cs`
- Modify: `Maliev.ShadcnBlazor.BrowserTests/CustomerResponsiveBrowserTests.cs`

**Interfaces:**
- Consumes: existing `ListToolbar<TSort>` parameters and callbacks unchanged.
- Produces: shared `.list-toolbar` command-strip layout used by all ten consumers.

- [ ] Add assertions for search dominance, action grouping, surface treatment, alignment, 36/44px sizing, and no overflow at 1280/768/390/320.
- [ ] Run focused tests and verify RED against the current boxed layout.
- [ ] Refine shared markup classes and scoped layout without changing controller behavior.
- [ ] Verify representative long English/Thai labels and Clear/Refresh states.
- [ ] Commit the independently validated toolbar slice.

### Task 3: Recompose the customer detail page

**Files:**
- Modify: `Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.razor`
- Modify: `Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.razor.css`
- Modify: `Legacy.Maliev.Intranet.Tests/CustomersViewWasmMigrationContractTests.cs`
- Modify: `Maliev.ShadcnBlazor.BrowserTests/CustomerDetailBrowserTests.cs`

**Interfaces:**
- Consumes: existing `CustomerDetail`, `CustomerUpdateRequest`, permissions, and HTTP flows.
- Produces: semantic page sections and responsive edit grid with unchanged behavior.

- [ ] Add source and production-browser assertions for header hierarchy, section layout, definition rows, edit grid, action separation, date field geometry, and mobile order.
- [ ] Run focused tests and verify RED against the current loose MudGrid/card composition.
- [ ] Recompose markup with page-owned semantic classes and implement scoped responsive CSS.
- [ ] Verify readonly/edit/invalid/submitting states, keyboard flow, Thai text, and zero overflow.
- [ ] Commit the independently validated customer-detail slice.

### Task 4: Integration and shipping gate

**Files:**
- Review all files changed by Tasks 1-3; no new production scope.

**Interfaces:**
- Consumes: the three validated slices.
- Produces: release evidence and a clean integrated branch.

- [ ] Build `Legacy.Maliev.Intranet.slnx` in Release and require 0 warnings/0 errors.
- [ ] Run focused form, toolbar, customer-detail, ownership, and localization tests.
- [ ] Run the full Legacy, package, and browser suites and record exact counts.
- [ ] Run Impeccable detector exactly once over changed Razor/CSS targets.
- [ ] Inspect Customers plus representative Orders, Invoices, Materials, and Suppliers pages at desktop/tablet/mobile.
- [ ] Audit console errors, process cleanup, `git diff --check`, and staged-file scope; commit any final integration-only correction.
