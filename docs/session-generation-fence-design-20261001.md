# Session Generation Fence Implementation Plan

> **Current phase, 2026-10-01:** Root reviewed genuine RED and authorized the bounded runtime implementation below. Runtime and local final gates are terminal; candidate/output ownership is released for root independent validation. Retained initial test/design sections are historical evidence, not current scope exclusions. No commits, deployment, IAM, persistent data or provider activation occurred.

## Current implementation and acceptance boundary

The session-only persistence interface, Redis Lua adapter, explicit Testing-only memory adapter, store observations, service reconciliation and shared production registration are present. Both production hosts select the Redis provider through `AddLegacyIntranetDataProtection`; no host routes, cookies, grants, v1 payloads or key purposes changed. Provider writes return committed/not-committed; conditional deletion returns matched/not-matched. Reads remain real RedisCache observations. Renew requires an observed, same-expiry generation, and Lua independently checks ciphertext, hash metadata and TTL. Initial and fresh reconciliation observations are separate. Authentication properties are cloned before rotation; newly received credentials are never forwarded until commit or verified same-owner committed-peer re-observation.

Implicit refresh teardown sets a dedicated request intent before SignOut. A lost conditional delete throws `SessionConditionalTeardownRejectedException` before CookieHandler emits browser deletion; the service catches only this outcome and returns unauthenticated without forwarding. Explicit logout overrides conditional intent and revokes unconditionally. Automatic expired/corrupt retrieval cleanup does not throw on a failed comparison. The legacy no-context Remove overload is explicit server revocation; no-context/unobserved Renew is rejected. Arbitrary distributed-cache construction without an atomic provider is rejected; direct actual MemoryDistributedCache compatibility is testing-only and is not distributed proof.

New real Redis/cookie tests cover malformed metadata, unchanged absolute expiry, unobserved renewal, ambiguous command acknowledgments and both host DI selection. Only controlled external primary Auth transport is synthetic; qualification egress is negative denial only. Old-writer drain and real AuthService readiness remain separate release gates. No positive upstream Quotation acceptance, production activation or whole source-owner retirement is claimed.

Evidence chronology: storage stage `session-fence-storage-red.trx` 9 RED/6 GREEN; unknown-outcome stage `session-fence-unknown-red.trx` 4 RED/16 GREEN; fresh Release then `session-fence-first-green.trx` 116 GREEN/zero skips. The first runtime diagnostic retained 11 failures/100 passes: the test's cache-Set key capture was incompatible with atomic create, and old adversarial fixtures attempted newly forbidden unobserved renewal. Root authorized deriving the key from the actual protected cookie and raw encrypted-v1 adversarial fixture seeding; original status/state/egress assertions were retained. One diagnostic used stale binaries and is not acceptance evidence. New loser browser-cookie expiration assertion then produced genuine `session-fence-cookie-red.trx` 1 RED/0 GREEN. The narrowly typed conditional-teardown repair follows that RED; fresh final gates are recorded below when terminal.

**Goal:** Prevent stale distributed employee-session requests from deleting a peer's committed generation or resurrecting an explicitly revoked session.

**Architecture:** Retain normal cookie authentication, encrypted server-only v1 ticket bytes and opaque keys. Introduce a session-specialized atomic Redis provider on the existing shared resource, using observed ciphertext as the generation token. Generic `IDistributedCache` remains unchanged for other consumers; process locks are not distributed correctness proof.

**Tech Stack:** .NET10, Microsoft.Extensions.Caching.StackExchangeRedis10.0.12, real Redis7.4.5 Testcontainers, normal Production BFF cookie/JWT/antiforgery pipeline, certificate-encrypted Redis Data Protection key ring.

**Spec:** Root's bounded Intranet238 session-generation fencing brief, parent239. Parent merged `2746d0579568d31a6d12fcbb8358439e33ef5b82`; exact-main36782454981 was pending at lane release, so no accepted-main claim is made here.

## Constraints and owned baseline

- Exclusive worktree `B:\maliev-legacy\.worktrees\intranet-quotation-employee-authority-20261001`, branch `codex/intranet-session-generation-fence-20261001`; tracked base clean, preserved private `.dependencies` only. No other outputs/writer active at release.
- Exact private clean CI pins: Defaults `d22f0e6f95254b10cf4fe891c8dce5df7c419f3f`, Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`, Customer `cebf45e8e1eeb600d760a565f8b0970c7f434148`, Auth `8319b23e8ffab0c85b66c886a0109f7dac034c6f`. All build outputs remain owned, no shared clone outputs.
- Preserve both cookie names (`__Host-Legacy.Maliev.Intranet` and `.Bff`), Secure/HttpOnly/SameSiteLax, 8h fixed envelope, sliding disabled, current grants/permission projection and server-only access/refresh tokens. No routes/contracts/schema/key-purpose or serialization version changes.
- No repository AGENTS/CLAUDE or ancestor instruction file was found. Supplied MALIEV protocol applies; TDD/auth/testing/planning skills used. No subagents or extra workspaces.
- Restore PASS, Release0 warnings/0 errors, existing focused baseline96/96 PASS/zero skips (`TestResults/session-fence-baseline-focus.trx`). Scope: EmployeeSessionContractTests, RedisDataProtectionIntegrationTests, CookieSecurityContractTests, DataProtectionContractTests, QuotationQualificationEmployeeForwardingTests. No full baseline duplicate requested.

## Historical baseline: actual trust and storage boundaries

`Server/Auth/DistributedTicketStore.cs` encrypts TicketSerializer bytes with purpose `Legacy.Maliev.Intranet.AuthenticationTicketStore.v1`. Logical keys begin `legacy-intranet:session:` and carry 32 random bytes. Production `LegacyDataProtection` supplies RedisCache with instance prefix `legacy-intranet:` via the existing shared `LegacyDataProtectionResources.Redis`; physical session keys therefore have both prefixes. Both compatibility `Legacy.Maliev.Intranet/Program.cs` and `Bff/Program.cs` register the concrete singleton store as cookie `SessionStore`, and use EmployeeSessionService. Neither host uses a conditional storage operation today.

EmployeeSessionService reconciles failed single-use refresh with a fresh distributed read, but its subsequent SignOut is still read→unconditional remove. Successful refresh mutates cached authentication properties and calls normal SignIn, whose renewal blindly writes even if logout removed the key. Corruption cleanup similarly reads→unconditional remove. These are residual atomicity defects after239, not an accusation that239's fresh-generation guard is absent.

The [exact RedisCache10.0.12 implementation](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.12/src/Caching/StackExchangeRedis/src/RedisCache.cs) uses prefixed hashes containing `data`, `absexp` (absolute .NET ticks) and `sldexp` (-1 absent). SetAsync performs HashSet plus KeyExpire without conditional generation comparison; RemoveAsync deletes the key. The new real-adapter characterization checks physical hash, fields, expiry and ticket round-trip. This coupling must be narrowly encapsulated/version-guarded, not inferred from a generic cache contract.

The [exact CookieAuthenticationHandler10.0.12 implementation](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.12/src/Security/Authentication/Cookies/src/CookieAuthenticationHandler.cs) retrieves via SessionStore; SignIn renews an existing key before emitting a cookie. SignOut removes the session **before** invoking `Events.SigningOut`; an OnSigningOut callback alone cannot label the remove in time. Expired-ticket removal is also implicit. A session service must set intent/observed-generation before calling SignOut; explicit logout uses a distinct store revocation operation or context intent before cookie cleanup.

Source originals/mirror checkpoint `bed10c7d15e0698e0b75f1329d0f312937f5d77f` remain read-only. This bounded target concurrency repair does not retire an entire source auth SHA or prove actual AuthService production readiness.

## Historical initial proposal (subsequently authorized)

1. New `Server/Auth/ISessionTicketPersistence.cs`: session-only create/compare-renew/compare-remove/revoke contract, returning explicit success/changed/missing/expired outcomes. Read can retain the existing pinned RedisCache adapter: snapshot includes exact ciphertext and logical key; it is server-only and never serialized/logged. Reads are observations, not locks or atomic read→write proof; all mutation preconditions are rechecked in the atomic Redis operation. This also preserves the tests' scheduling wrapper on real cache reads without introducing a production test hook.
2. New `Server/Auth/RedisSessionTicketPersistence.cs`: atomic Lua against existing Redis resource/prefix. Create-only random key; renew compares existing `data` to observed ciphertext, requires existing/unexpired entry, then atomically updates encrypted data/absolute expiry/no-sliding fields and TTL. Missing or changed entries never become new sessions. Implicit cleanup compares observed bytes before delete; explicit revoke unconditionally deletes. Preserve existing ticket payload/purpose/hash representation for old stored tickets. No provider-wide cache refactor or infrastructure.
3. `DistributedTicketStore.cs`: record key+generation observation in per-request Items after normal Retrieve; distinguish initial authenticated generation from fresh reconciliation observation. Pass explicit snapshots to atomic operations. Corrupt cleanup compares the corrupt bytes actually read. Preserve unavailable versus absent; do not silently substitute an in-memory backend in production.
4. `EmployeeSessionService.cs`: failed-refresh teardown marks conditional intent using the fresh observed generation; explicit SignOut marks unconditional revoke. Successful rotation must only return Available after committed renewal. A lost CAS cannot forward newly received credentials; reconcile a valid same-owner committed peer or return unauthenticated/unavailable without deleting it. Do not mutate shared authenticated state before commit outcome is known. BFF/compatibility registrations select the session provider on the existing Redis resource.

Exact names/signatures and legacy ITicketStore overload behavior require root runtime review. Never use an unobserved Renew as create/upsert. Historical/direct callers must not be treated as distributed acceptance; any compatibility fallback is Testing-only and separately tested, not production process locking.

## Rollout and failure decisions

- Old encrypted v1 records remain readable; no wholesale logout, key rename or serialization change. Ciphertext is a collision-resistant opaque generation identity from Data Protection; compare bytes, not refresh tokens/owner strings. Every writer emits fresh protection ciphertext.
- Both hosts must migrate their writer path together and drain old blind writers before activation. An old process can still recreate deleted keys; this lane cannot promise revocation fencing during mixed old-writer deployment. Rollback to blind writers reopens races. Root owns readiness/release bookkeeping; no activation in this lane.
- Explicit logout wins at the Redis delete linearization point. Any earlier-observed late renewal sees missing and cannot recreate. A request already forwarded before revocation is not retrospectively canceled; no distributed transaction with upstream Auth is claimed.
- Provider exceptions and caller cancellation preserve state and cannot imply committed renewal. Redis command cancellation may be outcome-unknown; reconcile safe committed same-owner state or fail closed, never return the uncommitted token. No credentials in errors/logs/test output.
- Owner mismatch, expired envelope, missing observation or malformed storage metadata cannot authorize forwarding/renewal. Expiry removal is conditional; corrupt cleanup cannot delete bytes written by a peer.

## Historical test-first gate and review focus

Create only `Legacy.Maliev.Intranet.Tests/Integration/SessionGenerationFenceTests.cs` and this doc. Tests use two actual Production BFF factories, real RSA JWT validation, real cookie/antiforgery handlers, shared certificate/Redis crypto, independent multiplexers/cache instances. Only external Auth primary HTTP transport supplies controlled login/refresh responses. Qualification egress is a root-authorized negative sentinel: counter plus empty403 denial, never synthetic success or positive provider acceptance. Neither external service's real readiness is proven.

- [x] Baseline build0W0E then96 existing focus PASS.
- [x] Add failed-refresh stale-read→peer-renew→implicit teardown. First current runtime deletes committed peer ticket: genuine RED.
- [x] Add corrupt-read→valid peer ciphertext write→cleanup. First current runtime deletes valid bytes: genuine RED. Controlled valid cache write represents a peer/old writer, not provider CAS proof.
- [x] Add normal-cookie explicit logout→late successful refresh. Initial run's BadRequest was a fixture CSRF error: prelogin anonymous CSRF is not valid for logged-in subject. Preserve initial diagnostic, obtain peer's fresh authenticated CSRF, rerun before counting intended RED.
- [x] Add separate changed-generation late successful refresh. Peer commits rotated ciphertext while Auth transport is blocked; stale success must not overwrite it or forward a token absent from the committed peer generation. Assert redacted tuple `(peer generation retained, uncommitted forwarded count)`, not token values.
- [x] Add actual Redis hash/TTL adapter proof; canceled caller, reconciliation cache fault, expired cookie and changed refresh owner controls. Add actual HTTP abort during blocked refresh.
- [x] Observe terminal RED: final sentinel run4 intended failures/102 passed/zero skips (106 total), including all96 existing controls and6 new guards. Root reviews complete test/design before runtime.
- [ ] On runtime release, implement narrow provider/store/service/registration repairs; fresh Release, focus, affected/full suites, format, audit, coverage and redacted scanner. No commit/push before independent root acceptance.

## Commands and retained evidence

All commands run from the owned workspace, serialized:

```powershell
dotnet restore Legacy.Maliev.Intranet.slnx -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/intranet-quotation-employee-authority-20261001/.dependencies -p:UseLocalMalievDependencies=true
dotnet build Legacy.Maliev.Intranet.slnx --configuration Release --no-restore -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/intranet-quotation-employee-authority-20261001/.dependencies -p:UseLocalMalievDependencies=true -v:minimal
dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~SessionGenerationFenceTests --logger 'trx;LogFileName=session-fence-expanded-red.trx' --results-directory TestResults -v:minimal
```

Initial new build0W0E; initial5 cases:2 PASS,2 genuine race RED,1 logout-CSRF fixture failure, zero skips (`session-fence-initial-red.trx`). Expanded9 cases:3 genuine race RED/6 guards PASS/zero skips (`session-fence-expanded-red.trx`). Adding tuple diagnostics initially produced two CS8123 compile errors (retained `session-fence-final-red-build.log`); matching tuple names fixed test compilation. Final negative-egress sentinel build0W0E (`session-fence-sentinel-build.log`); terminal `session-fence-sentinel-red.trx`: **4 intended RED / 102 PASS / zero skips**,106 total,72s. All96 existing controls stayed GREEN. Both leakage failures display redacted tuples: logout `(session present, forwarded)=(true,1)` instead of `(false,0)`; changed generation `(peer retained, uncommitted forwarded)=(false,1)` instead of `(true,0)`. No synthetic positive Quotation response remains. Tests are intentionally not feature-complete while race assertions remain RED. Main CI/protected acceptance and old-writer drain remain separate gates.

Final four intended failures in `SessionGenerationFenceTests`:

1. `FailedRefresh_StaleReconciliationRead_CannotDeletePeerRenewal`
2. `CorruptionCleanup_CannotRemoveValidCiphertextWrittenAfterItsRead`
3. `ExplicitLogout_WinsAgainstLateSuccessfulCookieRenewal_AndPreventsForwarding`
4. `LateSuccessfulRefresh_CannotOverwritePeerGeneration_OrForwardUncommittedToken`

At the initial test/design handoff, full relevant/browser/coverage/vulnerability gates were deferred until runtime authorization. That intentional RED stage was not a repaired feature or release-ready authentication claim. Current runtime gates supersede this historical stage; root independent acceptance and old-writer drain still remain.

## Runtime final gates, 2026-10-01

Fresh `session-fence-cookie-green-build.log`: Release0 warnings/0 errors. `session-fence-final-focus.trx`: 116 passed/0 failed/0 skipped, including all96 prior controls and20 new real Redis/crypto/cookie cases; loser browser-cookie expiration assertion is now GREEN. Runtime source stayed unchanged throughout these final runs.

```powershell
dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~SessionGenerationFenceTests|FullyQualifiedName~EmployeeSessionContractTests|FullyQualifiedName~RedisDataProtectionIntegrationTests|FullyQualifiedName~CookieSecurityContractTests|FullyQualifiedName~DataProtectionContractTests|FullyQualifiedName~QuotationQualificationEmployeeForwardingTests' --logger 'trx;LogFileName=session-fence-final-focus.trx' --results-directory TestResults -v:minimal
$env:DOTNET_PROCESSOR_COUNT='1'
dotnet test Legacy.Maliev.Intranet.slnx --configuration Release --no-build --no-restore --logger 'trx;LogFileName=session-fence-final-full.trx' --results-directory TestResults -v:minimal
```

Full solution terminal: unit/integration1352 passed/0 failed/0 skipped (11m11s); browser136 passed/0 failed/0 skipped (5m53s). `session-fence-final-full.trx` contains unit/integration; `session-fence-final-browser.trx` was copied after browser completion before the shared solution logger filename was overwritten by unit results. Full log retains both terminal summaries. Existing browser `FinalGateCapturesEveryCustomerWorkspaceTabAcrossRequiredModes` generated synthetic PNG artifacts under `.superpowers/sdd/2026-08-12-customer-history-site-links-plan/task7-captures`; those were preserved, not new implementation files or an extra skill workspace.

Joined acceptance: own exact CI producer pins, Release0 warnings/0 errors (`session-fence-acceptance-build.log`), `session-fence-acceptance.trx` 3 passed/0 failed/0 skipped (27s). This runs the existing isolated synthetic Customer/Auth contract fixture, not a production readiness or external provider claim.

```powershell
dotnet build acceptance/CustomerRevision/CustomerRevision.AcceptanceTests.csproj --configuration Release -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/intranet-quotation-employee-authority-20261001/.dependencies -p:UseLocalMalievDependencies=true --nologo
dotnet test acceptance/CustomerRevision/CustomerRevision.AcceptanceTests.csproj --configuration Release --no-build --no-restore -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/intranet-quotation-employee-authority-20261001/.dependencies -p:UseLocalMalievDependencies=true --logger 'trx;LogFileName=session-fence-acceptance.trx' --results-directory TestResults --nologo
dotnet build Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj --configuration Release --no-restore -p:EnableCoverageSymbols=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/intranet-quotation-employee-authority-20261001/.dependencies -p:UseLocalMalievDependencies=true
dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj --configuration Release --no-build --no-restore -p:EnableCoverageSymbols=true --collect:'XPlat Code Coverage' --settings coverage.runsettings --logger 'trx;LogFileName=session-fence-coverage.trx' --results-directory TestResults/session-fence-coverage -v:minimal
```

Coverage-enabled build0 warnings/0 errors; collector and remaining static gates are pending terminal at this ledger entry. No commit or activation occurred. Docker image builds and production rollout are not claimed by these local source/runtime checks.

Resume inspection at08:36+07:00: old coverage handle88955 no longer existed; no owned dotnet/testhost/vstest process remained. Original `session-fence-coverage.log` contained startup only, its collector directory was empty, and no TRX/Cobertura existed. This interrupted diagnostic was preserved, not classified as PASS. Fresh `session-fence-coverage-retry-build.log` Release0 warnings/0 errors; unchanged tests restarted with distinct `session-fence-coverage-retry.log` and `TestResults/session-fence-coverage-retry` outputs. All four private dependency clones were rechecked at exact pins and remained clean.

Coverage retry terminal:1352 passed/0 failed/0 skipped (7m47s), `TestResults/session-fence-coverage-retry/session-fence-coverage-retry.trx`. Actual report `TestResults/session-fence-coverage-retry/72a2e0ff-2f45-4ca9-8372-a473fea8d991/coverage.cobertura.xml`; existing `verify-test-coverage.ps1` PASS, exit0: BFF88.4% (minimum80), Server89.2% (minimum85), Contracts96.05% (minimum95). No coverage exclusion, threshold or old assertion was changed.

Whole-solution verify-only format exit0 (`session-fence-final-format.log`). Transitive vulnerability audit exit0, sixteen projects, zero reported vulnerable packages/source problems (`session-fence-vulnerable.json`). Deprecation audit completed exit0/no source problems, but reports ten occurrences of five unique pre-existing test-only xunit2.9.3 packages: xunit, xunit.assert, xunit.core, xunit.extensibility.core, xunit.extensibility.execution; marked Legacy with v3 alternatives. Test package references were unchanged, and dependency migration is outside this session-fencing slice. This finding is not a clean-deprecation claim.

Redacted gitleaks stdin scan of all nine owned files: exit0/zero findings (`session-fence-gitleaks.log`, `.json`); `git diff --check` exit0. No other files were staged or committed. Preserved private `.dependencies` and synthetic browser `.superpowers` captures are excluded from implementation ownership and commit scope. Root owns independent review, protected CI and release decisions; drain old blind session writers before deployment. No whole-source auth closure or real upstream readiness claim.

```powershell
pwsh -NoProfile -File scripts/verify-test-coverage.ps1 -CoverageFile TestResults/session-fence-coverage-retry/72a2e0ff-2f45-4ca9-8372-a473fea8d991/coverage.cobertura.xml
$env:MalievWorkspaceRoot='B:/maliev-legacy/.worktrees/intranet-quotation-employee-authority-20261001/.dependencies'
$env:UseLocalMalievDependencies='true'
dotnet format Legacy.Maliev.Intranet.slnx --verify-no-changes --no-restore --verbosity minimal
dotnet list Legacy.Maliev.Intranet.slnx package --vulnerable --include-transitive --no-restore --format json
dotnet list Legacy.Maliev.Intranet.slnx package --deprecated --include-transitive --no-restore --format json
Get-Content -LiteralPath $owned | gitleaks stdin --redact=100 --no-banner --report-format json --report-path TestResults/session-fence-gitleaks.json
git diff --check
```

`$owned` is exactly six Server/Auth files (DistributedTicketStore, EmployeeSessionService, LegacyDataProtection, ISessionTicketPersistence, RedisSessionTicketPersistence, TestingSessionTicketPersistence), the new SessionGenerationFenceTests, the approved fixture-only QuotationQualificationEmployeeForwardingTests change, and this design doc. Runtime source is unchanged since the final Release/focus/full/browser/acceptance and coverage-enabled gates. Historical sections above record phase-local pending gates; this final entry supersedes them.

## Independent integration review

Root independently reviewed all nine files and rebuilt Release with zero warnings/errors. Focused90 plus supplemental28 passed (116 distinct test names, two overlaps); the unfiltered service suite passed1352 and the browser suite passed136, all zero failures/skips. Exact root TRXs live under `TestResults/root-session-fence-full` and `TestResults/root-session-fence-browser`. Separately rebuilt existing joined Customer/Auth acceptance passed3/3 with zero skips (`TestResults/root-session-fence-joined`). Whole-solution verify-only formatting and sixteen-project transitive vulnerability audit passed. Root independently reran the unchanged coverage validator: BFF88.4%, Server89.2%, Contracts96.05%, each above its existing threshold. These are disposable/synthetic acceptance results, not production-derived Aspire or actual external-provider readiness.

Preserved `.dependencies` and `.superpowers` are not implementation files. Existing captured evidence stays ignored/untracked and out of the commit. Drain old blind session writers before eventual activation; this PR does not deploy anything or close broader issue183.
