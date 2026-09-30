# Intranet employee qualification forwarding: issue 237, parent 183

## Gate and ownership

Bounded runtime authorized after observed RED and root review. Owned worktree `intranet-quotation-employee-authority-20261001`, branch `codex/quotation-employee-authority-consumer-20261001`, clean base `2589c562815bbdea394a72e416325be220031b4e`. Owned files are the new forwarding tests/document/client/acquisition result plus BFF Program, the two quotation mappers, EmployeeSessionService and DistributedTicketStore. No old tests, CI, grants, activation, schema, deployment, original source, persistent data, GitHub, commit or push changes. Ordered peer-generation repair was separately approved; distributed CAS/fencing remains unresolved in [Intranet238](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/issues/238), linked237/183.

Read authentication-systems, maliev-blazor-standards, maliev-testing-standards, test-driven-development and verification-before-completion skills and the TDD writing-good-tests reference. The generic Blazor skill example copying an incoming browser Authorization header is explicitly inapplicable: reviewed legacy authority is the server-held employee token, never browser credentials. No fake auth handler or IAM signed-grant fallback is introduced.

## Exact source mapping and consumer defect

Read-only committed source mirror checkpoint `bed10c7d15e0698e0b75f1329d0f312937f5d77f`:

- `362308b605ff94878f684258ade46c67ae0b08ee`: producer qualification seven-field update, receipt, projection and immutable audit.
- `e78ab85594e688aed223f54ef31c7b6df399a735`: original Intranet `Pages/QuotationRequests/View.cshtml(.cs)`. The actual page resolves the authenticated ApplicationEmployee and requests/forwards that employee access token for receipt GET and update PUT. Those two files have no diff from this commit to checkpoint bed10.
- `99462c12c33fc62da281fc90fc61a8ee458d0d7a`: producer identifier/persistence/privacy hardening.
- Aggregate qualification-outcomes/readback is separate. Never fabricate a positive resource ID to authorize it through this bridge.

Current target `Program.cs` registers QuotationRequestsProxy with the unconditional LegacyServiceAuthenticationHandler; that handler replaces Authorization with the BFF workload token. Two qualification proxy methods have no employee-token input. All three positive-ID callers require a coherent repair:

1. GET `/bff/quotation-requests/{id}/qualification-receipt` -> GET `/quotationrequests/{id}/qualification-receipt`.
2. PUT `/bff/quotation-requests/{id}/qualification` -> PUT `/quotationrequests/{id}/qualification`.
3. POST `/bff/quotations` with SourceRequestId -> the same receipt GET in QuotationCreateEndpointMapper before binding producer SourceJourneyId.

The quotation creation workflow/gateway and ordinary CRUD/files remain workload-authenticated. Aggregate readback stays outside this boundary. Browser qualification command stays exactly state, reason, completeness, duplicateCount, unmatchedClassification, idempotencyKey, expectedVersion; no token or actor fields. Receipt/audit remain PII-free with existing expected-version and immutable ChangedBy/replay semantics. No new employee ACL policy is invented.

## Approved runtime shape

Add a qualification-only typed client without LegacyServiceAuthenticationHandler. Acquire the bearer solely through EmployeeSessionService from the authenticated encrypted ticket, then put it on the individual outgoing request. Never mutate shared HttpClient DefaultRequestHeaders, copy browser Authorization, reinterpret a service token as an employee, or bypass ordinary downstream JWT validation.

Opt-in `QuotationQualification:ForwardEmployeeToken` defaults false. When false preserve current service-client behavior and existing default-path tests; the default-off producer still denies, not a fallback positive authority path. Enabled employee acquisition/authority failure never retries via the old service client. No dependency readiness or default-off switch breaks ordinary quotation create or unrelated routes.

Wire all three callers to this client. Keep existing BFF permissions: read for receipt, update for transition, plus source-read check on linked create. Keep quotation-create's existing full permission conjunction, Idempotency-Key, durable workflow and downstream workload bearer. Keep CSRF filters and localized browser behavior; no UI runtime change currently required.

EmployeeSessionService previously conflated unavailable refresh (ticket preserved) and invalid/revoked/owner-mismatched refresh (ticket removed) as nullable access token. Approved structured acquisition result: `Available(token,currentPermissions)`, `Unauthenticated`, `Unavailable`, `Throttled(boundedRetryAfter)`. Existing nullable facade remains for unrelated consumers. Caller cancellation propagates; outage is never inferred merely from a null token.

Qualification response semantics: missing/invalid session401; missing BFF permission403 before transport; caller abort propagates; transient acquisition failure503 generic with ticket retained; owner-mismatched/revoked refresh401 and ticket cleared. Downstream401/403/404/409 retain existing public semantics; malformed receipt502; infrastructure503; bounded429/Retry-After preserve existing direct mapper contract. Linked-create currently maps arbitrary downstream non-success to502, so preserving a qualification429 there requires its own focused RED and root gate, not broad mapper cleanup.

## Session boundary retained

Production cookie authentication, LegacyAuthClient and normal RS256 validator establish the employee identity. DistributedTicketStore encrypts complete tickets in IDistributedCache; cookie contains an opaque key only. EmployeeSessionService stores employee access/refresh tokens and access expiry in server properties, refreshes at two-minute skew, validates refreshed ID against prior ticket owner, recreates validated permissions and signs out on invalid/revoked/owner mismatch. Existing peer-renewal race handling must be characterized with controlled concurrency before any repair; do not claim reread correctness from code inspection or alter global refresh rotation without RED/review.

Retain HTTPS-only HttpOnly `__Host-Legacy.Maliev.Intranet.Bff` SameSite=Lax/non-sliding eight-hour cookie and HTTPS-only HttpOnly Strict antiforgery cookie with X-CSRF-TOKEN. Never return access/refresh/workload token, credential, sid or stamp to browser responses/cookies/logs. Existing server session summary still exposes only approved identity/permissions/CSRF projection.

## Test boundary and limitations

New tests use the actual BFF Program, normal LegacyAuthClient, normal RSA JWT validator, EmployeeSessionService, CookieAuthenticationHandler, DistributedTicketStore, ServiceAccessTokenProvider, service-login transport and original delegating/client/gateway registrations. Only the primary HTTP transport of external services is controlled through IHttpMessageHandlerBuilderFilter. Synthetic RS256 fixture tokens are validated normally. Receipt responses characterize outbound BFF behavior, NOT positive live Auth authority; fixture sid is not a persisted Auth session proof. No fake auth handler, stub employee service/client/validator, fake quotation gateway or global IAM client is installed.

Testing environment uses the existing real encrypted distributed-ticket store over the supported memory distributed cache and ephemeral Data Protection provider. No Intranet PostgreSQL exists at this boundary; PG18 would add no BFF transport proof. Production Redis/certificate storage and joined actual Auth PostgreSQL session/Quotation audit proof require the later accepted-pin joined fixture. No current browser/session, notifications or real downstream writes are used; all PDF/upload/email transport is local fixture response.

Initial cases: employee bearer for all three callers with hostile browser header; exact seven-field update; linked SourceJourneyId from producer; actual unlinked and linked gateway create workload separation; near-expiry rotation; transient refresh preserves ticket503; revoked/wrong-owner refresh401/no forward; anonymous cookie-less injected bearer; missingCSRF; missing operation permissions; invalid employee login token signature/expiry/kind. New test names/expectations are behavioral, not source greps.

Further tests before corresponding runtime repairs: upstream status/receipt corruption, request cancellation, bounded throttle, second refreshed permission removal, ticket-cache outage, concurrent rotation/peer winner/owner invariants, same-key retry and no stale employee credential resurrection. Existing tests are unchanged default-path controls, not joined acceptance.

## Producer/readiness ordering

Accepted Auth producer `8cdb634b3b0abdf18b9b826a0948dbfd98c66ea0`, root supplied green exact-main CI36763076139. Frozen POST `/auth/v1/introspection/quotation-qualification`: outer exact `service:legacy-quotation` with separate `legacy-auth.quotation-qualification.introspect` capability; four-field body employeeAccessToken, permission, purpose, requestId. Permission is exact read/update, purpose quotation-request-qualification, ID positive Int32. Five-field decision allowed, subject, permission, purpose, requestId must bind exactly. Intranet does not call this endpoint directly or receive its authority decision.

Quotation issue76 consumer is UNMERGED; design read only at `quotation-qualification-bridge-consumer-20261001/docs/qualification-bridge-consumer-design-20261001.md`. No accepted producer pin inferred from candidate or controlled fixture. Actual joined test project/tooling must wait for protected-main green and separate root gate before adding references; do not build candidate or alter existing acceptance pins.

AppHost inspected revision `55d80acfeebf58d17a07f8d3226cc82a8dd38d79`: existing HTTPS BFF, Redis and certificate-protected keyring/session prerequisites; ordinary JWT issuer/audience/key; Services:Auth/Quotation; runtime legacy-intranet workload credential. It does not grant the dedicated Quotation introspection capability or activate the Auth/Quotation options. No AppHost changes in this lane.

Ordering: accepted Auth -> accepted Quotation -> tested Intranet consumer -> separately authorized capability/activation and production-derived Aspire acceptance. Old unbound employee JWTs fail producer authority; actual login/refresh must supply current bound sid, never choose an arbitrary active session. Replays reauthorize fresh. Revocation/live authority versus Quotation commit remains point-in-time across databases, not atomically revoked. No stronger TOCTOU guarantee or positive-authority cache invented.

## Validation evidence

Exact private CI pins, all clones initially clean: Defaults `d22f0e6f95254b10cf4fe891c8dce5df7c419f3f`, Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`, optional existing acceptance sources Customer `cebf45e8e1eeb600d760a565f8b0970c7f434148`, Auth `8319b23e8ffab0c85b66c886a0109f7dac034c6f`. These are CI pins, not the newer live-authority producer acceptance graph.

Command properties on all builds/tests: `-p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/intranet-quotation-employee-authority-20261001/TestResults/.dependencies`.

- Baseline `dotnet build Legacy.Maliev.Intranet.slnx -c Release`: terminal0W0E24.89s, only owned outputs/dependencies.
- Baseline `dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~BffQuotationRequestsContractTests|FullyQualifiedName~BffQuotationCreationContractTests|FullyQualifiedName~EmployeeSessionContractTests|FullyQualifiedName~LegacyAuthClientContractTests|FullyQualifiedName~ServiceAuthenticationContractTests' --logger trx --results-directory TestResults/qualification-baseline-focus`:52pass0fail0skip9s.
- Initial test compile identified three xUnit2031 analyzer errors, corrected only in new test file by Assert.Single predicate overload. Not accepted RED/runtime evidence. Final new-test build and observed RED recorded below once terminal.
- New-test initial solution Release0W0E9.89s; initial RED `TestResults/qualification-initial-red/natth_MALIEV-31USFIV_2026-10-01_03_06_58_net10.0.trx`:12pass7fail0skip16s. All seven fail on missing forwarding/refresh, not host/fixture setup.
- Scoped format found new-file whitespace; formatted only this new file. Moved its existing payload/workload/browser-safe assertions before the intended employee-bearer assertion so even the linked-create RED proves unchanged workload creation and producer SourceJourneyId binding.
- Final solution Release0W0E7.23s; confirmed RED `TestResults/qualification-confirmed-red/natth_MALIEV-31USFIV_2026-10-01_03_09_58_net10.0.trx`:12pass7fail0skip13s. Receipt/PUT/linked-create fail the explicit employee-bearer assertion; near-expiry case sees zero refresh calls; outage expects503/actual200 with preserved ticket; revoked and wrong-owner each expect401/actual200. Unlinked real gateway creation workload control, anonymous3, missingCSRF2, permissions3 and invalidJWT3 pass.
- Historical initial test SHA256 `F8EEB8209AAE6325B80D550761F6BFB26414BFD7649726C04F5488C035011CCB` is not the later candidate hash. Initial gate had no tracked runtime edits; private pins were exact and clean.

### Expanded RED and approved repairs

- Expanded formatted RED40:26pass14fail0skip43s, `qualification-formatted-boundary-red/...03_18_34_net10.0.trx`; malformed direct receipts, linked429, cancellation and renewed permissions precede corresponding repairs.
- First combined runtime focus92:91pass1fail. Transient503 fixture incorrectly froze resilience timers by installing FakeTimeProvider globally. Test-only TicketClock now controls ticket UTC while retaining real timers; JWT validity uses actual UTC. Isolated transient control passed1 in10s. This failed run is not acceptance.
- Follow-up RED11:1pass10fail0skip23s, `qualification-followup-red/...03_30_02_net10.0.trx`: seven refreshed create-policy removals, real GET307/PUT308 redirected transport and actual slow-body deadline failed; real body caller cancellation passed.
- Lifecycle RED8:0pass8fail0skip9s, `qualification-lifecycle-red/...03_35_42_net10.0.trx`. Six enabled no-store expectations failed, peer loser401 failed; proposed cache500 expectation was corrected after inspecting pinned Defaults InvalidOperationException mapping to generic400 (no middleware repair).
- Approved repairs retain the existing primary transport but disable automatic redirect only on the new client; buffer response content inside its10s deadline; set enabled-path no-store; and recheck all seven existing linked-create permissions plus source-read from the renewed projection before qualification transport or gateway. Ordinary create, workload clients and original Program policy conjunction are unchanged.
- Solution Release0W0E9.27s; new focused59:58pass1fail0skip69s, `qualification-repaired-focus/natth_MALIEV-31USFIV_2026-10-01_03_41_03_net10.0.trx`. Sole failure is peer rotation: winner200 then fresh session authenticated, loser401 then fresh session unauthenticated. Cache400 generic response/no leak/ticket preservation passes; all approved boundary repairs pass.
- Post-format solution Release0W0E3.54s; existing focused52pass0fail0skip7s, `qualification-repaired-existing-focus/natth_MALIEV-31USFIV_2026-10-01_03_45_11_net10.0.trx`. Scoped format verify and owned-file secret scan256671bytes pass.
- Pre-peer full1323:1321pass2fail0skip4m51s, `qualification-pre-peer-full/natth_MALIEV-31USFIV_2026-10-01_03_45_28_net10.0.trx`. Failures: peer rotation and existing DirectFrameworkPackages source scan includes optional exact Customer clone under TestResults/.dependencies (its10.0.3 testing package). The test excludes root .dependencies but not nested TestResults dependencies; no test change/filter/exclusion applied. Proposed owned clone-layout move is awaiting root approval. This is not accepted full-suite evidence.

### Peer-rotation review gate

CookieAuthenticationHandler caches its cookie ticket per request; repeating AuthenticateAsync cannot freshly read a winning peer's encrypted store entry. Official .NET10 implementation: [CookieAuthenticationHandler](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.0/src/Security/Authentication/Cookies/src/CookieAuthenticationHandler.cs). Its SignOut removes the same server-session key. The actual deterministic two-refresh barrier proves the loser deletes the winner's rotated ticket.

Implemented private seam after root approval: capture the opaque key only when the genuine cookie handler calls DistributedTicketStore.RetrieveAsync(key, context, token), then perform an internal fresh encrypted-store retrieval for that context. No browser-key parsing, reflection or replacement authentication handler. Accept only same nonblank owner, nonexpired ticket, changed nonblank refresh generation, nonblank access token with >2m envelope validity; use fresh permissions, never sign in/renew/recreate the peer ticket. Fresh reread is nonmutating even for corruption; genuine cookie-handler corruption cleanup is unchanged. Missing/expired/corrupt/other-owner entries cannot authorize or resurrect; cache outage503 preserves ticket and caller abort propagates. Only freshly observed same-owner same-refresh-generation may retain old invalid-session SignOut behavior.

Original public four-argument constructor is unchanged. Fresh reconciliation resolves the concrete registered store from HttpContext.RequestServices; actual Program test proves it is exactly the Cookie SessionStore singleton. With a concrete store but no validated key, fail closed without cached fallback. Absent-store legacy/direct callers retain original re-authenticate behavior only for facade compatibility; existing recording-auth tests are not evidence of fresh authority.

Unresolved [Intranet238](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/issues/238): fresh read followed by same-generation SignOut remove is not atomic with a later peer write; a late renewal can be removed or recreate a deleted session. Generic IDistributedCache has no CAS/fencing. Deterministic tests prove the supported winner-before-fresh-read and missing-before-fresh-read orderings, not that distributed read/remove or late-renew races are solved. Do not close the broad session/concurrency parent on this slice.

Independent root acceptance on the same candidate: Release build zero warnings/errors;
focused 120/120 and full 1332/1332, zero skips, followed by whole solution format
verification and diff checks. Exact reports are
`TestResults/root-qualification-focus/root-qualification-focus.trx` and
`TestResults/root-qualification-full/root-qualification-full.trx` (full duration 5m21s).
Root inspected actual singleton cookie-store registration, all three qualification
callers, fresh permission checks, peer-generation barriers and byte-preserving
corruption controls. This does not close #238 or establish joined Aspire acceptance.
Protected PR and exact-main CI remain required before this slice is complete.

- Peer boundary RED10:3pass7fail0skip7s, `qualification-peer-boundary-red/...03_54_46_net10.0.trx`; existing framework scan passes after clone layout correction.
- Stronger corrupt-byte RED8:7pass1fail0skip6s, `qualification-corrupt-read-red/...03_58_45_net10.0.trx`, with previous cleanup behavior retained before private nonmutating repair.
- Final solution Release0W0E6.96s; combined focus120pass0fail0skip77s (68new+52existing), `qualification-final-focus/natth_MALIEV-31USFIV_2026-10-01_03_59_52_net10.0.trx`. Whole solution format verify passes; exact-property environment package audit reports no vulnerable packages.

Clone-layout correction: four owned clean exact clones moved after absolute containment checks from TestResults/.dependencies to this worktree's root `.dependencies`; pins and outputs preserved, no old test edited. Current explicit property is `-p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/intranet-quotation-employee-authority-20261001/.dependencies`. Stale no-restore asset reference failure was corrected by exact-graph restore before subsequent zero-warning build; it is not accepted validation.

Final whole unit suite1332pass0fail0skip5m25s, `TestResults/qualification-final-full/natth_MALIEV-31USFIV_2026-10-01_04_02_36_net10.0.trx`. No old tests changed or excluded. Whole solution Release builds browser project but browser execution was not run for this server-only transport/session slice; actual BFF HTTP/cookie/JWT and loopback transport boundaries ran in the new tests. Joined actual authority/production Aspire remains deliberately unproven and requires separately accepted producer pins and authorization. No claim that #238 or broad concurrency is resolved. Writer and build ownership released to root after final evidence readback; no commits or pushes created.
