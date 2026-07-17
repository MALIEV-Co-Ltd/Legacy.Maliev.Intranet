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

During the Materials rollout, `LegacyEmployeeCompatibility:GrantCatalogMaterialsRead=true`
explicitly preserves the prior employee-wide read boundary after AuthService token validation.
JWT validation remains claim-faithful. Set the compatibility grant to `false` once AuthService
issues `legacy-catalog.materials.read`; the BFF policy then accepts only that validated token
claim. Sign-in and refresh rebuild the server-side cookie permissions from the validated token
plus the currently configured grant, so disabling the grant removes it on the next renewal.

Migration rules:

- preserve all 42 historical staff routes and validated workflows;
- use `Legacy.Maliev.AuthService` for employee authentication and keep access/refresh tokens server-side;
- call independently deployed legacy services through typed HTTP clients only;
- never reference employee or domain DbContexts;
- remove LoggerService, PredictionService, PayPal and certificate-validation bypasses;
- expose useful downstream failure UX and correlation IDs;
- deploy only to the existing GKE cluster in `maliev-legacy`, after the complete migration and staging gates pass.

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

The remaining 24 route workflows render an explicit migration state until their domain
workflow is fully wired and tested. This repository must not be deployed before all
of those route gates are complete.
