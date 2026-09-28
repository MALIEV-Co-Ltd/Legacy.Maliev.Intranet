# Customer revision joined-boundary acceptance

This opt-in test project hosts the Intranet BFF endpoint map and the actual
CustomerService controllers, application service, and repository against a
Testcontainers PostgreSQL database. Two independent authenticated BFF cookie
clients read one ETag; one edit wins, the stale edit receives 412 without
altering the stored profile, and a reload with a fresh ETag succeeds. It also
checks CSRF, precondition, permission, and anonymous failure paths. The
CustomerService transport uses a test-only service identity; employee login is
isolated in memory. No AuthService, Redis, persistent Aspire database, or
external side-effect service is contacted. This is joined producer/consumer
contract evidence, **not** full running-AppHost acceptance.

The project is outside the ordinary Intranet solution because it requires a
sibling CustomerService checkout. The PR validation workflow explicitly checks
out the audited producer SHA and runs this test; ordinary solution builds do
not need the producer repository. For the #197 exact-revision local run, use
Intranet `7378a4b65ad6b7023b5baa53839772bb51a1e687` and CustomerService
`def78a74147e4e6fad6b6d4cc35e07fae16fa5f8`; verify both SHAs before
testing. Prepare an isolated workspace root containing read-only
source copies of `Legacy.Maliev.CustomerService`,
`Legacy.Maliev.ServiceDefaults`, and `Legacy.Maliev.CompatibilityContracts`.
Build artifacts stay in those copies, not the canonical checkouts. Then run:

```powershell
dotnet build .\acceptance\CustomerRevision\CustomerRevision.AcceptanceTests.csproj -c Release -p:MalievWorkspaceRoot='<isolated-workspace-root>'
dotnet test .\acceptance\CustomerRevision\CustomerRevision.AcceptanceTests.csproj -c Release --no-build -p:MalievWorkspaceRoot='<isolated-workspace-root>'
```

Do not infer production-derived data parity or close #197 solely from this
test. A separately isolated running-app test with AuthService-issued sessions,
service login, Redis/DataProtection, and disposable PostgreSQL is still needed.
