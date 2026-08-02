# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

MALIEV employees who manage customers, quotations, orders, production, purchasing, inventory, accounting, and employee administration. The primary operating context is a desktop workstation, with tablet and mobile access required for quick operational checks and approvals.

## Product Purpose

Legacy.Maliev.Intranet is the employee CRM and ERP workspace migrated from the legacy MALIEV intranet to a .NET 10 standalone Blazor WebAssembly client and same-origin ASP.NET Core BFF. Migration succeeds when employees can use their existing credentials and complete every legacy workflow without lost data, broken authorization, or behavioral regressions.

## Operating Context

Employees scan queues, search business records, create quotations and orders, review customer activity, monitor manufacturing work, manage materials and suppliers, and perform finance and administration tasks. Operational dates are displayed in Asia/Bangkok while persisted timestamps remain UTC. Monetary values use THB and locale-appropriate formatting.

## Capabilities and Constraints

- Preserve the legacy route, authorization, DTO, API, database, and session contracts while adopting .NET 10 and Blazor WASM.
- The browser communicates through the same-origin BFF; credentials, refresh tokens, and service secrets never enter WebAssembly.
- Server-side authorization is required for every protected BFF and service operation; cookie-authenticated writes retain CSRF protection.
- Existing PostgreSQL-migrated production data remains the source for parity validation. UI work must not invent records or operational capabilities that are not backed by legacy-owned services.
- English and Thai are first-class locales. Thai must read naturally and professionally rather than as literal machine translation.
- Accessibility, keyboard operation, responsive behavior, loading, empty, degraded, and error states are required across all pages.

## Brand Commitments

Use the MALIEV wordmark and restrained blue, white, ink, and status colors. The interface is an operations workspace: dense, calm, precise, and legible. Typography uses Inter for Latin text, Noto Sans Thai for Thai text, and sans-serif as the final fallback.

## Evidence on Hand

- The original legacy implementation in `R:\maliev-web\Maliev.Intranet` is read-only behavioral and content evidence.
- The owner-supplied operations command-center reference image defines the approved information density and shell direction.
- Existing Blazor routes, BFF aggregators, authorization policies, parity manifests, and automated tests define the migrated contract.

## Product Principles

- Preserve every legacy workflow before retiring its compatibility route.
- Put the most frequent operational actions and records within immediate reach.
- Show real permission-scoped data and honest degraded states; never fabricate dashboard metrics.
- Keep one coherent design system across Blazor and compatibility Razor pages.
- Make Thai and English equally complete, usable, and natural.

## Accessibility & Inclusion

Target WCAG 2.2 AA, including keyboard navigation, visible focus, semantic landmarks, sufficient contrast, 44px touch targets, reduced-motion support, and layouts that remain usable at 200% zoom.
