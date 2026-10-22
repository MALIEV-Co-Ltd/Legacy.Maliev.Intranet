"""Verify actual hosted TRX identity joins; do not publish raw parameters or diagnostics."""
import json
import os
from pathlib import Path
import xml.etree.ElementTree as ET


def main():
    paths = list(Path(os.environ["FINDER_JOIN_RESULTS"]).glob("*.trx"))
    assert len(paths) == 1, "Exactly one joined TRX required"
    assert not paths[0].is_symlink() and 0 < paths[0].stat().st_size <= 32 * 1024 * 1024
    raw = paths[0].read_bytes()
    assert not any(x in raw.upper().replace(b"\x00", b"") for x in (b"<!DOCTYPE", b"<!ENTITY"))
    root = ET.fromstring(raw)
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counters = root.find("t:ResultSummary/t:Counters", ns)
    assert counters is not None
    for name in ("total", "executed", "passed"):
        assert counters.get(name) == "14", (name, counters.get(name))
    for name in ("failed", "error", "timeout", "aborted", "notExecuted", "disconnected", "inconclusive", "notRunnable", "pending", "inProgress"):
        assert int(counters.get(name, "0")) == 0, name
    rows = root.findall("t:Results/t:UnitTestResult", ns)
    definitions = root.findall("t:TestDefinitions/t:UnitTest", ns)
    entries = root.findall("t:TestEntries/t:TestEntry", ns)
    assert len(rows) == len(definitions) == len(entries) == 14
    defmap = {x.get("id"): x for x in definitions}
    assert len(defmap) == 14
    joins = {(x.get("testId"), x.get("executionId")) for x in entries}
    actual = set()
    families = {"Editor_EditOrClear_PersistsOnlyNoteAndReloadsRealApi": 0,
                "Editor_StaleVersion_LeavesWinningGraphAndLocalNoteWithoutReload": 0,
                "Bff_RejectedAuthority_NeverReachesWriter": 0}
    proof = []
    for row in rows:
        assert row.get("outcome") == "Passed"
        pair = (row.get("testId"), row.get("executionId"))
        assert all(pair) and pair not in actual and pair in joins
        actual.add(pair)
        definition = defmap[pair[0]]
        execution = definition.find("t:Execution", ns)
        method = definition.find("t:TestMethod", ns)
        assert execution is not None and execution.get("id") == pair[1]
        assert method is not None and method.get("className", "").startswith("QuotationRequestFinderPersistence.Acceptance.FinderPersistenceTests")
        family = next((name for name in families if name in row.get("testName", "")), None)
        assert family is not None
        families[family] += 1
        proof.append({"testId": pair[0], "executionId": pair[1], "family": family, "outcome": "Passed"})
    assert actual == joins
    assert list(families.values()) == [8, 2, 4], families
    output = Path(os.environ["FINDER_JOIN_EVIDENCE"])
    output.mkdir(parents=True, exist_ok=True)
    (output / "verified-join.json").write_text(json.dumps({"sourceRevision": os.environ["GITHUB_SHA"],
        "quotationRevision": "3c7fd8562cb61bd8e5b67095ed540c6aed1a9ecf", "actualPassed": 14,
        "families": families, "executions": proof, "genuineAuthIamClaimed": False}, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
