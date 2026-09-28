# Customer revision joined-boundary acceptance

This opt-in test project hosts the Intranet BFF endpoint map and the actual
CustomerService controllers, application service, and repository against a
Testcontainers PostgreSQL database. The original test checks the exact
producer/consumer contract with test-only employee identities, including CSRF,
precondition, permission, and anonymous failure paths. The additional test
hosts the pinned real AuthService API, seeds two disposable employee identities,
uses its HTTP login and service-login endpoints, and persists BFF tickets and
Data Protection keys in a disposable Redis container. It proves two distinct
AuthService refresh sessions and Redis-backed cookies, shared ETag, winning
write, stale 412 without overwrite, and successful fresh reload/write. The
CustomerService authorization transport remains test-only; its controller,
application, repository, and PostgreSQL persistence are real. This is a
running multi-service TestServer composition, **not** full Aspire AppHost or
browser acceptance.

The project is outside the ordinary Intranet solution because it requires a
sibling CustomerService and AuthService checkouts. PR validation checks out
the audited producer SHAs and runs both tests; ordinary solution builds do
not need those repositories. For this #197 slice, use Intranet base
`374bd8aca80018d4b159889913c10c0154214852`, CustomerService
`cebf45e8e1eeb600d760a565f8b0970c7f434148`, and AuthService
`8319b23e8ffab0c85b66c886a0109f7dac034c6f`. Prepare an isolated
workspace root containing source copies of `Legacy.Maliev.CustomerService`,
`Legacy.Maliev.AuthService`, `Legacy.Maliev.ServiceDefaults`, and
`Legacy.Maliev.CompatibilityContracts` at the workflow-pinned SHAs.
Build artifacts stay in those copies, not the canonical checkouts. Then run:

```powershell
dotnet build .\acceptance\CustomerRevision\CustomerRevision.AcceptanceTests.csproj -c Release -p:MalievWorkspaceRoot='<isolated-workspace-root>'
dotnet test .\acceptance\CustomerRevision\CustomerRevision.AcceptanceTests.csproj -c Release --no-build -p:MalievWorkspaceRoot='<isolated-workspace-root>'
```

Do not infer production-derived data parity or close #197 solely from these
tests. A separately isolated full AppHost/browser run with real CustomerService
service-JWT validation and UI interaction remains necessary.
