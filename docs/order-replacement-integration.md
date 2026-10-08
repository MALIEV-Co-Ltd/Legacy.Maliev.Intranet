# Order replacement workflow — local integration status

Business owner and approved design: [OrderService#61](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.OrderService/issues/61). Backend draft: [OrderService#62](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.OrderService/pull/62).

The new panel preserves original order manufactured/remaining/tracking/finished/status history. Separate cases record affected original identities, reason, private evidence references, approval, return gates/waivers, remake attempts and QA, own finished dates, replacement shipments, delivery outcomes, explicit retries, and operational recovery observations. Physical replacement never creates an invoice, receivable, credit, tax event, settlement or carrier submission.

BFF transport uses the current server-held employee token and no workload fallback. Browser input cannot supply customer/tenant/employee authority or original lineage. CSRF and fresh permissions protect writes. An uncertain operation freezes its GUID, payload, expected revision and initiating employee. Reconciliation is actor-scoped; explicit retry reuses the same request. Original editing is blocked until its outcome resolves. English and Thai UI use the existing Shadcn components.

## Disabled integration boundary

`ReplacementCases:Enabled` and `ReplacementCases:EvidencePickerEnabled` default false. Order backend additionally defaults current authority and evidence verification to unavailable. Configuration cannot manufacture acceptance of an owner contract.

Auth must provide accepted current-session/active-staff/trusted-tenant/customer authority, not display claims. File/NDA [Intranet#277](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/issues/277) must provide an accepted protected exact-version receipt/metadata producer and real authority/File fixture. Metadata is selection-only; final backend receipt validation is mandatory. No storage URLs or bearer credentials cross the picker boundary. All original orders must inherit NDA protection across attempts; expiry never releases protected evidence.

Order-owned CaseId is positive int. AttemptId and ShipmentId are case-local positive ints, not legacy shipment identities. DocumentId/VersionId remain Guids. Planned protected lineage GET `/replacementcases/{caseId:int}/lineage` returns PascalCase CaseId, CustomerId, OriginalOrderIds, AttemptIds, ShipmentIds, Revision. Local permission is `legacy.replacements.read`, live resource `/replacementcases/{caseId}`. No accepted live authority/source pin is advertised yet.

Accounting [#79](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.AccountingService/issues/79) owns commercial billing, tax events and settlement/corrections. Unavailable original quotation/invoice links remain null rather than browser-authorized references. No BillingOperationId is inferred without an acknowledged producer.

## Validation and follow-ups

Controlled UI/BFF tests and actual Chromium checks do not accept producer joins. Final source-specific full suite, coverage and browser evidence are recorded with the Order implementation ledger and task checkpoint before coherent commits. Original migration retains shared-path and native validation priority.

Deferred review minors: picker currently reads first20 versions; malformed metadata null entries can return500 rather than503; currency validation checks three-letter shape while authoritative currencies remain Accounting-owned. All leave critical evidence and finance integration blocked until accepted joins.

No activation, merge, deployment, production migration, live order amendment, customer message or carrier submission is part of this change.

Current local validation on the culture-fix baseline0e59e807: Release test/browser builds0warnings0errors; formatting verification passed; affected UI/BFF/original-editor42passed0failed0skipped; post-style actual Chromium6passed0failed0skipped (English/Thai375/1440 timeline, outer/panel overflow, lost-ack frozen-key/payload and original-editor guard). Thai mobile and English desktop screenshots visually inspected. Transitive vulnerability audit16projects reported no vulnerable packages; tracked/staged-source secret scan and whitespace checks passed. Exact-head hosted full-suite and strict source/PDB/coverage acceptance remain pending. Controlled browser/transport fixtures do not accept live producer joins.
