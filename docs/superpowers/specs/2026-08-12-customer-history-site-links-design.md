# Customer History and Site Link System

## Outcome

Turn the customer detail route into the operational home for one customer and establish a reusable Shadcn-style link system across the Intranet. Employees must be able to understand the customer, review permission-scoped orders, quotations, invoices, and recent activity, and navigate to the corresponding records without leaving customer context. All data must come through real same-origin BFF contracts; the browser must not download whole cross-customer datasets and filter them locally.

## Customer workspace

The existing customer identity header remains the page anchor. It contains the customer name, stable customer number, a quiet back-navigation action, and the permission-gated Edit customer action. The content is organized into five tabs:

1. **Overview** — contact, company, billing and shipping addresses, and record metadata.
2. **Activity** — a newest-first, bounded timeline combining customer-owned order, quotation, and invoice events.
3. **Orders** — a compact, paginated customer order table.
4. **Quotations** — a compact, paginated customer quotation table.
5. **Invoices** — a compact, paginated customer invoice table.

Overview is the default tab. The selected tab is represented in the URL so refresh, browser history, and deep links preserve context. History tabs display concise totals only when those totals are authorized and available. Every tab owns its loading, success, empty, error, retry, and pagination states so one unavailable downstream service does not erase the customer overview or other healthy history sections.

Desktop uses the existing dense operations workspace. Tablet and mobile retain the tab model through a horizontally contained, keyboard-operable tab list; records become compact responsive rows or cards rather than forcing document overflow. Long names, identifiers, amounts, and localized status text remain atomic or intentionally truncated with an accessible full-value affordance.

## History contracts and boundaries

The BFF is the only browser-facing history boundary. Customer ownership must be enforced by explicit `customerId` filters in the server-to-service calls; search text is not an ownership filter. Existing service projections remain authoritative:

- Orders use the existing customer-owned OrderService route.
- Quotations require a customer-scoped quotation query that returns the existing browser-safe quotation list projection.
- Invoices require a customer-scoped invoice query that returns the existing browser-safe invoice list projection.

The BFF exposes bounded customer-history reads for summary/activity and each paged record family. Response contracts contain only fields rendered by the customer workspace: stable record identifier, customer identifier, display/status fields, relevant monetary value and currency, event timestamp, and navigation target derived by the client from the record type and identifier. They must not expose persistence navigation properties, service credentials, storage identifiers, or internal exception details.

Activity composition happens server-side from bounded, customer-scoped results. Events use an explicit discriminated kind (`Order`, `Quotation`, or `Invoice`) and deterministic timestamp rules. Results are sorted newest first with stable identifier tie-breaking. No inferred business event is shown when its source record lacks the required timestamp.

Authorization is evaluated per record family. Overview continues to require customer read permission. Orders, quotations, and invoices require their existing read permissions; an unauthorized family is omitted rather than reported as empty. Downstream 401/403, 404, 429, timeout, malformed payload, and unavailable-service behavior follow the current BFF normalization conventions. The browser redirects only for an invalid employee session; family-specific permission or availability failures stay local to that tab.

## Site-wide link contract

The site must not style every `a` element identically. Links are migrated to explicit semantic roles, implemented through a shared Blazor link primitive or owned role classes:

- **Inline link** — used inside prose and definition data. Foreground color, underline with restrained decoration, clear hover state, and compact `focus-visible` ring.
- **Record link** — used for customer, order, quotation, invoice, and dashboard record navigation. Medium emphasis, atomic identifier treatment, deliberate truncation, and an accessible full value when truncated.
- **Navigation action** — used for Back, View all, and similar route changes that behave like quiet actions. Shadcn ghost styling, optional 16px leading/trailing icon, 36px desktop height, and 44px narrow/coarse-pointer target. It has no resting outline border.
- **External link** — uses inline-link styling plus an external-link indicator, new-window disclosure where applicable, and safe `rel` attributes.

Brand links, skip links, navigation-rail items, top-bar menus, and primary call-to-action links retain their specialized components. `MudButton Href` is not used merely to obtain link styling. Focus treatment is `focus-visible` only: a semantic 3px ring with appropriate offset that never crosses text, labels, or neighboring borders. Hover must not be required to understand that inline and record text is interactive. Disabled navigation is rendered as a disabled control rather than a clickable anchor.

The customer footer Back action becomes a quiet navigation action with a leading back arrow and a localized accessible name. The same role contract is applied to equivalent back/view-all actions across production pages during the migration, while preserving every route, query string, target, click handler, authorization condition, and localization key.

## Content, localization, and accessibility

- All new labels, empty states, statuses, tab names, timeline descriptions, pagination labels, and accessible names are localized in English and Thai.
- Dates use the existing Bangkok-aware presentation helpers; monetary values use the record currency and existing formatting conventions.
- Tabs implement the expected tab/tabpanel keyboard model and visible selected state.
- Link purpose is understandable from its accessible name and surrounding record context; repeated generic links such as `View` receive record-specific accessible names.
- Touch targets are at least 44 by 44 CSS pixels on coarse pointers and narrow layouts. Desktop retains the established 36px Shadcn density where appropriate.
- Focus order follows the visual order. Hidden responsive content and unauthorized tabs are absent from the accessibility tree.
- WCAG 2.2 AA contrast, 200 percent zoom, reduced motion, forced colors, English, and Thai are required verification modes.

## Responsive structure

- **Wide desktop:** identity/header row, contained tab list, overview grid, and dense history tables.
- **Tablet:** one-column overview flow where needed, contained tabs, and history tables with intentional internal scrolling or responsive rows without document overflow.
- **Mobile:** full-width header actions, horizontally contained tabs, compact record cards, and reachable pagination. No history section may create horizontal document travel.

Required geometry is verified at 1280, 768, 390, and 320 CSS pixels. The page must remain usable at 200 percent zoom and with Thai labels.

## Testing and delivery

Implementation is test-first and divided into independently coherent slices:

1. Freeze the shared link-role contract and migrate representative anchors and navigation actions without changing behavior.
2. Add customer-scoped service/BFF history contracts with producer-consumer and authorization tests.
3. Add the customer tabs, activity, paged sections, localized content, and responsive states.
4. Run a site-wide link inventory and migrate remaining production links, preserving specialized exceptions.

Validation includes build-first Release compilation with zero warnings and errors; focused contract/component tests; the full affected Legacy and package suites; production-client browser tests against real rendered MudBlazor DOM; permission, loading, empty, partial-failure, rate-limit, retry, English, Thai, keyboard, screen-reader semantics, dark mode, forced-colors, reduced-motion, responsive geometry, 200 percent zoom, and link destination checks. The Impeccable detector runs exactly once after the finished UI changes, followed by diff/status audits and scoped commits. No deployment is part of this design.
