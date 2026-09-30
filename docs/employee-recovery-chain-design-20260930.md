# Employee recovery consumer chain — test-first gate

## Ownership and immutable inputs

Issue: Intranet #229, with Auth #108 remaining the full acceptance gate.
Worktree: `intranet-employee-recovery-chain-20260930`; branch:
`codex/employee-recovery-chain-20260930`. Base Intranet:
`d55af0a55379fc52c4250d43cc1af6e0eb016e5d`.
Approved merged producer revision: `a1fb10334f8504a4bf441bde5a2420f4fa686d43`.
Ordinary Intranet baseline dependencies match its existing CI:
ServiceDefaults `d22f0e6f95254b10cf4fe891c8dce5df7c419f3f` and Contracts
`78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`.
All dependency outputs belong to ignored clones under this worktree's
`TestResults`, never canonical or another agent's producer checkout.

No runtime changes are authorized until root reviews terminal baseline results,
observed RED, and the structured outcome below. No commits, pushes, live
notification, persistent schema/data application, or deployment are authorized.
The canonical three untracked entries are unchanged.

## Proposed source-versus-delivery distinction

The confirmation request helper should return a structured internal result,
not overload `bool` to mean both source availability and message delivery:

```text
EmployeeConfirmationRequestStatus: Accepted | SourceUnavailable | SourceThrottled
EmployeeConfirmationRequestResult: Status, EmailSent, RetryAfter
```

- Auth HTTP success with `accepted=true` and no challenge: Accepted/false;
  anonymous resend returns the existing generic 202, identical to known users.
- Auth HTTP success with an available challenge: one notification attempt.
  Successful delivery: Accepted/true. Notification non-success or non-caller
  transport timeout: Accepted/false. Anonymous response is the same 202 in both
  cases and never exposes an account-existence distinction or delivery status.
- Auth non-success other than 429, non-caller transport failure, malformed JSON,
  missing envelope, or `accepted=false`: SourceUnavailable/false; resend returns
  generic 503. It never returns provider error text, token, or email.
- Auth 429: SourceThrottled/false; resend returns the existing recovery 429
  contract and only forwards a positive delta Retry-After no greater than one
  hour. Date Retry-After, invalid or excessive values are omitted.
- Caller-requested cancellation must propagate as cancellation from challenge
  acquisition or notification delivery. It is neither generic accepted nor an
  infrastructure error. Timeout without caller cancellation remains the
  appropriate source503 or masked notification202.

`SendEmailConfirmationAsync` can retain the creation call site's bool-returning
facade, but obtain `EmailSent` from the structured helper. Existing creation
continues to report `confirmationEmailSent=false` after unsuccessful delivery;
do not compensate an already created identity or claim successful delivery.
Resend uses Status, not that bool. Reset and completion cancellation catches
must follow the same caller-abort rule without introducing HTTP retries.

## RED tests and planned files

Initial test ownership: `Legacy.Maliev.Intranet.Tests/BffEmployeeRecoveryContractTests.cs`.
It covers Auth503/401/403/transport, malformed/false accepted envelopes,
caller cancellation, and enumeration-safe notification503/transport controls.
Existing trusted-origin, anonymous-CSRF and completion wire tests stay intact.
These mapper boundary tests do not substitute for the joined chain below.

### Observed baseline and RED

- Exact CI-pinned Release solution build: 0 warnings, 0 errors (51.72s).
- Baseline full unit suite: 1,249 passed, 0 failed/skipped (9m51s),
  `TestResults/recovery-baseline/baseline-full.trx`.
- Baseline full browser suite: 136 passed, 0 failed/skipped (6m6s),
  `TestResults/recovery-baseline-browser/baseline-browser.trx`.
- New test-only Release build: 0 warnings, 0 errors. An initial
  `--no-dependencies` attempt exposed missing external Release reference
  assemblies because the solution baseline built them in Debug; explicitly
  building the owned dependency clones in Release resolved this, without
  touching shared outputs or any runtime source.
- Focused RED: 25 passed, 9 failed, 0 skipped (20s),
  `TestResults/recovery-red/resend-red.trx`. Eight failed cases returned202
  instead of expected503/429; caller cancellation failed because no exception
  propagated. Existing 23 cases and both added masked notification controls
  passed. This is an observed behavioral RED, not build/fixture failure.
- Preparation script executes successfully and repeat execution verifies the
  same clean pins. Initial Windows long-path checkout failure was resolved
  using clone-local `core.longpaths=true`; only the four missing files in that
  newly created ignored clone were restored. Existing dirty/pin-mismatching
  clones are rejected, not reset or silently repaired.

Runtime remains unmodified at this gate. The intentionally failing tests are
not a completed implementation slice and must not be committed as accepted.

After the runtime gate, narrowly owned runtime files would be the recovery mapper
and optional internal result type only. Separate UI approval is required before
editing the reset component and English/Thai resources. No invoice/delegation
files or retained Razor anonymous placeholders belong to this lane.

## Actual joined chain plan

Create a separate opt-in `acceptance/EmployeeRecovery` test project, not an
ordinary Intranet project reference to mutable canonical Auth. The test project
references the uniquely cloned, exact-approved Auth API and Infrastructure and
the production BFF. A preparation tool verifies immutable SHAs, clean source
clones and workspace-local outputs. Do not repin CustomerRevision or invoice
acceptance projects/CI as a side effect of this feature.
Joined tests use producer-reviewed ServiceDefaults
`5c5f9479313710fa576f83d3b396442997a2fcf4` in their separate dependency root,
not the ordinary baseline's older CI pin. That build/test distinction must be
reported and both consumer lanes verified, not hidden as one unified baseline.

Use one disposable PostgreSQL18 Testcontainers fixture with separate employee,
customer and Auth-state databases per isolated scenario. Apply the reviewed
additive migrations only inside those containers; never startup DDL in runtime.
Seed synthetic identities and synthetic service-client credential hashes. Auth
hosts the real service-login controller, issuer, JWT bearer validator and
permission pipeline. BFF hosts its actual endpoint map, typed clients,
ServiceAccessTokenProvider and LegacyServiceAuthenticationHandler. Bridge only
primary HTTP transports to Auth TestServer; do not replace proxies, token
provider, authentication handlers, or production registrations. Block every
unrelated outbound client. Notification uses a controlled local transport,
never NotificationService/provider credentials or externally deliverable data.
Only the worker's timer may be suspended for deterministic fault windows;
exercise the production reconciliation method separately and record that limit.

Required scenarios:

1. Anonymous CSRF acquisition -> request -> one controlled notification ->
   callback parsing -> successful completion. No token in request response.
2. Known/unknown and masked notification errors have identical accepted bodies;
   disabled or physically unavailable Auth schema yields generic503 through BFF.
3. Actual service-login JWT subject is exactly `service:legacy-intranet` for
   both BFF and retained legacy delivery issuance clients; changing the caller
   cannot complete that challenge. Browser/cookie employee identity is never
   recovery owner. Service permission absence cannot authorize issuance.
4. Save/finalization fault creates durable pending receipt, returns503, then
   same owner/email/token/original password retry204 without second hash/stamp;
   different payload or purpose400, terminal replay400, no false success.
5. Controlled clock/identity lock expiry rejects a new unapplied effect; a
   committed receipt remains finalizable after original expiry. Worker does
   not authorize a mismatching action; refresh revocation preserves latest
   generation sessions. Access JWT lifetime is not represented as instantaneous
   revocation.
6. Missing CSRF blocks downstream calls, caller cancellation propagates, actual
   recovery rate limiter rejects excess attempts, no automatic HTTP replay.

## UI and rollout gates

Reset currently clears both password fields before sending. Pending completion
requires re-entering exactly the originally submitted password. Proposed minimal
UX is localized English/Thai same-password retry guidance after503 or transport
uncertainty, keeping fields cleared and never persisting passwords/tokens. It
must not assert that every503 committed an effect, nor advise a different
password as an equivalent retry. Rendered tests must exercise that guidance and
retry; no source-text-only assertion qualifies as UI acceptance.

Drain all old/bypass identity writers, apply additive schemas under separate
authorization, validate physical readiness, configure both deployed issuer
clients as `legacy-intranet` with existing recovery/notification permissions,
verify trusted HTTPS origin and callback routing, then enable the producer's
explicit opt-in. Retained Razor callback URLs currently render placeholders:
exclude that host from recovery callbacks unless its independent restoration
gate is authorized and verified. Do not call placeholders functional rollback.

Disposable joined tests are component evidence, not production-derived full
Aspire acceptance, actual provider delivery, or browser-network TLS/cookie
proof. Intranet #229 and Auth #108 stay open for those gates. Original private
history `5e2030b7339d4d9bd699fd8c3f406b71706b377d` is not locally available as
a tree and is not claimed as fresh original-source proof. Invoice delegation
and parent #97 remain independent.

## Approved implementation and terminal evidence

The earlier design-only gate was superseded by root approval of the observed
RED and structured result. The only runtime edit is the BFF recovery mapper:
confirmation resend distinguishes Accepted, SourceUnavailable and
SourceThrottled; the creation boolean facade remains delivery-only; caller
abort propagates rather than becoming a source failure. No UI, producer,
schema, permissions, route, credentials or CI pin was changed.

Validation in this owned worktree:

- Release solution builds: zero warnings, zero errors.
- Initial resend RED: 25 passed, 9 failed; cancellation RED: 0 passed, 4 failed.
- Final focused recovery/create/delivery/WASM contracts: 50 passed, 0 failed.
- Relevant full unit suite: 1,264 passed, 0 failed, 0 skipped
  (`TestResults/recovery-green-full-corrected/mapper-full-corrected.trx`).
- Browser suite: 136 passed, 0 failed, 0 skipped
  (`TestResults/recovery-green-browser/mapper-browser-final.trx`).
- Separate joined chain Release build: zero warnings, zero errors. Full chain:
  17 passed, 0 failed, 0 skipped in 24 seconds
  (`TestResults/recovery-chain-green/recovery-chain-green.trx`).
- Scoped whitespace verification, Git whitespace check, PowerShell parser and
  repeated exact-pin/clean-clone preparation passed. Redacted Gitleaks scan of
  the complete seven-file candidate scanned 108 KB with no findings.

The joined chain uses actual Auth/BFF/retained delivery hosts, actual service
login and JWT authorization, disposable PostgreSQL 18 and controlled local
notification transport. Early fixture failures were corrected without changing
producer runtime or weakening assertions: eagerly supplied CORS configuration,
actual typed-client registration names, a disposable policy-valid cookie login
identity, and real service-login prewarming before advancing recovery time.
The latter isolates recovery expiry from JWT wall-clock validation while
preserving the post-expiry matching-retry and finalized-wrong-payload checks.

All acceptance build products and immutable clones remain under this worktree's
ignored TestResults directory. This evidence does not close the UI guidance,
retained callback, full Aspire, real provider or deployment/readiness gates.
No commit, push, external notification or persistent migration was performed.

## Root independent acceptance checkpoint

Root inspected the complete mapper diff, joined test fixture/transports and
preparation script after the writer released exclusive ownership. Independent
commands used this worktree's private pinned dependencies and outputs:

- `dotnet build Legacy.Maliev.Intranet.slnx -c Release -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=<worktree>/TestResults/.dependencies -warnaserror -m:1`: zero warnings/errors.
- `dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~BffEmployeeRecoveryContractTests`: 38 passed, zero failed/skipped; `TestResults/root-recovery-focused/root-recovery-focused.trx`.
- Full unit project with the same Release/no-build/no-restore options: 1,264 passed, zero failed/skipped; `TestResults/root-unit-suite/root-unit-suite.trx`.
- Joined acceptance Release build with `--artifacts-path TestResults/recovery-chain-build`, reviewed recovery-chain dependency root and warnings as errors: zero warnings/errors.
- Joined full test project: 17 passed, zero failed/skipped; `TestResults/root-joined-chain/root-joined-chain.trx`.
- Scoped `dotnet format --verify-no-changes --no-restore`, Git whitespace check, repeated exact-pin preparation and both ordinary/joined transitive NuGet vulnerability checks passed; no vulnerable dependencies were reported.
- Redacted Gitleaks scan of all seven candidate files: 110.34 KB, no leaks.

The independent full browser run completed: 136 passed, zero failed/skipped;
`TestResults/root-browser-suite/root-browser-suite.trx`. Root accepted this
bounded seven-file candidate for a coherent commit and protected-main PR; CI
and post-merge main remain separate gates. The underlying issue remains open
for the separate release/UI/provider gates.

### Required-CI browser synchronization repair

Required run 36740367288 failed at the immediate conflict-message visibility
assertion in InvoiceCreateRetryBrowserTests (135 passed, one failed); all
1,264 unit tests passed. The click can complete before the response render.
Two assertions now use Playwright's condition-based visibility/enabled waits,
without sleeps, extended timeouts or weakened frozen-operation checks.
Independent Release build passed with zero warnings/errors; the failing case
passed and the full browser suite passed 136/136 with zero skips. Evidence:
`TestResults/invoice-render-synchronization-browser-full`. Scoped formatting
and whitespace verification passed. Fresh-head required CI is still required.
