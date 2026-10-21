"""Validate actual joined TRX. Parser controls are not native execution evidence."""
import collections
import hashlib
import json
import os
import pathlib
import subprocess
import uuid
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
MAX_BYTES = 32 * 1024 * 1024
CLASS = "EmployeeAdministration.Acceptance.EmployeeAdministrationAcceptanceTests"
METHODS = {
    "RealEmployeeSession_OrdersIdentityBoundAddressAndProfile_WithIndependentReadbacks": 1,
    "ConcurrentRealRelationMove_AfterIdentityCommit_StopsCoupledAddressAndProfile": 1,
    "LostRealAddress204Acknowledgement_IsUnknownAndNeverReplaysOrWritesProfile": 3,
    "RealNewAddress_ConfirmedProducerIdLinksOnceAndPreservesExistingRows": 1,
}
THEORY = "LostRealAddress204Acknowledgement_IsUnknownAndNeverReplaysOrWritesProfile"
EXPECTED_CASES = {CLASS + "." + method for method, count in METHODS.items() if count == 1}
EXPECTED_CASES.update(CLASS + "." + THEORY + '(failure: "' + failure + '")'
                      for failure in ("http", "io", "cancellation"))
# Public immutable source identity, not authentication or credential material.
ISSUER_COMMIT = "ef74d99f56cb53b600efc7f85819d9adc28f8935"
PINS = {
    "auth": (".dependencies/Legacy.Maliev.AuthService", ISSUER_COMMIT),
    "employee": (".dependencies/Legacy.Maliev.EmployeeService", "8a8a619b021e9dc71607fa279aa12257844b6359"),
    "defaults": (".dependencies/Legacy.Maliev.ServiceDefaults", "4517cf16f5f1159318e184969732d46eae4a8308"),
    "contracts": (".dependencies/Legacy.Maliev.CompatibilityContracts", "78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7"),
}

def guid(value):
    if not value:
        raise ValueError("Missing joined identity")
    normalized = str(uuid.UUID(value))
    if normalized != value.lower() or normalized == str(uuid.UUID(int=0)):
        raise ValueError("Noncanonical joined identity")
    return normalized

def owned_path(root, path):
    root, path = pathlib.Path(os.path.abspath(root)), pathlib.Path(os.path.abspath(path))
    if not root.is_dir() or not path.is_relative_to(root):
        raise ValueError("Invalid joined evidence boundary")
    for current in (root, *root.parents, path, *path.parents):
        if current.is_symlink():
            raise ValueError("Linked joined evidence boundary")
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError("Invalid joined evidence containment")
    return path

def parse_bytes(data):
    if not 0 < len(data) <= MAX_BYTES or any(marker in data.upper().replace(b"\x00", b"") for marker in (b"<!DOCTYPE", b"<!ENTITY")):
        raise ValueError("Unsafe joined evidence bytes")
    return ET.ElementTree(ET.fromstring(data))

def read_evidence(root, path):
    path = owned_path(root, path)
    if not path.is_file() or not 0 < path.stat().st_size <= MAX_BYTES:
        raise ValueError("Invalid joined evidence size")
    with path.open("rb") as source:
        data = source.read(MAX_BYTES + 1)
    return data, parse_bytes(data)

def validate(report):
    if report.getroot().tag != "{" + NS["t"] + "}TestRun":
        raise ValueError("Invalid joined TRX root")
    for container in ("Results", "TestDefinitions", "ResultSummary"):
        if len(report.findall("./t:" + container, NS)) != 1 or len(report.findall(".//t:" + container, NS)) != 1:
            raise ValueError("Invalid joined evidence container")
    rows = report.findall("./t:Results/t:UnitTestResult", NS)
    definitions = report.findall("./t:TestDefinitions/t:UnitTest", NS)
    if len(rows) != 6 or not definitions:
        raise ValueError("Expected six actual joined results")
    declared, anchors = {}, set()
    for definition in definitions:
        methods = definition.findall("./t:TestMethod", NS)
        if len(methods) != 1:
            raise ValueError("Invalid joined method definition")
        method = methods[0]
        identifier = guid(definition.get("id"))
        name = method.get("name", "")
        class_name = method.get("className", "").split(",")[0]
        if class_name != CLASS or name not in METHODS or declared.get(identifier, name) != name:
            raise ValueError("Invalid joined definition")
        declared[identifier] = name
        executions = definition.findall("./t:Execution", NS)
        if len(executions) > 1:
            raise ValueError("Invalid joined definition execution")
        if executions:
            anchors.add((identifier, guid(executions[0].get("id"))))
    executions, names, methods, used_definitions = set(), set(), collections.Counter(), set()
    for row in rows:
        identifier, execution = guid(row.get("testId")), guid(row.get("executionId"))
        key = (identifier, execution)
        method = declared.get(identifier)
        name = row.get("testName", "")
        expected_name = CLASS + "." + method if method is not None else ""
        named = name == expected_name if method != THEORY else name in EXPECTED_CASES
        if row.get("outcome") != "Passed" or not execution or execution in executions or method is None or not named or name in names:
            raise ValueError("Invalid joined execution")
        executions.add(execution)
        names.add(name)
        used_definitions.add(key)
        methods[method] += 1
    if names != EXPECTED_CASES or methods != METHODS or {key[0] for key in used_definitions} != set(declared) or not anchors.issubset(used_definitions):
        raise ValueError("Missing or unexpected joined case")
    summaries = report.findall("./t:ResultSummary", NS)
    if len(summaries) != 1 or summaries[0].get("outcome") != "Completed":
        raise ValueError("Invalid joined summary")
    counters = summaries[0].findall("./t:Counters", NS)
    required = dict.fromkeys(("total", "executed", "passed"), 6)
    required.update(dict.fromkeys(("failed", "error", "timeout", "aborted", "inconclusive",
        "passedButRunAborted", "notRunnable", "notExecuted", "disconnected", "warning",
        "completed", "inProgress", "pending"), 0))
    if len(counters) != 1 or counters[0].attrib != {key: str(value) for key, value in required.items()}:
        raise ValueError("Invalid joined counters")
    return dict(sorted(methods.items()))

def main():
    root = pathlib.Path(os.environ["JOIN_RESULTS"])
    owned_path(root, root)
    reports = list(root.rglob("*.trx"))
    if len(reports) != 1:
        raise ValueError("Expected exactly one nonempty joined TRX")
    raw, report = read_evidence(root, reports[0])
    methods = validate(report)
    graph = {"consumer": subprocess.check_output(["git", "rev-parse", "HEAD"], timeout=10, text=True).strip()}
    expected_consumer = os.environ.get("JOIN_CONSUMER_SHA", "")
    if len(expected_consumer) != 40 or any(character not in "0123456789abcdef" for character in expected_consumer) or graph["consumer"] != expected_consumer:
        raise ValueError("Immutable joined consumer mismatch")
    for name, (path, expected) in PINS.items():
        actual = subprocess.check_output(["git", "-C", path, "rev-parse", "HEAD"], timeout=10, text=True).strip()
        if actual != expected:
            raise ValueError("Immutable joined graph mismatch")
        graph[name] = actual
    proof = {"passed": 6, "failed": 0, "skipped": 0, "methods": methods, "graph": graph,
        "trxSha256": hashlib.sha256(raw).hexdigest(),
        "liveIamAcceptance": False, "productionDataParity": False, "deploymentAcceptance": False}
    evidence = pathlib.Path(os.environ["JOIN_EVIDENCE"])
    if evidence.exists() or evidence.is_symlink():
        raise ValueError("Joined output must be fresh")
    for ancestor in evidence.absolute().parents:
        if ancestor.is_symlink():
            raise ValueError("Linked joined output boundary")
    evidence.mkdir(parents=True, exist_ok=False)
    destination = owned_path(evidence, evidence / "employee-administration-proof.json")
    with destination.open("x", encoding="utf-8") as output:
        json.dump(proof, output, indent=2)
        output.write("\n")
    print("Verified six actual joined employee administration executions")

if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, ET.ParseError, subprocess.SubprocessError):
        raise SystemExit("Employee producer join evidence rejected") from None
