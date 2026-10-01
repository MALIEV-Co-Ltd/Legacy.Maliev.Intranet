# Finder envelope preservation: implementation review checkpoint

Issue: https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/issues/241

Exclusive baseline: `40275ec6c5b3547ab6d5363fcb8cc03b82d3f473`.
Root approved the minimal helper, compiled View, existing EN/TH resources and
regression tests after independently reproducing the initial RED checkpoint.
Projects, CI, API/schema/auth changes, commits, pushes and external mutations
remain excluded. Implementation is frozen for root review after local gates.

## Independent source records

| Full SHA | Parent | Subject | Quotation-owner paths |
| --- | --- | --- | --- |
| `9b8146c4ac78397959fe1689daf96f400cfd45e8` | `43ec8aeafe977e918693771de7254867da81d0b7` | feat(web): track persisted service finder journeys | `Maliev.QuotationRequestService.Common/ServiceFinderMetadataEnvelope.cs` |
| `476611058a0aba44177c98efceae2d22f8aa7858` | `0afa6ad9105f794315ab88f414870f338a838c19` | feat(web): contextualize service finder options | `Maliev.QuotationRequestService.Common/ServiceFinderMetadataEnvelope.cs` |
| `2b35ab97bc656d6cae64c6aa0288c18f7b2f4bbd` | `476611058a0aba44177c98efceae2d22f8aa7858` | feat(web): add adaptive service finder refinements | `Maliev.QuotationRequestService.Common/ServiceFinderMetadataEnvelope.cs`; `Maliev.QuotationRequestService.Common/Maliev.QuotationRequestService.Common.xml` |
| `b7539baa75d26d1ee0e1471c413005091137dae8` | `4dea8bfe6b37a251f17a6715276f81aba339394e` | chore(repo): track deployment and XML documentation updates | `Maliev.QuotationRequestService.Common/Maliev.QuotationRequestService.Common.xml` |

All four Quotation owner records are still pending, migration-required. Generated
XML needs an independent approved generated-artifact disposition, not restored
tracked compiler output. The first source also owns Intranet's original
`Maliev.Intranet/Pages/QuotationRequests/View.cshtml` and `View.cshtml.cs`.
Every Web path and other owner remains separately pending; this slice cannot
resolve their complete source commits. Source objects are read-only at frozen
`bed10c7d15e0698e0b75f1329d0f312937f5d77f`, not the mirror's main ref.

## Actual boundary and proposed repair

The source Web validates stable finder IDs and submits a version-1 JSON envelope
in `InternalComment`. Target Web already does this via
`Pages/Shared/ServiceFinderMetadataEnvelope.cs`, `ServiceFinderAttribution.cs` and
`Pages/Quotation/Index.cshtml.cs`. The helper supports physical parts plus optional
performance/environment answers, ordered paths and operator-comment merging.
Target Quotation persists this nullable string; JourneyId/TransactionId are
separate server-owned properties and must remain unchanged.

Source Intranet reads only the envelope's operator note into the editor, shows a
finder summary, and merges the edited note into the existing envelope. Current
target `Client.Features.Quotations/Pages/QuotationRequests/View.razor` instead
puts the entire raw JSON into its editable note and submits the replacement.
The existing BFF `QuotationRequestsProxy` forwards it verbatim, so ordinary note
editing erases finder context downstream.

After genuine RED and root approval, proposed runtime work is a browser-safe,
Intranet-owned helper and request editor integration, preserving five required
allowlisted answers, optional answers, recommendations/path, extra envelope
properties and trimmed/cleared operator_comment. Keep plain-note fallback for
invalid, unsupported or ordinary strings. Display encoded/localized context
using existing Shadcn primitives and English/Thai resources, not a new library.
No Quotation schema/API, auth, provider delivery or qualification activation change.
Keep the loaded ModifiedDate; do not refresh it independently before saving and
thereby silently overwrite another employee's changes.

## Test boundary

`QuotationRequestFinderEnvelopeHttpTests` invokes the compiled Razor page's actual
load/edit/save path using the same narrow reflection pattern as existing
`QuotationDecisionUiBehaviorTests`. This is not DOM/browser/visual validation.
Its HttpClient traverses an actual WebApplicationFactory BFF host with real
cookie login, policy checks, CSRF enforcement, endpoint mapper, typed proxy and
service-authentication handler. Only external Auth and Quotation transports are
controlled. The external Quotation boundary is a stateful wire recorder, not a
fake PostgreSQL repository and not evidence of producer transaction semantics.

Literal fixtures assert outgoing envelope content and returned editor state:
required answer IDs, files-real-part, optional performance/environment, ordered
recommendations/path, extension preservation, changed/cleared operator note.
Controls prove ordinary notes, unchanged expected timestamp, forbidden employee
write and downstream 409 without a successful reload. These tests intentionally
remain RED until the implementation gate is approved. Do not assert source
substrings as proof of the metadata-loss defect.

Private ignored dependencies match existing CI pins exactly:
ServiceDefaults `d22f0e6f95254b10cf4fe891c8dce5df7c419f3f` and
CompatibilityContracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`.
No sibling checkout/output is shared or modified.

## Deferred acceptance and exclusions

After implementation: build first with 0 warnings/errors, focused tests, affected
suite, formatting/static/security/coverage, independent review and exact-main CI.
Browser/visual/full-suite execution is deferred to root sequencing while Web
owns the exclusive browser/full window. Quotation producer HTTP/PostgreSQL string
persistence, stale update and JourneyId/TransactionId preservation proofs are a
separate root-sequenced lane. Summary rendering and invalid/unsupported envelope
fallback require additional helper/component tests in the implementation slice.

No source/mirror edits, source fetch, production data, containers, paid APIs,
provider calls, deployment, infra, authority activation, SQL Server, PayPal, CNC
expansion, Web email/header work, Accounting receipts or unrelated cleanup.

## Executed RED checkpoint

Commands ran in this exclusive worktree; the dependency property is the absolute
private `.worktrees/dependencies` directory, never sibling output:

```powershell
dotnet build Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj -c Release -p:MalievWorkspaceRoot=B:\maliev-legacy\.worktrees\intranet-finder-envelope-20261001\.worktrees\dependencies -p:UseLocalMalievDependencies=true --nologo
dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore -p:MalievWorkspaceRoot=B:\maliev-legacy\.worktrees\intranet-finder-envelope-20261001\.worktrees\dependencies -p:UseLocalMalievDependencies=true --filter FullyQualifiedName~QuotationRequestFinderEnvelopeHttpTests --nologo
dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore -p:MalievWorkspaceRoot=B:\maliev-legacy\.worktrees\intranet-finder-envelope-20261001\.worktrees\dependencies -p:UseLocalMalievDependencies=true --filter FullyQualifiedName~OrdersQuotationsBffBehaviorTests.QuotationRequestDetailAndUpdate --nologo
```

Baseline and post-test Release builds: **0 warnings / 0 errors**. New tests:
**7 total, 4 expected regression failures, 3 passing controls, 0 skipped**.
The existing request detail/update BFF control: **1 passed**.

- Load failure: expected `original staff note`, actual entire finder JSON.
- Normal save failures, optional and legacy envelopes: actual downstream string
  `reviewed by staff` instead of a preserved reserved JSON envelope.
- Cleared-note failure: actual downstream empty string instead of envelope.
- Passing controls: plain-note roundtrip with original concurrency header;
  downstream 409 leaves external state unchanged and avoids reload; missing
  employee update permission forbids before downstream PUT.

Early fixture attempts failed at the existing login domain check (401), not the
product behavior. Corrected only test data/EmployeeIdentity argument ordering;
the reported four RED cases above reached the actual editor and quotation wire
boundary. At that initial checkpoint no runtime repair had been attempted.
No commit was created.

Targeted `dotnet format whitespace` with the private dependency environment,
`--no-restore --verify-no-changes --include` the new test: passed (exit 0).
Direct new-file `git diff --check --no-index -- NUL <file>` checks: no whitespace
errors for test or design (Git reports the repository's expected LF-to-CRLF
conversion notice for the new C# file). Final status contains only these two
owned untracked files at that initial checkpoint; both private dependency checkouts were clean.

## Approved minimal implementation and expanded gates

The Intranet-owned browser-safe `Models/ServiceFinderMetadataEnvelope.cs` reads
exact source version-one stable IDs. Unsupported versions, malformed values,
unknown known-field IDs, invalid service paths and duplicate root/answers
properties fall back to ordinary note text. Typed display exposes only known
answers; merge retains all other root and nested answer extension properties.
Clearing the trimmed operator note removes only `operator_comment`.

`View.razor` edits only the parsed operator note and merges against the originally
loaded `InternalComment`. The originally loaded `ModifiedDate` remains the
expected version; no fresh-version GET precedes PUT. BFF, DTO, permissions,
CSRF and service authentication implementations are unchanged. The existing
Shadcn alert renders Razor-encoded stable-ID labels from `View.resx` and
`View.th.resx`; it never displays extension fields or injects raw HTML.

Expanded tests before implementation: **44 total, 7 genuine failures, 37 passing
controls, 0 skipped**, recorded in
`TestResults/finder241-expanded-red/finder-expanded-red.trx`. The two additional
summary failures executed the actual Router/RouteView in English/Thai and found
no summary element; the additional HTTP failure lost an unknown answer extension.

After implementation: Release builds including coverage symbols **0 warnings /
0 errors**; focused component/HTTP/helper regressions **65 passed, 0 failed,
0 skipped** (`TestResults/finder241-focused/finder-final-focused.trx`). Targeted
`dotnet format whitespace --verify-no-changes` passed. Resource XML parsing,
duplicate-key rejection and EN/TH key parity passed (106 keys per culture).
`gitleaks dir` found no leaks in the quotations feature or Tests directory;
NuGet test-project transitive vulnerability audit reported none.

First full-suite attempt found an unchanged DeliveryContract project-discovery
failure because private dependencies under `.worktrees/dependencies` were not
excluded. Root approved relocating only those private ignored fixtures, after
resolved-path and absent-target verification, to `.worktrees/.dependencies`.
Both independent checkout revisions remained exact and clean:
ServiceDefaults `d22f0e6f95254b10cf4fe891c8dce5df7c419f3f`,
CompatibilityContracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`.
No discovery assertion or project configuration was weakened.

The first diagnostic full run finished **1,417 total / 1,416 passed / 1 fixture-
layout failure / 0 skipped** in 9m03s, preserved in `TestResults/finder241-full`.
Its coverage gate passed (BFF 88.40%, Server 89.20%, Contracts 96.86%) but is not
final acceptance because that run had the fixture-discovery failure.

Root review identified that a present non-string `operator_comment` could be
silently treated as empty and lost on save. Four new inputs (number, object,
array and JSON null) reproduced **12 genuine failures / 66 controls passed /
0 skipped** across helper, actual HTTP editor and rendered component cases
(`TestResults/finder241-reserved-note-red/finder-reserved-note-red.trx`). A tiny
optional-property type guard now requires string when that reserved property is
present. Deliberately stricter than the legacy helper, JSON null also falls back
to ordinary text to avoid relabeling/dropping persisted malformed data. Absent
or empty-string notes remain valid context, proven by a separate control. No
Web producer changed; its normal envelopes omit or stringify the staff note.

## Final frozen local gates

Final build used the relocated private directory, completed before focused/full
tests, and reported **0 warnings / 0 errors**:

```powershell
dotnet build Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj -c Release --no-restore -p:MalievWorkspaceRoot=B:\maliev-legacy\.worktrees\intranet-finder-envelope-20261001\.worktrees\.dependencies -p:UseLocalMalievDependencies=true -p:EnableCoverageSymbols=true --nologo
dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~QuotationRequestFinder|FullyQualifiedName~ServiceFinderMetadataEnvelopeTests" --logger "trx;LogFileName=finder-final-focused.trx" --results-directory TestResults/finder241-final-focused --nologo
dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore --collect:"XPlat Code Coverage" --settings coverage.runsettings --logger "trx;LogFileName=finder-final-full.trx" --results-directory TestResults/finder241-final-full --nologo
```

Final focused result: **78 passed / 0 failed / 0 skipped**. Final unfiltered
affected Tests project result: **1,430 passed / 0 failed / 0 skipped**, 6m45s.
The full worktree stayed unchanged while that final test process held outputs.
`scripts/verify-test-coverage.ps1` passed against the final Cobertura report:
BFF **88.40%** (80% minimum), Server **89.20%** (85%), Contracts **96.86%** (95%).
These repository coverage gates are not a separate measured helper coverage
claim; direct helper and compiled consumer regressions prove the finder cases.

Final `dotnet format --no-restore --verify-no-changes --include` the helper and
three owned regression files passed, with `MalievWorkspaceRoot` and
`UseLocalMalievDependencies` set to the final private environment. Final feature
and Tests `gitleaks dir --redact` scans passed; tracked and new helper/test
`git diff --check` checks found no whitespace defects. Both private dependency
checkouts remain clean. Only the approved eight owned files differ: helper,
View, EN/TH resources, three regression files and this evidence document.
No old test, project, workflow, API, auth or source ledger was modified.

Actual browser and producer PostgreSQL acceptance remain unexecuted. Compiled
component rendering plus actual authenticated HTTP BFF/proxy tests with controlled
external transports cannot replace those acceptance lanes. This slice does not
resolve source owner ledgers, revive generated XML, introduce provider/SQL/PayPal
behavior, or enable migration-authority forwarding. No commits or pushes.

## Independent root integration checkpoint

Root inspected the complete helper, compiled editor change, expanded HTTP tests,
component/helper regressions, bilingual resources and this evidence. Independent
whole-solution Release build passed with zero warnings/errors. Root focused
78 tests and unfiltered affected suite 1,430 tests passed, zero failures/skips;
the full rerun took 5m44s. Root full TRX SHA256:
`EADAC2A9621F2B700B309EB6135A2BC4D0E90785BD375CCFE3E11D7FCBA2DDC0`.
Root independently parsed both resource files, rejected duplicate names and
verified all 106 keys match across cultures, and scanned all eight owned files
without secret findings. Formatting/audit/workflow and protected-main CI remain
separate gates until their actual results are recorded.

The consumer PR must reference, not automatically close, issue 241: its genuine
Quotation HTTP/PostgreSQL finder-string persistence and browser gates have not
yet been executed. Generic existing producer attribution/concurrency tests do
not establish this exact envelope roundtrip. No source-owner ledger disposition
is promoted on the strength of this compiled-consumer slice alone.
