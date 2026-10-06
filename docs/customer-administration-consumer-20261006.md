# Customer administration consumer — issue263 draft

This evidence lane is unpublished and uncompiled. The authored inventory is
55 pure contract, 32 normal BFF HTTP/coordinator and 24 real Razor/bUnit cases:
111 forecasts, not native passes. Controlled external HTTP does not execute
Auth/Customer persistence, locks, IAM or a deployment. The UI cases are not
browser end-to-end acceptance.

## Finite provenance and contracts

Issue: https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/issues/263

Original private root commit `5fac706a7983a6d359b39acbd670e6800afe020e`
has no parent. Mixed commit `ada840421bd015f60447ade21260034dc940908a`
has parent `beecf3120d63404778747ad0993b4d98cab2c3c2`.
Only the customer administration portions are applicable to this slice; neither
commit is wholly closed. Current original checkpoint is
`135e526d0dab85c415b3afdcefd7b70fe2c82e2f`; the two customer view blobs are
`b0e6410af8fb553bc1f760285999a1b3ca840120` for
`Maliev.Intranet/Pages/Customers/View.cshtml` and
`4b2716ea9d8032dd8379f9fc2b81e024c79f7629` for
`Maliev.Intranet/Pages/Customers/View.cshtml.cs`. Exact path/blob mapping was
read back from that committed checkpoint; original configuration was not copied.

Producer contracts are Auth `2d01cd55a27526a23291793a115fdd490e04cf0e`
and Customer `f80f574354c40e9b67fa26c94a3fead352ad22dd`. This focused lane
consumes controlled HTTP shapes, not real execution of either producer.
Customer PascalCase/null-omitted relation fields and Auth protected settings
are covered separately. The same-origin flow retains independent captured
versions, server-held acting-employee credentials, permission rechecks, CSRF,
profile-first/identity-second ordering and typed partial/unknown outcomes.
No service fallback, resend or distributed atomicity is inferred.

## Evidence admission

The dedicated workflow builds Release with warnings as errors before executing
the three exact classes under the existing coverage settings. The retainer
requires 111 passing unique GUID execution IDs, exact method cardinalities and
all16 TRX counters. Shared theory test IDs/identical definitions are allowed;
conflicting mappings, missing/duplicate executions, failed/skipped results,
missing raw hits and malformed evidence fail closed. It binds tracked clean
source bytes and the actual consumer Git HEAD, and retains SHA256 hashes of
the actual raw TRX and coverage report. It uploads only sanitized method names,
execution IDs, counters, source hashes and actual raw coverage: no raw TRX,
theory parameter values, stdout, exception text or attachments.

Canonical D-format GUIDs are normalized before uniqueness checks. Nonempty
display names are compared only ephemerally per declared method to reject a
duplicate theory case with a new execution ID; names/parameters and their hashes
are never retained. Raw coverage permits one collector-GUID copy, or exactly
that copy plus the byte-identical known machine/timestamp/In/machine attachment.
Unknown layouts, extra/conflicting copies, mismatched machine names, symlinks,
oversized XML and DTD/entity declarations fail closed; this is not arbitrary
content deduplication.

Offline adversarial controls are synthetic parser validation, not C# acceptance.
Source attribute counting is a fail-closed authored inventory, not discovery.
Actual native discovery/TRX must agree before acceptance; source changes require
an explicitly reviewed inventory update. Focused raw coverage is not a full-suite
floor and the receipt explicitly denies such acceptance.

The existing full workflow is untouched. Before publication/merge acceptance,
require actual zero-warning/error native build, focused111, relevant full suite,
browser/joined checks, independently retained pristine PE/PDB/source-bound raw
generated-inclusive coverage at unchanged floors, formatting/security checks,
protected required checks and fresh-main proof. No source closure, operational
acceptance or deployment is established by this authored gate alone.

Job lifetime is bounded to30minutes, artifact retention to7days. No local SDK,
container, provider, database, browser or helper is authorized for this draft.
