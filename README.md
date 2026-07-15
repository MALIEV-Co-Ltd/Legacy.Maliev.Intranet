# Legacy.Maliev.Intranet

Public .NET 10 server-side BFF replacing the tightly coupled private legacy Intranet.

Migration rules:

- preserve all 42 historical staff routes and validated workflows;
- use `Legacy.Maliev.AuthService` for employee authentication and keep access/refresh tokens server-side;
- call independently deployed legacy services through typed HTTP clients only;
- never reference employee or domain DbContexts;
- remove LoggerService, PredictionService, PayPal and certificate-validation bypasses;
- expose useful downstream failure UX and correlation IDs;
- deploy only to the existing GKE cluster in `maliev-legacy`, after the complete migration and staging gates pass.

The customer domain is now the first completed typed workflow slice:

- `/Customers/Index` preserves search, sorting, bounded pagination, and profile links;
- `/Customers/View` reads the profile projection from CustomerService;
- `/Customers/Create` creates the profile in CustomerService and the identity in AuthService, sends the password only in JSON, and compensates by deleting the profile if identity creation fails.

The remaining protected routes render an explicit migration state until their domain
workflow is fully wired and tested. This repository must not be deployed before all
of those route gates are complete.
