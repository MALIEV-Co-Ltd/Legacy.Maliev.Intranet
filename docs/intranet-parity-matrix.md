# Legacy Intranet parity matrix

This matrix is the cutover checklist for the .NET 10 Blazor migration. It is
generated from the current source route inventory in `B:\maliev\Maliev.Intranet`
(baseline `d8e943b`) and the legacy route/feature inventory in this repository.
It deliberately distinguishes a tested compatibility alias from a domain that
has not been migrated; an alias must never be used to make an unrelated page
look complete.

## Mapped to an existing, tested legacy workflow

| Current workspace route | Legacy owner | Boundary evidence |
| --- | --- | --- |
| `/customers`, `/sales/customers` | Customers list | Customers feature assembly + `/bff/customers` |
| `/customers/new`, `/sales/customers/new` | Customer create | Customer creation workflow + CSRF-protected BFF |
| `/mfg/materials` | Materials list | Catalog feature assembly + `/bff/catalog/materials` |
| `/sales/orders` | Orders list | Orders feature assembly + `/bff/orders` |
| `/purchasing`, `/mfg/procurement` | Purchase-order list | Procurement feature assembly + `/bff/purchase-orders` |
| `/purchasing/new` | Purchase-order create | Idempotent, CSRF-protected PO workflow |
| `/purchasing/suppliers` | Supplier list | Procurement feature assembly + `/bff/suppliers` |
| `/accounting`, `/finance/invoices` | Invoice list | Accounting feature assembly + `/bff/invoices` |
| `/accounting/new` | Invoice create | Accounting creation workflow + `/bff/invoices/from-quotation` |
| `/hr/profile` | Read-only employee profile | Employees feature assembly + `/bff/session` + `/bff/employees/{legacyDatabaseId}`; self-service edits/preferences remain explicitly disabled |
| `/purchasing/{id}`, `/mfg/procurement/{id}` | Legacy purchase-order detail redirect | Numeric compatibility routes redirect to `/PurchaseOrders/View?id={id}`; no GUID translation |

Historical PascalCase routes remain supported and are tracked by
`LegacyRoutes.All`. The aliases above are loaded through the same lazy feature
assemblies and do not create a second API contract.

## Still requiring a real domain migration before cutover

The following routes exist in the current `Maliev.Intranet` source but do not
have an equivalent legacy service contract yet. They must not be silently
pointed at an unrelated legacy page:

- `/sales/orders/{OrderId}` (the current route is unconstrained; the legacy redirect only accepts a proven numeric identifier)
- `/sales/projects`, `/sales/projects/new`, `/sales/projects/{id}`
- `/commerce/catalog`, `/commerce/catalog/new`, `/commerce/catalog/{handle}`, `/commerce/collections`
- `/finance/delivery-notes`, `/finance/delivery-notes/new`, `/delivery-notes`
- `/mfg/equipment`, `/mfg/equipment/{id}`, `/mfg/production-schedule`
- `/hr/leave` (the profile read projection is now mapped; self-service profile edits/preferences still require the current EmployeeService contract)
- `/iam`, `/iam/users/new`, `/iam/users/{id}`, `/iam/roles/{id}`
- `/admin`, `/admin/web-content`, `/admin/chatbot-instructions`, `/admin/reference-data`, `/admin/system-health`
- `/search`
- `/sales/contact-requests` and `/sales/contact-requests/{id}` (the legacy quotation-request contract is a different persisted model)

These gaps are cutover blockers for a claim of full current-workspace parity.
The legacy shell intentionally exposes only the migrated workflows until the
corresponding service, permission, DTO, database, and browser contract tests
are available.

## Latest validation checkpoint (2026-07-27)

- Branch: `main` (current local HEAD `f88b763`, built on validation merge `1a96322`); the parity branch now also contains
  browser-safe Maps configuration (`e70253f`) and nonce-bound Google employee
  sign-in (`d410b4c`, sourced from AuthService `8911dcc`). AppHost wires the
  optional local Google client/hosted-domain values and the dedicated
  `legacy-auth.google-identity.exchange` permission (`da3c0dd`, with the
  formatter-only follow-up `259fbee`).
- The current-source route baseline is machine-checked in
  `docs/current-intranet-route-parity.json` at `Maliev.Intranet` commit
  `d8e943b`: 53 current routes are classified exactly once (18 exact legacy
  owners, 35 explicit blockers). When `MALIEV_CURRENT_INTRANET_ROOT` is set,
  the contract test compares the manifest with the actual current Razor route
  directives; it never treats an absent current checkout as proof of parity.
- The WASM shell now matches the current loading/error behavior and branding
  assets, including keyboard focus, reduced-motion, forced-colors, reload, and
  dismiss states. The employee gateway now also preserves the current
  two-step corporate-email/password flow, nonce-bound Google sign-in host,
  Remember Me request, local theme bootstrap, safe return-url handling, and
  responsive/reduced-motion/forced-colors states.
- `/hr/profile` is a protected, lazy-loaded read projection backed by the
  existing `/bff/session` and `/bff/employees/{legacyDatabaseId}` contracts;
  self-service edits remain fail-closed until the owning EmployeeService
  preference contract is migrated.
- The legacy workspace navigation now exposes the 24 legacy-owned CRM/ERP
  workflows (including create routes and employee profile/server errors) and
  intentionally omits current-only project, commerce, delivery-note, IAM,
  and administration links until their contracts are migrated. The Orders
  page now has a tested Refresh action that reuses the existing bounded
  `/bff/orders` load path.
- Employee password sign-in now validates the `@maliev.com` boundary on both
  browser and BFF sides, revalidates the identity returned by AuthService, and
  carries Remember Me through the opaque server-side session ticket. Supported
  cultures are normalized to `en-TH`, `th-TH`, or `en-US`.
- `dotnet test Legacy.Maliev.Intranet.slnx -c Release --no-build --no-restore`
  passed **551/551** with zero skips after the navigation, Orders Refresh, and
  WASM runtime/login interop slices; focused navigation passed **2/2**, Orders
  passed **4/4**, and the runtime/login contract subset passed **4/4**. The
  clean Release build passed with **0 warnings and 0 errors** after stopping
  the serving BFF, and whitespace/diff verification passed. AuthService passed
  **107/107** and FileService passed
  **28/28** after pinning `Google.Apis.Auth` to `1.75.0` in the storage data
  adapter (`2fffcbe`), removing the AppHost migration-runner assembly
  conflict. AppHost passed **88/88** and its Release build passed with **0
  warnings and 0 errors**. The FileService checkout used by this local
  validation is `main` at `2fffcbe`; fetched `origin/main` is already at
  `bd7cd33` with 43 newer delegated upload/Instant-Quotation commits. Those
  commits remain owned by the FileService/Web lanes and are not claimed as
  integrated into this Intranet validation.
- Aspire local validation currently reports **17 running executable resources**
  (14 legacy APIs, Intranet BFF, Web, and the dashboard), with all **22 DCP
  service resources Ready** and all **19 migration runners Finished with exit
  code 0**. The 14 API resources' `aspire-liveness`, `liveness`, and
  `readiness` probes returned HTTP 200 (**42/42**); the BFF liveness/readiness
  probes also returned HTTP 200. The Web proxy
  (`http://localhost:5188/Account/Login`), BFF login
  (`https://localhost:58513/Login`), and dashboard
  (`http://localhost:15888`) returned HTTP 200. The fresh browser login tab
  had no console warnings/errors and no horizontal overflow at desktop
  (1,280px) or mobile (390px) width. Google nonce exchange is deliberately
  HTTP 503 until a local client ID is supplied, while password sign-in remains
  available; the Maps config endpoint is HTTP 401 without the required
  employee permission.
- The WASM client now explicitly loads all globalization data when
  `InvariantGlobalization` is false, preventing the supported `en-TH`/`th-TH`/
  `en-US` culture switch from crashing the browser. Login Google interop is
  guarded until the host is rendered and retries fail-closed, preventing the
  empty `ElementReference` startup race.
- This is local evidence only. No production/GKE deployment, database write,
  credential reuse, or legacy PostgreSQL cutover was performed.

The current migration readiness validator accepts the checked-in restore and
copy receipts (`evidenceValid=true`) and still reports `cutoverAuthorized=false`.
The verified gates are limited to source-backup integrity and initial-copy
parity. Existing-cluster capacity, CNPG backup/WAL, recovery drill, shadow-read
parity, final-sync parity, rollback rehearsal, owner Aspire review, and service
owner approval remain open. The legacy Intranet BFF is intentionally database
free; twelve data-bearing legacy services have PostgreSQL/Testcontainers
migration coverage, but no live GCS restore, `legacy-postgres-main-*` schema/
row/key reconciliation, or service-to-database shadow-read proof has been run
for this cutover.

The source-history review also found current-only authorization work after the
legacy baseline (`d8e943b`, `008ba29`, `c79885f`, `fbf91c8`, `c3bed92`, and the
recent IAM hardening commits). Those changes protect live employee analytics,
profile reads, dashboard/project reads, and IAM bindings; they are not covered
by the legacy int-based contracts yet. Each permission and resource-ownership
decision must be ported and tested before the workspace can be called
behavior-compatible.

The 2026-07-26 validation slice also proves that authenticated-but-unauthorized
routes render `AccessDenied`, anonymous routes redirect to the BFF login flow,
and unknown routes render the accessible `NotFound` page. The BFF session
projection preserves distinct server-issued `permissions` claims without
exposing tokens. These are compatibility safeguards, not evidence that the
unresolved current-only domains above have been migrated.

## Employee identity and data safety gates

- Login remains same-origin `/bff/login` with CSRF and the existing AuthService
  credentials; access/refresh tokens stay in the server-side distributed ticket.
- Google Identity Services is an optional, nonce-bound employee sign-in path:
  the BFF consumes the protected nonce cookie before exchanging the credential,
  AuthService verifies audience/hosted-domain/nonce and active employee status,
  and the browser receives only the normal server session. Missing local
  client-id configuration fails closed while password sign-in remains available.
- Google Maps configuration is exposed only through the authenticated,
  `CustomersRead`-scoped `/bff/address/google-config` endpoint. The response
  contains the browser-restricted key and map defaults only; embed/server keys
  are never sent to WASM.
- Every BFF read/write is authenticated and permission-scoped; browser WASM
  never receives service credentials or database access.
- Dashboard counts are permission-scoped and degrade per downstream source;
  raw downstream responses are not returned to the browser.
- No production/GKE or legacy PostgreSQL cutover is authorized by this matrix.

## Required evidence before production approval

1. Current-source route/feature history review for each unresolved route.
2. Contract tests for every BFF endpoint and DTO, including live permission
   checks and refresh/session rotation.
3. Testcontainers/Postgres compatibility checks against the backed-up legacy
   schema, with row-count and key-set reconciliation before any write.
4. Aspire browser journeys at desktop/tablet/mobile widths using a real
   employee test account; no production credential should be copied into the
   browser.
5. Owner review of the remaining current-domain migrations, followed by a
   separate GKE deployment change.
