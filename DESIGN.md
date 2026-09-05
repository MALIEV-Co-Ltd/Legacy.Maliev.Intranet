# Design System

<!-- impeccable:design-schema 1 -->

## Direction

An operations command center for a Thai precision-manufacturing business. The visual language is compact and disciplined: a persistent white navigation rail, a focused utility bar, clear content hierarchy, and dense operational tables. Brand expression comes from precise spacing, typography, and blue interaction accents rather than decorative effects.

## Surface Mode

Operate. Scanability, task completion, permission clarity, and truthful system state take priority over marketing expression.

## Typography

Use the self-hosted `IBM Plex Sans Thai, sans-serif` family everywhere except actual code or machine identifiers. Maintain an obvious hierarchy without eyebrow labels: page titles, section titles, body, metadata, and tabular data. Keep body copy readable and controls large enough for touch. The shared semantic layer follows the shadcn neutral token contract for surfaces, foregrounds, borders, focus rings, radius, and density; MALIEV identity remains in the wordmark and content, not one-off control styling.

## Color

- Canvas: warm white to pale neutral.
- Navigation and panels: white.
- Primary interaction: shadcn neutral primary/foreground pairs.
- Primary text: neutral ink/foreground.
- Secondary text: neutral muted-foreground with WCAG AA contrast.
- Status colors: green success, amber warning, red failure, blue informational; always pair color with text or icon meaning.

## Layout

- Desktop: persistent grouped left rail, utility bar with global search and permission-scoped quick actions, then the page workspace.
- Tablet: collapsible rail/drawer with the same complete navigation and a compact utility bar.
- Mobile: compact top header plus reachable navigation drawer; required actions remain visible and tables become deliberate responsive views rather than accidental one-column stacks.
- Content uses fluid grids and bounded widths. Dense lists favor data tables on wide screens and structured records on narrow screens.
- Command-center dashboards use a dense 12-column bento grid with 4-, 8-, and 12-column panel spans. Panels omitted by permission or unavailable data leave no reserved gaps; dense placement reflows the remaining panels, and the grid becomes one column at narrow mobile widths (760px and below).
- Interactive mobile controls and record links retain a minimum 44px target. In the collapsed desktop rail, navigation links occupy the rail width and center their icons rather than inheriting expanded-state alignment.

## Components

Shared primitives include AppShell, NavigationRail, GlobalSearch, QuickActions, PageHeader, MetricSummary, OperationalTable, StatusPill, EmptyState, ErrorState, LoadingState, and responsive form sections. Components must expose loading, empty, success, degraded, disabled, and error states.

- Workspace metric tiles are permission-scoped navigation summaries: render only workspaces returned for the employee's grants, and do not fabricate hidden or zero-value placeholders.
- Keep employee preferences, including language and theme, inside the profile popover alongside profile actions. The desktop profile trigger is a true capsule; at compact widths it may reduce to the avatar-sized control while preserving the 44px target.
- In Blazor CSS isolation, a parent layout that sizes or aligns markup rendered inside a child component must deliberately cross the isolation boundary with `::deep`; this applies to bento panel spans, shell controls, chart internals, and similar parent-owned composition rules.

## Motion

Motion is restrained and functional. Respect `prefers-reduced-motion`; use brief state transitions for drawers, focus, loading, and data refresh only. No decorative dependency or repeated entrance animation.

## Content and Localization

All user-facing strings come from resources. English and Thai share identical functionality, information order, routes, actions, and validation semantics. Dates display in Asia/Bangkok, persisted values remain UTC, and money is formatted as THB.

## Compatibility Host

The compatibility Razor host uses the same tokens, typography, shell proportions, focus states, and responsive rules immediately. It must not look like a separate product while routes are retired feature by feature.
