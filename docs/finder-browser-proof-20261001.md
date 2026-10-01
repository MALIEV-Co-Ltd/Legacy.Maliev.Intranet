# Finder envelope browser proof

## Independent root integration verification (October 2 Bangkok)

Root inspected the complete new browser test, four-file diff, controlled Thai conflict screenshot and final TRX counters directly. Independently executed the whole-solution Release build with the existing coverage-symbol/private-dependency flags: zero warnings/errors; actual Chromium finder focus 18 passed and existing HTTP focus 29 passed, zero failures/skips. Whole-solution formatting, whitespace and unsuppressed four-file secret scans passed. Full suites were not duplicated: directly inspected browser 154/154 and unit 1430/1430 evidence, with the unchanged BFF/Server/Contracts coverage gate passing.

Verified full TRX SHA-256: browser `32FF35903B40331DF1DDF0804FF2DBBA16E64C17D54EFC49FC807409A879F88A`; unit `AC0BDDF4655258A25E32F4457D1E801021A72287A77A66437153F95D27001B65`. These remain intercepted controlled-browser proofs, not normal-cookie/PostgreSQL or joined Aspire acceptance. Parent PR242 required CI remains pending; no merge or deployment is asserted. Unrelated `.superpowers/` files remain preserved and excluded.

## Scope and provenance

This isolated slice starts at PR242 candidate `ac1166e64476872b25983409e58863e78284131b`, not an assertion that the candidate is accepted main. Root review and integration sequencing remain required. Worktree: `B:\maliev-legacy\.worktrees\intranet-finder-browser-20261001`; branch: `codex/intranet-finder-browser-20261001`. Execution began on 2026-10-01 Bangkok time and continued after midnight on 2026-10-02.

Owned files are the new browser test, this document, the explicitly authorized request-save state repair in `Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/View.razor`, and exactly two private-field selectors in `Legacy.Maliev.Intranet.Tests/QuotationRequestFinderEnvelopeHttpTests.cs`. Those selectors change `error` to `saveError`; expected messages, response statuses, no-reload, no-mutation, timestamps, and every other old assertion are unchanged. Existing fixtures, helpers, resources, API, authentication, schema, projects, workflow, and qualification-authority code are unchanged. Unrelated `.superpowers/` artifacts are excluded and preserved.

Private, ignored dependency checkouts under `.worktrees/.dependencies` are clean at existing CI pins:

- ServiceDefaults `d22f0e6f95254b10cf4fe891c8dce5df7c419f3f`.
- CompatibilityContracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`.

The unchanged Playwright package is `Microsoft.Playwright.Xunit` 1.61.0. Its installed Chromium/headless-shell revision is 1228, browser version 149.0.7827.55. No browser installation or upgrade was performed.

## What the tests prove

`QuotationRequestFinderBrowserTests` uses unchanged `Infrastructure/PlaywrightFixture`, `Infrastructure/IntranetClientServerFixture`, and `CustomerBrowserCollection`. The compiled Release Blazor client runs on the existing isolated loopback server; installed pinned Chromium reads and operates the actual DOM. Each browser context selects `en-TH` or `th-TH` through the application's existing culture storage path.

The BFF routes are explicitly intercepted. The session projection is exactly the existing `QuotationDecisionBrowserTests` employee/session/permission projection, not a new permission grant. These tests prove UI and captured client wire behavior, not normal cookie authentication, real BFF forwarding, PostgreSQL persistence, joined Aspire, or production parity.

Eighteen new cases exercise localized finder summary labels/path, encoded hostile operator text including a closing textarea tag, editable operator note rather than raw valid envelope JSON, trimmed/cleared notes, preservation of every original context and unknown extension property, and malformed/ordinary/future-version/duplicate/unknown-ID/null or object operator-note plain fallback. Captured PUTs contain the exact eleven existing request fields, original loaded timestamp, and session CSRF header. Failures 409 in both cultures, 403, 429, 502, network abort, and success-then-denied save retain the visible local note without an automatic GET, controlled-state mutation, or false saved notice.

The independent hostile note is `</textarea><img id=finder-injected src=x onerror=window.finderInjected=true> ไทย / English`. The DOM must retain it as the textarea value without an injected node or executable flag. Unknown extension properties are retained in the submitted envelope but are not rendered as summary content. No source substring assertion substitutes for actual browser interaction.

## Genuine RED and bounded repair

At candidate baseline, the initial 13-case focused run had 11 passing controls and two genuine EN/TH stale-409 failures. The unfiltered baseline with those tests had 149 total, 147 passed, two failed, zero skipped. The localized conflict was visible and the wire/context/no-GET controls passed, but the fatal `error` branch removed the editor from the DOM.

Five additional failure controls were added before runtime changes. The expanded focused RED was 18 total, 11 passed, seven failed, zero skipped; all seven failed because the save error removed the local-note editor. RED TRX: `TestResults/finder-browser-expanded-red/finder-browser-expanded-red.trx`.

The approved repair separates request-save `saveError` from load/fatal `error`, renders an encoded localized destructive alert within the still-visible detail branch, clears save error and stale notice at submit start, and handles ordinary HTTP/cancellation failures as save unavailable. The edit context, loaded ModifiedDate, finder envelope merge, CSRF header, API contract, qualification authority, and fatal-load branch remain unchanged. No automatic conflict refresh or force overwrite was introduced.

## Commands and observed gates

Commands run from the isolated worktree with `MalievWorkspaceRoot` set to its absolute private dependency directory, `UseLocalMalievDependencies=true`, and test-host `DOTNET_PROCESSOR_COUNT=1`, matching the existing CI isolation convention.

```powershell
dotnet build Legacy.Maliev.Intranet.BrowserTests\Legacy.Maliev.Intranet.BrowserTests.csproj -c Release --no-restore -p:MalievWorkspaceRoot=B:\maliev-legacy\.worktrees\intranet-finder-browser-20261001\.worktrees\.dependencies -p:UseLocalMalievDependencies=true --nologo
dotnet test Legacy.Maliev.Intranet.BrowserTests\Legacy.Maliev.Intranet.BrowserTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~QuotationRequestFinderBrowserTests --logger 'trx;LogFileName=finder-browser-green.trx' --results-directory TestResults\finder-browser-green --nologo
dotnet test Legacy.Maliev.Intranet.BrowserTests\Legacy.Maliev.Intranet.BrowserTests.csproj -c Release --no-build --no-restore --logger 'trx;LogFileName=finder-browser-final-full.trx' --results-directory TestResults\finder-browser-final-full --nologo
dotnet build Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release -p:MalievWorkspaceRoot=B:\maliev-legacy\.worktrees\intranet-finder-browser-20261001\.worktrees\.dependencies -p:UseLocalMalievDependencies=true --nologo
dotnet test Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~QuotationRequestFinderEnvelopeHttpTests --logger 'trx;LogFileName=finder-browser-http-control.trx' --results-directory TestResults\finder-browser-http-control --nologo
dotnet build Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-restore -p:EnableCoverageSymbols=true -p:MalievWorkspaceRoot=B:\maliev-legacy\.worktrees\intranet-finder-browser-20261001\.worktrees\.dependencies -p:UseLocalMalievDependencies=true --nologo
dotnet test Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore -p:EnableCoverageSymbols=true --collect:'XPlat Code Coverage' --settings coverage.runsettings --logger 'trx;LogFileName=finder-browser-unit-coverage-final.trx' --results-directory TestResults\finder-browser-unit-coverage-final --nologo
pwsh -NoProfile -File scripts\verify-test-coverage.ps1 -CoverageFile TestResults\finder-browser-unit-coverage-final\5a63485f-143c-42cb-8219-9082d5df24a2\coverage.cobertura.xml
```

Observed results:

- Browser and unit Release builds: zero warnings and errors; explicit CI coverage-symbol rebuild also zero warnings and errors.
- Existing baseline decision browser control: 1 passed, zero failures/skips.
- Repaired finder Chromium focus: 18 passed, zero failures/skips (37 seconds).
- Unfiltered repaired Browser suite: 154 passed, zero failures/skips (7 minutes 18 seconds).
- Existing HTTP/BFF finder controls: 29 passed, zero failures/skips.
- Initial unit full: 1430 passed, zero failures/skips (13 minutes 35 seconds). Its coverage report is diagnostic only: BFF 88.40% and Server 89.20% exceeded thresholds, but Contracts was missing because this first build omitted existing CI `EnableCoverageSymbols=true`. The Contracts project deliberately suppresses Release symbols without that flag. No assertions/configuration were relaxed.
- Final unchanged unfiltered unit rerun after the correct CI coverage-symbol build: 1430 passed, zero failures/skips (11 minutes 48 seconds). The unchanged repository coverage gate passed: BFF 88.40% (minimum 80%), Server 89.20% (minimum 85%), Contracts 96.86% (minimum 95%). Final TRX/report are under `TestResults/finder-browser-unit-coverage-final/`.
- Targeted `dotnet format whitespace --no-restore --verify-no-changes` on the new browser file and the existing HTTP test: passed.
- `git diff --check`: passed.
- `gitleaks dir <owned-file> --no-banner --redact` on each of the three code files and this document: no leaks.
- `dotnet list <BrowserTests-or-Tests.csproj> package --vulnerable --include-transitive --format json`: no reported vulnerable packages in either target.

Ignored screenshots under `TestResults/finder-browser-proof/output/playwright/` include EN/TH finder summaries and failed-save local-note editor states. They are controlled-fixture UI captures, not deployed environment evidence. The repaired Thai conflict capture was directly viewed and shows the preserved local-note editor. Separate DOM assertions establish the localized alert and summary above the scrolled editor.

## Full source records retained separately

Frozen source checkpoint: `bed10c7d15e0698e0b75f1329d0f312937f5d77f` in read-only source mirror. No source object or migration ledger is modified. The following records remain independently tracked, not collapsed into one owner-completion claim:

| Source SHA | Parent | Subject | Quotation-owned path and disposition |
| --- | --- | --- | --- |
| `9b8146c4ac78397959fe1689daf96f400cfd45e8` | `43ec8aeafe977e918693771de7254867da81d0b7` | feat(web): track persisted service finder journeys | `Maliev.QuotationRequestService.Common/ServiceFinderMetadataEnvelope.cs`; behavior proof bounded here, owner disposition remains pending root ledger review. |
| `476611058a0aba44177c98efceae2d22f8aa7858` | `0afa6ad9105f794315ab88f414870f338a838c19` | feat(web): contextualize service finder options | Same helper path; physical-part ID behavior retained, owner disposition independently pending. |
| `2b35ab97bc656d6cae64c6aa0288c18f7b2f4bbd` | `476611058a0aba44177c98efceae2d22f8aa7858` | feat(web): add adaptive service finder refinements | Same helper plus `Maliev.QuotationRequestService.Common/Maliev.QuotationRequestService.Common.xml`; optional refinement behavior retained, generated companion path separately pending. |
| `b7539baa75d26d1ee0e1471c413005091137dae8` | `4dea8bfe6b37a251f17a6715276f81aba339394e` | chore(repo): track deployment and XML documentation updates | `Maliev.QuotationRequestService.Common/Maliev.QuotationRequestService.Common.xml` only; independent generated-artifact disposition pending; no tracked compiler output restored. |

Original Intranet View producer/consumer paths and other source owners are not marked complete by this browser slice. Root-owned producer PostgreSQL acceptance remains separate evidence, not supplied by these controlled interceptions. Whole-owner completion, normal-cookie joined browser acceptance, deployment, and migration-authority enablement are excluded. SQL/provider/PayPal, Web, Accounting, CNC, schema, infrastructure, and persistent production data are untouched. No commits, pushes, GitHub changes, or deployment occurred in this slice.
