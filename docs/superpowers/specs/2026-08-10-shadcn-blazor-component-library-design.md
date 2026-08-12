# Shadcn Blazor Component Library Design

## Status and decision

This design was approved in chat on 2026-08-10.

The approved direction is a reusable MudBlazor-backed Razor Class Library (RCL) that implements the complete Shadcn Base component catalog supplied for this work. `Legacy.Maliev.Intranet` is the first integration consumer and regression surface, not the owner of the component APIs.

The library uses MudBlazor where it already provides mature behavior and adds composed or custom Blazor implementations where no equivalent exists. A CSS-only skin is insufficient because several requested components own behavior or semantics that MudBlazor does not provide. Replacing MudBlazor wholesale is also rejected because it would rebuild accessible forms, focus management, validation, tables, calendars, and overlays without improving the requested visual result.

## Observable outcome

The work is complete when all of the following are true:

1. A reusable `Maliev.ShadcnBlazor` RCL builds as a standalone package and has no dependency on Intranet services, routes, DTOs, localization resources, authentication, or business state.
2. The RCL exposes all 64 requested Shadcn components through stable Blazor APIs.
3. Every component is individually compared with the pinned Shadcn Base reference for structure, dimensions, spacing, typography, colors, borders, radii, shadows, motion, responsive behavior, pointer behavior, keyboard behavior, focus, disabled state, invalid state, and all component-specific states.
4. Existing MudBlazor controls can be placed under an opt-in Shadcn scope and receive the same tokens and component treatment without changing their current event, binding, validation, or provider behavior.
5. All 41 MudBlazor component types currently rendered by `Legacy.Maliev.Intranet` are migrated to the reusable package and reviewed in their actual application flows.
6. A committed showcase renders deterministic fixtures for every component and state.
7. Automated component, accessibility, interaction, computed-style, and visual-regression checks pass in the supported browser matrix.
8. The Intranet solution builds with zero warnings, its focused tests pass, its complete affected suite passes, and authenticated desktop, tablet, and mobile browser journeys pass in Thai and English, light and dark modes.

## Authoritative reference

The initial reference snapshot is pinned to:

- Repository: `https://github.com/shadcn-ui/ui`
- Commit: `6261bd89f72d794aea491482cc2acfd8dc3d63e2`
- Base registry: `apps/v4/registry/bases/base/ui`
- Vega style source: `apps/v4/registry/styles/style-vega.css`
- Visual style: Base UI, Vega geometry, Neutral semantic palette
- Default radius: `0.625rem`, with the upstream derived radius scale
- Component chrome: upstream Base/Vega sizes, typography, borders, focus rings, disabled treatment, overlay placement, and animation durations

The repository commit, registry item blob SHA, upstream path, and retrieved timestamp must be recorded in a checked-in reference manifest. Source updates are explicit work: changing the pinned commit requires regenerating the reference fixtures, rerunning the full conformance suite, reviewing the mismatch ledger, and committing the manifest change with its resulting component changes.

The reference contains 61 same-named Base registry files for the requested catalog. Data Table, Date Picker, and Typography are official compositions rather than same-named files and are specified from their official documentation and constituent registry sources. Toast uses the requested `toast.tsx` contract. Sonner is excluded from this objective and cannot silently replace the requested API.

Shadcn does not require a single application font. The package default is the upstream sans-serif stack. Consumers override one semantic font token. Intranet binds that token to its self-hosted IBM Plex Sans Thai family so Thai and English remain legible without changing component geometry or weights.

## Package and project structure

The initial implementation adds these projects to the current repository:

```text
Maliev.ShadcnBlazor/
  Components/
  Adapters/MudBlazor/
  Icons/
  Interop/
  Theming/
  wwwroot/css/
  wwwroot/js/
  Maliev.ShadcnBlazor.csproj

Maliev.ShadcnBlazor.Tests/
  Components/
  Accessibility/
  Contracts/

Maliev.ShadcnBlazor.Showcase/
  Pages/Components/
  Fixtures/

Maliev.ShadcnBlazor.BrowserTests/
  Reference/
  Interaction/
  Visual/
```

The RCL targets `net10.0`, enables nullable reference types and implicit usings, and treats warnings as errors. It depends on MudBlazor 9.7.x through central package management and produces static web assets using normal RCL conventions. Consumers need one service-registration call, one provider root, and one stylesheet import.

The public namespace is `Maliev.ShadcnBlazor`. Public component names use a `Shadcn` prefix where a name would collide with framework or MudBlazor types, for example `ShadcnButton`, `ShadcnSelect<TValue>`, and `ShadcnDialog`. Child composition types use the same family prefix, such as `ShadcnCardHeader` and `ShadcnDialogTitle`.

No project in the package may reference `Legacy.Maliev.Intranet.*`. Intranet references the package, never the reverse.

## Styling and theme architecture

### Scope and cascade

`ShadcnThemeProvider` renders a root carrying `.shadcn-scope` and the active light/dark data attribute. All package styles are scoped beneath this root except documented root-level custom properties. This prevents the package from restyling unrelated MudBlazor surfaces in another application.

The provider supplies the matching MudTheme, direction, reduced-motion state, and overlay providers. Provider-driven overlays must receive the active scope class even when they render through a portal. A provider test must prove that dialog, menu, popover, select, tooltip, and toast content retain the theme outside the triggering component subtree.

### Tokens

The package owns semantic Shadcn variables for:

- background and foreground;
- card and card foreground;
- popover and popover foreground;
- primary and primary foreground;
- secondary and secondary foreground;
- muted and muted foreground;
- accent and accent foreground;
- destructive and destructive foreground;
- border, input, and ring;
- chart 1 through chart 5;
- sidebar surface, foreground, primary, accent, border, and ring;
- radius scale, shadow scale, control heights, spacing, typography, and motion.

Components consume semantic variables only. Status variants may add semantic success, warning, and information pairs for MALIEV business states, but these cannot change the canonical Shadcn variants.

### Icons

Library-owned default icons use the Lucide shapes used by the pinned Shadcn sources. The implementation uses a pinned Blazor-compatible Lucide package or embedded official SVG paths with its license retained. Public components expose icon `RenderFragment` slots so consumers are not forced to use Lucide for business-specific imagery. Intranet migrates component-chrome icons to their Lucide counterparts; vendor or business artwork remains unchanged.

### Motion and accessibility media

Animations use the pinned upstream durations, easing, transform origins, and open/closed state transitions. `prefers-reduced-motion` removes nonessential transforms and reduces durations to effectively immediate state changes. Forced-colors mode preserves visible boundaries and system focus colors. Coarse-pointer layouts retain at least 44 by 44 CSS pixel interactive targets without changing desktop density.

## Public API rules

Every visual component follows these conventions where applicable:

- `ChildContent`, `Class`, `Style`, and unmatched attributes are supported.
- Generic value components expose `Value`, `ValueChanged`, and `ValueExpression` so `@bind-Value` and `EditForm` validation work normally.
- Boolean controls expose typed checked-state binding and an explicit indeterminate state where the reference supports it.
- Open-state components expose controlled and uncontrolled modes through `Open`, `OpenChanged`, and documented defaults.
- Actions use `EventCallback` and never swallow or reorder consumer callbacks.
- IDs are stable across server prerendering and hydration. Labels, descriptions, errors, and controls link through deterministic ARIA attributes.
- Disabled and read-only are separate states. Disabled controls do not emit actions; read-only controls remain focusable when the native/reference behavior requires it.
- All overlay components require an accessible title. A visually hidden title is supported, but an absent title is rejected by tests.
- Component-specific child types enforce valid composition through typed render fragments or runtime development assertions.
- MudBlazor implementation details are not exposed in the public API unless the component is explicitly named as a Mud adapter.
- An `AdditionalMudParameters` escape hatch is not provided. Missing capabilities are added deliberately to the stable API rather than leaking MudBlazor internals.

## Complete component catalog

The implementation classification is part of the contract:

- **Adapter** means MudBlazor supplies the principal behavior while the RCL owns the public API, composition, tokens, and visual states.
- **Composition** means the component combines package primitives and one or more MudBlazor behaviors.
- **Custom** means the package owns behavior because no adequate MudBlazor equivalent exists.

| Component | Classification | Required implementation contract |
|---|---|---|
| Accordion | Adapter | Expansion behavior, single/multiple modes, keyboard navigation, indicator rotation |
| Alert | Adapter | Default and destructive structure with icon, title, and description slots |
| Alert Dialog | Composition | Modal confirmation semantics, required title, cancel/action focus policy |
| Aspect Ratio | Custom | Ratio-preserving wrapper with consumer content and responsive sizing |
| Attachment | Custom | File/media metadata, preview, actions, upload/progress/error states, grouped layout |
| Avatar | Adapter | Image, fallback, delayed fallback, sizes, grouping support |
| Badge | Adapter | Default, secondary, destructive, outline, and link-capable variants |
| Breadcrumb | Composition | List semantics, separators, ellipsis, current page, responsive collapse |
| Bubble | Custom | Incoming/outgoing alignment, variants, grouping, reactions, collapse |
| Button | Adapter | All upstream variants and text/icon sizes, loading composition, link styling helper |
| Button Group | Composition | Attached geometry, orientation, separators, nested input/button support |
| Calendar | Adapter | Standalone selection surface, navigation, range/multiple states, disabled dates |
| Card | Composition | Header, title, description, action, content, and footer slots |
| Carousel | Adapter | Previous/next, orientation, keyboard behavior, index state, overflow treatment |
| Chart | Composition | Engine-neutral container, semantic series config, tooltip, legend, and accessibility; MudChart adapter for Intranet |
| Checkbox | Adapter | Checked, unchecked, indeterminate, disabled, invalid, label association |
| Collapsible | Adapter | Controlled/uncontrolled open state and animated content |
| Combobox | Composition | Search input, filtered command list, single/multiple selection, empty state |
| Command | Custom | Search, grouped results, separators, empty/loading states, keyboard selection |
| Context Menu | Composition | Pointer and keyboard invocation, groups, checkbox/radio items, submenus |
| Data Table | Composition | Generic rows, columns, sorting, filtering, selection, visibility, pagination, loading/empty/error states |
| Date Picker | Composition | Field plus popover plus calendar, manual input policy, validation, timezone-safe date-only binding |
| Dialog | Adapter | Modal/non-modal modes, required title, description, close control, focus trap/restore |
| Direction | Custom | LTR/RTL cascading context and DOM direction propagation |
| Drawer | Adapter | Edge placement, drag/close behavior where supported, focus and modal semantics |
| Dropdown Menu | Composition | Groups, labels, separators, checkbox/radio items, submenus, shortcuts |
| Empty | Custom | Header, media, title, description, content, and action composition |
| Field | Custom | Field group/set/legend, label, description, error list, horizontal/responsive layouts |
| Hover Card | Composition | Delayed pointer/focus opening without modal behavior, collision handling |
| Input | Adapter | Text input types, sizes, placeholder, disabled, read-only, invalid, file treatment |
| Input Group | Custom | Prefix/suffix add-ons, text, icon, button, input, and textarea composition |
| Input OTP | Custom | Paste, backspace, arrow navigation, grouping, separators, invalid and disabled states |
| Item | Custom | Media, content, title, description, actions, header/footer, variants and sizes |
| Kbd | Custom | Keyboard token and grouped shortcut presentation |
| Label | Custom | Native label semantics and disabled/peer-state styling |
| Marker | Custom | Inline semantic marker/highlight variants used by conversation content |
| Menubar | Composition | Top-level keyboard traversal, menus, groups, submenus, checkbox/radio items |
| Message | Custom | Avatar, alignment, header, bubble content, metadata, footer/actions |
| Message Scroller | Custom | Anchoring, auto-follow, prepend preservation, visibility state, scroll commands |
| Native Select | Custom | Native select semantics with Shadcn wrapper, icon, invalid and disabled states |
| Navigation Menu | Composition | Triggers, viewport, links, indicators, keyboard traversal, responsive behavior |
| Pagination | Composition | Previous/next, numbered pages, ellipsis, active page, link/button semantics |
| Popover | Adapter | Controlled open state, focus behavior, placement, collision, side/align animation |
| Progress | Adapter | Determinate/indeterminate states, accessible value contract, sizes |
| Questionnaire | Custom | Multi-step single/multiple/freeform/skipped answers, progress, validation, navigation |
| Radio Group | Adapter | Typed value binding, arrow-key movement, disabled and invalid states |
| Resizable | Custom | Horizontal/vertical panels, handles, keyboard resizing, min/max constraints |
| Scroll Area | Custom | Styled viewport and scrollbars, both orientations, native scrolling semantics |
| Select | Adapter | Groups, labels, separators, scroll buttons, selected indicator, typeahead |
| Separator | Adapter | Horizontal/vertical decorative or semantic separator |
| Sheet | Composition | Dialog-derived edge panel with side variants and matching transitions |
| Sidebar | Composition | Provider, inset/floating variants, rail, groups, menu items, mobile sheet, persistence |
| Skeleton | Adapter | Text, circular, and rectangular fixtures with upstream pulse treatment |
| Slider | Adapter | Single/range values, steps, orientation, keyboard, disabled state |
| Spinner | Adapter | Inline accessible loading indicator with current-color sizing |
| Switch | Adapter | Checked binding, keyboard/pointer operation, disabled and invalid states |
| Table | Composition | Header, body, footer, row, head, cell, caption, selection/hover states, responsive overflow |
| Tabs | Adapter | List, trigger, content, active state, arrow/Home/End keyboard behavior |
| Textarea | Adapter | Sizes, vertical resize, placeholder, disabled, read-only, invalid states |
| Toast | Composition | Provider, viewport, queue, action, close, variants, timeout, pause, swipe dismissal |
| Toggle | Adapter | Pressed binding, variants, sizes, disabled state |
| Toggle Group | Adapter | Single/multiple modes, variants, sizes, orientation, roving focus |
| Tooltip | Adapter | Provider delay, pointer/focus triggers, placement, collision, noninteractive semantics |
| Typography | Custom | Typeset-compatible prose, heading, paragraph, list, quote, code, lead, large, small, muted primitives |

## Intranet migration inventory

The static audit found 41 rendered MudBlazor types across 1,647 production source render sites, including four `RenderTreeBuilder` calls. Runtime instance counts vary because loops and conditional templates expand source sites.

| Family | Existing MudBlazor types | Source sites | Migration concern |
|---|---|---:|---|
| Providers and layout | ThemeProvider, PopoverProvider, DialogProvider, SnackbarProvider, Layout, MainContent | 10 | One package provider must preserve portal scope and theme state |
| Responsive surfaces | Item, Paper, Grid, Container, Stack, Divider, ExpansionPanels, ExpansionPanel, Tabs, TabPanel, Breadcrumbs, List, ListItem, Chip | 499 | Grid breakpoints and current responsive records must not regress |
| Typography, icons, links | Text, Icon, Link | 271 | Preserve semantic heading tags, external-link safety, and Thai typography |
| Forms and inputs | Form, TextField, NumericField, Select, SelectItem, DatePicker, CheckBox | 353 | Preserve typed binding, `For`, required, clearable, disabled, read-only, adornments, validation |
| Actions | Button, IconButton | 158 | Preserve href/click behavior, busy wrappers, disabled rules, icon placement |
| Data display | Table, SimpleTable, Th, Td, Chart | 221 | Preserve hover, responsive `DataLabel`, chart data, legends, and loading states |
| Feedback | Alert, ProgressLinear, ProgressCircular, Skeleton | 135 | Preserve severity, live regions, indeterminate state, and skeleton layouts |

Existing direct wrappers (`PrimaryButton`, `SecondaryButton`, `ProgressiveSkeleton`), shared composites (`ListToolbar`), and shell composites are migrated onto package primitives or retained only as application-specific compositions. They cannot duplicate canonical component styling after migration.

The separate Razor compatibility host is outside this component-library migration because it does not render MudBlazor. Adopting the tokens there requires a separate objective and is not evidence for completion here.

## State and behavior matrix

Every component dossier lists applicable states. A state is not considered covered merely because a CSS selector exists.

The universal matrix is:

- default, hover, active, pressed, focus-visible, and focus-within;
- disabled, read-only, required, invalid, valid, and loading;
- checked, unchecked, indeterminate, selected, and deselected;
- open, closed, expanded, collapsed, modal, and non-modal;
- empty, populated, loading, success, degraded, and error data states;
- dragging, resizing, scrolling, auto-follow, prepend preservation, and overflow;
- mouse, keyboard, touch/coarse pointer, and programmatic focus;
- light, dark, RTL, reduced motion, and forced colors;
- desktop, tablet, and mobile viewports.

Component-specific keyboard tables are derived from the pinned Base primitive and relevant WAI-ARIA pattern. Tests verify focus movement, activation keys, Escape behavior, focus trapping/restoration, typeahead, selection, and prevention of events from disabled controls.

## Reference showcase and comparison system

The Blazor showcase has one route per component and query-addressable fixtures for every documented variant and state. Fixtures are deterministic: fixed text, dates, data, dimensions, and animation state. Thai fixtures are included for controls whose size can change with localized content.

The browser test project starts:

1. a reference React host generated from the pinned Shadcn Base/Vega/Neutral sources; and
2. the Blazor showcase using the package.

Both hosts use the same browser engine, viewport, device scale factor, color mode, reduced-motion setting, font files, fixture content, and animation clock. The reference host is test infrastructure only and is not shipped to consuming applications.

For every fixture the test stores:

- reference URL and upstream source identity;
- Blazor URL and component identity;
- semantic/ARIA snapshot;
- relevant computed styles and box geometry;
- reference screenshot;
- Blazor screenshot;
- mismatch result and any approved exception.

Computed properties that define the contract must match exactly after normalizing browser serialization: display, position, size, min/max size, padding, margin, gap, font family/size/weight/line-height/letter spacing, color, background, border, radius, outline, box shadow, opacity, overflow, transform, transition, and animation.

Screenshot comparison must have no unexplained differences. Text anti-aliasing may be masked only when both sides have identical computed typography and box geometry. Any other mask or threshold exception must be component-specific, justified in the mismatch ledger, and reviewed in the same commit.

The supported browser matrix is Chromium, Firefox, and WebKit on desktop widths, plus Chromium tablet and mobile emulation. Interaction tests run in all desktop engines; screenshot baselines run in a pinned Chromium build for deterministic rendering.

## Automated test layers

### Library tests

- Render every component and public variant.
- Verify parameters, two-way binding, callbacks, disabled suppression, controlled/uncontrolled state, and validation integration.
- Verify required ARIA roles, names, relationships, live regions, and focus semantics.
- Verify provider and portal behavior.
- Verify JavaScript modules dispose listeners and observers.
- Verify the public API surface against an approved API snapshot.

### Browser interaction tests

- Keyboard navigation and focus restoration.
- Pointer hover, press, drag, resize, scroll, and swipe behavior.
- Open/close and collision placement for overlays.
- Input, number, select, calendar, OTP, questionnaire, and form validation flows.
- Table sorting/filtering/selection/pagination.
- Message scroller auto-follow and prepend preservation.
- Theme, RTL, reduced-motion, forced-colors, and responsive state changes.

### Intranet integration tests

- Preserve the existing source and architecture contract tests while replacing weak styling-string checks with package and rendered-contract checks.
- Build the solution before testing.
- Run focused theme, component conformance, shell, typography, and operations tests.
- Run the complete `Legacy.Maliev.Intranet.Tests` suite.
- Start the sibling AppHost and exercise authenticated Intranet journeys at desktop, tablet, and mobile widths.
- Cover Thai and English, light and dark modes, navigation, list toolbars, forms, numeric inputs, selects, date picking, validation, dialogs, charts, loading, empty, success, and error states.

The required validation order for each coherent implementation slice is:

1. build affected projects with zero warnings and errors;
2. run focused component tests;
3. run the relevant affected suite;
4. run static/API/accessibility checks;
5. run browser interaction and visual checks.

## Error handling and degradation

- JavaScript-dependent components render a stable initial state and fail closed if interop is unavailable.
- A failed observer, resize, or scrolling call cannot block rendering or lose application data.
- Overlay and focus failures are surfaced in development and tests, not silently ignored.
- Unknown enum values throw clear development-time exceptions rather than falling back to arbitrary styling.
- Missing required composition, such as a dialog title, fails an automated test and emits a development diagnostic.
- Browser-only features document their prerendered state and become interactive after hydration without layout shift.

## Localization, money, dates, and time

The package contains no business copy. Accessible default labels that must exist are localizable through package resource interfaces. Consumer content remains caller supplied.

Intranet continues to use resource-backed Thai and English strings. Money remains THB with the existing locale-aware formatting. Display dates and times use Asia/Bangkok. Stored timestamps and `occurredAtUtc`-style values remain UTC. Date Picker binds date-only values without applying an unintended timezone conversion.

## Compatibility and rollout

Existing MudBlazor pages first opt into package scope without changing their data or event contracts. Components are then migrated family by family to public package primitives where structural composition is required. During migration, direct Mud controls and package components must coexist inside the same provider and token scope.

No API, DTO, JSON, authentication, authorization, cookie, storage, message, or database contract changes are part of this work. Any discovered application behavior change requires a revised goal before implementation.

The old Intranet `design-tokens.css`, `mudblazor-overrides.css`, and `shadcn.css` are removed or reduced to application layout only after all equivalent rules are owned by the package and the browser suite proves parity. Duplicate theme definitions in `MainLayout` and `EmptyLayout` are replaced with the package theme. Unrelated local commits are preserved; history is not rewritten.

## Commit and delivery boundaries

Each slice is independently buildable, tested, browser-reviewed, and committed:

1. Reference manifest, package skeleton, token system, provider, showcase shell, and test harness.
2. Direction, Aspect Ratio, Typography, Label, Field, Item, Kbd, Separator, and Empty.
3. Button, Button Group, Toggle, Toggle Group, Checkbox, Radio Group, Switch, and Slider.
4. Input, Textarea, Input Group, Input OTP, Native Select, Select, Combobox, Calendar, and Date Picker.
5. Alert, Progress, Spinner, Skeleton, Toast, Avatar, Badge, Card, and Carousel.
6. Accordion, Collapsible, Resizable, Scroll Area, Breadcrumb, Pagination, Tabs, Navigation Menu, and Sidebar.
7. Dialog, Alert Dialog, Drawer, Sheet, Popover, Hover Card, Tooltip, Dropdown Menu, Context Menu, Menubar, and Command.
8. Table, Data Table, and Chart, including the Intranet MudChart adapter.
9. Attachment, Bubble, Marker, Message, Message Scroller, and Questionnaire.
10. Intranet provider/theme adoption and migration of all 41 current MudBlazor types.
11. Full catalog/state audit, cross-browser evidence, documentation, package metadata, license inventory, and release candidate.

Commits contain only their slice. No commit is created until that slice's required validation passes. Nothing is pushed unless explicitly requested.

## Completion ledger

The repository maintains a machine-readable component ledger with one entry per requested component. An entry cannot be marked complete until it identifies:

- public component and child APIs;
- upstream file or official composition reference;
- implemented variants and sizes;
- applicable state-matrix rows;
- component-test evidence;
- accessibility evidence;
- interaction-test evidence;
- computed-style evidence;
- visual evidence;
- Intranet usage evidence where applicable;
- approved deviations, with an empty list being the expected result.

Project-level completion requires all 64 entries complete, all 41 Intranet MudBlazor types accounted for, all required commands passing with fresh output, and no unresolved mismatch or contract regression.

## Deliberate exclusions

- The package does not ship React, Base UI, Radix, Tailwind, or the reference host to production.
- The package does not implement Intranet business workflows, data access, authentication, or localization content.
- The compatibility Razor host is not migrated in this objective because it does not use MudBlazor.
- Additional Shadcn blocks or components introduced upstream after the pinned revision require a separate manifest update.
- Publishing to a public NuGet feed, pushing commits, opening GitHub issues, or creating a GitHub project requires separate explicit authorization.
