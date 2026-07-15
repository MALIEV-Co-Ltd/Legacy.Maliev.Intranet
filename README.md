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

Protected routes currently render an explicit migration state until their domain workflow is fully wired and tested. This repository must not be deployed before those route gates are complete.
