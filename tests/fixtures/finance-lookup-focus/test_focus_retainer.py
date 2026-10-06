"""Offline adversarial controls; these synthetic fixtures are not native test evidence."""

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import uuid
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location("finance_focus", REPO / "scripts/verify-finance-lookup-focus.py")
FOCUS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(FOCUS)
TAG = lambda name: f"{{{FOCUS.NAMESPACE}}}{name}"
REVISION = "a" * 40


def valid_trx():
    root = ET.Element(TAG("TestRun"))
    definitions = ET.SubElement(root, TAG("TestDefinitions"))
    results = ET.SubElement(root, TAG("Results"))
    for method, count in FOCUS.EXPECTED.items():
        for _ in range(count):
            identity, execution = str(uuid.uuid4()), str(uuid.uuid4())
            definition = ET.SubElement(definitions, TAG("UnitTest"), id=identity, name="parameter-marker-not-retained")
            ET.SubElement(definition, TAG("TestMethod"), className=FOCUS.CLASS, name=method)
            result = ET.SubElement(results, TAG("UnitTestResult"), testId=identity, executionId=execution, outcome="Passed")
            ET.SubElement(ET.SubElement(result, TAG("Output")), TAG("StdOut")).text = "private-test-output"
    summary = ET.SubElement(root, TAG("ResultSummary"))
    ET.SubElement(summary, TAG("Counters"), total="35", executed="35", passed="35", failed="0", notExecuted="0")
    return root


class FocusRetainerControls(unittest.TestCase):
    def test_native_collector_and_identical_trx_attachment_copy_are_admitted(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            collector = root / "2f449604-cb13-4ee9-9f63-787aa8d5977f/coverage.cobertura.xml"
            attachment = root / "_runner_2026-10-05_19_54_36/In/runner/coverage.cobertura.xml"
            for path in (collector, attachment):
                path.parent.mkdir(parents=True)
                path.write_bytes(b"<coverage />")
            self.assertEqual(collector, FOCUS.select_coverage(root, [attachment, collector]))

    def test_conflicting_or_extra_native_coverage_copies_are_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            collector = root / "2f449604-cb13-4ee9-9f63-787aa8d5977f/coverage.cobertura.xml"
            attachment = root / "_runner_2026-10-05_19_54_36/In/runner/coverage.cobertura.xml"
            for path in (collector, attachment):
                path.parent.mkdir(parents=True)
                path.write_bytes(b"<coverage />")
            attachment.write_bytes(b"<coverage different='true' />")
            with self.assertRaisesRegex(ValueError, "conflicting coverage copies"):
                FOCUS.select_coverage(root, [collector, attachment])
            with self.assertRaises(ValueError):
                FOCUS.select_coverage(root, [collector, attachment, root / "extra/coverage.cobertura.xml"])

    def test_unknown_or_mismatched_attachment_layout_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            collector = root / "2f449604-cb13-4ee9-9f63-787aa8d5977f/coverage.cobertura.xml"
            for relative in ("other/coverage.cobertura.xml", "_runner_2026-10-05_19_54_36/In/different/coverage.cobertura.xml"):
                with self.subTest(relative=relative), self.assertRaisesRegex(ValueError, "unexpected coverage copy layout"):
                    FOCUS.select_coverage(root, [collector, root / relative])

    def test_missing_coverage_is_rejected(self):
        with self.assertRaises(ValueError):
            FOCUS.select_coverage(Path("fixture"), [])

    def test_failure_diagnostics_are_fixed_codes_and_never_external_details(self):
        environment = {"FOCUS_RESULTS": "fixture-results", "FOCUS_EVIDENCE": "fixture-evidence", "EXPECTED_SOURCE_REVISION": REVISION}
        failures = (
            (ValueError("source identity mismatch"), "source_identity_mismatch"),
            (ValueError("private-diagnostic-canary"), "unclassified_failure"),
            (OSError("private-diagnostic-canary"), "unclassified_failure"),
        )
        for failure, code in failures:
            with self.subTest(code=code), patch.dict(FOCUS.os.environ, environment), patch.object(FOCUS, "retain", side_effect=failure), patch("builtins.print") as output:
                self.assertEqual(1, FOCUS.main())
                output.assert_called_once_with(f"[finance-focus] FAILED: {code}; details redacted")
                self.assertNotIn("private-diagnostic-canary", str(output.call_args))

    def test_success_entrypoint_preserves_retention_result(self):
        environment = {"FOCUS_RESULTS": "fixture-results", "FOCUS_EVIDENCE": "fixture-evidence", "EXPECTED_SOURCE_REVISION": REVISION}
        with patch.dict(FOCUS.os.environ, environment), patch.object(FOCUS, "retain") as retain:
            self.assertEqual(0, FOCUS.main())
            retain.assert_called_once()

    def test_complete_method_matrix(self):
        records = FOCUS.verify_trx(valid_trx())
        self.assertEqual(35, len(records))
        self.assertEqual(FOCUS.EXPECTED, {method: sum(row["method"] == method for row in records) for method in FOCUS.EXPECTED})

    def test_missing_case_rejected(self):
        root = valid_trx()
        root.find(TAG("Results")).remove(root.find(TAG("Results"))[0])
        with self.assertRaises(ValueError):
            FOCUS.verify_trx(root)

    def test_failed_and_skipped_cases_rejected(self):
        for outcome in ("Failed", "NotExecuted"):
            with self.subTest(outcome=outcome):
                root = valid_trx()
                root.find(TAG("Results"))[0].set("outcome", outcome)
                with self.assertRaises(ValueError):
                    FOCUS.verify_trx(root)

    def test_failed_counter_rejected(self):
        root = valid_trx()
        root.find(f"{TAG('ResultSummary')}/{TAG('Counters')}").set("failed", "1")
        with self.assertRaises(ValueError):
            FOCUS.verify_trx(root)

    def test_duplicate_execution_rejected(self):
        root = valid_trx()
        results = root.find(TAG("Results"))
        results[1].set("executionId", results[0].get("executionId"))
        with self.assertRaises(ValueError):
            FOCUS.verify_trx(root)

    def test_repeated_theory_identity_with_unique_executions_is_valid(self):
        root = valid_trx()
        definitions, results = root.find(TAG("TestDefinitions")), root.find(TAG("Results"))
        shared = definitions[0].get("id")
        for index in range(30):
            definitions[index].set("id", shared)
            results[index].set("testId", shared)
        records = FOCUS.verify_trx(root)
        self.assertEqual(35, len(records))
        self.assertEqual(35, len({row["executionId"] for row in records}))
        self.assertEqual(6, len({row["testId"] for row in records}))

    def test_conflicting_shared_definition_rejected(self):
        root = valid_trx()
        definitions = root.find(TAG("TestDefinitions"))
        definitions[30].set("id", definitions[0].get("id"))
        with self.assertRaises(ValueError):
            FOCUS.verify_trx(root)

    def test_wrong_method_cardinality_rejected(self):
        root = valid_trx()
        root.find(TAG("TestDefinitions"))[0].find(TAG("TestMethod")).set("name", "Lookup_MalformedSuccessfulResponseRemainsUnavailable")
        with self.assertRaises(ValueError):
            FOCUS.verify_trx(root)

    def test_foreign_test_rejected(self):
        root = valid_trx()
        root.find(TAG("TestDefinitions"))[0].find(TAG("TestMethod")).set("className", "Other.Class")
        with self.assertRaises(ValueError):
            FOCUS.verify_trx(root)

    def test_retention_and_source_raw_rejection(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / FOCUS.SOURCE
            source.parent.mkdir(parents=True)
            source.write_text("synthetic source control", encoding="utf-8")
            results = root / "results"
            results.mkdir()
            ET.ElementTree(valid_trx()).write(results / "focus.trx", encoding="utf-8", xml_declaration=True)
            coverage = results / "coverage.cobertura.xml"
            coverage.write_text('<coverage><packages><package><classes><class><lines><line hits="1" number="1" /></lines></class></classes></package></packages></coverage>', encoding="utf-8")
            def git_result(command, **kwargs):
                return REVISION if command[1] == "rev-parse" else b"synthetic source control"

            with patch.object(FOCUS.subprocess, "check_output", side_effect=git_result), patch("builtins.print"):
                with self.assertRaises(ValueError):
                    FOCUS.retain(root, results, root / "mismatched", "b" * 40)
                source.write_text("changed source", encoding="utf-8")
                with self.assertRaises(ValueError):
                    FOCUS.retain(root, results, root / "dirty-source", REVISION)
                source.write_text("synthetic source control", encoding="utf-8")
                FOCUS.retain(root, results, root / "evidence", REVISION)
                named = (root / "evidence/named-results.json").read_text(encoding="utf-8")
                self.assertNotIn("parameter-marker-not-retained", named)
                self.assertNotIn("private-test-output", named)
                self.assertEqual(35, len(json.loads(named)["cases"]))
                self.assertEqual(coverage.read_bytes(), (root / "evidence/coverage.cobertura.xml").read_bytes())
                manifest = json.loads((root / "evidence/manifest.json").read_text(encoding="utf-8"))
                self.assertEqual(REVISION, manifest["sourceRevision"])
                self.assertFalse(manifest["rawTrxRetained"])
                coverage.unlink()
                with self.assertRaises(ValueError):
                    FOCUS.retain(root, results, root / "missing-raw", REVISION)
                self.assertFalse((root / "missing-raw").exists())


if __name__ == "__main__":
    unittest.main()
