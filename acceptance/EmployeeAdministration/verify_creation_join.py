"""Creation proof parser: pure controls are not native execution evidence."""
import collections
import hashlib
import json
from pathlib import Path
from xml.etree import ElementTree as ET
import verify_join as old

CLASS = old.CLASS
HELPERS = "EmployeeAdministration.Acceptance.CreationJoinAdmissionTests"
EXPECTED = set(old.EXPECTED_CASES)
EXPECTED.update(CLASS + '.RealCreation_PersistsProfileBoundIdentityAndOptionalAddress(mode: "' + mode + '")'
                for mode in ("meaningful", "null", "empty", "zero"))
EXPECTED.add(CLASS + ".RealCreation_UnknownCountryStopsBeforeProducerWrites")
EXPECTED.update(CLASS + ".RealCreation_CommitAmbiguityOrIdentityFailureRetainsLinkedPair(loseAcknowledgement: " + value + ")"
                for value in ("True", "False"))
EXPECTED.update(HELPERS + "." + name for name in (
    "ExactMinimumMemoryAndCurrentOwnedLeaseAreAccepted",
    "CleanupFailureStillAttemptsEveryOwnedResource",
    "CleanupWaitsForActualDisposalCompletion"))
for run, supervisor, lease, memory in (
    ("null", "run", "valid", 4194304), ('"run"', "other", "valid", 4194304),
    ('"run"', "run", "expired", 4194304), ('"run"', "run", "too-long", 4194304),
    ('"run"', "run", "invalid", 4194304), ('"run"', "run", "valid", 4194303)):
    EXPECTED.add(HELPERS + '.InvalidAdmissionIsRejectedBeforeStartup(run: ' + run +
                 ', supervisor: "' + supervisor + '", lease: "' + lease + '", memory: ' + str(memory) + ')')


def validate_source_seal(root, path, expected_sha):
    raw = path.read_bytes()
    if hashlib.sha256(raw).hexdigest() != expected_sha:
        raise ValueError("Reviewed source seal hash mismatch")
    seal = json.loads(raw)
    files = seal["files"]
    if seal["status"] != "source-reviewed-native-pending" or not files:
        raise ValueError("Reviewed graph required")
    seen = set()
    for row in files:
        name = row["path"]
        if "\\" in name or ":" in name or name in seen or any(part in ("", ".", "..") for part in name.split("/")):
            raise ValueError("Ambiguous source seal path")
        seen.add(name)
        target = old.owned_path(root, root / name)
        if not target.is_file() or hashlib.sha256(target.read_bytes()).hexdigest() != row["sha256"]:
            raise ValueError("Compiled source graph mismatch")
    required = ["acceptance/EmployeeAdministration/", ".dependencies/Legacy.Maliev.AuthService/",
                ".dependencies/Legacy.Maliev.EmployeeService/", ".dependencies/Legacy.Maliev.CountryService/",
                ".dependencies/Legacy.Maliev.ServiceDefaults/", ".dependencies/Legacy.Maliev.CompatibilityContracts/"]
    if any(not any(name.startswith(prefix) for name in seen) for prefix in required):
        raise ValueError("Incomplete source graph")
    project = root / "acceptance/EmployeeAdministration/EmployeeAdministration.AcceptanceTests.csproj"
    tree = ET.parse(project)
    if not any("EMPLOYEE_CREATION_SOURCE_JOIN" in (node.text or "") for node in tree.iter("DefineConstants")):
        raise ValueError("Creation mode not compiled by consumer project")
    return expected_sha


def validate(raw):
    report = old.parse_bytes(raw)
    ns = old.NS
    if report.getroot().tag != "{" + ns["t"] + "}TestRun":
        raise ValueError("Unexpected TRX root")
    for container in ("Results", "TestDefinitions", "ResultSummary"):
        if len(report.findall("./t:" + container, ns)) != 1 or len(report.findall(".//t:" + container, ns)) != 1:
            raise ValueError("Ambiguous evidence containers")
    declared, anchors = {}, set()
    for definition in report.findall("./t:TestDefinitions/t:UnitTest", ns):
        identifier = old.guid(definition.get("id"))
        methods = definition.findall("./t:TestMethod", ns)
        if identifier in declared or len(methods) != 1:
            raise ValueError("Duplicate definition or method")
        method = methods[0]
        class_name = method.get("className", "").split(",")[0]
        base = class_name + "." + method.get("name", "")
        if class_name not in (CLASS, HELPERS) or not any(name == base or name.startswith(base + "(") for name in EXPECTED):
            raise ValueError("Unexpected native test")
        declared[identifier] = base
        executions = definition.findall("./t:Execution", ns)
        if len(executions) > 1:
            raise ValueError("Ambiguous definition execution")
        if executions:
            anchors.add((identifier, old.guid(executions[0].get("id"))))
    seen, executions, used = set(), set(), set()
    for result in report.findall("./t:Results/t:UnitTestResult", ns):
        identifier, execution = old.guid(result.get("testId")), old.guid(result.get("executionId"))
        name = result.get("testName", "")
        if result.get("outcome") != "Passed" or name not in EXPECTED or name in seen or execution in executions:
            raise ValueError("Missing, duplicate, skipped or failed creation case")
        base = declared.get(identifier)
        if base is None or not (name == base or name.startswith(base + "(")):
            raise ValueError("Result/definition mismatch")
        seen.add(name)
        executions.add(execution)
        used.add((identifier, execution))
    if seen != EXPECTED or {identifier for identifier, _ in used} != set(declared) or not anchors.issubset(used):
        raise ValueError("Incomplete native creation case set")
    summary = report.find("./t:ResultSummary", ns)
    counters = summary.findall("./t:Counters", ns)
    expected = dict.fromkeys(("total", "executed", "passed"), 22)
    expected.update(dict.fromkeys(("failed", "error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
                                  "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending"), 0))
    if summary.get("outcome") != "Completed" or len(counters) != 1 or counters[0].attrib != {k: str(v) for k, v in expected.items()}:
        raise ValueError("Invalid native counters")
    return {"passed": 22, "failed": 0, "skipped": 0, "trxSha256": hashlib.sha256(raw).hexdigest(),
            "genuineIamAcceptance": False, "fullApplicationSuiteAcceptance": False,
            "productionDataParity": False, "deploymentAcceptance": False}
