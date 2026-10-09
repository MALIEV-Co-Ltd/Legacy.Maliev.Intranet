# Intranet lookup integration — AppHost #144

Status: the two startup calls are now authored as a separate isolated-branch review commit; hosted validation of that candidate is pending. Remaining form and joined acceptance handoffs are incomplete. Scoped draft publication for hosted validation was explicitly authorized; no merge,
deployment, migration, live provider call or production permission change has
been performed by this lane.

## Implemented draft

The shared Shadcn search fields debounce 350 ms, cancel replaced searches and
discard late responses even when a provider ignores cancellation. Administrative
and postcode filters combine with AND. Province/district/subdistrict changes
invalidate dependent suggestions; street, building and country fields remain
editable. Postcode-first matching offers complete combinations without selecting
the first row. Typing a postcode changes search constraints only; manual domain fields
remain unchanged until an explicit administrative selection. Pagination preserves the query and rejects dataset-version changes.

Pasted addresses retain their original text, show exact/ambiguous/not-found/conflict
outcomes, and require a reviewed candidate and an explicit apply action before
replacing street detail. More-than-preview ambiguity is visibly flagged for further narrowing.
Provider outcomes and lookup permission failures remain visible; manual input is
available throughout. Company suggestions apply only returned names and tax IDs,
with no invented status, website, contacts or registered address. Provided retrieval
timestamps are displayed in the browser timezone; absent timestamps are omitted.

Supplier create and edit use the existing supplier-owned persistence workflow.
No lookup component writes domain data. Existing save/delete permissions, CSRF,
ownership, compensation, concurrency and failure semantics are preserved. Because
the existing address has no dedicated district/subdistrict properties, the reviewed
Thai subdistrict and district names are stored together in City; province is State,
postcode is PostalCode. Address1/Address2/Building/CountryId are preserved except
when the user explicitly applies reviewed pasted street detail to Address1.

The default address-assist state is off so foreign/manual addresses are preserved.
Changing the country disables and invalidates the suggestion session. Thai lookup
does not guess a database CountryId or change the country.

## Source and contract inspection

Baseline: `08a92177fcb4840d27e08edaccaa88c6b90ddb45`.
Branch: `feature/thai-lookup-intranet`.
Catalog proposals: issues #44 comment 6010129894, #45 comment 6010135057,
#46 comment 6010135306. These are not released APIs.

Catalog source revision inspected: `6b591f86a720ae4953d9bd33c93724f4b5703b89`.

The authored Catalog LookupModels.cs and ThaiAddressesController/CompaniesController
were read. The consumer DTOs match their final `provinceCode`/`districtCode` administrative-area wire shape,
string conflict fields, string codes/postcodes, version-bound pages, and suggestion
capability with nullable unavailable company facts. Final producer revision/OpenAPI
confirmation and joined HTTP tests remain required. No prototype replacement was used.

The existing BFF LegacyServiceAuthenticationHandler supplies its server-held service
token; browser session credentials are not forwarded to Catalog. Exact employee
lookup claims are checked before downstream access. The new typed client has a
10-second deadline, a 256-KiB response bound, no redirects, no retries and no default
HttpClient query logging. The BFF resolve request is bounded to 16 KiB of actual bytes
and 2048 text characters, including chunked requests. Domain writes remain separate.

## Startup-owner handoff (isolated draft calls authored)

Customer migration PR #265 merged at 1b2e6d32. The two minimal calls below are now authored only in this isolated draft branch for separate review; no owner checkout was modified and no merge was performed. The existing migration owner retains the shared startup merge boundary. Preserve the following
independent extension hooks without replacing other registrations or mappings:

```csharp
// Before builder.Build(), using the existing Services:Catalog configuration.
builder.Services.AddCatalogLookups(builder.Configuration);

// After the existing authentication/authorization/rate-limit middleware,
// before fallback routing. Namespace Catalog is already imported at this baseline.
app.MapCatalogLookups();
```

The extension exposes `/bff/lookups/thai-addresses/{provinces,districts,subdistricts,postcodes,autocomplete}`,
POST `/bff/lookups/thai-addresses/resolve`, and GET `/bff/lookups/companies/search`.
Claims: `legacy-catalog.locations.read` and `legacy-catalog.companies.read`.
The owner must confirm workload and employee provisioning; this lane has not granted
permissions or weakened authorization to make lookups work.

**The last tested candidate bca88175 lacked these hooks: normal BFF lookup requests returned 404 and the
authored normal-cookie BFF tests failed.** The new startup candidate needs fresh hosted evidence. Supplier manual entry stays
available, but lookup availability is not accepted. This is a material integration
blocker, not a completed slice.

## Actual surface inventory and coverage

| Surface | Actual behavior and draft coverage |
| --- | --- |
| SupplierCreate.razor | Company data entry and editable address; shared assists authored |
| SupplierView.razor | Existing supplier/address editor; shared assists authored |
| CustomerCreate.razor | At baseline creates identity/profile, without company/address editor |
| CustomerView/CustomerOverview | Company and billing/shipping are read-only projections at baseline |
| Customer edit and relation editors #261/#262/#263 | Existing active owner; no overlapping edits or duplicate relation editor; shared assists ready for owner hooks |
| EmployeeCreate | Profile/identity fields; no editable address in the current WASM form |
| EmployeeView/EmployeeProfile | Address projections are read-only; no new employee write contract invented |
| PurchaseOrderCreate | Existing address relation selects; billing/shipping company text inputs need company-assist hooks after reservation |
| InvoiceCreate.AddressSection | Actual editable billing/shipping addresses and company text; needs hooks preserving IntentLocked/idempotency and field limits |
| QuotationRequests/View | Editable company/tax data entry; needs company-assist hook preserving existing submission/qualification boundaries |
| Other order/quotation/catalog/accounting forms | Targeted input-binding search found no further editable administrative address fields |
| Retained Razor Pages supplier/purchase-order templates | Legacy source exists; current BFF/WASM hosting and route parity need confirmation before introducing a second UI integration |

The inventory does not claim customer, employee, invoice, purchase-order or
quotation-request integration is complete. Shared-file reservations and existing
owner handoffs are required before changing the remaining live editors.

## Validation and outstanding acceptance

Latest completed evidence before the startup-only correction, candidate `bca88175`:

- Focus run `37438623139`: both builds zero warnings/errors; behavior/components
  **16/16 passed**, rendered browser **4/4 passed**, normal-startup BFF
  **0 passed / 17 failed / 0 skipped**. Actual bounded artifact records `gate: failed`.
- Finance `37438623194`: build zero warnings/errors and **35/35 passed**.
  Context admission `37438622920` passed.
- Full run `37438623243`: builds zero warnings/errors; browser **169/169 passed**;
  main **1592 passed / 17 failed / 0 skipped** (1609 total). All 17 failures reached
  absent startup routes (GET 404 / POST 405). Format, joined acceptance and coverage
  were not accepted. These results do not certify the new startup correction.


Actual checks performed: `git diff --check` passed; shared-project and both localization
XML files parsed; localization keys matched 41/41 with zero differences. Static
Roslyn 5.0 syntax parsing inspected 15 new C# files with zero syntax errors. The first
attempt to load SDK Roslyn assemblies reported an assembly-version mismatch; the
successful parser check used the version already loaded by PowerShell. These checks
do not validate Razor generation, references, compilation, runtime or persistence.

Authored tests cover debounce/stale failure rejection/disposal, parent invalidation,
detail preservation, truthful company mapping, same-origin filters and parent relationships,
session CSRF for parsing, malformed success and explicit provider errors. Component
tests cover manual foreign addresses and accessible failure feedback. Normal-cookie
BFF tests cover exact permissions, unauthorized access, filters, CSRF, upstream
failure status, no retries and payload leakage. Playwright tests exercise rendered
postcode-first keyboard selection/detail preservation and company manual fallback
using controlled responses. Additional cases verify pasted-text preview, explicit candidate
review and apply, successful normal-cookie CSRF forwarding, empty/overlong parser text,
and the 16-KiB body bound with unknown content length. Mocked responses are not real joined acceptance.

Hosted evidence at commit `7904222967830a4e785966a4f16ee50b6d9327a6`:

- Finance focus run `37428447475`: Release build with warnings as errors passed,
  **zero warnings and zero errors**. Existing finance regression passed **35/35**,
  zero failed and zero skipped. This is not focused lookup-feature acceptance.
- Context admission run `37428447348`: both locked restore and image builds passed.
  Project-reference lock edges were updated without changing package versions or hashes.
- Normal PR validation run `37428447751`: solution build passed with zero warnings/errors.
  Browser suite passed **167/167**. Main suite: **1592 passed, 13 failed, 0 skipped**
  (1605 total). All failures are the normal-startup LookupBffProxyTests: absent routes
  returned 404 for GET and 405 for POST instead of the expected boundary responses.
  Formatting, vulnerability audit, joined customer acceptance and native coverage were
  skipped after test failure. Preserved evidence is explicitly partial with no TRX or
  coverage certification; console results and partial binaries were retained.
- GitGuardian check passed. Static parsing and diff checks described above passed.

At `11e7388f`, finance run `37432706730` again built with zero warnings/errors and
passed 35/35 existing finance regressions; context admission `37432706781` passed.
At `7d2991b8`, finance run `37433183642` built with zero warnings/errors and passed
35/35 existing finance regressions; context admission `37433183843` passed.
Normal validation `37433183957` was running before the parser acceptance additions.
The new parser review and body-bound regressions require their own hosted result.

The solution suites ran on the earlier published head, with failures limited to missing BFF startup
routes. A separate focused lookup pass, formatting, native coverage and real joined
Catalog/domain persistence/browser acceptance remain outstanding. The normal
BFF tests intentionally use the real startup and will expose absent startup hooks;
no test-only route registration masks that dependency. Local builds/test hosts/browser
workers/containers remain prohibited by the coordinator resource mandate. PR #266
stays draft; no merge or deployment. The partial-postcode correction and browser
regression are awaiting hosted validation on their own next published head.

The additional `thai-lookup-focus.yml` lane builds both test targets with warnings as
errors, then runs 16 behavior/component cases, 4 rendered browser cases and 17 real-
startup cookie/CSRF cases independently. It has read-only repository permissions,
a 20-minute job lease and five-minute limits for each test group. Failures remain
failures; the full-suite and native coverage gates are unchanged. The retainer validates
exact methods/cardinality and source identity, removes parameters/captured output,
and emits only bounded actual results. It never certifies production coverage or
joined acceptance. Its 13 synthetic rejection/control tests passed locally; YAML,
Python syntax, workflow context availability, immutable action pins and real Git
identity checks passed. These controls are not actual .NET lookup test results.

After startup integration, execute on an admitted hosted runner:

```text
dotnet build Legacy.Maliev.Intranet.slnx -c Release -p:UseLocalMalievDependencies=true
dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter FullyQualifiedName~Lookup
dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build
dotnet test Legacy.Maliev.Intranet.BrowserTests/Legacy.Maliev.Intranet.BrowserTests.csproj -c Release --no-build --filter FullyQualifiedName~LookupSupplierBrowserTests
dotnet test Legacy.Maliev.Intranet.BrowserTests/Legacy.Maliev.Intranet.BrowserTests.csproj -c Release --no-build
dotnet format Legacy.Maliev.Intranet.slnx --verify-no-changes --no-restore
```

Use the repository's pinned dependency checkouts and existing unweakened coverage
gate. Confirm zero warnings/errors before tests. Finally prove persisted supplier and
customer/address values through the actual domain APIs, unauthorized and unavailable
flows, locale/keyboard behavior, ownership/concurrency, postcode ambiguity and parsing
with the exact producer revision. Creden production access/usage rights remain an
external release prerequisite; no live provider enablement is implied.

## Resource ledger

No task-owned build host, test host, browser, container, database connection, port
forward, tool server or detached process was started. Inspection commands finished.
The isolated clone contains persistent reviewable source and recovery evidence and
is retained. The earlier unused empty worktree was removed, and its absence was
verified. No shared process or
another owner's checkout was stopped or changed.

## Supplier qualification on main — 2026-10-09

PR [#286](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/pull/286) merged through normal protected linear history as `c1571a3e74923ab4da777f8e43dbe238ca19d60c`. Its tree `95e388be4dbfd3874d1594d1efe22c62aacc02e1` is identical to reviewed head `dc22a41bc0f92dc7aa6c324749ccb4623b42cc26` and tested PR merge `2bc7e24ff056dcf2b7eaab5f8213850c7d4ef1f0`.

The exact-main supplier address run [37884896498](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/actions/runs/37884896498) passed 17 native cases: 13 resource controls, 3 browser observer controls and the original pasted-address journey. The journey compares real Catalog/BFF resolution, requires candidate review, edited detail and explicit Apply, then uses ordinary supplier PUT, independent Procurement readback and page reload with original supplier/address IDs. CSRF and permission denials preserve the original fields. Downloaded sanitized receipts independently verify all 21 clients and both backend participants settled/released, original two backend IDs absent, and the original fetch restored with no active reader tasks/timers.

Required PR validation [37879053982](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/actions/runs/37879053982) passed 1,953 main, 200 browser and 3 joined tests, plus the separate 1,953-test native coverage execution. Builds had zero warnings/errors; format, audit, image and coverage gates passed. All 12 retained file hashes and tested tree identity were independently verified. Exact-main full validation [37884896712](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/actions/runs/37884896712) is still running at this checkpoint; its result must be read back separately.

This closes the demonstrated supplier response-observation failure only. Synthetic issuer and guarded provider boundaries remain explicit. It does not close live IAM/Creden, AppHost #144, other editors or broader UI coverage. Current physical UI line coverage is 1,208/2,417 (49.98%); unchanged BFF/Server/Contracts floors remain 80/85/95%. Historical failures above are retained for their original source revisions. Purchase-order company hooks are owned by PR #285, and customer document paths by PR #287; this lane does not edit those files.
