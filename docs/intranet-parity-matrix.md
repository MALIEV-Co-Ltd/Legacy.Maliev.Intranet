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
| `/sales/orders/{id}` | Legacy order detail redirect | Numeric compatibility route redirects to `/Orders/View?id={id}`; no new API contract |
| `/purchasing/{id}`, `/mfg/procurement/{id}` | Legacy purchase-order detail redirect | Numeric compatibility routes redirect to `/PurchaseOrders/View?id={id}`; no GUID translation |

Historical PascalCase routes remain supported and are tracked by
`LegacyRoutes.All`. The aliases above are loaded through the same lazy feature
assemblies and do not create a second API contract.

## Still requiring a real domain migration before cutover

The following routes exist in the current `Maliev.Intranet` source but do not
have an equivalent legacy service contract yet. They must not be silently
pointed at an unrelated legacy page:

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

The 2026-07-26 validation slice also proves that authenticated-but-unauthorized
routes render `AccessDenied`, anonymous routes redirect to the BFF login flow,
and unknown routes render the accessible `NotFound` page. The BFF session
projection preserves distinct server-issued `permissions` claims without
exposing tokens. These are compatibility safeguards, not evidence that the
unresolved current-only domains above have been migrated.

## Employee identity and data safety gates

- Login remains same-origin `/bff/login` with CSRF and the existing AuthService
  credentials; access/refresh tokens stay in the server-side distributed ticket.
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
