"""Require actual unchanged browser executions, including the existing EN/TH DOM cases."""
import json
import os
from pathlib import Path
import xml.etree.ElementTree as ET

paths = list((Path(os.environ["RUNNER_TEMP"]) / "finder-browser-results").glob("*.trx"))
assert len(paths) == 1 and not paths[0].is_symlink() and 0 < paths[0].stat().st_size <= 32 * 1024 * 1024
raw = paths[0].read_bytes()
assert not any(x in raw.upper().replace(b"\x00", b"") for x in (b"<!DOCTYPE", b"<!ENTITY"))
root = ET.fromstring(raw)
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
rows = root.findall("t:Results/t:UnitTestResult", ns)
assert rows and all(x.get("outcome") == "Passed" for x in rows)
names = [x.get("testName", "") for x in rows]
assert all("QuotationRequestFinderBrowserTests" in name for name in names)
for culture in ("en-TH", "th-TH"):
    assert any("FinderSummary_LocalizedDom_EncodesNoteAndPreservesEnvelopeOnSave" in name and culture in name for name in names), culture
counts = root.find("t:ResultSummary/t:Counters", ns)
assert counts is not None and int(counts.get("total", "0")) == int(counts.get("executed", "0")) == int(counts.get("passed", "0")) == len(rows)
output = Path(os.environ["FINDER_JOIN_EVIDENCE"])
output.mkdir(parents=True, exist_ok=True)
(output / "verified-existing-browser.json").write_text(json.dumps({
    "sourceRevision": os.environ["GITHUB_SHA"], "actualPassed": len(rows),
    "unchangedFinderCohort": True, "localizedDomCultures": ["en-TH", "th-TH"],
    "browserBffInterceptIsNotJoinedPersistence": True,
}, indent=2), encoding="utf-8")
