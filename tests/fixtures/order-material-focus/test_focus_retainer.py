"""Finite offline synthetic controls; never native/compiler/test acceptance evidence."""

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import uuid
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location("order_material_focus", REPO / "scripts/verify-order-material-focus.py")
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
            definition = ET.SubElement(definitions, TAG("UnitTest"), id=identity, name="private-parameter-canary")
            ET.SubElement(definition, TAG("Execution"), id=execution)
            ET.SubElement(definition, TAG("TestMethod"), className=FOCUS.CLASS + ", TestAssembly", name=method)
            result = ET.SubElement(results, TAG("UnitTestResult"), testId=identity, executionId=execution, outcome="Passed", testName="private-parameter-canary")
            ET.SubElement(ET.SubElement(result, TAG("Output")), TAG("StdOut")).text = "private-output-canary"
    summary = ET.SubElement(root, TAG("ResultSummary"), outcome="Completed")
    ET.SubElement(summary, TAG("Counters"), **{key: "14" if key in {"total", "executed", "passed"} else "0" for key in FOCUS.COUNTERS})
    return root


class FocusRetainerControls(unittest.TestCase):
    def reject(self, root):
        with self.assertRaises(ValueError):
            FOCUS.verify_trx(root)

    def test_complete_method_matrix_and_parameter_redaction(self):
        records = FOCUS.verify_trx(valid_trx())
        self.assertEqual(14, len(records))
        self.assertEqual(FOCUS.EXPECTED, {method: sum(row["method"] == method for row in records) for method in FOCUS.EXPECTED})
        self.assertNotIn("private-", json.dumps(records))

    def test_shared_theory_test_ids_with_exact_execution_bindings_are_admitted(self):
        root = valid_trx()
        definitions, results = root.find(TAG("TestDefinitions")), root.find(TAG("Results"))
        shared = {}
        for definition, result in zip(definitions, results):
            method = definition.find(TAG("TestMethod")).get("name")
            shared.setdefault(method, definition.get("id"))
            definition.set("id", shared[method])
            result.set("testId", shared[method])
        records = FOCUS.verify_trx(root)
        self.assertEqual(14, len(records))
        self.assertEqual(len(FOCUS.EXPECTED), len({row["testId"] for row in records}))
        self.assertEqual(14, len({row["executionId"] for row in records}))

    def test_shared_test_id_conflicting_methods_are_rejected(self):
        root = valid_trx()
        definitions = root.find(TAG("TestDefinitions"))
        definitions[1].set("id", definitions[0].get("id"))
        with self.assertRaisesRegex(ValueError, "conflicting definition"):
            FOCUS.verify_trx(root)

    def test_all_required_counters_are_mandatory(self):
        for key in FOCUS.COUNTERS:
            with self.subTest(key=key):
                root = valid_trx()
                del root.find(f"{TAG('ResultSummary')}/{TAG('Counters')}").attrib[key]
                self.reject(root)

    def test_nonzero_nonpassing_or_wrong_passing_counters_rejected(self):
        for key in FOCUS.COUNTERS:
            with self.subTest(key=key):
                root = valid_trx()
                root.find(f"{TAG('ResultSummary')}/{TAG('Counters')}").set(key, "1")
                self.reject(root)

    def test_unknown_or_malformed_counters_rejected(self):
        for key, value in (("invented", "0"), ("failed", "-1"), ("passed", "13x")):
            with self.subTest(key=key):
                root = valid_trx()
                root.find(f"{TAG('ResultSummary')}/{TAG('Counters')}").set(key, value)
                self.reject(root)

    def test_noncompleted_summary_or_duplicate_summary_rejected(self):
        root = valid_trx()
        root.find(TAG("ResultSummary")).set("outcome", "Failed")
        self.reject(root)
        root = valid_trx()
        root.append(root.find(TAG("ResultSummary")))
        self.reject(root)

    def test_wrong_namespace_rejected(self):
        self.reject(ET.Element("TestRun"))

    def test_missing_case_rejected(self):
        root = valid_trx()
        results = root.find(TAG("Results"))
        results.remove(results[0])
        self.reject(root)

    def test_extra_result_rejected(self):
        root = valid_trx()
        results = root.find(TAG("Results"))
        results.append(results[0])
        self.reject(root)

    def test_failed_and_skipped_case_rejected(self):
        for outcome in ("Failed", "NotExecuted", "Skipped"):
            with self.subTest(outcome=outcome):
                root = valid_trx()
                root.find(TAG("Results"))[0].set("outcome", outcome)
                self.reject(root)

    def test_class_and_method_drift_rejected(self):
        for key, value in (("className", FOCUS.CLASS + "Other"), ("name", "UnexpectedMethod")):
            with self.subTest(key=key):
                root = valid_trx()
                root.find(TAG("TestDefinitions"))[0].find(TAG("TestMethod")).set(key, value)
                self.reject(root)

    def test_method_cardinality_drift_rejected(self):
        root = valid_trx()
        definitions = root.find(TAG("TestDefinitions"))
        definitions[0].find(TAG("TestMethod")).set("name", list(FOCUS.EXPECTED)[1])
        self.reject(root)

    def test_duplicate_definition_and_unused_definition_rejected(self):
        root = valid_trx()
        definitions = root.find(TAG("TestDefinitions"))
        definitions.append(definitions[0])
        self.reject(root)
        root = valid_trx()
        definitions = root.find(TAG("TestDefinitions"))
        extra = ET.fromstring(ET.tostring(definitions[0]))
        extra.set("id", str(uuid.uuid4()))
        definitions.append(extra)
        self.reject(root)

    def test_execution_must_match_definition_and_be_unique(self):
        root = valid_trx()
        results = root.find(TAG("Results"))
        results[0].set("executionId", str(uuid.uuid4()))
        self.reject(root)
        root = valid_trx()
        results = root.find(TAG("Results"))
        results[1].set("executionId", results[0].get("executionId"))
        self.reject(root)

    def test_unknown_or_malformed_test_identity_rejected(self):
        for value in (str(uuid.uuid4()), "invalid", "", None):
            with self.subTest(value=value):
                root = valid_trx()
                result = root.find(TAG("Results"))[0]
                if value is None:
                    del result.attrib["testId"]
                else:
                    result.set("testId", value)
                self.reject(root)

    def test_missing_definition_execution_rejected(self):
        root = valid_trx()
        definition = root.find(TAG("TestDefinitions"))[0]
        definition.remove(definition.find(TAG("Execution")))
        self.reject(root)

    def fixture(self, root):
        repository, results, output = root / "repository", root / "results", root / "evidence"
        results.mkdir()
        for name in FOCUS.SOURCES:
            path = repository / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"controlled-source\n")
        (results / "actual.trx").write_bytes(ET.tostring(valid_trx()))
        return repository, results, output

    @staticmethod
    def git(command, **kwargs):
        return REVISION + "\n" if command[1] == "rev-parse" else b"controlled-source\n"

    def test_retain_only_sanitized_outcomes_hashes_and_revision(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(FOCUS.subprocess, "check_output", side_effect=self.git):
            repository, results, output = self.fixture(Path(temporary))
            with patch("builtins.print"):
                FOCUS.retain(repository, results, output, REVISION)
            self.assertEqual({"named-results.json", "manifest.json"}, {path.name for path in output.iterdir()})
            manifest = json.loads((output / "manifest.json").read_text())
            self.assertEqual(REVISION, manifest["sourceRevision"])
            self.assertEqual(14, manifest["passedCases"])
            self.assertEqual(len(FOCUS.SOURCES), len(manifest["sources"]))
            self.assertFalse(manifest["rawTrxRetained"])
            self.assertFalse(manifest["paramsOrOutputRetained"])
            self.assertNotIn("private-", "".join(path.read_text() for path in output.iterdir()))

    def test_wrong_revision_or_modified_source_rejected_before_retention(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(FOCUS.subprocess, "check_output", side_effect=self.git):
            repository, results, output = self.fixture(Path(temporary))
            with self.assertRaisesRegex(ValueError, "source identity mismatch"):
                FOCUS.retain(repository, results, output, "b" * 40)
            (repository / FOCUS.SOURCES[0]).write_bytes(b"modified\n")
            with self.assertRaisesRegex(ValueError, "source differs from commit"):
                FOCUS.retain(repository, results, output, REVISION)
            self.assertFalse(output.exists())

    def test_missing_or_ambiguous_trx_rejected(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(FOCUS.subprocess, "check_output", side_effect=self.git):
            repository, results, output = self.fixture(Path(temporary))
            (results / "extra.trx").write_bytes(ET.tostring(valid_trx()))
            with self.assertRaisesRegex(ValueError, "missing or ambiguous TRX"):
                FOCUS.retain(repository, results, output, REVISION)
            for path in results.iterdir():
                path.unlink()
            with self.assertRaisesRegex(ValueError, "missing or ambiguous TRX"):
                FOCUS.retain(repository, results, output, REVISION)

    def test_existing_destination_rejected(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(FOCUS.subprocess, "check_output", side_effect=self.git):
            repository, results, output = self.fixture(Path(temporary))
            output.mkdir()
            with self.assertRaisesRegex(ValueError, "evidence destination must be fresh"):
                FOCUS.retain(repository, results, output, REVISION)

    def test_unsafe_xml_and_symlink_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            path = root / "test.trx"
            path.write_bytes(b"<!DOCTYPE private [<!ENTITY x 'private'>]><TestRun />")
            with self.assertRaisesRegex(ValueError, "unsafe XML"):
                FOCUS.read_xml(path)
            with patch.object(Path, "is_symlink", return_value=True), self.assertRaisesRegex(ValueError, "unsafe evidence path"):
                FOCUS.read_xml(path)

    def test_entrypoint_private_failures_are_redacted(self):
        environment = {"FOCUS_RESULTS": "results", "FOCUS_EVIDENCE": "evidence", "EXPECTED_SOURCE_REVISION": REVISION}
        for error, code in ((ValueError("invalid counters"), "invalid_counters"), (ValueError("private-canary"), "unclassified_failure"), (OSError("private-canary"), "unclassified_failure")):
            with self.subTest(code=code), patch.dict(FOCUS.os.environ, environment), patch.object(FOCUS, "retain", side_effect=error), patch("builtins.print") as output:
                self.assertEqual(1, FOCUS.main())
                output.assert_called_once_with(f"[order-material-focus] FAILED: {code}; details redacted")

    def test_entrypoint_success_preserves_result(self):
        environment = {"FOCUS_RESULTS": "results", "FOCUS_EVIDENCE": "evidence", "EXPECTED_SOURCE_REVISION": REVISION}
        with patch.dict(FOCUS.os.environ, environment), patch.object(FOCUS, "retain") as retain:
            self.assertEqual(0, FOCUS.main())
            retain.assert_called_once()


if __name__ == "__main__":
    unittest.main()
