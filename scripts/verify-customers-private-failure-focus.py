"""Fail closed on actual Customers8 TRX; retain no display parameters or output."""

import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import uuid
import xml.etree.ElementTree as ET

NAMESPACE = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
CLASS = "Legacy.Maliev.Intranet.Tests.BffCustomersPrivateFailureObservationTests"
EXPECTED = {
    "NormalCustomerPrivateFailure_RetryChainRecordsOnlyUnrecoveredTerminalResult": 2,
    "NormalCustomerPrivateFailure_UnknownTransportPreservesGeneric503AndOpaqueIncident": 1,
    "NormalCustomerPrivateFailure_CallerCancellationRemainsQuietAfterRealTransportAdmission": 1,
    "NormalCustomerPrivateFailure_UpstreamAuthStatusPreservesInvalidationWithout5xxIncident": 2,
    "NormalCustomerPrivateFailure_ExistingEmployeeAdmissionRejectsBeforeSelectedClient": 2,
}
COUNTERS = {
    "total", "executed", "passed", "error", "failed", "timeout", "aborted", "inconclusive",
    "passedButRunAborted", "notRunnable", "notExecuted", "disconnected", "warning", "completed",
    "inProgress", "pending",
}
SOURCES = (
    "Legacy.Maliev.Intranet.Bff/Program.cs",
    "Legacy.Maliev.Intranet.Bff/Customers/CustomersProxy.cs",
    "Legacy.Maliev.Intranet.Tests/BffCustomersPrivateFailureObservationTests.cs",
    "Legacy.Maliev.Intranet.Tests/CustomersPrivateFailureWorkflowContractTests.cs",
    "Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj",
    ".github/workflows/_build-and-test.yml",
    "scripts/verify-customers-private-failure-focus.py",
    "tests/fixtures/customers-private-failure/test_focus_retainer.py",
)


def require(value):
    if not value:
        raise ValueError("invalid Customers evidence")


def identity(value):
    require(isinstance(value, str))
    parsed = uuid.UUID(value)
    require(str(parsed) == value and parsed.int != 0)
    return value


def verify_trx(root):
    tag = lambda name: f"{{{NAMESPACE}}}{name}"
    require(root.tag == tag("TestRun"))
    summaries = root.findall(tag("ResultSummary"))
    require(len(summaries) == 1 and summaries[0].get("outcome") == "Completed")
    counters = summaries[0].findall(tag("Counters"))
    require(len(counters) == 1 and set(counters[0].attrib) == COUNTERS)
    require(all(re.fullmatch(r"\d+", value) for value in counters[0].attrib.values()))
    require(all(int(value) == (8 if key in {"total", "executed", "passed"} else 0)
                for key, value in counters[0].attrib.items()))
    require(len(root.findall(tag("TestDefinitions"))) == 1 and len(root.findall(tag("Results"))) == 1)
    definition_container = root.find(tag("TestDefinitions"))
    result_container = root.find(tag("Results"))
    require(all(child.tag == tag("UnitTest") for child in definition_container))
    require(all(child.tag == tag("UnitTestResult") for child in result_container))
    definitions, bindings = {}, {}
    for definition in definition_container:
        test_id = identity(definition.get("id"))
        display_name = definition.get("name")
        require(isinstance(display_name, str) and display_name.strip() and display_name == display_name.strip())
        require(all(child.tag in {tag("TestMethod"), tag("Execution")} for child in definition))
        methods, executions = definition.findall(tag("TestMethod")), definition.findall(tag("Execution"))
        require(len(methods) == 1 and len(executions) == 1)
        method = methods[0]
        require(method.get("className", "").split(",", 1)[0] == CLASS and method.get("name") in EXPECTED)
        method_name = method.get("name")
        require(test_id not in definitions or definitions[test_id] == method_name)
        binding = (test_id, identity(executions[0].get("id")))
        require(binding not in bindings)
        definitions[test_id] = method_name
        bindings[binding] = display_name
    records, used_bindings, used_executions, used_names = [], set(), set(), set()
    counts = dict.fromkeys(EXPECTED, 0)
    for result in result_container:
        require(all(child.tag == tag("Output") for child in result) and len(result) <= 1)
        test_id, execution_id = identity(result.get("testId")), identity(result.get("executionId"))
        binding = (test_id, execution_id)
        require(binding in bindings and execution_id not in used_executions and result.get("outcome") == "Passed")
        display_name = result.get("testName")
        require(display_name == bindings[binding] and display_name not in used_names)
        used_names.add(display_name)
        used_bindings.add(binding)
        used_executions.add(execution_id)
        method = definitions[test_id]
        counts[method] += 1
        records.append({"method": method, "testId": test_id, "executionId": execution_id, "outcome": "Passed"})
    require(counts == EXPECTED and len(records) == 8 and used_bindings == set(bindings))
    return records


def safe_path(path, *, directory=False):
    require(not path.is_symlink() and not any(parent.is_symlink() for parent in path.parents))
    require(path.is_dir() if directory else path.is_file())


def git(repository, *arguments):
    return subprocess.check_output(["git", *arguments], cwd=repository, timeout=10)


def retain(repository, results, output, expected_revision):
    revision = git(repository, "rev-parse", "HEAD").decode("ascii").strip()
    require(re.fullmatch(r"[0-9a-f]{40}", expected_revision) and revision == expected_revision)
    sources = []
    for name in SOURCES:
        path = repository / name
        safe_path(path)
        data = path.read_bytes()
        committed = git(repository, "show", f"HEAD:{name}")
        require(data.replace(b"\r\n", b"\n") == committed.replace(b"\r\n", b"\n"))
        sources.append({"path": name, "checkoutSha256": hashlib.sha256(data).hexdigest(), "gitBlobSha256": hashlib.sha256(committed).hexdigest()})
    require(not output.exists() and not output.is_symlink() and not any(parent.is_symlink() for parent in output.parents))
    safe_path(results, directory=True)
    trx = list(results.rglob("*.trx"))
    require(len(trx) == 1)
    safe_path(trx[0])
    require(trx[0].stat().st_size <= 16 * 1024 * 1024)
    raw = trx[0].read_bytes()
    require(b"<!DOCTYPE" not in raw.upper() and b"<!ENTITY" not in raw.upper())
    records = verify_trx(ET.fromstring(raw))
    require(git(repository, "rev-parse", "HEAD").decode("ascii").strip() == revision)
    sanitized = json.dumps({"class": CLASS, "methodCardinalities": EXPECTED, "cases": records}, sort_keys=True, indent=2).encode()
    output.mkdir(parents=True)
    (output / "named-results.json").write_bytes(sanitized)
    manifest = {
        "schemaVersion": 1, "sourceRevision": revision, "complete": True, "passedCases": 8,
        "methodCardinalities": EXPECTED, "sources": sources,
        "actualRawTrxSha256": hashlib.sha256(raw).hexdigest(),
        "files": [{"path": "named-results.json", "bytes": len(sanitized), "sha256": hashlib.sha256(sanitized).hexdigest()}],
        "rawTrxRetained": False, "paramsOrOutputRetained": False,
        "scope": "Actual Customers8 normal Program execution inventory only; not full suite, coverage, browser or real issuer acceptance",
    }
    (output / "manifest.json").write_text(json.dumps(manifest, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    print("[customers-private-failure] actual eight passed executions verified; sanitized proof retained")


def main():
    try:
        retain(Path.cwd(), Path(os.environ["FOCUS_RESULTS"]), Path(os.environ["FOCUS_EVIDENCE"]), os.environ["EXPECTED_SOURCE_REVISION"])
        return 0
    except Exception:
        print("[customers-private-failure] FAILED: invalid_or_unavailable_evidence; details redacted")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
