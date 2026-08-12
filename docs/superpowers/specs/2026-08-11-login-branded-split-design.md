# MALIEV Login Branded Split Design

## Objective

Replace the shrink-wrapped employee login column with a full-viewport, wider branded split composition while preserving the existing authentication behavior and MALIEV Shadcn/MudBlazor system. Fix every issue recorded in the 2026-08-11 Impeccable critique: viewport ownership, malformed dark wordmark, spacing rhythm, unavailable Google state, contrast, loading feedback, responsive overflow, touch targets, duplicated footer content, and employee recovery guidance.

## Approved Direction

- Use a wider branded split layout rather than a quiet centered utility shell.
- Hide the unavailable Google control, but retain a compact note explaining that Google sign-in is currently unavailable and email remains available.
- Fix all P1 and P2 findings in one coordinated implementation.
- Preserve the existing email/password behavior, Google initialization path, theme toggle, language selector, same-origin authentication boundary, localization, and authenticated redirect behavior.

## Spatial Thesis

The primary task path is: identify the secure MALIEV employee gateway, choose an available authentication method, enter credentials, and recover or obtain help when blocked. The login form is the leading interactive region. The branded region supports trust and orientation without competing with the form.

At wide viewports, the page fills the available shell and uses two columns: a restrained MALIEV brand panel and a generously sized authentication panel. The form remains limited to a readable measure of approximately 28rem, but the page, header, and footer no longer shrink-wrap around it. At narrow viewports the brand panel collapses into a compact branded header so the authentication form retains the first useful screenful. DOM and focus order remain brand/header, authentication content, then footer/support.

Use one spacing cadence based on 4px increments, with 8px for closely related text, 12px for control internals, 16px for form siblings, 24px for groups, and 32–48px for major regions. No page region may create document overflow solely from nested viewport-height rules.

## Composition

### Brand Panel

- Occupies roughly 42–46% of the wide composition with a dark token-based surface that works in both application themes.
- Shows one corrected MALIEV wordmark, a concise employee-gateway statement, and a small trust/support message.
- Uses no decorative stock imagery and no fabricated claims.
- Uses the canonical wordmark geometry. The white asset must share the corrected viewBox/transform behavior of the black asset, or the implementation must use one themeable canonical SVG.
- The logo link has at least a 44×44px interaction area without visually inflating the wordmark.

### Authentication Panel

- Contains the language selector and theme toggle in a compact utility row.
- Keeps the authentication card visually quiet and wider than the current card, with a maximum width around 28rem.
- Uses a single title treatment; do not repeat a second malformed logo inline with the heading.
- Keeps Google and email paths in the existing logical order when Google is available.
- When Google is unavailable, removes the empty 44px host from layout and shows a compact muted service note immediately above the email divider or form.
- Separates the primary button from the employee note by at least 16px.
- Consolidates the repeated external company link and adds a clearly named employee-access help route using existing factual contact/support infrastructure only. If no internal recovery URL exists, use localized support copy without inventing an endpoint.

## States and Behavior

### Authentication Preflight

While the existing authentication state is checked, render a stable loading treatment in the authentication panel using the same approximate card footprint. Announce the state politely and avoid shifting the layout when the form appears.

### Google Available

Render the real Google Identity host and retain the existing status region and initialization contract.

### Google Unavailable

Hide the empty Google host after the interop reports unavailability. Show a compact, low-priority note communicating that Google sign-in is temporarily unavailable and that work-email sign-in remains available. The message must not visually compete with validation errors.

### Email and Password

Preserve domain validation, local `.test` allowance, autocomplete, Enter-key behavior, Change action, Remember me, processing spinner, return URL validation, and current error handling. Do not log, persist, or expose credentials.

### Error and Recovery

Keep errors adjacent to the form and announced through the existing alert semantics. Preserve the employee email after a failed password attempt. Clearing the password is allowed for security, but the page must provide a localized recovery/support hint so the employee has a next action.

## Visual and Accessibility Requirements

- Placeholder and helper text must meet WCAG AA text contrast for their rendered size; the email placeholder target is at least 4.5:1.
- Interactive boundaries and visible focus indicators must reach at least 3:1 against adjacent surfaces.
- Disabled controls must remain clearly identifiable without stacking low-alpha colors and container opacity into illegibility.
- Language selector, theme toggle, logo link, inputs, primary action, and actionable support links must expose at least a 44×44px touch target at narrow viewports.
- Links must have a persistent non-color affordance or sufficient contextual distinction.
- The corrected layout must not produce horizontal overflow at 320px or vertical overflow caused solely by the layout wrapper at common viewport heights.
- At 200% zoom, the task order, labels, form controls, error content, and support path remain usable without two-dimensional scrolling.
- Light and dark themes use the same spatial structure and valid logo geometry.
- Reduced-motion and forced-colors behavior remain supported.

## Responsive Rules

- Wide, approximately 960px and above: two-column split surface spanning the full available viewport.
- Intermediate, approximately 641–959px: retain full-width ownership, reduce brand-panel width, and keep the form at a readable maximum width.
- Narrow, 640px and below: collapse to one column; retain a compact brand header, place utilities within easy reach, remove nonessential brand-panel copy, and prioritize the form.
- Extremely narrow, 320px: use 16px minimum page gutters and allow localized Thai/English copy to wrap without clipping.

## Implementation Boundaries

Expected production changes are limited to the login surface, its empty-layout ownership rule, the canonical logo asset, localized login copy if required, and shared CSS selectors that directly own this page. Authentication client contracts, BFF/API endpoints, credential validation rules, and session behavior are out of scope.

The existing Shadcn token system remains authoritative. Do not introduce a second color system, raw theme-specific grays, or broad unscoped `.mud-*` overrides.

## Verification Contract

Implementation begins with failing regression tests that assert:

- the empty layout stretches the login surface without nested viewport overflow;
- the login markup contains distinct brand and authentication regions;
- the unavailable Google host can collapse while retaining a status note;
- authentication preflight renders an announced loading state;
- the corrected white wordmark geometry matches the canonical visible viewBox;
- the spacing, contrast, link, and minimum-touch-target contracts are represented by page-owned selectors and tokens.

After the tests fail for the current implementation, make the minimum production changes, then run the focused login tests, the affected project suite, the Release solution build, and browser checks at desktop and narrow viewports in light, dark, loading, Google-unavailable, email, password, invalid, disabled, keyboard-focus, and 200%-zoom states. Inspect screenshots for logo integrity, hierarchy, spacing, clipping, and contrast before committing.
