"""Synthetic rejection controls for the retainer; never lookup execution evidence."""

import importlib.util
import contextlib
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import uuid
import xml.etree.ElementTree as ET

SCRIPT = Path(__file__).resolve().parents[3] / "scripts/verify-thai-lookup-focus.py"
SPEC = importlib.util.spec_from_file_location("lookup_retainer", SCRIPT)
GATE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GATE)


def fixture(expected, outcome="Passed"):
    tag = lambda name: f"{{{GATE.NS}}}{name}"
    root = ET.Element(tag("TestRun"))
    definitions = ET.SubElement(root, tag("TestDefinitions"))
    results = ET.SubElement(root, tag("Results"))
    total = 0
    for cls, methods in expected.items():
        for method, count in methods.items():
            for _ in range(count):
                identity = str(uuid.uuid4())
                unit = ET.SubElement(definitions, tag("UnitTest"), id=identity)
                ET.SubElement(unit, tag("TestMethod"), className=cls, name=method)
                result = ET.SubElement(results, tag("UnitTestResult"), testId=identity,
                                       executionId=str(uuid.uuid4()), outcome=outcome,
                                       testName="DO_NOT_RETAIN_PRIVATE_PARAMETERS")
                ET.SubElement(result, tag("Output")).text = "DO_NOT_RETAIN_PRIVATE_OUTPUT"
                total += 1
    summary = ET.SubElement(root, tag("ResultSummary"))
    ET.SubElement(summary, tag("Counters"), total=str(total), executed=str(total),
                  passed=str(total if outcome == "Passed" else 0), failed=str(total if outcome == "Failed" else 0))
    return root


class RetainerTests(unittest.TestCase):
    def run_receipt(self, failed=False, missing=False, source_valid=True, build="success"):
        with tempfile.TemporaryDirectory() as directory:
            results, evidence = Path(directory) / "results", Path(directory) / "evidence"
            results.mkdir()
            for filename, expected in GATE.EXPECTED.items():
                if missing and filename == "lookup-browser.trx":
                    continue
                outcome = "Failed" if failed and filename == "lookup-bff.trx" else "Passed"
                ET.ElementTree(fixture(expected, outcome)).write(results / filename, encoding="utf-8", xml_declaration=True)
            environment = {"LOOKUP_RESULTS": str(results), "LOOKUP_EVIDENCE": str(evidence),
                           "EXPECTED_SOURCE_REVISION": "1" * 40, "CANDIDATE_HEAD": "2" * 40, "BUILD_OUTCOME": build}
            with patch.dict(os.environ, environment), patch.object(GATE, "verify_source", return_value="1" * 40,
                    side_effect=None if source_valid else ValueError("source_mismatch")), contextlib.redirect_stdout(io.StringIO()):
                code = GATE.main()
            return code, json.loads((evidence / "lookup-focus.json").read_text())

    def test_complete_receipt_does_not_claim_coverage_or_joined_acceptance(self):
        code, receipt = self.run_receipt()
        self.assertEqual(0, code)
        self.assertEqual("passed", receipt["gate"])
        self.assertFalse(receipt["productionCoverageCertified"])
        self.assertFalse(receipt["joinedAcceptanceCertified"])
        self.assertNotIn("DO_NOT_RETAIN", json.dumps(receipt))

    def test_failed_boundary_cannot_become_passing_receipt(self):
        code, receipt = self.run_receipt(failed=True)
        self.assertEqual(1, code)
        self.assertEqual("failed", receipt["gate"])
        self.assertEqual({"Failed": 18}, receipt["groups"]["lookup-bff.trx"]["counts"])

    def test_missing_browser_evidence_cannot_pass(self):
        code, receipt = self.run_receipt(missing=True)
        self.assertEqual(1, code)
        self.assertEqual("incomplete", receipt["gate"])

    def test_wrong_source_cannot_pass(self):
        code, receipt = self.run_receipt(source_valid=False)
        self.assertEqual(1, code)
        self.assertEqual("incomplete", receipt["gate"])

    def test_failed_build_cannot_pass(self):
        code, receipt = self.run_receipt(build="failure")
        self.assertEqual(1, code)
        self.assertEqual("incomplete", receipt["gate"])

    def test_complete_actual_groups_are_counted_and_sensitive_fields_removed(self):
        for filename, expected in GATE.EXPECTED.items():
            records = GATE.verify_trx(fixture(expected), expected)
            self.assertEqual(sum(sum(methods.values()) for methods in expected.values()), len(records))
            self.assertNotIn("DO_NOT_RETAIN", str(records))
            self.assertTrue(all(record["outcome"] == "Passed" for record in records))

    def test_failed_boundary_results_are_retained_as_failed(self):
        expected = GATE.EXPECTED["lookup-bff.trx"]
        records = GATE.verify_trx(fixture(expected, "Failed"), expected)
        self.assertEqual(18, len(records))
        self.assertTrue(all(record["outcome"] == "Failed" for record in records))

    def test_missing_execution_rejected(self):
        expected = GATE.EXPECTED["lookup-unit.trx"]
        root = fixture(expected)
        results = root.find(f"{{{GATE.NS}}}Results")
        results.remove(results[0])
        with self.assertRaises(ValueError):
            GATE.verify_trx(root, expected)

    def test_duplicate_execution_rejected(self):
        expected = GATE.EXPECTED["lookup-unit.trx"]
        root = fixture(expected)
        results = root.find(f"{{{GATE.NS}}}Results")
        results[1].set("executionId", results[0].get("executionId"))
        with self.assertRaises(ValueError):
            GATE.verify_trx(root, expected)

    def test_unrelated_method_rejected(self):
        expected = GATE.EXPECTED["lookup-unit.trx"]
        root = fixture(expected)
        root.find(f".//{{{GATE.NS}}}TestMethod").set("name", "UnrelatedTest")
        with self.assertRaises(ValueError):
            GATE.verify_trx(root, expected)

    def test_counter_disagreement_rejected(self):
        expected = GATE.EXPECTED["lookup-unit.trx"]
        root = fixture(expected)
        root.find(f".//{{{GATE.NS}}}Counters").set("passed", "999")
        with self.assertRaises(ValueError):
            GATE.verify_trx(root, expected)

    def test_dtd_and_entity_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "unsafe.trx"
            path.write_text('<!DOCTYPE TestRun [<!ENTITY x "private">]><TestRun/>')
            with self.assertRaises(ValueError):
                GATE.read_xml(path)

    def test_invalid_source_identity_rejected(self):
        with self.assertRaises(ValueError):
            GATE.verify_source("not-a-revision", "not-a-revision")


if __name__ == "__main__":
    unittest.main()
