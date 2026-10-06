# Order detail material-dependent finishes

Bounded implementation for [Intranet issue 264](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/issues/264). This is an authored, unexecuted source draft, not migration acceptance or a whole historical commit closure.

## Source and compatibility boundary

Original checkpoint `135e526d0dab85c415b3afdcefd7b70fe2c82e2f` retains initial commit `5fac706a7983a6d359b39acbd670e6800afe020e` (no parents). Source paths:

- `Maliev.Intranet/Pages/Orders/View.cshtml`, blob `dee307eea0576966a72e5632b84664721f4a361d`: material selection invokes `GetFinish`.
- `Maliev.Intranet/Pages/Orders/View.cshtml.cs`, blob `1557f70790436f6192d6c1f58340276cafb003e2`: `OnGetGetFinishesAsync` reads `/materials/{materialId}/surfacefinishes/`.

The original detail handler scopes surface finishes, not colors. This implementation preserves global color options/selection and initial material/finish selections; it does not copy the create form's color filtering/reset behavior. On a successful material-change read only an incompatible finish is cleared. Explicitly clearing material clears finish without a catalog request. Failure preserves the previous finish, blocks order save, and offers an explicit retry. An empty successful list is valid and clears the incompatible finish.

The existing `/bff/catalog/materials/{id:int}/surface-finishes` returns `CatalogMaterialSurfaceFinish(Id, Name)` and requires `legacy-catalog.materials.read`, already required by the initial order-detail aggregation. No new grants, browser credentials, BFF routes, producer changes, or `OrdersCreate` dependency are introduced. The create/materials endpoint is deliberately not used because it requires `OrdersCreate`.

## Lifecycle and test boundary

Material reads have the existing 15-second presentation deadline covering deserialization, caller cancellation, a generation/material/order fence, and disposal cancellation. Responses and cancellation sources are disposed. Unknown material choices and malformed/null/duplicate/invalid-ID payloads fail closed. The finish control and order save are disabled while loading or failed; a synthetic form submission cannot bypass that guard. Other existing status/file operations are unchanged.

`OrderDetailMaterialChoicesUiTests` renders the actual Razor page through Router/RouteView and real Shadcn components. Only same-origin HTTP transport is controlled. Forecast: 14 cases covering initial custom selection/PUT mapping, compatible and incompatible scoped finishes, valid empty scoped choices with an explicit saved null finish, null material, unknown material, six malformed/error variants with explicit retry, overlapping responses/cancellation, and disposal. Held-response tests release their controlled tasks in `finally`; no live service is used.

These tests do not prove real BFF authorization, Catalog runtime/storage, browser interaction, order concurrency, or downstream write acceptance. No broader initial-page or in-flight-save navigation guarantee is added. Root owns the direct Orders feature test-project reference and hosted validation. Required before acceptance: zero-warning/error build, these actual bUnit cases, affected full suite, formatting/static checks, and appropriate real BFF/Catalog boundary evidence.

## Current validation and cleanup

The focused hosted workflow builds Release with warnings as errors before executing only this real component class. Its retainer requires fourteen passing execution identities, the exact method cardinalities and all sixteen TRX counters, binds the nine slice/gate paths to the tested Git revision, and exports only sanitized outcomes and hashes. Shared theory test IDs do not substitute for unique executions. Raw parameters/output are not retained. This focused proof does not replace the full suite, coverage, formatting or real BFF/Catalog acceptance.

Root independently executed the finite offline retainer controls: 24 tests passed, including shared-ID binding, missing/extra/failed/skipped results, counter drift, source/revision mismatch and redaction checks. XML resource/project parsing, transferred-content continuity and `git diff --check` passed. Those controls use synthetic evidence solely to test rejection behavior; they are not native bUnit execution or migrated-data proof. The isolated checkout is based on accepted main `08a92177fcb4840d27e08edaccaa88c6b90ddb45`.

Only source/diff inspection and structural static checks are permitted in this authoring task. No local SDK, test, build, container, browser, provider, database, or deployment operation was executed. No commit or push was created. No background process, helper, container, or browser worker was created; there are no owned live resources to clean up. Employee work and all non-owned paths are preserved.
