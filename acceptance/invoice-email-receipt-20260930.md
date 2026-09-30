# Invoice email receipt consumer acceptance — issue 233

## Scope and source classification

Intranet base: `25425a817367d61c8e98f8676a41b9aed4c19b84` (main CI `36678241378`, success checked before implementation). Isolated branch: `codex/intranet-invoice-email-receipt-20260930`. Canonical divergent/dirty work, original repositories, producer code, deployment, permissions, and migration ledgers were not changed. The delegated writer did not commit or push; root integrates this bounded slice through the owner-authorized protected-main PR workflow.

Accounting committed `0ec928ee470e29777151e8028b3f300a93f5b538` was source-inspected: `Completed=0`, `Reconciled=1`; email `NotRequested=0`, `Delivered=1`, `ExplicitRetryRequired=2`. A reconciled existing invoice with requested email returns a positive invoice ID, state 1, email state 2 and null provider message ID without sending another notification. The controller exposes this result as HTTP 200. This is a successful invoice receipt requiring explicit email reconciliation, not an invented `EmailFailed` or `NeedsReconciliation` successful enum.

This consumer fix does not establish actual Intranet → Auth → Accounting employee-chain execution or actual notification-publisher execution. Broader issue 197/Accounting 23/Auth 97 and source c821/283 migration-ledger claims remain pending. No API versioning or preserved-route changes were introduced.

## Behavior

- A positive invoice ID with known state 0/1 and email state 2 stays on the create page, displays an English/Thai explicit reconciliation notice and `/Invoices/View?id=N` link, and locks editing and duplicate submission. The existing opaque session marker remains `1`.
- A positive ID with missing, unknown, or wrong-typed state/email state retains the invoice reference, displays an unconfirmed-outcome warning, and blocks retry/new creation. It does not infer success, resume, or a new operation.
- Duplicate state/email-state fields are ambiguous even when their last values are known numbers. Duplicate invoice-ID fields block retry/navigation without choosing a reference, even when both IDs agree. Only a uniquely valid invoice ID produces an existing-invoice link.
- Known state 0/1 and email state 0/1 retain the original guard clearing and invoice-view navigation.
- Without a positive ID, existing frozen same-operation retry and reload safeguards remain. Reload retains only the opaque marker, not invoice intent, identity, customer data, or invoice ID; after reload explicit support reconciliation is still required.
- No automatic email, status polling, resume endpoint, new UUID, or second create request is introduced. Existing CSRF, permission, routes, delegation default-off and request wire remain unchanged.

Browser receipt presence/type checks apply to browser-visible JSON only. The unchanged BFF mapper first deserializes integer DTO properties: absent producer properties can be defaulted to zero before serialization to the browser. This slice does **not** claim to detect that upstream omission. Additional mapper/published-receipt fixture work requires separately approved scope.

## Evidence and validation

Dependencies were local no-fetch clones of committed objects into ignored `TestResults/.dependencies`, pinned to ServiceDefaults `d22f0e6f95254b10cf4fe891c8dce5df7c419f3f` and CompatibilityContracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. Build flags: `-p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/intranet-invoice-email-receipt-20260930/TestResults/.dependencies` (final build also `-p:EnableCoverageSymbols=true`).

Baseline Release solution build: 0 warnings/0 errors; focused BFF invoice behavior 15/15; existing invoice create browser tests 8/8, no skips. Test-only RED: the positive-ID state1/email2 case failed for the missing notice; five unknown/missing/wrong-type cases also failed for missing fail-closed notice/reference behavior. Generated TRX evidence is ignored under project `TestResults` directories.

Browser tests exercise the real production WASM component with intercepted session/preview/create HTTP responses and disposable Chromium contexts/loopback development host. They cover English/Thai, light/dark, 375/1440 widths, keyboard submission/link focus, overflow checks, one create POST, locked fields/submit, preserved marker, seven unconfirmed receipts, two duplicate invoice-reference receipts, and four normal navigation combinations. The normal-navigation destination GET is intercepted with 503 so its unrelated BFF/auth behavior cannot change the navigation assertion. This is intercepted UI proof, **not** integrated employee authentication/delegation or live Accounting/notification proof. BFF unit assertions pin numeric state/email-state/provider-message passthrough.

The first full unit run passed 1234 and failed 1 of 1235 (no skips): its image-lock source audit included proof-only dependency projects from initial `TestResults/dependencies`, because its existing exclusion is named `.dependencies`. This artifact-layout failure is separate from application behavior. After the run terminated, both old/new absolute paths were checked beneath this worktree's TestResults, and only owned clones were moved to `TestResults/.dependencies`. Build/test validation is repeated against the final path. An initial focused browser run passed 24/25; the one destination-boundary timeout was corrected by intercepting the unrelated invoice-view GET, then focused tests passed 25/25 before duplicate cases were added.

Final-path validation:

- `dotnet build Legacy.Maliev.Intranet.slnx -c Release --nologo` with the dependency/coverage flags above: 0 warnings, 0 errors (14.17 seconds).
- `dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~InvoiceAccountingBehaviorTests`: 15 passed, 0 failed, 0 skipped.
- `dotnet test Legacy.Maliev.Intranet.BrowserTests/Legacy.Maliev.Intranet.BrowserTests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~InvoiceCreateRetryBrowserTests|FullyQualifiedName~InvoiceCreateRenderBrowserTests'`: 29 passed, 0 failed, 0 skipped (1 minute 13 seconds).
- Full unit run with `--collect:'XPlat Code Coverage' --settings coverage.runsettings --results-directory TestResults/issue233-unit-final`: 1235 passed, 0 failed, 0 skipped (4 minutes 29 seconds). `scripts/verify-test-coverage.ps1` on its Cobertura report passes: BFF 87.37% (minimum 80%), Server 87.83% (85%), Contracts 96.05% (95%).
- `dotnet test Legacy.Maliev.Intranet.BrowserTests/Legacy.Maliev.Intranet.BrowserTests.csproj -c Release --no-build --no-restore`: 136 passed, 0 failed, 0 skipped (8 minutes 34 seconds); terminal process exit 0. Evidence: `Legacy.Maliev.Intranet.BrowserTests/TestResults/final-full-browser.trx`.

Static checks: changed C# files pass `dotnet format Legacy.Maliev.Intranet.slnx --verify-no-changes --no-restore --include Legacy.Maliev.Intranet.BrowserTests/InvoiceCreateRetryBrowserTests.cs Legacy.Maliev.Intranet.Tests/InvoiceAccountingBehaviorTests.cs`; `git diff --check` passes; both resource XML files parse with identical unique key sets; all six changed files pass strict UTF-8 readback. An allowed-file check confirms exactly the six authorized source/document files. Scoped security/source review confirms normal CSRF/UUID headers, encoded localized output and integer-only invoice links; no unsafe HTML, credentials, new browser persistence or MudBlazor is introduced. Duplicate numeric fields are rejected; these UI safeguards are not server authorization.

The full browser suite's existing `CustomerDetailBrowserTests.FinalGateCapturesEveryCustomerWorkspaceTabAcrossRequiredModes` also generated 29 PNG artifacts beneath `.superpowers/sdd/2026-08-12-customer-history-site-links-plan/task7-captures` (timestamps within this run). These are unrelated suite output, not candidate source changes; they are preserved and must not be staged. They do not prove this invoice notice's visual appearance. All unit/browser processes are terminal at handoff; root owns subsequent output management and independent validation.

Excluded checks: no cloud/persistent Aspire deployment, live employee delegation flow, actual notification sending, Docker image build or unrelated joined customer/auth acceptance run was performed. Those checks are not replaced by intercepted browser tests. No external state, source migration ledger or feature enablement was changed.

## Independent root acceptance

Root reviewed the complete six-file candidate and preserved all unrelated generated captures. Independent exact-pinned Release build passed with zero warnings/errors. Root focused unit tests passed 15/15 and focused browser tests passed 29/29, with zero skips. The complete unit suite passed 1235/1235 (5 minutes 55 seconds); the complete browser suite passed 136/136 (11 minutes 9 seconds). Durable ignored TRXs are under `TestResults/root-issue233/`: `root-invoice-focused.trx`, `root-invoice-browser-focused.trx`, `root-invoice-unit-full.trx`, and `root-invoice-browser-full.trx`. These are disposable/intercepted acceptance results, not production-derived Aspire or integrated notification evidence.

## Next publisher-consumer proof (not implemented)

A separately authorized bounded fixture should obtain an actual published Accounting workflow receipt for an already-matching invoice with `SendEmail=true`, assert positive ID/state1/email2/null provider ID and zero notification effects, pass that receipt through the actual Intranet mapper, and exercise this UI. Repeat with delivered/not-requested receipts and retained durable replay. Stub only external workflow dependencies, use disposable storage, and retain actual serializers and mapper. The present hard-coded browser receipt cannot substitute for this regression proof.
