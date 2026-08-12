# Intranet Shadcn MudBlazor Integration Design

## Status and decision

This design was approved in chat on 2026-08-10. It is the production-integration companion to `2026-08-10-shadcn-blazor-component-library-design.md` and does not replace that package contract.

`Legacy.Maliev.Intranet.Client` will consume the reusable `Maliev.ShadcnBlazor` Razor Class Library (RCL). MudBlazor remains the behavior and rendering foundation for existing production controls; the RCL becomes the canonical owner of their Base UI, Vega geometry, Neutral palette appearance. Production-specific styles retain ownership only of application shell, page layout, and named business compositions.

This approach is selected over a final override stylesheet because the current client has several competing generic `.mud-*` style owners. It is selected over page-by-page component replacement because replacing MudBlazor behavior would create unnecessary risk in typed binding, validation, focus management, tables, pickers, and overlays.

## Observable outcome

The integration is complete only when all of the following are true:

1. `Legacy.Maliev.Intranet.Client` references and registers `Maliev.ShadcnBlazor` through its supported public API.
2. The authenticated and anonymous layouts use one package provider contract instead of duplicated Mud provider stacks and handwritten themes.
3. `LegacyThemeService` remains the sole runtime authority for persisted light/dark state, including the login page, and the pre-WASM bootstrap still prevents a theme flash.
4. Every one of the 41 MudBlazor component types currently rendered in production has an explicit reusable styling contract for every applicable visual and interaction state.
5. All current bindings, callbacks, validation, accessibility semantics, loading behavior, navigation, authentication/session behavior, lazy-loaded feature assemblies, and overlay behavior remain intact.
6. Desktop controls use the pinned Vega density: 36 CSS pixels for default controls and 32 CSS pixels for small controls. Coarse-pointer and mobile layouts provide at least 44 by 44 CSS pixel interactive targets.
7. Light and dark modes, Thai and English content, desktop/tablet/mobile layouts, keyboard interaction, and portal-rendered surfaces pass automated and real-browser validation.
8. The old client CSS no longer contains competing generic MudBlazor appearance rules. Each visual concern has one documented owner.

The separate Razor compatibility host is excluded because it does not render MudBlazor or consume the WASM provider. Its adoption requires a separate design.

## Authoritative visual reference

The production migration uses the same reference frozen by the package contract:

- Shadcn repository commit: `6261bd89f72d794aea491482cc2acfd8dc3d63e2`
- Base registry: `apps/v4/registry/bases/base/ui`
- Style source: `apps/v4/registry/styles/style-vega.css`
- Primitive family: Base UI
- Geometry: Vega
- Semantic palette: Neutral
- Base radius: `0.625rem` and its upstream derived scale
- Default control height: 36 CSS pixels
- Small control height: 32 CSS pixels
- Standard icon size: 16 CSS pixels
- Default component chrome: 14 CSS pixel text with upstream weight and line-height rules

Production may bind the semantic font token to the self-hosted IBM Plex Sans Thai family. It may not change component geometry, weights, focus rings, borders, radii, shadows, state opacity, or motion merely to preserve an existing local style.

## Runtime integration architecture

### Dependency registration

`Legacy.Maliev.Intranet.Client` adds a project reference to `Maliev.ShadcnBlazor`. `Program.cs` replaces direct `AddMudServices()` registration with `AddMalievShadcn(...)`; both calls must not coexist. The package registration remains responsible for MudBlazor services and the overlay-scope integration required by package components.

The eight lazy-loaded feature assemblies remain unchanged. Their pages render beneath the client layout provider and therefore inherit the same theme, direction, tokens, and overlay services without adding feature-level package registration.

### Provider ownership

`MainLayout` and `EmptyLayout` replace their separate `MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider`, and `MudSnackbarProvider` stacks with `ShadcnThemeProvider`.

The provider owns:

- the matching Mud theme;
- `data-shadcn-theme` and the `.shadcn-scope` marker;
- explicit document direction;
- Mud RTL, popover, dialog, and snackbar providers;
- overlay theme/scope propagation;
- the package theme context cascaded to descendants.

The provider wrapper must have `min-height: 100dvh` so it cannot collapse the existing full-height application or login layouts.

Both supported cultures, `en-TH` and `th-TH`, are LTR. Culture selection must not infer RTL. Direction remains an explicit provider input so future RTL support does not become coupled to language persistence.

### Theme state flow

`LegacyThemeService` remains the only interactive theme-state service. It initializes from the existing `malievTheme` JavaScript contract, exposes the active mode, persists toggles, and notifies the layout so the provider rerenders.

The blocking head bootstrap continues to read local storage and cookie fallback before Blazor starts. It sets `data-maliev-theme`, `data-shadcn-theme`, and `color-scheme` consistently. A stored dark preference must render a dark first frame without waiting for hydration.

`Login.razor` stops calling the theme JavaScript functions directly. It uses `LegacyThemeService` so the anonymous provider, persisted state, toggle label, icon, and DOM attributes change in one transaction.

Unknown theme or direction values fail with an explicit diagnostic and do not silently choose an unrelated appearance.

## CSS ownership and cascade

### Reusable package files

`Maliev.ShadcnBlazor/wwwroot/css/shadcn-base.css` owns:

- canonical light/dark semantic variables;
- radius, shadow, typography, spacing, control-height, chart, sidebar, and motion tokens;
- package scope normalization;
- reduced-motion, coarse-pointer, and forced-colors primitives.

A new `Maliev.ShadcnBlazor/wwwroot/css/shadcn-mudblazor.css` owns all reusable MudBlazor appearance:

- dimensions, padding, gaps, typography, borders, radii, shadows, color, and opacity;
- variants and sizes;
- hover, active, pressed, focus-visible, focus-within, disabled, read-only, required, invalid, selected, checked, indeterminate, expanded, open, and loading states;
- light/dark and responsive appearance;
- overlay-safe selectors for portal-rendered surfaces;
- the complete production inventory listed below.

The adapter stylesheet may depend on MudBlazor 9.7 DOM and state classes. Those dependencies are package contracts protected by tests. Product CSS must not duplicate them.

### Client files after consolidation

- `design-tokens.css` retains MALIEV and legacy aliases that reference package semantic variables. It no longer defines a competing `--shadcn-*` token system.
- `app.css` owns the document, login, and application-shell layout.
- `module-pages.css` owns `.mlv-*` module layout primitives.
- `operations-pages.css` owns operations-page grids, wrapping, overflow, and responsive composition. It does not force universal desktop control heights or appearance.
- `utilities.css` remains utility-only.
- `mudblazor-overrides.css` retains only documented Mud DOM or behavior compatibility fixes that cannot be expressed in the reusable adapter. It does not own visual design.
- `shadcn.css` retains only named product shell and business-composition rules. It contains no generic `.mud-*` component appearance.
- `.razor.css` files retain component-local geometry and responsive layout. Any deep Mud selector requires a named local hook and may not redefine the reusable component contract.
- `loading-shell.css` remains limited to pre-Blazor loading and fatal-error surfaces.

### Required stylesheet order

The client loads styles in this order:

1. MudBlazor CSS
2. IBM Plex Sans Thai
3. package `shadcn-base.css`
4. client MALIEV/legacy token aliases
5. package `shadcn-mudblazor.css`
6. application, module, operations, and utility layout CSS
7. generated CSS-isolation bundle
8. loading-shell CSS

There is no final last-loaded generic Mud override sheet. Intentional product variations use a semantic wrapper class or package variant rather than winning through specificity or `!important`.

## Production component inventory and state contract

The static production inventory contains 41 rendered MudBlazor types across 1,647 source render sites. Source counts are not runtime instance counts because loops, templates, and conditionals expand dynamically.

### Providers and shell

| Mud type | Required contract |
|---|---|
| `MudThemeProvider` | Replaced by the package provider; exact light/dark semantic mapping and live updates |
| `MudPopoverProvider` | Replaced by the package provider; menu, select, picker, and popover scope survives portal rendering |
| `MudDialogProvider` | Replaced by the package provider; overlay, content, focus, sizing, motion, and dark mode |
| `MudSnackbarProvider` | Replaced by the package provider; viewport placement, severity, action, close, multiline, mobile, and RTL-safe geometry |
| `MudLayout` | Full-height shell, semantic background, foreground, and responsive navigation geometry |
| `MudMainContent` | Shell spacing and overflow remain product-owned without overriding component appearance |

### Layout, surfaces, and navigation

| Mud type | Required states or variants |
|---|---|
| `MudContainer` | Width constraints, gutters, mobile padding, light/dark surfaces |
| `MudGrid`, `MudItem` | Existing breakpoints and responsive ordering; no adapter-induced negative-margin regression |
| `MudStack` | Row/column, spacing, wrapping, mobile composition |
| `MudPaper` | Default, outlined, elevation compatibility, cards/surfaces, dark mode |
| `MudDivider` | Horizontal/vertical color and thickness |
| `MudExpansionPanels`, `MudExpansionPanel` | Group surface, default/hover/focus/disabled, expanded/collapsed, indicator rotation, multi-expansion |
| `MudTabs`, `MudTabPanel` | Default/hover/focus/disabled/active, indicator, keyboard focus, responsive overflow, panel spacing |
| `MudBreadcrumbs` | Link hover/focus, current page, separator, responsive wrapping |
| `MudList`, `MudListItem` | Default/hover/focus/disabled/selected/active, dense compatibility, nested content |
| `MudChip` | Filled/outline/status variants, hover/focus/disabled/selected, icon and small size |

### Typography and actions

| Mud type | Required states or variants |
|---|---|
| `MudText` | Existing semantic heading tags and all used typography roles mapped to the package scale |
| `MudIcon` | Current-color sizing and alignment without business-icon replacement |
| `MudLink` | Default, hover, active, focus-visible, visited policy, disabled composition, external-link semantics |
| `MudButton` | Filled/outline/text mapped to Shadcn variants; all used colors, icon positions, full width, link/button semantics, disabled and loading |
| `MudIconButton` | Default/hover/active/focus/disabled/selected; sizes; 44px mobile target without making checkbox internals full width |

### Forms and inputs

| Mud type | Required states or variants |
|---|---|
| `MudForm` | Existing validation and submission behavior; structural layout only |
| `MudTextField` | Outlined/filled/text compatibility; default/hover/focus/disabled/read-only/required/invalid; multiline, clearable, adornments, all used input types |
| `MudNumericField` | Text-field states plus min/max, typed values, unit adornments, read-only, spin-button/mobile behavior |
| `MudSelect`, `MudSelectItem` | Trigger states; open/closed; item hover/focus/disabled/selected; clearable, required, typed values, groups, portal scope |
| `MudDatePicker` | Field states; calendar open/closed; day hover/focus/selected/today/range/disabled; navigation and portal scope |
| `MudCheckBox` | Unchecked/checked/indeterminate, hover/focus/invalid/disabled, label association, coarse-pointer target |

The migration does not change `Value`, `ValueChanged`, `For`, `Required`, `Immediate`, `Clearable`, `ReadOnly`, `Disabled`, `Min`, `Max`, `Lines`, `MaxLength`, or validation-message behavior. Existing business forms continue to use their current models and handlers.

### Data display and feedback

| Mud type | Required states or variants |
|---|---|
| `MudTable`, `MudSimpleTable`, `MudTh`, `MudTd` | Header/body/footer, hover, selected, focus-within, dense, responsive `DataLabel`, pagination, loading/empty composition, dark mode |
| `MudChart` | Semantic chart-1 through chart-5 series, axes, grid, labels, legend, tooltip where supported, Bar/Donut/Line, dark mode, responsive sizing |
| `MudAlert` | Default, information, success, warning, destructive; icon/title/content/close; live-region semantics |
| `MudProgressLinear` | Determinate/indeterminate, track/bar, semantic color, reduced motion |
| `MudProgressCircular` | Determinate/indeterminate, current-color sizing, loading-button use, reduced motion |
| `MudSkeleton` | Text/rectangle/circle, wave and pulse compatibility, dark mode, reduced motion |

## Application wrappers and compositions

Existing wrappers remain only when they express application behavior:

- `PrimaryButton` keeps busy state, busy label, and callback suppression but delegates all button and spinner appearance to the package.
- `SecondaryButton` keeps its convenience API but delegates appearance.
- `ProgressiveSkeleton` keeps its application-specific table/detail/form/list arrangements but delegates primitive appearance.
- `ListToolbar` keeps search, sort, page-size, debounce, and busy behavior but contains no generic component styling.
- Navigation, language, global-search, and quick-action shell components retain business behavior and named layout hooks.

No wrapper may fork the canonical hover, focus, invalid, disabled, selected, open, or dark-mode contract.

## Accessibility and interaction rules

- `:focus-visible` uses the package ring token at the pinned three-pixel treatment. Mouse click does not leave an unnecessary keyboard ring.
- Invalid inputs use destructive border and ring treatment in outlined, filled, and underline variants.
- Disabled controls suppress interaction, use the upstream opacity, and retain sufficient forced-colors boundaries.
- Read-only fields remain visually distinct from disabled fields and preserve applicable focus behavior.
- Existing `aria-live`, `aria-busy`, `aria-expanded`, `aria-selected`, roles, labels, descriptions, and validation associations remain present.
- Reduced motion removes nonessential transforms and makes transitions effectively immediate.
- Forced-colors mode uses system colors for borders, focus, selection, and disabled states.
- Mobile rules target actual interactive roots. Nested checkbox and icon-button internals may not inherit full-width button rules.
- Overlay transitions and placement use the pinned upstream side/open/closed states without changing focus trapping or restoration.

## Error handling and compatibility

- Theme initialization fails closed to a stable light appearance if JS interop is unavailable, without blocking app rendering.
- A provider or overlay integration failure must produce a focused test failure and development diagnostic rather than silently rendering an unscoped portal.
- No API, DTO, JSON, authentication, authorization, cookie, storage, message, database, currency, date, or timezone contract changes are permitted.
- User-facing copy remains resource-backed Thai and English. Money remains THB with existing locale formatting. Display date/time remains Asia/Bangkok; stored timestamps remain UTC.
- Existing MudBlazor 9.7 references in shared and lazy feature projects remain version-aligned with the package.

## Testing and acceptance evidence

### Static and component contracts

Tests must prove:

- the client references the package and uses only the supported registration call;
- both layouts use `ShadcnThemeProvider` and no duplicate provider stacks or local themes remain;
- Login uses `LegacyThemeService` rather than direct theme JS calls;
- the required stylesheet order is exact;
- generic Mud appearance selectors have one package owner;
- all 41 inventory types map to explicit adapter contract sections;
- desktop and coarse-pointer density tokens are present;
- dark, reduced-motion, and forced-colors contracts exist;
- package assets are included once and resolve through `_content/Maliev.ShadcnBlazor/...`.

Provider/component tests must render and interact with theme changes, direction, dialog, popover, select, date picker, snackbar, validation, disabled controls, busy buttons, selected items, expanded panels, tabs, tables, progress, skeleton, and charts.

### Browser fixtures

The package showcase will add a deterministic `MudBlazor production inventory` fixture that renders all 41 used types and all applicable hard-to-reach states without production data. It imports the same package adapter assets consumed by the client. The actual Intranet browser lane separately exercises authenticated and anonymous production pages, proving the client stylesheet order, provider integration, shell compositions, and feature-assembly inheritance. No test-only route is added to the production client.

Browser checks cover:

- Chromium desktop, tablet, and mobile widths;
- light and dark themes;
- English and representative Thai content;
- pointer hover/press and keyboard focus;
- disabled, read-only, invalid, selected, expanded, open, loading, and empty states;
- dialogs, popovers, menus, selects, date pickers, tooltips, and snackbars across portal boundaries;
- chart, table, and responsive overflow behavior;
- no horizontal page overflow at supported mobile widths;
- no page errors, uncaught exceptions, failed CSS requests, or unexpected console errors.

Computed geometry and relevant styles are compared to the package showcase and pinned Shadcn reference. Screenshots supplement, but do not replace, state and computed-style assertions.

### Required validation order

Every coherent implementation slice runs:

1. affected Release build with zero warnings and zero errors;
2. focused tests for the changed behavior;
3. the complete affected project suite;
4. relevant static, API, accessibility, and asset checks;
5. browser interaction and visual checks for the affected states.

The completion gate additionally runs the complete Intranet suite, package suite, browser suite, live pinned-reference verifier, package boundary inspection, and a clean-worktree/diff audit.

## Implementation and commit boundaries

The implementation is divided into independently buildable, validated commits:

1. **Production integration contracts:** package reference, registration, provider/theme flow, login service convergence, stylesheet links, and RED/GREEN contract tests.
2. **Reusable Mud adapter foundation:** `shadcn-mudblazor.css`, inventory ledger, token/density/state primitives, package tests, and showcase fixture updates.
3. **Actions, typography, and forms:** buttons, icons, links, text, text/number/select/date inputs, checkbox, validation, disabled/read-only, and wrappers.
4. **Surfaces, navigation, and overlays:** paper, grids, lists, breadcrumbs, chips, expansion panels, tabs, dialog, popover, picker, and snackbar states.
5. **Data and feedback:** tables, charts, alerts, progress, skeletons, responsive and dark-mode behavior.
6. **Client CSS consolidation:** remove duplicate generic appearance rules, retain named product layouts, fix page-specific conflicts, and lock ownership with tests.
7. **Accessibility and responsive gate:** coarse-pointer targets, reduced motion, forced colors, Thai content, desktop/tablet/mobile browser fixtures.
8. **Final completion audit:** requirement ledger, full builds/suites/browser evidence, pinned-source verification, package inspection, and clean repository state.

Each commit contains only its slice. A slice is not committed until its required build and tests pass. Existing unrelated changes are preserved. Pushing requires explicit user authorization.

## Deliberate exclusions

- The non-Mud Razor compatibility host is not migrated.
- Business workflows, copy, service contracts, and persistence are not redesigned.
- Existing application markup is not mechanically replaced when a reusable adapter can deliver the approved appearance without behavior risk.
- React, Tailwind, Base UI JavaScript, and the reference host are not shipped to production.
- Upstream Shadcn changes after the pinned commit are not adopted implicitly.
- Public NuGet publication and deployment are separate authorization boundaries.
