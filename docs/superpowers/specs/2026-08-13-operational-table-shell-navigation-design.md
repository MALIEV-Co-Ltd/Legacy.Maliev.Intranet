# Operational Tables, Shell Navigation, and Material Icon Design

## Outcome

Create one coherent MALIEV operations interface for data-heavy pages. The change standardizes operational tables, list controls, breadcrumbs, navigation hierarchy, top-bar alignment, and icon usage while preserving every existing route, query value, permission, localization key, API contract, and record action.

Orders is the reference implementation. The same shared contracts then migrate Customers, Quotation Requests, Quotations, Invoices and accounting records, Employees, Materials and catalog records, Suppliers, Purchase Orders, Diagnostics, and other table-bearing feature pages in bounded module groups.

This is a frontend-system change. It does not add or modify backend endpoints, database models, authentication behavior, or deployment configuration.

## Approved direction

The selected architecture is shared primitives with explicit page adapters. A generic table must not infer business meaning from reflection, arbitrary dictionaries, CSS selectors, or route conventions. Each feature declares its columns, record identity, existing detail destination, localized action names, and quick-view content through typed component parameters or feature-owned render fragments.

The rejected alternatives are:

- Independent page-by-page restyling, because it would preserve the current responsive and interaction inconsistencies.
- A fully schema-driven universal data grid, because it would obscure feature-specific authorization, navigation, localization, and record semantics.
- Mobile card conversion, because it produces excessively tall records, duplicates labels, and makes pagination and comparison difficult.

## Shared operational table

`OperationalTable` is the shared composition boundary. It owns table containment, responsive priority behavior, sticky regions, expansion state, row actions, loading/empty/error presentation, and accessibility wiring. Feature adapters continue to own data retrieval, sorting, filtering, paging, permissions, formatting, and destinations.

The table remains a semantic table at every supported width. Horizontal overflow belongs to its local scroll container; the document must never scroll horizontally. Arbitrary `overflow-wrap:anywhere` is prohibited for identifiers, dates, money, quantities, statuses, and action labels.

Column definitions explicitly identify:

- Stable key and localized header.
- Desktop width or width bounds.
- Priority: essential, supporting, or quick-view-only.
- Alignment and atomic/truncation behavior.
- Sort behavior when the existing page supports it.
- Cell renderer and accessible full-value behavior.

Identity remains reachable at the left and actions at the right. On wide and intermediate layouts these regions may be sticky within the table's own scroll container when doing so does not cover content. Sticky styling must include an opaque semantic background, border separation, and correct stacking order.

## Row actions and quick view

Every migrated record row has two separate compact Material icon actions when both behaviors exist:

1. **Open detail** navigates to the existing detail route without altering query or authorization behavior.
2. **Quick view** expands or collapses secondary information below the record without navigating.

The buttons use 20px outlined icons in dense contexts, localized tooltips, and record-specific accessible names. Their interactive roots remain at least 44 by 44 CSS pixels on narrow or coarse-pointer layouts. Filled icons are reserved for a selected or active state and are never the only state cue.

Only one record can be expanded per table. Opening another record closes the previous one. Activating the current expand control collapses it. Filtering, sorting, paging, refreshing, replacing the data set, or leaving the route clears expansion. The expand control exposes `aria-expanded` and `aria-controls`; the controlled row has a stable identifier derived from the feature and record identity.

The expanded row spans the visible table columns and contains a responsive definition-list composition. It presents secondary fields, workflow/status context, relevant dates and quantities, ownership, related identifiers, and existing authorized actions. It does not duplicate all primary values or trigger additional network requests unless the feature already owns that behavior and supplies explicit loading, success, empty, and failure states.

## Responsive information hierarchy

### Wide layouts: 1180px and above

- Show essential columns and selected supporting columns.
- Keep a sticky header when the page scroll model permits it.
- Keep identity and action regions visible where practical.
- Use 36px dense desktop controls and compact rows without reducing readability.

### Intermediate layouts: 721px through 1179px

- Retain the semantic table and contained horizontal scrolling.
- Show identity, status, the most important operational value, and actions.
- Move lower-priority fields into quick view.
- Prevent the table, toolbar, or sticky regions from expanding the document width.

### Narrow layouts: 720px and below

- Retain the semantic table instead of transforming every cell into a labeled card.
- Show approximately three to five essential columns according to the feature's approved priority map.
- Keep both row actions visible and at least 44 by 44 CSS pixels.
- Put all secondary fields in quick view.
- Preserve contained scrolling with a visible edge/overflow affordance when more columns are available.

Long human-readable values use controlled ellipsis or a small line clamp. Full values remain available through the quick view or an accessible disclosure. IDs, money, quantities, dates, statuses, and action labels remain atomic.

## Shared list toolbar

`ListToolbar` remains the single shared search, sort, page-size, clear, and refresh composition. It becomes a quiet command strip aligned to the table rather than a visually dominant nested form.

- Search owns the flexible track.
- Sort and page-size controls use bounded tracks.
- Conditional Clear and Refresh actions form a compact action cluster.
- Refresh becomes an icon-only `MudIconButton` using the outlined Material refresh icon, with localized tooltip and accessible name.
- Existing search debounce, query serialization, sort wire values, page-size values, clear behavior, callbacks, disabled states, and localization remain unchanged.

Wide layouts use one row where space permits. Intermediate layouts use two balanced rows without leaving Refresh alone. Narrow layouts place search on its own row, pair sort and page size when localized content fits, and place actions in a compact final row. Every narrow/coarse interactive surface is at least 44px high.

## Breadcrumbs and page hierarchy

`PageBreadcrumbs` becomes a shared semantic navigation primitive rendered above the page header. Feature pages provide explicit localized crumb labels and existing destinations; the primitive does not infer them from URLs.

- The navigation landmark has a localized accessible name.
- Intermediate crumbs are links using the shared content-navigation link treatment.
- The current page is text with `aria-current="page"` and is not a redundant link.
- Long trails collapse at feature-approved boundaries rather than truncating the current record identity.
- Browser history remains the source of browser back/forward behavior; breadcrumbs provide deterministic hierarchy navigation.

## Navigation rail hierarchy

Navigation metadata gains an explicit parent/child relationship. Primary destinations such as Customers and Orders retain the normal rail treatment. Create actions such as New customer and New order render directly beneath their parent as quieter, indented child actions.

Child actions retain their current routes, permission requirements, icons, localized labels, and active matching. The hierarchy must be represented structurally, not only through indentation: parent groups and child links remain understandable to screen readers and keyboard users. Collapsed-rail presentation uses tooltips and a visual relationship that does not rely on text indentation.

## Top bar alignment

The shared top bar uses a stable grid with four logical zones:

1. Brand and navigation toggle.
2. Flexible global search.
3. Quick-create actions.
4. Language, theme, and employee profile utilities.

This replaces negative-margin and incidental flex alignment. All controls share one vertical alignment and control-height vocabulary. At intermediate widths, lower-priority quick actions move into an overflow/menu owner before search becomes unusable. At narrow widths, the rail becomes a drawer and the top bar retains the menu, brand, essential action/search entry, and profile access without horizontal document overflow.

## Material icon contract

The application already uses Google's Material icon vocabulary through MudBlazor's embedded SVG paths. That remains the primary icon source and avoids adding an icon-font download.

- Use outlined Material icons for default actions and navigation.
- Use filled variants only for selected or active states.
- Use 20px icons for dense table/toolbar controls and 24px icons for standard navigation or standalone actions.
- Icon-only controls always have localized accessible names and tooltips.
- Icons are decorative when adjacent text already supplies the name.
- If MudBlazor lacks an approved symbol, add a reviewed self-hosted Google Material Symbols SVG asset. Do not add a runtime Google Fonts dependency.
- Replace any non-Material icon assets discovered during migration only after confirming their semantic equivalent.

## Visual and interaction system

The interface remains calm, dense, and precise:

- Semantic white/token surfaces, subtle one-pixel borders, and minimal elevation.
- An 8px spacing rhythm with deliberate 4px dense subdivisions.
- 36px desktop controls and 44px minimum narrow/coarse targets.
- Static labels and helper/error content that never overlap control borders or values.
- A restrained semantic focus ring with no browser-native outline layered over MudBlazor borders.
- English and Thai treated as equal layout targets.

Loading, empty, error, forbidden, partial-data, disabled, and refreshing states stay explicit. Reduced-motion and forced-colors behavior remains supported. Focus must remain visible and predictable after expanding, collapsing, refreshing, or paging.

## Migration boundaries and sequencing

Implementation proceeds through independently buildable commits:

1. Shared contracts for Material icon use, breadcrumbs, table definitions, row actions, and toolbar refresh.
2. Shared shell work for navigation hierarchy and top-bar alignment.
3. Orders reference adapter, including one-row quick view and the approved responsive priority map.
4. Sales and customer-facing tables.
5. Accounting, procurement, catalog, employee, diagnostics, and remaining operational tables.
6. Final cross-application browser verification and Impeccable critique fixes.

Each feature migration inventories every current column and classifies it as essential, supporting, or quick-view-only. No information may disappear without an approved destination. Existing detail routes, authorization, filters, sorts, paging, localization, loading/error behavior, and record actions are frozen with focused contracts before markup changes.

## Accessibility requirements

- WCAG 2.2 AA color contrast and non-text contrast.
- Keyboard operation for search, sort, paging, breadcrumbs, detail navigation, and expand/collapse.
- Visible focus in normal, dark, and forced-colors modes.
- Record-specific accessible names for repeated row actions.
- Correct table semantics and header associations at every breakpoint.
- Hidden responsive columns and controls must not leave invisible focus targets.
- 44px targets for narrow/coarse interaction surfaces.
- No two-dimensional document scrolling at 200% zoom.
- Reduced-motion mode removes nonessential expansion and scroll animation.

## Verification strategy

Changes are test-first at each shared or feature boundary:

- Source and component contracts freeze routes, permission conditions, callback wiring, query values, localized names, and column priority definitions.
- Executable component tests cover one-expanded-row state, collapse, replacement, refresh/paging reset, and stable accessible associations.
- Production-client browser tests exercise real MudBlazor DOM and scoped CSS rather than duplicated markup.
- Orders is verified first at 1280, 768, 390, and 320 CSS pixels in English and Thai.
- Representative pages from every migrated module receive the same geometry and behavior coverage.
- Keyboard, dark mode, forced colors, reduced motion, coarse pointer, and modeled 200% zoom are explicit browser modes.
- Browser checks assert contained table scrolling, zero document overflow, atomic values, 44px targets, row-action names, focus behavior, and one-row-only expansion.

Every coherent slice requires a Release build with zero warnings and errors, focused tests, the affected full suite, applicable package tests, browser tests, formatting verification, and clean diff checks. After all migrations, run the Impeccable detector exactly once and perform a final visual critique across the shared shell and representative table pages. Any shared defect found by the final critique is corrected at its actual owner and revalidated before completion.

## Completion criteria

The design is complete when:

- All in-scope operational table pages use the shared table and toolbar behavior or have an explicit documented exception.
- Rows expose separate detail and quick-view actions where applicable, with at most one expanded row.
- Refresh is icon-only throughout shared list toolbars.
- Create destinations are structurally subordinate to their navigation parents.
- Shared breadcrumbs and aligned top-bar zones are present across migrated pages.
- The Material icon contract is enforced without a runtime icon-font dependency.
- Existing functional and authorization contracts remain unchanged.
- Automated and visual evidence passes at all required widths and modes with no unresolved Critical or Important finding.
