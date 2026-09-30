# Employee reset trusted-origin request-boundary acceptance

## Scope and provenance

Test-only coverage on protected Intranet main
`25425a817367d61c8e98f8676a41b9aed4c19b84`, whose required
`validate / validate` check passed in run `36678241378`.
The older canonical local main `f8dde101c12ca71c2464c5712f18b88a158a6d1d`
used request Scheme/Host before canonical convergence; it is preserved on
`codex/preserve-intranet-main-f8dde101-20260930`, not the current main baseline.

Issue #227 / PR #228 already migrated reset and confirmation callbacks to the
configured HTTPS `EmployeeConfirmation:PublicOrigin` at
`455b81ca90ffb3e018d0e9dad6bcb07cd9426071`. This patch does not fix that
runtime behavior again. Mapper source remains byte-identical to the baseline.
PR #61 (`cad8917536281361400daacb6700a768e0eafe08`) introduced the WASM/BFF
recovery contract. Relevant original source is
`5e2030b7339d4d9bd699fd8c3f406b71706b377d`, including
`Maliev.Intranet/Pages/Employees/ForgotPassword.cshtml.cs` and
`Maliev.Intranet/Pages/Employees/ResetPassword.cshtml.cs`. These original objects
were inspected read-only; this evidence does not close their whole owner scope.

## Exercised boundary

`BffEmployeeRecoveryContractTests` uses the real BFF TestServer host, session
antiforgery cookie/token acquisition, registered anonymous POST route, mapper,
Auth proxy, notification proxy, JSON serialization and HTML callback encoding.
Only external Auth/Notification HTTP calls are replaced by the existing
synthetic recording handler. No live account, email, database or credential is
used. Expectations derive from literal fixture values, not the callback builder.

Added coverage:

- Host, X-Forwarded-Host, and combined Host/Forwarded headers cannot select the
  reset callback authority; HTTPS is retained so scheme downgrade does not
  short-circuit the callback test at antiforgery.
- A valid explicit HTTPS origin with port 8443 produces the expected reset path;
  plus-addressed email and an opaque token containing `+ / = & ?` survive the
  HTML-link/URI/query boundary without injection or extra query parameters.
- Missing, empty, whitespace, malformed, HTTP, userinfo, path, query and fragment
  origins return 503 before any Auth challenge or notification call. Browser
  error output contains neither fixture email nor challenge token.
- Both an HTTP 503 notification result and a transport exception retain the
  same generic accepted body as an unknown identity, with one delivery attempt.

Existing CSRF rejection, known/unknown enumeration safety, JSON-only completion,
password mismatch, confirmation failures and WASM route contracts remain.
No runtime retry, DTO, permissions, rate limit, CSRF, configuration or UI change.

## Validation

With `DOTNET_PROCESSOR_COUNT=1` and `MalievWorkspaceRoot=B:\maliev-legacy`:

1. Baseline Release solution build: zero warnings/errors.
2. Baseline full `Legacy.Maliev.Intranet.Tests`: 1,235 passed, zero failed/skipped
   (15m54s).
3. Final changed Release solution build: zero warnings/errors.
4. Focused BFF recovery plus WASM migration contracts: 24 passed, zero
   failed/skipped (18s).
5. Final full `Legacy.Maliev.Intranet.Tests`: 1,249 passed, zero failed/skipped
   (8m33s). Result: `Legacy.Maliev.Intranet.Tests/TestResults/employee-reset-origin-suite.trx`.
6. Changed C# format verification, `git diff --check`, and redacted gitleaks
   stdin scan of the test diff: passed; 9.33KB scanned, no leaks.

The initial fixture run had four failures: scheme-downgrade headers returned 400,
and exact percent-encoding assertions overconstrained valid callback output.
A second run had three remaining fixture failures because `@` is permitted
unescaped in these query values. The HTTPS fixture, corrected literal callback
and semantic URI/query assertions resolved those assumptions; the final focused
run passed 24/24. No production edit or runtime RED/GREEN claim resulted.

Commands: `dotnet build Legacy.Maliev.Intranet.slnx -c Release --no-restore
-p:MalievWorkspaceRoot=B:\maliev-legacy -m:1`; `dotnet test
Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj -c Release
--no-build --no-restore` with the focused class filter, then unfiltered with
`--logger 'trx;LogFileName=employee-reset-origin-suite.trx'`; `dotnet format
Legacy.Maliev.Intranet.slnx --verify-no-changes --no-restore --include
Legacy.Maliev.Intranet.Tests/BffEmployeeRecoveryContractTests.cs`.

## Deliberate limitations and independent gap

### Independent exact-pinned root validation

Root reran Release with zero warnings/errors against the CI-pinned
ServiceDefaults `d22f0e6f95254b10cf4fe891c8dce5df7c419f3f` and Contracts
`78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`, using the ignored dependency root
in the invoice-email-receipt worktree's `TestResults/.dependencies`.
The BFF focused run passed 23/23; the separately corrected class filter
`FullyQualifiedName~EmployeeRecoveryWasmMigrationContractTests` passed 1/1.
The complete unit suite passed 1,249/1,249 with zero skips (11m8s).
Scoped `dotnet format --verify-no-changes --no-restore --include` and diff checks
passed. TRX files are retained, ignored, under `TestResults/root-origin/`:
`root-origin-focused.trx`, `root-origin-full.trx`, and `root-origin-wasm.trx`.
The first root combined filter matched only the 23 BFF cases; the supplemental
WASM run is recorded separately rather than claiming that filter covered it.

The retained Razor executable maps anonymous employee recovery URLs to
`Pages/AnonymousLegacyRoute.cshtml`, which renders a migration placeholder and
correlation ID, not a functional recovery form/POST handler. This is an
independent functional rollback gap; inspection is not a runtime/browser proof
and this test-only patch does not repair or certify that lane.

TestServer proof is not deployed proxy/TLS/cookie, service permission, real
provider delivery or rendered browser acceptance. Issue #229 retains the
deployment callback/permission/end-to-end release gate. No deployment, live
service call or source mutation was performed. This evidence slice is submitted
through protected-main review; it does not close #229 or the Razor rollback gap.
