# Employee administration consumer — implementation checkpoint

Issue: https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/issues/261

This unpublished checkpoint has a passing Release build (zero warnings/errors)
and 102 passing relevant employee-administration cases (zero failures/skips).
The exact joined producer graph also built with zero warnings/errors and passed
all six actual local producer/consumer cases, with no failures/skips. The immutable
clean-source fixture `eb2030511ef6af65a351e7d70ea15eed35f7a0cb` passed all 1,817
Intranet tests, all 16 TRX counters and individual outcomes checked. Its Release
build had zero warnings/errors; the focused finance suite passed 35 cases and
formatting passed. The separate coverage-verifier suite passed 28 cases. Protected
hosted acceptance, raw coverage floors and fresh-main verification remain pending.
Original committed checkpoint `135e526d0dab85c415b3afdcefd7b70fe2c82e2f`
retains `Maliev.Intranet/Pages/Employees/View.cshtml` and `View.cshtml.cs`.
The original administrative action orders identity, home address, then profile.
The migrated read-only view does not satisfy that action.

## Implemented draft boundaries

- Same-origin administrative edit GET/PUT requires the existing authenticated
  employee cookie, exact permissions and antiforgery on PUT.
- Dedicated external clients acquire the acting employee's server-held token.
  Each send rechecks renewed permissions; there is no service-token fallback,
  automatic redirect or inherited retry handler on these writes.
- Auth and Employee versions are captured independently. Original quoted versions
  remain unchanged; refreshed reads verify bindings, not replace stale versions.
- Identity accepts only the source editable fields. Existing two-factor and
  lockout-end settings must be present in the safe read and are preserved rather
  than offered as browser-editable fields. No password/hash/stamp is returned.
- Shared email is limited to the Employee database's 256-character boundary before
  identity mutation, even though the identity writer alone permits 320.
- Address identifiers come only from the verified employee relationship or a
  confirmed address creation response. Nested address validation happens before
  the first write. Source incomplete optional new-address input remains a no-op.
- Save stops at the first failed step. Confirmed steps and uncertain outcomes are
  distinguished. It does not claim distributed atomicity, compensate silently,
  retry writes or replay the whole sequence after an acknowledgement is lost.

## Historical producer acceptance; consumer integration still required

The earlier address-only conditional writer did not fence concurrent movement
of the employee's home-address relation. EmployeeService PR #48 now provides
a coupled employee/address transactional route on protected main
`f7f00b7c57ef42c8c945ef5a2644065010fb377f`, with both captured versions and
relationship verification. The draft consumer uses that accepted producer
route, with an internal derived `AddressId`, address `If-Match`, employee
`X-Employee-If-Match`, and both renewed permissions. Its success receipt must
contain the new address ETag and unchanged employee `X-Employee-ETag` before the
last profile write. Root inspected the committed controller/request/repository
and application-service delta. Fresh-main run `37411451160` passed; independent
readback found 330 unique passing executions, all 16 counters clean, TRX SHA256
`9189BAA58BA5891C9AAB86512CD3C5AB5F3DE46A7E9BD80145B953A4BCB9AC6B`.
The raw report hash is
`93CE98C0FEC4534FF952ECC9C10583ED5603CF99B78A0DC07BEF5F0702D1B5E3`;
root recomputed API 395/473, Application 193/195, Data 1152/1202 and Domain
36/36 with no exclusions. This producer evidence does not prove the draft
consumer's compilation, joined workflow, UI or deployment.
Do not treat a BFF preflight read as a transactional ownership guarantee.

## Validation and completion gates

Root integrated the component-owned EmployeeEdit English/Thai resource pair with
61 keys per language, including the dynamic validation messages and PageTitle.
Static readback verifies verbatim translation provenance from the existing View
resources and exact normalized equality with the reviewed six-file correction.
The explicit routed-page inventory is now 47, with only `/Employees/Edit` added
to the non-legacy ownership allowlist. The normal BFF surface is exactly 105:
58 GET, 14 PUT, 24 POST and 9 DELETE. Independent source inspection verified
the corresponding mapper registrations and retained authorization/antiforgery.
These are source/XML/inventory checks, not executed localization or HTTP evidence.

Parallel isolated work adds normal BFF HTTP regressions and localized Blazor
editing. Controlled external HTTP verifies consumer contracts only, not producer
PostgreSQL locking semantics. The producer's real PostgreSQL regression evidence
is a separate requirement. Local build and controlled HTTP/Razor validation ran.
The later joined acceptance also executed six real producer/consumer cases using
bounded disposable PG18 and Redis7 resources; this does not claim a browser or
production-data acceptance run. `git diff --check` is static evidence only.

The draft currently includes 48 authored normal-BFF HTTP cases and 17 authored
real-Razor/Shadcn bUnit cases. Additional serialization tests freeze the internal
derived address request and reject browser-supplied producer/protected fields.
The relevant suite now passed 102 cases, including four additional EN/TH
partial-save explicit-readback regressions. These four first failed on absent
stored values, then passed with a minimal read-only display and no automatic
synchronization. This is not full-suite or actual-producer acceptance. The bUnit held-PUT
fixture releases its synchronization handle in `finally`; it creates no worker,
container or browser process. Hosted compilation and actual execution remain
mandatory, including the real producer relationship-fence tests.

The separate joined project passed six actual local Program executions across
four methods: ordered identity/address/profile persistence, an ordinary Employee
relation move before the coupled write, three lost committed-address acknowledgement
variants (HTTP, transport/acknowledgement I/O and noncaller cancellation), and a
real new-address creation/link using the producer-confirmed ID. Its 30-minute
hosted workflow verifies immutable producer/dependency HEADs before strict build.
The bounded verifier binds exact method multiplicities and canonical execution
identities to definitions and all 16 counters. Forty-two offline synthetic parser
controls passed previously; the current parser revision passed 46 offline controls.
These do not establish native joined execution. Only a fresh
sanitized aggregate/method/graph/TRX-hash proof is uploaded, never raw TRX,
assertion snapshots, parameters or output. One class fixture owns the PG18/Redis7
containers; each case retains independent GUID databases, Redis namespace, hosts,
keys and sessions. Cleanup is attempted with bounded waits, not guaranteed shutdown.
The joined workflow now pins accepted protected-main Auth
`ef74d99f56cb53b600efc7f85819d9adc28f8935` and Employee
`8a8a619b021e9dc71607fa279aa12257844b6359` in checkout, pre-build checks and
the sanitized proof verifier. Older producer receipts above are historical,
not acceptance of this exact graph. Causal producer review confirms unchanged
identity DTOs, nullable lockout, version framing and genuine actor grants, and
unchanged coupled Employee writer behavior. Fresh hosted joined validation remains pending.
Setup still rejects missing genuine issuer grants. No fixture grants are
manufactured and no service credential replaces the employee actor. Test-only
SSH.NET `2026.0.0` overrides Testcontainers 4.10's older transitive dependency;
native restore/audit remains required, with no warning waiver.

Before a coherent commit/PR is admitted, require Release build with zero warnings
and errors, focused and affected full hosted suites, raw coverage with unchanged
floors, formatting/security checks, relevant browser/joined acceptance and actual
protected-main/fresh-main evidence. Maintain per-source-SHA tracking. Completing
this action must not close the whole mixed-owner initial source commit.

No production data synchronization, provider operation, deployment, infrastructure
or traffic change is part of this code-only slice.
