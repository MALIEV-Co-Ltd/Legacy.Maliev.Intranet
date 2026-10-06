"""Verify actual focused TRX identities; retain no display parameters or execution output."""

import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import uuid
import xml.etree.ElementTree as ET

NAMESPACE = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
CLASS = "Legacy.Maliev.Intranet.Tests.OrderDetailMaterialChoicesUiTests"
EXPECTED = {
    "InitialSelections_NoScopedReadAndUnchangedWritePreservesCustomFinishAndColor": 1,
    "MaterialChanged_UsesReadOnlyCatalogRouteAndClearsOnlyIncompatibleFinish": 2,
    "EmptyScopedChoices_ClearsIncompatibleFinishPreservesColorAndAllowsSave": 1,
    "MaterialCleared_NoRequestClearsFinishNotColor": 1,
    "UnknownMaterial_NeverRequestsOrWritesAndRetainsCapturedSelections": 1,
    "FailedChoices_PreservesSelectionsLocksWriteAndRequiresExplicitRetry": 6,
    "NewerMaterial_CancelsHeldReadAndLateResponseCannotOverwriteScopedChoices": 1,
    "Dispose_CancelsPendingReadWithoutWriteOrLateRender": 1,
}
COUNTERS = {
    "total", "executed", "passed", "error", "failed", "timeout", "aborted", "inconclusive",
    "passedButRunAborted", "notRunnable", "notExecuted", "disconnected", "warning", "completed",
    "inProgress", "pending",
}
SOURCES = (
    "Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderDetail.razor",
    "Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderDetail.resx",
    "Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderDetail.th.resx",
    "Legacy.Maliev.Intranet.Tests/OrderDetailMaterialChoicesUiTests.cs",
    "Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj",
    "docs/order-detail-material-choices-source-20261006.md",
    ".github/workflows/order-material-focus.yml",
    "scripts/verify-order-material-focus.py",
    "tests/fixtures/order-material-focus/test_focus_retainer.py",
)
FAILURES = {
    "unsafe evidence path", "unsafe XML", "invalid TRX", "invalid counters", "nonpassing counters",
    "invalid definition", "nonallowlisted test", "duplicate definition", "conflicting definition", "invalid identity",
    "invalid execution", "failed or skipped case", "method cardinality mismatch",
    "source identity mismatch", "source differs from commit", "evidence destination must be fresh",
    "missing or ambiguous TRX",
}


def identity(value):
    try:
        if not isinstance(value, str) or str(uuid.UUID(value)) != value.lower():
            raise ValueError("invalid identity")
    except (ValueError, AttributeError) as error:
        raise ValueError("invalid identity") from error
    return value.lower()


def read_xml(path):
    if path.is_symlink() or not path.is_file() or any(parent.is_symlink() for parent in path.parents):
        raise ValueError("unsafe evidence path")
    data = path.read_bytes()
    if len(data) > 16 * 1024 * 1024 or b"<!DOCTYPE" in data.upper() or b"<!ENTITY" in data.upper():
        raise ValueError("unsafe XML")
    return data, ET.fromstring(data)


def verify_trx(root):
    tag = lambda name: f"{{{NAMESPACE}}}{name}"
    if root.tag != tag("TestRun"):
        raise ValueError("invalid TRX")
    summaries = root.findall(tag("ResultSummary"))
    if len(summaries) != 1 or summaries[0].get("outcome") != "Completed":
        raise ValueError("invalid TRX")
    counter_elements = summaries[0].findall(tag("Counters"))
    if len(counter_elements) != 1 or set(counter_elements[0].attrib) != COUNTERS:
        raise ValueError("invalid counters")
    counters = counter_elements[0].attrib
    if any(not re.fullmatch(r"\d+", value) for value in counters.values()):
        raise ValueError("invalid counters")
    if any(int(counters[key]) != (14 if key in {"total", "executed", "passed"} else 0) for key in COUNTERS):
        raise ValueError("nonpassing counters")
    if len(root.findall(tag("TestDefinitions"))) != 1 or len(root.findall(tag("Results"))) != 1:
        raise ValueError("invalid TRX")
    definitions, bindings = {}, set()
    for definition in root.findall(f"{tag('TestDefinitions')}/{tag('UnitTest')}"):
        test_id = identity(definition.get("id"))
        methods, executions = definition.findall(tag("TestMethod")), definition.findall(tag("Execution"))
        if len(methods) != 1 or len(executions) != 1:
            raise ValueError("invalid definition")
        method = methods[0]
        if method.get("className", "").split(",", 1)[0] != CLASS or method.get("name") not in EXPECTED:
            raise ValueError("nonallowlisted test")
        method_name = method.get("name")
        if test_id in definitions and definitions[test_id] != method_name:
            raise ValueError("conflicting definition")
        binding = (test_id, identity(executions[0].get("id")))
        if binding in bindings:
            raise ValueError("duplicate definition")
        definitions[test_id] = method_name
        bindings.add(binding)
    records, used_bindings, used_executions = [], set(), set()
    counts = dict.fromkeys(EXPECTED, 0)
    for result in root.findall(f"{tag('Results')}/{tag('UnitTestResult')}"):
        test_id, execution_id = identity(result.get("testId")), identity(result.get("executionId"))
        binding = (test_id, execution_id)
        if binding not in bindings or execution_id in used_executions:
            raise ValueError("invalid execution")
        if result.get("outcome") != "Passed":
            raise ValueError("failed or skipped case")
        used_bindings.add(binding)
        used_executions.add(execution_id)
        method = definitions[test_id]
        counts[method] += 1
        records.append({"method": method, "testId": test_id, "executionId": execution_id, "outcome": "Passed"})
    if counts != EXPECTED or len(records) != 14 or used_bindings != bindings:
        raise ValueError("method cardinality mismatch")
    return records


def retain(repository, results, output, expected_revision):
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repository, text=True).strip()
    if not re.fullmatch(r"[0-9a-f]{40}", expected_revision) or revision != expected_revision:
        raise ValueError("source identity mismatch")
    sources = []
    for name in SOURCES:
        path = repository / name
        if path.is_symlink() or not path.is_file() or any(parent.is_symlink() for parent in path.parents):
            raise ValueError("unsafe evidence path")
        data = path.read_bytes()
        committed = subprocess.check_output(["git", "show", f"HEAD:{name}"], cwd=repository)
        if data.replace(b"\r\n", b"\n") != committed.replace(b"\r\n", b"\n"):
            raise ValueError("source differs from commit")
        sources.append({"path": name, "checkoutSha256": hashlib.sha256(data).hexdigest(), "gitBlobSha256": hashlib.sha256(committed).hexdigest()})
    if output.exists() or output.is_symlink() or any(parent.is_symlink() for parent in output.parents):
        raise ValueError("evidence destination must be fresh")
    if results.is_symlink() or not results.is_dir() or any(parent.is_symlink() for parent in results.parents):
        raise ValueError("unsafe evidence path")
    trx = list(results.rglob("*.trx"))
    if len(trx) != 1:
        raise ValueError("missing or ambiguous TRX")
    raw, root = read_xml(trx[0])
    records = verify_trx(root)
    sanitized = json.dumps({"class": CLASS, "methodCardinalities": EXPECTED, "cases": records}, sort_keys=True, indent=2).encode()
    output.mkdir(parents=True)
    (output / "named-results.json").write_bytes(sanitized)
    manifest = {
        "schemaVersion": 1, "sourceRevision": revision, "complete": True, "passedCases": 14,
        "methodCardinalities": EXPECTED, "sources": sources,
        "actualRawTrxSha256": hashlib.sha256(raw).hexdigest(),
        "files": [{"path": "named-results.json", "bytes": len(sanitized), "sha256": hashlib.sha256(sanitized).hexdigest()}],
        "rawTrxRetained": False, "paramsOrOutputRetained": False,
        "scope": "Actual focused UI execution inventory only; not full suite, coverage, browser, BFF/Catalog or migration acceptance",
    }
    (output / "manifest.json").write_text(json.dumps(manifest, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    print("[order-material-focus] actual 14 passed executions verified; sanitized evidence retained")


def main():
    try:
        retain(Path.cwd(), Path(os.environ["FOCUS_RESULTS"]), Path(os.environ["FOCUS_EVIDENCE"]), os.environ["EXPECTED_SOURCE_REVISION"])
        return 0
    except Exception as error:
        # Fixed code-owned messages only; never print input XML, parameters, paths or test output.
        code = str(error).replace(" ", "_") if type(error) is ValueError and str(error) in FAILURES else "unclassified_failure"
        print(f"[order-material-focus] FAILED: {code}; details redacted")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
