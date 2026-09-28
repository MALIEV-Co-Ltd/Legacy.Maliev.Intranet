# Customer revision joined-boundary acceptance

This opt-in test project hosts the Intranet BFF endpoint map and the actual
CustomerService controllers, application service, and repository against a
Testcontainers PostgreSQL database. The original test checks the exact
producer/consumer contract with test-only employee identities, including CSRF,
precondition, permission, and anonymous failure paths. The additional test
hosts the pinned real AuthService API, seeds two disposable employee identities,
uses its HTTP login and service-login endpoints, and persists BFF tickets and
Data Protection keys in a disposable Redis container. Its CustomerService
host registers the production `AddJwtAuthentication` RSA validator and
permission policy/handler from pinned ServiceDefaults, not the synthetic
`TestAuthenticationHandler` or its permissive Testing validator. AuthService
issues the service token used by the BFF and direct seed/read checks. It proves
two distinct AuthService refresh sessions and Redis-backed cookies, shared
ETag, winning write, stale 412 without overwrite, and successful fresh
reload/write. Anonymous, spoofed-header, forged-signature, read-only service
token, CSRF, and malformed/missing revision paths are denied. Auth and Customer
use disposable PostgreSQL databases; this minimal TestServer composition does
not register outbound notification, payment, or analytics clients.

This is **not** full Aspire AppHost or rendered browser acceptance. No
production-derived data parity is inferred. #197 remains open until an
isolated exact-SHA AppHost/browser run with the two-employee UI reload
sequence and disabled outbound integrations is recorded.

The project is outside the ordinary Intranet solution because it requires a
sibling CustomerService and AuthService checkouts. PR validation checks out
the audited producer SHAs and runs both tests; ordinary solution builds do
not need those repositories. For this #197 slice, use Intranet base
`1dd57cc3ad2d9dd5d91a2e203b5b10f190716129`, CustomerService
`cebf45e8e1eeb600d760a565f8b0970c7f434148`, and AuthService
`8319b23e8ffab0c85b66c886a0109f7dac034c6f`. Prepare an isolated
workspace root containing source copies of `Legacy.Maliev.CustomerService`,
`Legacy.Maliev.AuthService`, `Legacy.Maliev.ServiceDefaults`, and
`Legacy.Maliev.CompatibilityContracts` at the workflow-pinned SHAs.
Build artifacts stay in those copies, not the canonical checkouts. Then run:

```powershell
dotnet build .\acceptance\CustomerRevision\CustomerRevision.AcceptanceTests.csproj -c Release -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot='<isolated-workspace-root>'
dotnet test .\acceptance\CustomerRevision\CustomerRevision.AcceptanceTests.csproj -c Release --no-build -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot='<isolated-workspace-root>'
```

Do not infer production-derived data parity or close #197 solely from these
tests. A separately isolated full AppHost/browser run with real CustomerService
service-JWT validation and UI interaction remains necessary.
