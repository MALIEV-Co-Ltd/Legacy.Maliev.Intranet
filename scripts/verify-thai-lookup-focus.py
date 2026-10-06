"""Retain bounded actual lookup results, without parameters, logs or coverage claims."""

from collections import Counter
import json
import os
from pathlib import Path
import re
import subprocess
import uuid
import xml.etree.ElementTree as ET

NS = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
UNIT = "Legacy.Maliev.Intranet.Tests."
BROWSER = "Legacy.Maliev.Intranet.BrowserTests."
EXPECTED = {
    "lookup-unit.trx": {
        UNIT + "LookupBehaviorTests": {
            "SearchDebouncesAndOnlyAcceptsLatestInput": 1,
            "CancellationIgnoringProviderCannotReplaceNewerSelectionOrReportStaleFailure": 1,
            "ParentChangeOrDisposalRejectsLateResults": 1,
            "ProvinceChangeClearsChildrenButPreservesSupplierDetailAndCountry": 1,
            "CompanySuggestionDoesNotInventOrClearMissingDetails": 1,
            "SameOriginClientPreservesAllFiltersAndDecodesParentCodeAndLeadingZeros": 1,
            "PasteUsesSessionCsrfAndRetainsOriginalTextWithoutErrorBodyLeak": 1,
            "CompanyFailureIsExplicitInsteadOfEmptyMatch": 4,
            "MalformedSuccessCannotMasqueradeAsEmptyLookup": 3,
        },
        UNIT + "LookupComponentTests": {
            "NonThaiManualAddressDoesNotLaunchLookupRequests": 1,
            "UnavailableSearchKeepsManualEntryAndAccessibleStatus": 1,
        },
    },
    "lookup-browser.trx": {
        BROWSER + "LookupSupplierBrowserTests": {
            "PostcodeFirstKeyboardSelectionPreservesStreetAndManualCountry": 1,
            "UnavailableCredenSearchLeavesManualCompanyFieldsEditable": 1,
            "CompanySelectionPreservesManualTaxAndUsesLocalRetrievalTime": 1,
            "PastedAddressRequiresCandidateReviewAndExplicitApply": 1,
        },
    },
    "lookup-bff.trx": {
        UNIT + "LookupBffProxyTests": {
            "NormalCookieBoundaryForwardsAndFiltersWithOnlyServerServiceToken": 1,
            "MissingSessionOrPermissionStopsBeforeCatalog": 2,
            "InvalidFilterDoesNotReachCatalog": 4,
            "PasteWithoutCsrfIsRejectedBeforeCatalog": 1,
            "CompanyFailureIsExplicitAndNotRetriedOrLeaked": 4,
            "MalformedSuccessfulPayloadIsBadGateway": 1,
            "PasteWithCookieAndCsrfForwardsBoundedTextAndPreservesResolution": 1,
            "InvalidOrOversizedPastedBodyStopsBeforeCatalog": 3,
        },
    },
}


def read_xml(path):
    if path.is_symlink() or not path.is_file():
        raise ValueError("unsafe_evidence")
    if path.stat().st_size > 16 * 1024 * 1024:
        raise ValueError("oversized_evidence")
    data = path.read_bytes()
    if len(data) > 16 * 1024 * 1024:
        raise ValueError("oversized_evidence")
    text = data.decode("utf-8-sig")
    if "<!DOCTYPE" in text.upper() or "<!ENTITY" in text.upper():
        raise ValueError("unsafe_xml")
    return ET.fromstring(text)


def verify_trx(root, expected):
    tag = lambda name: f"{{{NS}}}{name}"
    if root.tag != tag("TestRun"):
        raise ValueError("invalid_namespace")
    allowed = {(cls, method): count for cls, methods in expected.items() for method, count in methods.items()}
    definitions = {}
    for definition in root.findall(f"{tag('TestDefinitions')}/{tag('UnitTest')}"):
        identity = definition.get("id")
        method = definition.find(tag("TestMethod"))
        if not identity or method is None or identity in definitions:
            raise ValueError("invalid_definition")
        key = (method.get("className", "").split(",", 1)[0], method.get("name"))
        if key not in allowed:
            raise ValueError("nonallowlisted_method")
        definitions[identity] = key
    records, executions, identities, counts = [], set(), set(), Counter()
    for result in root.findall(f"{tag('Results')}/{tag('UnitTestResult')}"):
        identity, execution, outcome = (result.get(key) for key in ("testId", "executionId", "outcome"))
        if identity not in definitions or not execution or execution in executions:
            raise ValueError("invalid_execution")
        if str(uuid.UUID(identity)) != identity.lower() or str(uuid.UUID(execution)) != execution.lower():
            raise ValueError("invalid_execution")
        if outcome not in ("Passed", "Failed", "NotExecuted"):
            raise ValueError("invalid_outcome")
        cls, method = definitions[identity]
        executions.add(execution)
        identities.add(identity)
        counts[(cls, method)] += 1
        records.append({"class": cls, "method": method, "outcome": outcome})
    if dict(counts) != allowed or identities != set(definitions):
        raise ValueError("incomplete_methods")
    counter = root.find(f"{tag('ResultSummary')}/{tag('Counters')}")
    if counter is None:
        raise ValueError("missing_counters")
    outcomes = Counter(record["outcome"] for record in records)
    required = {"total": len(records), "executed": outcomes["Passed"] + outcomes["Failed"],
                "passed": outcomes["Passed"], "failed": outcomes["Failed"], "notExecuted": outcomes["NotExecuted"]}
    if any(int(counter.get(key, "0")) != count for key, count in required.items()):
        raise ValueError("counter_mismatch")
    if any(int(value) != 0 for key, value in counter.attrib.items() if key not in required):
        raise ValueError("unexpected_counter")
    return records


def verify_source(expected, candidate):
    if not all(re.fullmatch(r"[0-9a-f]{40}", value or "") for value in (expected, candidate)):
        raise ValueError("invalid_source")
    observed = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True, stderr=subprocess.DEVNULL).strip()
    header = subprocess.check_output(["git", "cat-file", "-p", "HEAD"], text=True, stderr=subprocess.DEVNULL).split("\n\n", 1)[0]
    parents = [line.removeprefix("parent ") for line in header.splitlines() if line.startswith("parent ")]
    if observed != expected or candidate not in [observed, *parents]:
        raise ValueError("source_mismatch")
    return observed


def main():
    results = Path(os.environ["LOOKUP_RESULTS"])
    destination = Path(os.environ["LOOKUP_EVIDENCE"])
    receipt = {"schemaVersion": 1, "productionCoverageCertified": False,
               "joinedAcceptanceCertified": False, "groups": {}, "gate": "incomplete"}
    valid_source = False
    try:
        receipt["sourceRevision"] = verify_source(os.environ.get("EXPECTED_SOURCE_REVISION"), os.environ.get("CANDIDATE_HEAD"))
        receipt["candidateHead"] = os.environ["CANDIDATE_HEAD"]
        valid_source = os.environ.get("BUILD_OUTCOME") == "success"
        receipt["buildOutcome"] = "success" if valid_source else "unavailable"
    except (ValueError, subprocess.SubprocessError):
        receipt["sourceOutcome"] = "unavailable"
    for filename, expected in EXPECTED.items():
        try:
            files = list(results.rglob(filename))
            if results.is_symlink() or len(files) != 1:
                raise ValueError("missing_or_ambiguous_evidence")
            records = verify_trx(read_xml(files[0]), expected)
            counts = Counter(record["outcome"] for record in records)
            receipt["groups"][filename] = {"records": records, "counts": dict(counts)}
        except (ValueError, OSError, ET.ParseError):
            receipt["groups"][filename] = {"outcome": "unavailable"}
    complete = valid_source and all("records" in group for group in receipt["groups"].values())
    passed = complete and all(record["outcome"] == "Passed" for group in receipt["groups"].values() for record in group["records"])
    receipt["gate"] = "passed" if passed else "failed" if complete else "incomplete"
    if destination.exists():
        raise ValueError("evidence_destination_not_fresh")
    destination.mkdir(parents=True)
    (destination / "lookup-focus.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print("lookup focused gate: " + receipt["gate"])
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
