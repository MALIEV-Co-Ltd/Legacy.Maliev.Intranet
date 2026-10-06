"""Fail closed on actual focused TRX; retain no display parameters or test output."""

import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import uuid
import xml.etree.ElementTree as ET

NAMESPACE = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
CLASS = "Legacy.Maliev.Intranet.Tests.FinanceEmptyLookupHttpTests"
EXPECTED = {
    "Lookup_OnlySuccessfulEmptyOrNotFoundBecomesEmpty": 30,
    "Lookup_MalformedSuccessfulResponseRemainsUnavailable": 2,
    "MissingPaymentRemainsNotFoundAndMissingFilesRemainEmpty": 1,
    "EmptyProducerLookupsThroughRealBffRenderExistingEditorWithoutInventedOptions": 2,
}
SOURCE = "Legacy.Maliev.Intranet.Tests/FinanceEmptyLookupHttpTests.cs"
SAFE_FAILURE_CODES = {
    message: message.replace(" ", "_")
    for message in (
        "unsafe evidence path", "unsafe XML", "unexpected TRX namespace", "missing counters",
        "incomplete run", "nonpassing counter", "invalid definition", "nonallowlisted test",
        "conflicting definition", "duplicate or missing execution", "invalid execution identity",
        "failed or skipped case", "method cardinality mismatch", "source identity mismatch",
        "test source differs from exact commit", "evidence destination must be fresh",
        "missing or ambiguous raw evidence", "missing actual raw hits", "invalid raw hits",
        "unexpected coverage copy layout", "conflicting coverage copies",
    )
}


def read_xml(path):
    if path.is_symlink() or not path.is_file():
        raise ValueError("unsafe evidence path")
    data = path.read_bytes()
    if len(data) > 16 * 1024 * 1024 or b"<!DOCTYPE" in data.upper() or b"<!ENTITY" in data.upper():
        raise ValueError("unsafe XML")
    return data, ET.fromstring(data)


def verify_trx(root):
    tag = lambda name: f"{{{NAMESPACE}}}{name}"
    if root.tag != tag("TestRun"):
        raise ValueError("unexpected TRX namespace")
    counters = root.find(f"{tag('ResultSummary')}/{tag('Counters')}")
    if counters is None:
        raise ValueError("missing counters")
    for key in ("total", "executed", "passed"):
        if counters.get(key) != "35":
            raise ValueError("incomplete run")
    if any(int(value) != 0 for key, value in counters.attrib.items() if key not in ("total", "executed", "passed")):
        raise ValueError("nonpassing counter")
    definitions = {}
    for definition in root.findall(f"{tag('TestDefinitions')}/{tag('UnitTest')}"):
        identity = definition.get("id")
        method = definition.find(tag("TestMethod"))
        if not identity or method is None:
            raise ValueError("invalid definition")
        if method.get("className", "").split(",", 1)[0] != CLASS or method.get("name") not in EXPECTED:
            raise ValueError("nonallowlisted test")
        if identity in definitions and definitions[identity] != method.get("name"):
            raise ValueError("conflicting definition")
        definitions[identity] = method.get("name")
    records = []
    identities, executions = set(), set()
    counts = dict.fromkeys(EXPECTED, 0)
    for result in root.findall(f"{tag('Results')}/{tag('UnitTestResult')}"):
        identity, execution = result.get("testId"), result.get("executionId")
        if identity not in definitions or not execution or execution in executions:
            raise ValueError("duplicate or missing execution")
        if str(uuid.UUID(identity)) != identity.lower() or str(uuid.UUID(execution)) != execution.lower():
            raise ValueError("invalid execution identity")
        if result.get("outcome") != "Passed":
            raise ValueError("failed or skipped case")
        identities.add(identity)
        executions.add(execution)
        method = definitions[identity]
        counts[method] += 1
        records.append({"method": method, "testId": identity, "executionId": execution, "outcome": "Passed"})
    if counts != EXPECTED or len(records) != 35 or len(executions) != 35 or identities != set(definitions):
        raise ValueError("method cardinality mismatch")
    return records


def select_coverage(results, coverage):
    if len(coverage) == 1:
        return coverage[0]
    if len(coverage) != 2:
        raise ValueError("missing or ambiguous raw evidence")
    collectors, attachments = [], []
    for path in coverage:
        parts = path.relative_to(results).parts
        if len(parts) == 2 and re.fullmatch(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}", parts[0]):
            collectors.append(path)
        elif len(parts) == 4 and parts[1] == "In" and re.fullmatch(r"[A-Za-z0-9_.-]{1,128}", parts[2]):
            folder = "_" + re.escape(parts[2]) + r"_\d{4}-\d{2}-\d{2}_\d{2}_\d{2}_\d{2}"
            if re.fullmatch(folder, parts[0]):
                attachments.append(path)
    if len(collectors) != 1 or len(attachments) != 1:
        raise ValueError("unexpected coverage copy layout")
    collector_bytes, _ = read_xml(collectors[0])
    attachment_bytes, _ = read_xml(attachments[0])
    if collector_bytes != attachment_bytes:
        raise ValueError("conflicting coverage copies")
    return collectors[0]


def retain(repository, results, output, expected_revision):
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repository, text=True).strip()
    if not re.fullmatch(r"[0-9a-f]{40}", expected_revision) or revision != expected_revision:
        raise ValueError("source identity mismatch")
    source = (repository / SOURCE).read_bytes()
    committed = subprocess.check_output(["git", "show", f"HEAD:{SOURCE}"], cwd=repository)
    if source.replace(b"\r\n", b"\n") != committed:
        raise ValueError("test source differs from exact commit")
    if output.exists():
        raise ValueError("evidence destination must be fresh")
    trx = list(results.rglob("*.trx"))
    coverage = list(results.rglob("coverage.cobertura.xml"))
    if len(trx) != 1:
        raise ValueError("missing or ambiguous raw evidence")
    trx_bytes, root = read_xml(trx[0])
    records = verify_trx(root)
    raw, coverage_root = read_xml(select_coverage(results, coverage))
    if coverage_root.tag != "coverage" or not coverage_root.findall("./packages/package/classes/class/lines/line"):
        raise ValueError("missing actual raw hits")
    if any(not re.fullmatch(r"\d+", line.get("hits", "")) for line in coverage_root.findall("./packages/package/classes/class/lines/line")):
        raise ValueError("invalid raw hits")
    output.mkdir(parents=True)
    sanitized = json.dumps({"class": CLASS, "cases": records, "methodCardinalities": EXPECTED}, sort_keys=True, indent=2).encode()
    (output / "named-results.json").write_bytes(sanitized)
    (output / "coverage.cobertura.xml").write_bytes(raw)
    files = [
        {"path": name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}
        for name, data in (("named-results.json", sanitized), ("coverage.cobertura.xml", raw))
    ]
    manifest = {
        "schemaVersion": 1, "sourceRevision": revision, "complete": True,
        "testSourceSha256": hashlib.sha256(source).hexdigest(),
        "testGitBlobSha256": hashlib.sha256(committed).hexdigest(),
        "actualRawTrxSha256": hashlib.sha256(trx_bytes).hexdigest(),
        "actualRawCoverageCopies": len(coverage),
        "passedCases": 35, "methodCardinalities": EXPECTED, "files": files,
        "scope": "Focused case inventory and actual hits only; not full-suite coverage or full-floor acceptance",
        "rawTrxRetained": False, "paramsOrOutputRetained": False,
    }
    (output / "manifest.json").write_text(json.dumps(manifest, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    print("[finance-focus] actual 35 passed executions verified (30+2+1+2); sanitized evidence retained")


def main():
    try:
        retain(Path.cwd(), Path(os.environ["FOCUS_RESULTS"]), Path(os.environ["FOCUS_EVIDENCE"]), os.environ["EXPECTED_SOURCE_REVISION"])
        return 0
    except Exception as error:
        # Fixed code-owned categories only; never print external errors, XML, parameters or output.
        code = SAFE_FAILURE_CODES.get(str(error), "unclassified_failure") if type(error) is ValueError else "unclassified_failure"
        print(f"[finance-focus] FAILED: {code}; details redacted")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
