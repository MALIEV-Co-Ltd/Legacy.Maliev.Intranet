# Native Maliev.ShadcnBlazor Migration Design

**Date:** 2026-08-29

**Status:** Approved for planning

## Objective

Replace every direct MudBlazor UI component integration in the Legacy MALIEV
Intranet with the released `Maliev.ShadcnBlazor` component library. The final
consumer code must compose its UI exclusively from native HTML, application
components, and public `Maliev.ShadcnBlazor` APIs. MudBlazor may remain only as
an implementation dependency of `Maliev.ShadcnBlazor` and as the source of the
static assets that the released package explicitly requires.

The migration must preserve application behavior and must turn any verified
consumer-facing library limitation into a detailed issue in
`MALIEV-Co-Ltd/Maliev.ShadcnBlazor`.

## Current State

The authoritative baseline is repository commit `9588ba1a`. The repository
contains an early embedded `Maliev.ShadcnBlazor` Razor Class Library whose only
public component is `ShadcnThemeProvider`; its remaining integration is a CSS
adapter over direct MudBlazor components.

Production consumer projects currently contain:

- 1,485 direct `<Mud*>` render sites;
- 36 distinct MudBlazor component types;
- 60 affected Razor files out of 79 Razor files inspected; and
- direct MudBlazor package references and namespaces across the shell, shared
  UI, and feature projects.

The upstream repository currently releases `Maliev.ShadcnBlazor` 1.2.2. That
version targets .NET 10, pins MudBlazor 9.7.0 internally, and exposes 66 native
component families. Its documented public API, installed assembly, and XML
documentation are authoritative; similarly named React or MudBlazor APIs must
not be inferred.

The unchanged baseline solution builds in Release with zero warnings and zero
errors. Baseline test results are recorded separately in the implementation
plan because the test process was still running while this design was written.

## Completion Boundary

The migration is complete only when all of the following are true:

1. No production Intranet `.razor` or `.cs` file renders or references a
   `Mud*` component or imports a MudBlazor namespace.
2. No Intranet consumer project directly references the MudBlazor NuGet
   package. The transitive dependency and required MudBlazor CSS/JavaScript
   assets owned by `Maliev.ShadcnBlazor` are allowed.
3. Every application UI element is native HTML, an application-specific
   composition, or a public component from released `Maliev.ShadcnBlazor`.
4. The stale embedded library, showcase, package tests, and package browser
   tests are removed from this application repository after their useful
   consumer coverage has been migrated. Library implementation and conformance
   tests remain owned by the upstream library repository.
5. Routes, API calls, DTOs, authentication and authorization, bindings,
   validation, localization, money and date semantics, user-visible states,
   and application workflows remain behaviorally compatible.
6. Every migrated interactive surface has evidence for the states users can
   reach, including loading, empty, success, error, disabled, read-only, and
   validation states where applicable.
7. Keyboard navigation, focus behavior, accessible names and relationships,
   light and dark themes, narrow layouts, and overlays are exercised at the
   affected boundaries.
8. A final automated inventory proves zero direct consumer MudBlazor usage and
   maps every baseline component family to its replacement.
9. Every verified library difficulty discovered during implementation is
   either linked to an existing upstream issue or reported in a new detailed
   upstream issue.

## Architecture

### Dependency ownership

All Intranet UI projects reference the released `Maliev.ShadcnBlazor` package
at one exact version. Central package ownership is preferred so feature
projects cannot drift independently. Application projects do not reference the
upstream source tree or copy Showcase internals.

The application root calls `AddMalievShadcn`, renders exactly one
`ShadcnThemeProvider`, and loads package assets in the documented order. The
provider owns any internal MudBlazor providers required by the package. The
application does not render duplicate theme, popover, dialog, or snackbar
providers.

### UI ownership

Package components own reusable behavior, semantics, and component styling.
Application components own routes, business data, resource-backed copy,
permissions, workflow state, and service calls. Application CSS may define
page layout and product-specific composition using public semantic tokens, but
must not target private package markup, generated IDs, or internal
`shadcn-*` implementation structure.

Application-specific compositions are permitted when no single component
matches a workflow. They must be built from native HTML and public package
components, stay independent of MudBlazor APIs, and remain local unless the
behavior is demonstrably reusable across applications.

### Migration waves

The repository moves through independently buildable waves:

1. Package, provider, asset, namespace, inventory, and test-foundation cutover.
2. Shell, typography, icons, links, surfaces, layout, and shared components.
3. Buttons, icon actions, alerts, progress, skeletons, and other feedback.
4. Forms, typed inputs, numeric inputs, checkboxes, selects, date selection,
   field validation, and submission flows.
5. Navigation, breadcrumbs, lists, accordions, tabs, dialogs, and overlays.
6. Tables, responsive record presentation, charts, and data-heavy composites.
7. Feature-by-feature cleanup of Accounting, Quotations, Catalog,
   Procurement, Orders, Customers, Employees, and Diagnostics.
8. Removal of the embedded library projects and direct MudBlazor dependencies,
   followed by the zero-Mud completion audit.

Temporary coexistence is allowed only within an active migration wave. Each
committed wave must reduce the direct Mud inventory and leave the repository
buildable. The final state cannot retain a compatibility façade that mimics the
MudBlazor API.

## Component Mapping Rules

Each baseline usage is classified before replacement:

- Direct semantic replacement, such as Button, Alert, Progress, Skeleton,
  Checkbox, Select, Date Picker, Breadcrumb, Tabs, Table, or Chart.
- Native HTML plus Shadcn styling semantics for basic layout or typography
  where a package component would add no behavior.
- Application composition built from multiple package components for
  operational toolbars, responsive record rows, summaries, and workflow
  panels.
- Upstream library gap when required reusable behavior cannot be expressed
  through the installed public API without private selectors, copied internals,
  raw JavaScript duplicating package behavior, or loss of accessibility.

Replacement work preserves exact value types, nullability, bind/event timing,
validation messages, disabled and read-only semantics, navigation behavior,
and cancellation behavior. A visually similar replacement that changes a
business or interaction contract is not acceptable.

## Library Issue Protocol

When an apparent package defect or missing reusable capability blocks or
degrades a migration:

1. Confirm the behavior against package version 1.2.2 and the installed public
   API.
2. Check the upstream documentation, Showcase dossier, and open and closed
   issues for an existing report.
3. Reduce the problem to the smallest independent .NET 10 Blazor reproduction.
4. Record environment, package version, browser when relevant, exact Razor and
   C# code, reproduction steps, actual behavior, expected consumer behavior,
   accessibility or workflow impact, and any safe workaround.
5. Open or link the issue in `MALIEV-Co-Ltd/Maliev.ShadcnBlazor` and record the
   issue URL in the migration ledger.

Issue reporting does not authorize changing or publishing the library. The
Intranet may use a local application composition as a temporary workaround
only when it preserves behavior and does not depend on private library
internals.

## Testing and Evidence

Each wave follows this order:

1. Build every affected project in Release with zero warnings and zero errors.
2. Run focused component or contract tests that first demonstrate the previous
   behavior and then cover the replacement.
3. Run the complete `Legacy.Maliev.Intranet.Tests` suite.
4. Run formatting, static, accessibility, and inventory checks applicable to
   the changed files.
5. Run Playwright coverage for affected routes at desktop and mobile widths,
   with keyboard interaction, focus restoration, light and dark themes, and
   Thai content where relevant.

Authenticated browser validation uses the real BFF boundary with controlled
backend dependencies. If credentials or an authenticated environment are not
available, the exact blocked journey and residual risk are recorded; anonymous
or source-only evidence cannot be promoted as authenticated end-to-end proof.

The final audit includes a clean Release build, all Intranet tests, all
consumer browser tests, `git diff --check`, package-reference inspection,
namespace inspection, and a repository inventory that fails on direct Mud UI
usage in consumer projects.

## Commit Boundaries

Each migration wave is committed only after its applicable validation passes.
Commits contain only the files for that coherent wave and never include the
pre-existing untracked `.impeccable` critique or `.superpowers` artifacts.
Unrelated work is preserved. No commit is pushed and no deployment is
performed without separate explicit authorization.

## Deliberate Exclusions

- No API, DTO, JSON, database, messaging, authentication, authorization, or
  deployment contract changes.
- No redesign of business workflows merely because a new component permits it.
- No direct changes, releases, or pull requests to the upstream library.
- No adoption of unreleased upstream APIs without a separately approved
  versioning decision.
- No claim of complete browser coverage where authentication or environment
  prerequisites prevent the relevant journey.
