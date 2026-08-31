# Design QA: ShadcnBlazor inset workspace shell

- Source reference: `C:\Users\natth\AppData\Local\Temp\codex-clipboard-5eb92b79-71c5-494e-adc8-b6e98a6a60b7.png`
- Source viewport: 1010 x 894 CSS pixels, density 1x, expanded light-theme sidebar example
- Implementation route: `https://localhost:55848/Quotations/Index`
- Implementation viewport: 1280 x 900 CSS pixels, density 1x, expanded light-theme authenticated shell
- Implementation screenshot: unavailable because the selected in-app browser rejected the local HTTPS page under its URL security policy after the live resource reload

## Comparison target

The reference is the Maliev.ShadcnBlazor production-workspace example. The application keeps its own navigation labels and operational content, while matching the example's component configuration and visual system: `Inset` sidebar variant, 15 rem rail, 3.5 rem icon rail, package-owned neutral sidebar surface, 32 px brand mark, 32 px desktop menu rows, inset content radius and shadow, compact footer identity, and package sans typography.

## Findings and correction history

1. P0 - The application used the default sidebar variant rather than `Inset`. Corrected by setting `ShadcnSidebarVariant.Inset` and adding the package rail component.
2. P1 - Application CSS replaced the package sidebar surface with the card surface and added a header divider. Removed those overrides so Maliev.ShadcnBlazor owns the sidebar color and state styling.
3. P1 - The rail used a large SVG wordmark and 44 px desktop navigation rows. Replaced with the documented compact mark/copy anatomy and restored the package's 32 px desktop row metric. Mobile rows remain 44 px for touch access.
4. P1 - The client replaced the package font with IBM Plex Sans Thai. Restored the `BaseVegaNeutral` preset typography and the package's self-hosted Geist and Noto Sans Thai assets.
5. P1 - Table IDs and money values inherited the coding font. Table-scoped atomic values now inherit the table's sans font and retain tabular number alignment.
6. Automated render evidence at 1280 x 900: sidebar width 240 px; brand mark 32 px; active row 32 px; sidebar and wrapper surfaces match; inset radius 14 px with package shadow; tabular cells resolve to the table font; no document-level horizontal overflow across 1280, 768, 390, and 320 px; mobile drawer focus remains contained.

## Remaining visual check

A same-input screenshot comparison could not be completed because the in-app browser blocked further access to the local HTTPS route. The source contract and computed render metrics pass, but a final pixel-level comparison still requires reopening the implementation page in an allowed browser session.

final result: blocked
