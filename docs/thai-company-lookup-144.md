# Intranet lookup integration — AppHost #144

Status: draft implementation, not build-validated or release-ready. Scoped draft publication for hosted validation was explicitly authorized; no merge,
deployment, migration, live provider call or production permission change has
been performed by this lane.

## Implemented draft

The shared Shadcn search fields debounce 350 ms, cancel replaced searches and
discard late responses even when a provider ignores cancellation. Administrative
and postcode filters combine with AND. Province/district/subdistrict changes
invalidate dependent suggestions; street, building and country fields remain
editable. Postcode-first matching offers complete combinations without selecting
the first row. Pagination preserves the query and rejects dataset-version changes.

Pasted addresses retain their original text, show exact/ambiguous/not-found/conflict
outcomes, and require a reviewed candidate and an explicit apply action before
replacing street detail. More-than-preview ambiguity requires further narrowing.
Provider outcomes and lookup permission failures remain visible; manual input is
available throughout. Company suggestions apply only returned names and tax IDs,
with no invented status, website, contacts or registered address.

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

The authored Catalog LookupModels.cs and ThaiAddressesController/CompaniesController
were read. The consumer DTOs match their `parentCode` administrative-area shape,
string conflict fields, string codes/postcodes, version-bound pages, and suggestion
capability with nullable unavailable company facts. Final producer revision/OpenAPI
confirmation and joined HTTP tests remain required. No prototype replacement was used.

The existing BFF LegacyServiceAuthenticationHandler supplies its server-held service
token; browser session credentials are not forwarded to Catalog. Exact employee
lookup claims are checked before downstream access. The new typed client has a
10-second deadline, a 256-KiB response bound, no redirects, no retries and no default
HttpClient query logging. The BFF resolve request is bounded to 16 KiB of actual bytes
and 2048 text characters, including chunked requests. Domain writes remain separate.

## Startup-owner handoff (not yet integrated)

The existing migration owner retains Bff/Program.cs. Integrate the following
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

**Until these hooks are integrated, normal BFF lookup requests return 404 and the
authored normal-cookie BFF tests are expected to fail.** Supplier manual entry stays
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

Actual checks performed: `git diff --check` passed; shared-project and both localization
XML files parsed; localization keys matched 41/41 with zero differences. Static
Roslyn 5.0 syntax parsing inspected 15 new C# files with zero syntax errors. The first
attempt to load SDK Roslyn assemblies reported an assembly-version mismatch; the
successful parser check used the version already loaded by PowerShell. These checks
do not validate Razor generation, references, compilation, runtime or persistence.

Authored tests cover debounce/stale failure rejection/disposal, parent invalidation,
detail preservation, truthful company mapping, same-origin filters and parentCode,
session CSRF for parsing, malformed success and explicit provider errors. Component
tests cover manual foreign addresses and accessible failure feedback. Normal-cookie
BFF tests cover exact permissions, unauthorized access, filters, CSRF, upstream
failure status, no retries and payload leakage. Playwright tests exercise rendered
postcode-first keyboard selection/detail preservation and company manual fallback
using controlled responses. Mocked responses are not real joined acceptance.

Build, focused tests, affected full suites, dotnet format, native coverage and real
joined Catalog/domain persistence/browser tests have **not** run. The coordinator's
resource mandate prohibits local builds/test hosts/browser workers/containers; hosted
validation needs an authorized publication route. A scoped push/draft-PR permission
request was approved. The initial publication is a draft snapshot for hosted testing, not a validated slice. NuGet project-reference lock
updates must be regenerated and reviewed during authorized hosted restore.

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
