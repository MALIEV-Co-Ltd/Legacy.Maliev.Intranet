# Disposable employee recovery joined acceptance

This opt-in project runs the production Intranet BFF and retained host delivery
helper against the real merged AuthService API, service-login issuer, JWT bearer
validation and permission pipeline. The production typed HTTP registrations,
ServiceAccessTokenProvider and LegacyServiceAuthenticationHandler stay intact.
Only their primary transports are bridged to Auth TestServer or to the local
controlled notification transport; every unrelated outbound client is blocked.
No fake authentication handler, provider credential, live account, notification
service or external delivery is used.

One PostgreSQL18 Testcontainers fixture supplies separate employee, customer
and Auth-state databases per case. Reviewed migrations run only in those
disposable databases. The production worker timer is suspended to preserve
deterministic HTTP fault windows; separate cases call its actual reconciler,
including immutable-owner mismatch rejection. This is not a claim about timer
scheduling or hosted-worker lifecycle (covered in the producer suite).

The accepted producer revision is `a1fb10334f8504a4bf441bde5a2420f4fa686d43`.
Joined ServiceDefaults is `5c5f9479313710fa576f83d3b396442997a2fcf4`, Contracts
is `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. These are separate from the
ordinary Intranet solution's existing CI Defaults pin `d22f0e6f95254b10cf4fe891c8dce5df7c419f3f`.
The preparation tool verifies exact clean committed clones under ignored
`TestResults/.dependencies/recovery-chain`; it never resets mismatching/dirty
clones, fetches arbitrary branch heads, or builds canonical sources.

```powershell
pwsh -NoProfile -File scripts/prepare-employee-recovery-acceptance.ps1
$root = Join-Path (Get-Location) 'TestResults/.dependencies/recovery-chain'
dotnet build acceptance/EmployeeRecovery/EmployeeRecovery.AcceptanceTests.csproj `
  -c Release --artifacts-path TestResults/recovery-chain-build `
  -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=$root -m:1
dotnet test acceptance/EmployeeRecovery/EmployeeRecovery.AcceptanceTests.csproj `
  -c Release --no-build --no-restore --artifacts-path TestResults/recovery-chain-build `
  -p:MalievWorkspaceRoot=$root --logger 'trx;LogFileName=recovery-chain.trx' `
  --results-directory TestResults/recovery-chain
```

Cases exercise service JWT subject/permissions, both recovery purposes, actual
anonymous CSRF/rate gates, safe delivery/callback serialization, terminal replay,
cross-host stable owner, actual employee cookie versus separate recovery target,
wrong owner/email/purpose/payload, physical readiness and opt-in failure503,
unapplied-save failure without challenge burn, committed pending receipt and
same-password retry without a second identity effect, receipt completion after
original expiry, generation-bound/explicit-NULL revocation, actual reconciler
and mismatch rejection, and controlled-clock expiry while a real identity row
lock is held. No sleeps establish correctness windows.

The cookie case seeds a synthetic workspace-domain identity solely inside the
container to pass the unchanged domain policy. The recovery target uses reserved
`maliev.test`; all notification traffic stays in the controlled transport.
The retained host case exercises its actual delivery helper/HTTP registration,
not its anonymous Razor callback placeholder. It does not certify that placeholder
as functional recovery or functional rollback.

This is joined **component** evidence: TestServer transport, ephemeral Testing
Data Protection, synthetic data, controlled delivery and injected deterministic
faults. It is not production-derived full Aspire acceptance, browser-network
cookie/TLS parity, or real provider delivery. Intranet #229/Auth #108 remain open
for those gates. No UI implementation, invoice-delegation changes, deployment,
persistent schema/data application or production recovery activation belongs to
this slice. This project remains outside the ordinary solution; CI integration
is an explicit follow-up and does not repin unrelated acceptance projects.
