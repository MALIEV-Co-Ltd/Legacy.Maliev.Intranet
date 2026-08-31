# Design QA: MALIEV workspace sidebar refinement

- Source visual truth: the four annotated `https://localhost:56714/customers` browser captures supplied on 2026-08-31, plus `C:\Users\natth\AppData\Local\Temp\codex-clipboard-5eb92b79-71c5-494e-adc8-b6e98a6a60b7.png` for the Maliev.ShadcnBlazor production-workspace reference
- Source viewport: 1569 x 1032 CSS pixels for the annotated application state; 1010 x 894 pixels for the package reference
- Implementation route: `https://localhost:56714/customers`
- Implementation viewport: automated checks at 1280 x 900, 768 x 844, 390 x 844, and 320 x 844 CSS pixels, density 1x
- Implementation screenshot: unavailable because the selected in-app browser rejected local HTTPS capture under its URL security policy after the Aspire resource reload
- State: authenticated, light theme, expanded and icon-collapsed desktop sidebar, with customer child navigation opened and closed

## Full-view and focused evidence

The source annotations identify four focused regions: the collapsed icon rail, sidebar identity, navigation hierarchy, and topbar controls. The rendered implementation could not be captured into the same visual-comparison input. Automated browser evidence verifies the corresponding geometry and interaction behavior but does not substitute for a pixel comparison.

## Findings and correction history

1. P1 - The sidebar identity rendered a text `M` approximation. Replaced it with the supplied `images/MALIEV_BLACK.svg` wordmark and a theme-aware contrast token; the placeholder mark no longer exists.
2. P1 - Primary destinations and child create-actions were visually too similar. Primary rows now use the stronger foreground/weight while child rows use a smaller, muted foreground that returns to the package accent foreground for hover and active states.
3. P1 - Child actions were permanently expanded. Each branch now uses the released `ShadcnCollapsible`, exposes a labelled disclosure trigger, preserves correct keyboard order, and hides its subtree in icon-collapse mode.
4. P1 - Collapsed navigation and footer controls did not share a centerline. Automated geometry now reports no more than 0.5 px variance across all visible sidebar menu buttons, with child links absent from the collapsed rail.
5. P1 - Search, quick actions, language select, theme button, and profile button resolved to mixed heights. All visible topbar controls now render at 44 px with at most 0.5 px variance.
6. P2 - Quotation requests had no numeric context. The released `ShadcnSidebarMenuBadge` now shows the real BFF `TotalRecords` value after the authenticated session arrives. The read is background-only, permission-scoped, cancellable, and bounded by the shared presentation timeout. It is intentionally described as total requests because the current downstream contract has no unhandled-only filter.
7. Automated regression evidence: 81 browser tests pass, including English/Thai hierarchy, desktop/mobile drawers, nested disclosure keyboard order, equal topbar heights, official logo asset, real badge projection, collapsed centerline, independent scrolling, responsive containment, and console-error checks.

## Required fidelity surfaces

- Fonts and typography: package Geist/Noto Sans Thai remains active; primary and child navigation hierarchy is differentiated without reintroducing monospace UI text.
- Spacing and layout rhythm: package 32 px desktop navigation rows and 44 px topbar controls are preserved; collapsed controls share one centerline.
- Colors and tokens: hierarchy uses package sidebar foreground/accent tokens; no replacement theme palette was introduced.
- Image quality and asset fidelity: the canonical vector MALIEV wordmark is used directly; no CSS-drawn or text substitute remains.
- Copy and content: existing localized labels remain; new English and Thai disclosure/count labels were added.

## Remaining visual check

A same-input screenshot comparison is blocked by the selected in-app browser's local HTTPS security rejection. Reopening the refreshed Aspire URL in an allowed in-app browser session is required for a final pixel-level pass.

final result: blocked
