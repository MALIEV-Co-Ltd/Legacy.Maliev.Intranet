# Legacy.Maliev.Intranet

Public .NET 10 migration target replacing the tightly coupled private legacy Intranet.

The approved employee UI architecture is being introduced alongside the compatibility
application so routes can move independently without a flag-day rewrite:

- `Legacy.Maliev.Intranet.Client` is the standalone Blazor WebAssembly employee UI and uses MudBlazor;
- `Legacy.Maliev.Intranet.Bff` is the same-origin cookie, CSRF, authorization, proxy, upload, and SignalR boundary;
- `Legacy.Maliev.Intranet.Contracts` contains browser-safe BFF DTOs only;
- `Legacy.Maliev.Intranet` remains the Razor Pages compatibility host until every route passes parity gates.

Service credentials, access/refresh tokens, and session tickets must never enter the WASM
client. The BFF must remain free of domain business logic and must authorize every server
endpoint. Razor routes are removed only after route, authorization, DTO, and browser parity
are proven for the replacement feature assembly.

The standalone BFF Materials rollout requires `Legacy.Maliev.AuthService` commit `647c21c6`
or later, which issues `legacy-catalog.materials.read` on validated employee login and refresh
tokens. `LegacyEmployeeCompatibility:GrantCatalogMaterialsRead` defaults to `false`; set it to
`true` only as an emergency rollback to the prior employee-wide read boundary. JWT validation
remains claim-faithful, and sign-in/refresh rebuild server-side cookie permissions from the
validated token plus only that explicitly configured rollback grant. Catalog calls still use
the separate least-privilege `legacy-intranet` service token and never forward employee tokens.

The lazy `/Customers/Index` rollout requires `Legacy.Maliev.AuthService` commit `aa75fd55`
or later, which issues the canonical `legacy-customer.customers.list` permission on employee
login and refresh JWTs. The BFF requires that exact claim on the opaque employee cookie and
replaces the employee token with the separate `legacy-intranet` service token before calling
CustomerService. AppHost grants the same exact list permission to that service identity; neither
the employee access token nor the machine credential is exposed to WebAssembly.

The lazy `/Customers/Create` rollout requires `Legacy.Maliev.AuthService` commit
`b4a51aa19a712fcc10c87ef8ba36fb63ae1d32df` or later, which issues the employee cookie's
exact `legacy-customer.customers.create` permission and accepts the identity-creation POST only
from a service identity with `legacy-auth.customer-identities.create`. It also requires
`Legacy.Maliev.AppHost` commit `8ffded2e1470af47cca9de25fa6680e11cc7abda` or later, which grants
that Auth permission only to `legacy-intranet`. The BFF sends passwords only in the AuthService
JSON body, never the customer profile, URL, response, or browser-visible service credential.

The lazy `/Customers/View` rollout requires `Legacy.Maliev.AuthService` commit
`dd4eff994116bb41ca0a0a1947aeb81241a8e196` or later, which issues the employee cookie's
exact `legacy-customer.customers.read` permission on login and refresh. `Legacy.Maliev.AppHost`
grants the same read permission to the separate `legacy-intranet` service identity. The BFF
validates the complete CustomerService detail projection and never exposes either token to
WebAssembly.

Migration rules:

- preserve all 41 historical staff routes and validated workflows (39 active and 2 intentionally retired);
- use `Legacy.Maliev.AuthService` for employee authentication and keep access/refresh tokens server-side;
- call independently deployed legacy services through typed HTTP clients only;
- never reference employee or domain DbContexts;
- remove LoggerService, PredictionService, PayPal and certificate-validation bypasses;
- expose useful downstream failure UX and correlation IDs;
- deploy only to the existing GKE cluster in `maliev-legacy`, after the complete migration and staging gates pass.

Delivery produces separate immutable images for the Razor Pages compatibility host and the
same-origin BFF. Both workloads are namespace-confined, non-root, project the shared runtime
secret only at runtime, and remain behind the explicit publish and GitOps gates.

The customer, employee, material, supplier, purchase-order, and order-list domains are completed typed workflow slices:

- `/Customers/Index` preserves search, sorting, bounded pagination, and profile links;
- `/Customers/View` reads the profile projection from CustomerService;
- `/Customers/Create` creates the profile in CustomerService and the identity in AuthService, sends the password only in JSON, and compensates by deleting the profile if identity creation fails.
- `/Employees/Index` preserves employee search, sorting, bounded pagination, and profile links;
- `/Employees/View` reads profile, role, and address projections from EmployeeService;
- `/Employees/Create` creates the profile in EmployeeService and the identity directly in AuthService, sends the password only in JSON, and compensates by deleting the profile if identity creation fails.
- `/Materials/Index` preserves material search, sorting, and bounded pagination through CatalogService;
- `/Materials/Create` preserves the complete material property payload and Catalog reference lookups;
- `/Materials/View` edits the complete material and differentially synchronizes color and surface-finish associations.
- `/Suppliers/Index` preserves supplier search, sorting, and bounded pagination through ProcurementService;
- `/Suppliers/Create` idempotently creates a supplier and its owned address with compensating rollback;
- `/Suppliers/View` updates supplier/address data and performs deletion through a CSRF-protected POST.
- `/PurchaseOrders/Index` preserves search, sorting, pagination, and employee display names;
- `/PurchaseOrders/Create` idempotently creates the order and line items, renders the bilingual document through the QuestPDF DocumentService, passes the PDF through FileService quarantine and malware scanning before GCS promotion, then records ProcurementService file metadata;
- `/PurchaseOrders/View` reads the complete order, line items, employee/supplier projections, and clean-object signed URLs, while deletion uses a CSRF-protected POST and removes dependants before the parent;
- `/Orders/Index` preserves search, supported sorting, bounded pagination, the pending working set, employee/process labels, and the signed-in employee's assignment view through OrderService;
- `/Orders/Create` validates the customer and complete order payload, creates the initial status idempotently, streams attachments through FileService quarantine/malware scanning, compensates partial writes, and optionally sends the modern NotificationService confirmation;
- `/Orders/View` preserves complete edits with ModifiedDate concurrency, permitted status transitions, Accepted cancellation locking, signed clean-file downloads/removal, and QuestPDF order-label rendering;
- failed create workflows compensate in reverse order so metadata, cloud objects, line items, and the parent order are not orphaned.

Remaining active route workflows render an explicit migration state until their domain
workflow is fully wired and tested. `/Travelers/Create` and `/Travelers/Index` are retained
only in the historical inventory and return `410 Gone`: the legacy PageModels were inert
stubs, and no Traveler entity, repository, controller, DTO, persistence, or wire contract
exists to migrate. Any future manufacturing-traveler capability requires a separately
designed bounded context; it is intentionally not fabricated in this legacy migration.
