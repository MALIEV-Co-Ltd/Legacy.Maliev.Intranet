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
use disposable PostgreSQL databases. Unrelated BFF HTTP clients are blocked
with an in-process 503 handler; no notification, payment, or analytics call
may leave this disposable composition.

The third test renders the production WebAssembly client on a unique loopback
port in two isolated Chromium contexts. Each context forwards BFF requests to
its own real authenticated cookie client; responses are copied from the actual
BFF pipeline, never mocked. Employee sign-in uses real AuthService HTTP login
before navigation, not the rendered login form. Browser controls perform the
shared-revision edit sequence: A 204, stale B 412, preserved unsaved B draft and
persisted A winner, explicit reload, then B fresh-revision 204. It records and
checks the actual read ETags and write If-Match/status sequence. Static host,
browser, PostgreSQL, Redis, and test hosts are disposed after the run.

This is **not** full Aspire AppHost acceptance or browser-network cookie/TLS
parity: browser BFF transport uses TestServer and server-side cookie jars.
Production-derived data parity is not inferred. #197 remains open until a
separately isolated exact-SHA AppHost/browser runtime gate is recorded.

The project is outside the ordinary Intranet solution because it requires a
sibling CustomerService and AuthService checkouts. PR validation checks out
the audited producer SHAs and runs all three tests; ordinary solution builds do
not need those repositories. For this #197 slice, use Intranet base
`455b81ca`, CustomerService
`cebf45e8e1eeb600d760a565f8b0970c7f434148`, and AuthService
`8319b23e8ffab0c85b66c886a0109f7dac034c6f`. Prepare an isolated
workspace root containing source copies of `Legacy.Maliev.CustomerService`,
`Legacy.Maliev.AuthService`, `Legacy.Maliev.ServiceDefaults`, and
`Legacy.Maliev.CompatibilityContracts` at the workflow-pinned SHAs.
Build artifacts stay in those copies, not the canonical checkouts. Then run:

```powershell
dotnet build .\acceptance\CustomerRevision\CustomerRevision.AcceptanceTests.csproj -c Release -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot='<isolated-workspace-root>'
pwsh -NoProfile -File .\acceptance\CustomerRevision\bin\Release\net10.0\playwright.ps1 install chromium
dotnet test .\acceptance\CustomerRevision\CustomerRevision.AcceptanceTests.csproj -c Release --no-build -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot='<isolated-workspace-root>'
```

Do not infer production-derived data parity or close #197 solely from these
tests. Full AppHost orchestration, real browser-network cookie/TLS and deployment
configuration parity remain necessary. This rendered joined proof supplements
the existing CSRF, permission, invalid JWT and precondition/error tests; it does
not replace those tests or relax production authentication.
