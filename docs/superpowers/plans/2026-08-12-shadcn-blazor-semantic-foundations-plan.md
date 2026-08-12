# Shadcn Blazor Semantic Foundations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement and certify Direction, Aspect Ratio, Typography/Typeset, Label, Field, Item, Kbd, Separator, and Empty as reusable Blazor components in `Maliev.ShadcnBlazor`.

**Architecture:** Add small semantic Razor primitives with a shared attribute/class/style base, keeping public APIs independent from MudBlazor while consuming the package token system. Components render the same slot/data-attribute structure as the pinned Shadcn Base/Vega reference; bUnit verifies API and accessibility contracts, and deterministic showcase/browser fixtures verify geometry, computed styles, RTL, dark mode, and responsive behavior.

**Tech Stack:** .NET 10, Blazor RCL, MudBlazor 9.7.0 provider foundation, bUnit 2.9.0, xUnit 2.9.3, Playwright 1.61.0, pinned Shadcn commit `6261bd89f72d794aea491482cc2acfd8dc3d63e2`.

## Global Constraints

- Preserve the approved design in `docs/superpowers/specs/2026-08-10-shadcn-blazor-component-library-design.md` and roadmap in `docs/superpowers/plans/2026-08-10-shadcn-blazor-implementation-roadmap.md`.
- Use the manifest's pinned Base/Vega/Neutral source identities; current official documentation remains a drift check, not an implicit upgrade.
- Public components support `ChildContent`, `Class`, `Style`, and unmatched attributes where their rendered element allows them.
- Explicit `Class` and `Style` parameters merge with unmatched `class` and `style`; duplicate framework-owned attributes cannot override required roles, slots, orientation, or ARIA semantics.
- Component presentation uses only `--shadcn-*` semantic variables and package-owned component classes.
- No package project may reference `Legacy.Maliev.Intranet.*`.
- Unknown enum values throw `ArgumentOutOfRangeException`; invalid aspect ratios throw `ArgumentOutOfRangeException` before rendering.
- Build before tests; each task ends with focused tests and a scoped commit. Do not stage the pre-existing `.impeccable/critique` file and do not push.

---

## File and responsibility map

- `Maliev.ShadcnBlazor/Components/Primitives/ShadcnComponentBase.cs` — common class/style/unmatched-attribute merge and required-attribute protection.
- `Maliev.ShadcnBlazor/Components/Direction/ShadcnDirectionProvider.razor` — nested DOM/cascading direction override.
- `Maliev.ShadcnBlazor/Components/Layout/ShadcnAspectRatio.razor` — ratio-preserving wrapper.
- `Maliev.ShadcnBlazor/Components/Typography/*.razor` — semantic typeset container and explicit typography primitives.
- `Maliev.ShadcnBlazor/Components/Forms/*.razor` — label, field set/group/field/content/title/description/error/separator.
- `Maliev.ShadcnBlazor/Components/Content/*.razor` — item family, keyboard tokens, separator, and empty family.
- `Maliev.ShadcnBlazor/wwwroot/css/shadcn-semantic-foundations.css` — package-scoped styling for this family.
- `Maliev.ShadcnBlazor.Showcase/Pages/SemanticFoundations.razor` — deterministic English/Thai fixtures and theme/direction query state.
- `Maliev.ShadcnBlazor.Tests/Components/SemanticFoundations/*Tests.cs` — render, parameter, attribute, and accessibility contracts.
- `Maliev.ShadcnBlazor.Tests/Contracts/PublicApiSnapshotTests.cs` and `Contracts/public-api.txt` — approved public types and parameters.
- `Maliev.ShadcnBlazor.BrowserTests/SemanticFoundationsBrowserTests.cs` — responsive, direction, computed-style, keyboard-focus, and screenshot checks.
- `docs/shadcn-component-ledger.json` — evidence status for the nine components.

---

### Task 1: Add the reusable DOM attribute contract

**Files:**
- Create: `Maliev.ShadcnBlazor/Components/Primitives/ShadcnComponentBase.cs`
- Create: `Maliev.ShadcnBlazor.Tests/Components/SemanticFoundations/ShadcnComponentBaseTests.cs`

**Interfaces:**
- Produces: abstract `ShadcnComponentBase : ComponentBase` with `[Parameter] string? Class`, `[Parameter] string? Style`, `[Parameter(CaptureUnmatchedValues = true)] IReadOnlyDictionary<string, object>? AdditionalAttributes`, `MergeClass(string)`, `MergeStyle(string?)`, and `AttributesExcept(params string[])`.
- Required behavior: case-insensitive handling of `class` and `style`, stable caller attribute order, no duplicate class tokens introduced by the helper, and exclusion of protected framework attributes.

- [ ] Write bUnit/helper tests for merging explicit and unmatched class/style, filtering protected attributes case-insensitively, retaining `id`, `aria-*`, and `data-*`, and tolerating null dictionaries.
- [ ] Run `dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --filter FullyQualifiedName~ShadcnComponentBaseTests`; expect failure because the base type is absent.
- [ ] Implement the base with pure deterministic helpers and XML documentation on every public/protected member required by warnings-as-errors.
- [ ] Build `Maliev.ShadcnBlazor.csproj`, rerun the focused tests, then commit `feat: add shadcn component attribute contract`.

### Task 2: Implement Direction and Aspect Ratio

**Files:**
- Create: `Maliev.ShadcnBlazor/Components/Direction/ShadcnDirectionProvider.razor`
- Create: `Maliev.ShadcnBlazor/Components/Layout/ShadcnAspectRatio.razor`
- Create: `Maliev.ShadcnBlazor.Tests/Components/SemanticFoundations/DirectionAndAspectRatioTests.cs`
- Modify: `Maliev.ShadcnBlazor/_Imports.razor`

**Interfaces:**
- `ShadcnDirectionProvider`: `Direction` defaults from nearest `ShadcnContext`; emits `dir="ltr|rtl"`, `data-slot="direction"`, and cascades a context preserving `IsDarkMode` with the overridden direction.
- `ShadcnAspectRatio`: required `double Ratio`; emits `data-slot="aspect-ratio"`; style includes `position: relative; width: 100%; aspect-ratio: <invariant ratio>`; content wrapper fills the box; rejects non-finite or non-positive ratios.

- [ ] Write failing tests for default/nested direction, context propagation, unmatched attributes, 16:9, square, portrait, invariant-culture serialization, and invalid ratios.
- [ ] Run the focused test filter and confirm missing-component failures.
- [ ] Implement both components with deterministic slots and protected attributes.
- [ ] Build, run focused tests, run all package tests, and commit `feat: add direction and aspect ratio primitives`.

### Task 3: Implement Typography and Typeset

**Files:**
- Create: `Maliev.ShadcnBlazor/Components/Typography/ShadcnTypeset.razor`
- Create: `Maliev.ShadcnBlazor/Components/Typography/ShadcnTypography.razor`
- Create: `Maliev.ShadcnBlazor/Components/Typography/ShadcnTypographyVariant.cs`
- Create: `Maliev.ShadcnBlazor.Tests/Components/SemanticFoundations/TypographyTests.cs`
- Modify: `Maliev.ShadcnBlazor/wwwroot/css/shadcn-semantic-foundations.css`

**Interfaces:**
- `ShadcnTypeset`: semantic `Tag` (`div`, `article`, or `section` only), `Size`, `Leading`, `Flow`, and optional `MaxWidth`; renders class `shadcn-typeset`, data slot, and CSS custom properties without owning layout width by default.
- `ShadcnTypography`: `Variant` enum values `H1`, `H2`, `H3`, `H4`, `Paragraph`, `Blockquote`, `InlineCode`, `Lead`, `Large`, `Small`, `Muted`, `UnorderedList`, `OrderedList`; renders the matching native element and `data-variant`.

- [ ] Write failing tests for exact native tags, all variants, child markup preservation, invalid tags/enums, custom rhythm variables, and no implicit maximum width.
- [ ] Run the focused tests and confirm missing types.
- [ ] Implement components and the upstream-derived typeset rhythm/typography CSS, including headings, paragraphs, lists, tables, quotes, code, links, responsive readability, semantic theme colors, and stable streaming margins using adjacent-sibling flow rules.
- [ ] Build, run focused and package suites, and commit `feat: add shadcn typography and typeset`.

### Task 4: Implement Label and Field composition

**Files:**
- Create: `Maliev.ShadcnBlazor/Components/Forms/ShadcnLabel.razor`
- Create: `Maliev.ShadcnBlazor/Components/Forms/ShadcnFieldSet.razor`
- Create: `Maliev.ShadcnBlazor/Components/Forms/ShadcnFieldLegend.razor`
- Create: `Maliev.ShadcnBlazor/Components/Forms/ShadcnFieldGroup.razor`
- Create: `Maliev.ShadcnBlazor/Components/Forms/ShadcnField.razor`
- Create: `Maliev.ShadcnBlazor/Components/Forms/ShadcnFieldContent.razor`
- Create: `Maliev.ShadcnBlazor/Components/Forms/ShadcnFieldLabel.razor`
- Create: `Maliev.ShadcnBlazor/Components/Forms/ShadcnFieldTitle.razor`
- Create: `Maliev.ShadcnBlazor/Components/Forms/ShadcnFieldDescription.razor`
- Create: `Maliev.ShadcnBlazor/Components/Forms/ShadcnFieldError.razor`
- Create: `Maliev.ShadcnBlazor/Components/Forms/ShadcnFieldSeparator.razor`
- Create: `Maliev.ShadcnBlazor/Components/Forms/ShadcnFieldOrientation.cs`
- Create: `Maliev.ShadcnBlazor/Components/Forms/ShadcnFieldLegendVariant.cs`
- Create: `Maliev.ShadcnBlazor.Tests/Components/SemanticFoundations/FieldTests.cs`
- Modify: `Maliev.ShadcnBlazor/wwwroot/css/shadcn-semantic-foundations.css`

**Interfaces:**
- `ShadcnLabel`: native `<label>`, `For`, and standard attributes.
- `ShadcnField`: role group; `Orientation` values `Vertical`, `Horizontal`, `Responsive`; `Invalid`, `Disabled`, and deterministic `DescriptionId`/`ErrorId` hooks for controls.
- `ShadcnFieldError`: `Errors` accepts `IReadOnlyList<string?>`; removes null/blank and duplicate messages in first-seen order; one message renders directly, multiple render a list; no output when empty; always `role="alert"` when rendered.
- Field family slot names mirror upstream: `field-set`, `field-legend`, `field-group`, `field`, `field-content`, `field-label`, `field-description`, `field-error`, and `field-separator`.

- [ ] Write failing tests for native label/fieldset/legend semantics, orientations, invalid/disabled data state, linked IDs, error deduplication, empty error suppression, legend variants, and caller attributes.
- [ ] Run focused tests and confirm failures for missing components.
- [ ] Implement the complete family and CSS for vertical, horizontal, container-responsive, disabled, invalid, option-card label, descriptions, and separator-with-content states.
- [ ] Build, run focused/package tests, and commit `feat: add shadcn field composition`.

### Task 5: Implement Item, Kbd, Separator, and Empty

**Files:**
- Create: `Maliev.ShadcnBlazor/Components/Content/ShadcnItem*.razor`
- Create: `Maliev.ShadcnBlazor/Components/Content/ShadcnItemVariant.cs`
- Create: `Maliev.ShadcnBlazor/Components/Content/ShadcnItemSize.cs`
- Create: `Maliev.ShadcnBlazor/Components/Content/ShadcnItemMediaVariant.cs`
- Create: `Maliev.ShadcnBlazor/Components/Content/ShadcnKbd.razor`
- Create: `Maliev.ShadcnBlazor/Components/Content/ShadcnKbdGroup.razor`
- Create: `Maliev.ShadcnBlazor/Components/Content/ShadcnSeparator.razor`
- Create: `Maliev.ShadcnBlazor/Components/Content/ShadcnSeparatorOrientation.cs`
- Create: `Maliev.ShadcnBlazor/Components/Content/ShadcnEmpty*.razor`
- Create: `Maliev.ShadcnBlazor/Components/Content/ShadcnEmptyMediaVariant.cs`
- Create: `Maliev.ShadcnBlazor.Tests/Components/SemanticFoundations/ContentFoundationTests.cs`
- Modify: `Maliev.ShadcnBlazor/wwwroot/css/shadcn-semantic-foundations.css`

**Interfaces:**
- Item family: group/list semantics; `ShadcnItem` supports `Default`, `Outline`, `Muted` and `Default`, `Small`; optional `Href` switches root to an anchor while preserving focus styling; media supports `Default`, `Icon`, `Image`; header/footer/actions/content/title/description child components expose upstream slots.
- `ShadcnKbd` renders `<kbd>`; `ShadcnKbdGroup` renders a semantic `<span role="group">` rather than invalid nested `<kbd>` assumptions.
- `ShadcnSeparator` supports horizontal/vertical and decorative/semantic modes; decorative emits `role="none"`, semantic emits `role="separator"` plus `aria-orientation`.
- Empty family exposes root/header/media/title/description/content with default/icon media variants.

- [ ] Write failing tests covering every variant/size, anchor vs div behavior, list/role semantics, separator ARIA modes, keyboard groups, empty composition, and invalid enums.
- [ ] Run the focused filter and confirm missing-component failures.
- [ ] Implement components and package-scoped upstream geometry/state CSS.
- [ ] Build, run focused/package tests, and commit `feat: add shadcn content foundations`.

### Task 6: Freeze public API and add deterministic showcase fixtures

**Files:**
- Create: `Maliev.ShadcnBlazor.Tests/Contracts/PublicApiSnapshotTests.cs`
- Create: `Maliev.ShadcnBlazor.Tests/Contracts/public-api.txt`
- Create: `Maliev.ShadcnBlazor.Showcase/Pages/SemanticFoundations.razor`
- Modify: `Maliev.ShadcnBlazor.Showcase/Layout/MainLayout.razor`
- Modify: `Maliev.ShadcnBlazor.Showcase/wwwroot/index.html`
- Modify: `Maliev.ShadcnBlazor/README.md`

**Interfaces:**
- Route `/components/semantic-foundations`; query parameters `theme=light|dark`, `dir=ltr|rtl`, `locale=en|th`, `fixture=<name>`.
- Public API snapshot is sorted by fully qualified type and public parameter/property signature; intentional API changes require updating the checked-in snapshot in the same component slice.

- [ ] Write failing snapshot and showcase contract tests before adding the snapshot/page.
- [ ] Add deterministic fixtures for every component, variant, size, invalid/disabled state, English/Thai long text, square/landscape/portrait ratios, and both directions.
- [ ] Import `shadcn-semantic-foundations.css` after `shadcn-base.css` and document all new components with minimal usage examples.
- [ ] Build, run package tests, launch the showcase smoke test, and commit `test: showcase shadcn semantic foundations`.

### Task 7: Add browser conformance and complete the nine ledger entries

**Files:**
- Create: `Maliev.ShadcnBlazor.BrowserTests/SemanticFoundationsBrowserTests.cs`
- Modify: `docs/shadcn-component-ledger.json`

**Interfaces:**
- Browser test records exact computed display, dimensions, padding, gap, font size/weight/line-height, color, background, border, radius, focus outline/ring, and overflow for named fixture slots.
- Screenshots run at 1440x900, 768x1024, 390x844, and 320x568 in light/dark and LTR/RTL; Thai fixtures run at 390 and 320 widths.

- [ ] Write browser tests for console/page errors, horizontal overflow, direction propagation, aspect geometry within 0.5 CSS px, field semantics, keyboard focus visibility, responsive field orientation, and deterministic screenshots.
- [ ] Run the tests before ledger changes; expect ledger/evidence assertions to fail while entries remain `planned`.
- [ ] Fix component defects only in files owned by Tasks 1-6; do not weaken thresholds or mask unexplained geometry/style differences.
- [ ] Mark Direction, Aspect Ratio, Typography, Label, Field, Item, Kbd, Separator, and Empty `complete` only after API, component, accessibility, interaction, computed-style, and visual evidence passes; leave `intranet` false until Plan 10.
- [ ] Build the full solution, run package tests, browser tests, and `Legacy.Maliev.Intranet.Tests`; run `git diff --check`; commit `test: certify shadcn semantic foundations`.

## Slice completion gate

- All nine public component families compile with zero warnings/errors and have stable API snapshots.
- Package tests cover parameters, invalid inputs, attribute merging, native semantics, ARIA, and composition.
- Browser tests pass at all required viewports with no console errors or unexplained overflow; screenshots and computed styles are fresh.
- The package remains independent from Intranet assemblies.
- The nine ledger entries are complete except `intranet`, which remains pending by design until Plan 10.
- The pre-existing unrelated critique file remains untracked and no push occurs.
