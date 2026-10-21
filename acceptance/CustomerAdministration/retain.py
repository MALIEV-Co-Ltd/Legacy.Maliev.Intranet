"""Fail-closed, PII-free retention of focused consumer executions; not producer acceptance."""
import argparse
import collections
import hashlib
import json
import pathlib
import re
import subprocess
import uuid
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
PREFIX = "Legacy.Maliev.Intranet.Tests."
EXPECTED = {
    "CustomerAdministrationContractTests": {
        "Save_ExactWireExcludesAllIdentifiersAndProtectedSettings": 1,
        "Save_RejectsUnknownOwnershipAndSecurityMembers": 14,
        "Save_RequiresExplicitFlagsAndOriginalVersions": 5,
        "IdentityRead_MissingSettingsCannotBeSilentlyDefaultedAndPreserved": 5,
        "Versions_RejectMissingWeakMultipleOrDecoratedValues": 8,
        "Versions_DistinctGrammarPreservesActualProducerLengthAndCasing": 1,
        "Binding_RejectsWrongResourceOrBodyVersion": 5,
        "Mapping_PreservesSharedContactsSourceFlagsAndCapturedProtectedSettings": 1,
        "Mapping_WrongOwnerOrReplacedIdentityVersionCannotBuildAnUpdate": 1,
        "IdentityUpdate_RejectsReadOnlyOrSecurityMembers": 7,
        "Save_UsesExistingProfileValidationLengthsAndEmailRules": 1,
        "SharedEmail_RejectsBothProducerPolicyConflictsBeforeEitherWrite": 3,
        "SharedEmail_AllowsTheActualAuthUsernameAlphabet": 2,
        "SharedEmail_AllowsCustomerStorageBoundaryOf256Characters": 1,
    },
    "BffCustomerAdministrationCoordinatorTests": {
        "Preflight_OriginalStaleVersionReturns412BeforeAnyWrite": 2,
        "Profile_Explicit400RejectsWithoutIdentityWriteOrFalseCompletion": 1,
        "Profile_UncertainAcknowledgementStopsWithoutRetryOrIdentityWrite": 3,
        "Identity_Stale412ReportsConfirmedProfileOnlyWithoutReplay": 1,
        "Identity_LostAcknowledgementReportsUnknownAndNeverRepeatsEitherWrite": 2,
        "Cancellation_AfterConfirmedProfileBeforeIdentityKeepsNextUnattemptedStage": 1,
        "Success_NormalCookieCsrfPreservesCapturedVersionsRelationsAndProtectedIdentity": 1,
        "ProducerExactPascalCaseNullOmission_EditAndSavePreserveActualRelationIds": 3,
        "MissingIdentityUpdatePermission_DeniedWithoutWorkloadFallbackOrProducerCalls": 1,
        "Email_InvalidSharedProducerBoundaryRejectsBeforeAdministrationCalls": 3,
        "Email_Exactly256AllowedCharactersReachesBothWritesUnchanged": 1,
        "MissingCsrf_NormalAuthenticatedSaveRejectsBeforeAdministrationCalls": 1,
        "Anonymous_AdministrationRequiresNormalCookieBeforeAnyProducerCall": 2,
        "Identity_MalformedProtectedProjectionFailsClosedBeforeEitherWrite": 10,
    },
    "CustomerAdministrationUiTests": {
        "Save_CapturesBothOriginalVersionsContactsAndFlagsWithoutProtectedSettings": 1,
        "Save_SuccessLocksWithoutAutomaticReadbackUntilExplicitReloadCapturesNewVersions": 1,
        "Save_PartialUnknownStaleOrMalformedReceiptNeverResends": 10,
        "Load_InvalidBoundProjectionNeverEnablesSave": 5,
        "Save_EmailOutsideProducerPolicyRejectsBeforeSessionOrPut": 3,
        "Save_QueryChangeCancelsHeldPutAndIgnoresLateSuccessUntilExplicitReload": 1,
        "Save_DisposeCancelsHeldPutWithoutLateReadbackOrReplay": 1,
        "Load_QueryChangeCancelsHeldReadAndLateOldDocumentCannotReplaceNewCustomer": 1,
        "Load_DisposeCancelsHeldReadWithoutSessionOrWrite": 1,
    },
}
METHODS = {PREFIX + cls + "." + name: count for cls, methods in EXPECTED.items() for name, count in methods.items()}
TOTAL = 111
DEPENDENCIES = {
    "Legacy.Maliev.ServiceDefaults": "4517cf16f5f1159318e184969732d46eae4a8308",
    "Legacy.Maliev.CompatibilityContracts": "78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7",
}
COUNTERS = dict.fromkeys(("total", "executed", "passed"), TOTAL)
COUNTERS.update(dict.fromkeys(("failed", "error", "timeout", "aborted", "inconclusive",
                             "passedButRunAborted", "notRunnable", "notExecuted", "disconnected",
                             "warning", "completed", "inProgress", "pending"), 0))
SOURCES = ["Legacy.Maliev.Intranet.Tests/" + cls + ".cs" for cls in EXPECTED] + [
    "Legacy.Maliev.Intranet.Contracts/CustomerAdministrationContracts.cs",
    "Legacy.Maliev.Intranet.Bff/Customers/CustomerAdministrationCoordinator.cs",
    "Legacy.Maliev.Intranet.Bff/Customers/CustomerAdministrationEndpointMapper.cs",
    "Legacy.Maliev.Intranet.Bff/Customers/CustomerAdministrationIdentityClient.cs",
    "Legacy.Maliev.Intranet.Bff/Customers/CustomerAdministrationProfileClient.cs",
    "Legacy.Maliev.Intranet.Bff/Program.cs",
    "Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerEdit.razor",
    "Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerEdit.razor.css",
    "Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerEdit.resx",
    "Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerEdit.th.resx",
    "Legacy.Maliev.Intranet.Client.Features.Customers/Pages/CustomerView.razor",
    "Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj",
    "coverage.runsettings", ".github/workflows/customer-administration-join.yml",
    "acceptance/CustomerAdministration/retain.py", "acceptance/CustomerAdministration/test_retain.py",
    "docs/customer-administration-consumer-20261006.md",
]


def require(condition):
    if not condition:
        raise ValueError("Invalid customer administration evidence")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def guid(value):
    require(bool(value))
    normalized = str(uuid.UUID(value))
    require(normalized == value.lower())
    return normalized


def read_evidence(results, path):
    require(results.is_dir() and not results.is_symlink() and path.is_file())
    require(path.resolve().is_relative_to(results.resolve()))
    current = path
    while current != results:
        require(not current.is_symlink())
        current = current.parent
    data = path.read_bytes()
    require(0 < len(data) <= 32 * 1024 * 1024)
    require(b"<!DOCTYPE" not in data.upper() and b"<!ENTITY" not in data.upper())
    ET.fromstring(data)
    return data


def select_coverage(results, coverage):
    require(len(coverage) in (1, 2))
    collectors, attachments = [], []
    for path in coverage:
        parts = path.relative_to(results).parts
        if len(parts) == 2 and parts[1] == "coverage.cobertura.xml" and re.fullmatch(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}", parts[0]):
            collectors.append(path)
        elif len(parts) == 4 and parts[1] == "In" and parts[3] == "coverage.cobertura.xml" and re.fullmatch(r"[A-Za-z0-9_.-]{1,128}", parts[2]):
            folder = "_" + re.escape(parts[2]) + r"_\d{4}-\d{2}-\d{2}_\d{2}_\d{2}_\d{2}"
            if re.fullmatch(folder, parts[0]):
                attachments.append(path)
    require(len(collectors) == 1 and len(attachments) == len(coverage) - 1)
    data = read_evidence(results, collectors[0])
    if attachments:
        require(read_evidence(results, attachments[0]) == data)
    return data


def source_identity(expected, head, tracked, dirty):
    require(re.fullmatch(r"[0-9a-f]{40}", expected) and expected == head)
    require(set(tracked) == set(SOURCES) and not dirty)


def source_map(root):
    result = {}
    for relative in SOURCES:
        path = root / relative
        require(path.is_file() and not path.is_symlink() and path.resolve().is_relative_to(root.resolve()))
        data = path.read_bytes()
        require(0 < len(data) <= 4 * 1024 * 1024)
        result[relative] = digest(data)
    for cls, expected in EXPECTED.items():
        source = (root / ("Legacy.Maliev.Intranet.Tests/" + cls + ".cs")).read_text(encoding="utf-8-sig")
        matches = re.findall(r"((?:    \[(?:Fact|Theory|InlineData).*?\]\r?\n)+)    public (?:async )?(?:Task|void) (\w+)\(", source, re.S)
        actual = {}
        for attributes, method in matches:
            require(method not in actual)
            actual[method] = attributes.count("[InlineData(") or 1
        require(actual == expected)
    require(sum(METHODS.values()) == TOTAL)
    return result


def verify(trx, raw):
    require(0 < len(trx) <= 32 * 1024 * 1024 and 0 < len(raw) <= 32 * 1024 * 1024)
    require(all(marker not in value.upper() for value in (trx, raw) for marker in (b"<!DOCTYPE", b"<!ENTITY")))
    document = ET.fromstring(trx)
    require(document.tag == "{" + NS["t"] + "}TestRun")
    summaries = document.findall("./t:ResultSummary", NS)
    require(len(summaries) == 1 and summaries[0].get("outcome") == "Completed")
    counters = summaries[0].findall("./t:Counters", NS)
    require(len(counters) == 1 and counters[0].attrib == {k: str(v) for k, v in COUNTERS.items()})
    declared = {}
    for definition in document.findall("./t:TestDefinitions/t:UnitTest", NS):
        methods = definition.findall("./t:TestMethod", NS)
        require(len(methods) == 1)
        method = methods[0]
        identity = method.get("className", "") + "." + method.get("name", "")
        identifier = guid(definition.get("id"))
        require(identity in METHODS and declared.get(identifier, identity) == identity)
        declared[identifier] = identity
    require(declared)
    executions, counts, used, displayed_cases = set(), collections.Counter(), set(), set()
    rows = document.findall("./t:Results/t:UnitTestResult", NS)
    require(len(rows) == TOTAL)
    for row in rows:
        identifier, execution = guid(row.get("testId")), guid(row.get("executionId"))
        require(identifier in declared and execution not in executions and row.get("outcome") == "Passed")
        display = row.get("testName", "")
        require(bool(display.strip()) and (declared[identifier], display) not in displayed_cases)
        # Ephemeral duplicate-case detection only: never retain names, parameters or hashes of them.
        displayed_cases.add((declared[identifier], display))
        # Never retain testName (theory parameters), stdout, exception text or attachments.
        executions.add(execution)
        used.add(identifier)
        counts[declared[identifier]] += 1
    require(dict(counts) == METHODS and used == set(declared))
    coverage = ET.fromstring(raw)
    require(coverage.tag == "coverage")
    lines = coverage.findall("./packages/package/classes/class/lines/line")
    require(lines and all(re.fullmatch(r"[0-9]+", item.get("hits", "")) for item in lines))
    return {"counters": COUNTERS, "methods": dict(sorted(counts.items())),
            "executionIds": sorted(executions), "trxSha256": digest(trx), "rawCoverageSha256": digest(raw),
            "rawCoverageFloorAcceptance": False, "producerPersistenceAcceptance": False,
            "deploymentAcceptance": False}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=pathlib.Path, required=True)
    parser.add_argument("--results", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--source", required=True)
    args = parser.parse_args()
    require(re.fullmatch(r"[0-9a-f]{40}", args.source))
    head = subprocess.check_output(["git", "-C", str(args.root), "rev-parse", "HEAD"], text=True, timeout=10).strip()
    hashes = source_map(args.root)
    tracked = subprocess.check_output(["git", "-C", str(args.root), "ls-files", "--", *SOURCES], text=True, timeout=10).splitlines()
    dirty = subprocess.check_output(["git", "-C", str(args.root), "status", "--porcelain", "--", *SOURCES], text=True, timeout=10)
    source_identity(args.source, head, tracked, dirty)
    graph = {}
    for name, expected in DEPENDENCIES.items():
        graph[name] = subprocess.check_output(["git", "-C", str(args.root / ".dependencies" / name), "rev-parse", "HEAD"], text=True, timeout=10).strip()
        require(graph[name] == expected)
    reports, raw = list(args.results.rglob("*.trx")), list(args.results.rglob("coverage.cobertura.xml"))
    require(len(reports) == 1)
    raw_data = select_coverage(args.results, raw)
    proof = verify(read_evidence(args.results, reports[0]), raw_data)
    proof["actualRawCoverageCopies"] = len(raw)
    proof.update({"sourceRevision": head, "sourceSha256": hashes, "dependencyGraph": graph,
                  "scope": "issue263-focused-consumer-only"})
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "proof.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
    (args.output / "coverage.cobertura.xml").write_bytes(raw_data)
    print("Verified 111 focused consumer executions; full native acceptance remains separate")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, ET.ParseError, subprocess.SubprocessError):
        raise SystemExit("Customer administration evidence rejected") from None
