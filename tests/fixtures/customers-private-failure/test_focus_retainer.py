"""Synthetic parser controls only; not native Customers execution evidence."""

import importlib.util
import json
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location("customers_focus", REPO / "scripts/verify-customers-private-failure-focus.py")
FOCUS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(FOCUS)
TAG = lambda name: f"{{{FOCUS.NAMESPACE}}}{name}"


def valid_trx():
    root = ET.Element(TAG("TestRun"))
    definitions = ET.SubElement(root, TAG("TestDefinitions"))
    results = ET.SubElement(root, TAG("Results"))
    for method, count in FOCUS.EXPECTED.items():
        test_id = str(uuid.uuid4())
        for variant in range(count):
            execution = str(uuid.uuid4())
            display_name = f"{method}(private-parameter-canary-{variant})"
            definition = ET.SubElement(definitions, TAG("UnitTest"), id=test_id, name=display_name)
            ET.SubElement(definition, TAG("Execution"), id=execution)
            ET.SubElement(definition, TAG("TestMethod"), className=FOCUS.CLASS + ", TestAssembly", name=method)
            result = ET.SubElement(results, TAG("UnitTestResult"), testId=test_id, executionId=execution, outcome="Passed", testName=display_name)
            ET.SubElement(ET.SubElement(result, TAG("Output")), TAG("StdOut")).text = "private-output-canary"
    summary = ET.SubElement(root, TAG("ResultSummary"), outcome="Completed")
    ET.SubElement(summary, TAG("Counters"), **{key: "8" if key in {"total", "executed", "passed"} else "0" for key in FOCUS.COUNTERS})
    return root


class CustomersFocusControls(unittest.TestCase):
    def reject(self, root):
        with self.assertRaises(ValueError):
            FOCUS.verify_trx(root)

    def test_exact_eight_theory_bindings_and_redaction(self):
        records = FOCUS.verify_trx(valid_trx())
        self.assertEqual(8, len(records))
        self.assertEqual(5, len({row["testId"] for row in records}))
        self.assertEqual(8, len({row["executionId"] for row in records}))
        self.assertNotIn("private-", json.dumps(records))

    def test_all_sixteen_counters_required_and_exact(self):
        for key in FOCUS.COUNTERS:
            with self.subTest(key=key):
                root = valid_trx()
                del root.find(f"{TAG('ResultSummary')}/{TAG('Counters')}").attrib[key]
                self.reject(root)
                root = valid_trx()
                root.find(f"{TAG('ResultSummary')}/{TAG('Counters')}").set(key, "1")
                self.reject(root)

    def test_counter_and_namespace_drift_rejected(self):
        self.reject(ET.Element("TestRun"))
        for key, value in (("unknown", "0"), ("failed", "-1"), ("passed", "8x")):
            root = valid_trx()
            root.find(f"{TAG('ResultSummary')}/{TAG('Counters')}").set(key, value)
            self.reject(root)

    def test_failed_skipped_and_incomplete_runs_rejected(self):
        for outcome in ("Failed", "NotExecuted", "Skipped"):
            root = valid_trx()
            root.find(TAG("Results"))[0].set("outcome", outcome)
            self.reject(root)
        root = valid_trx()
        root.find(TAG("ResultSummary")).set("outcome", "Failed")
        self.reject(root)
        root = valid_trx()
        root.find(TAG("Results")).remove(root.find(TAG("Results"))[0])
        self.reject(root)

    def test_duplicate_or_unbound_execution_rejected(self):
        root = valid_trx()
        results = root.find(TAG("Results"))
        results[1].set("executionId", results[0].get("executionId"))
        self.reject(root)
        root = valid_trx()
        root.find(TAG("Results"))[0].set("executionId", str(uuid.uuid4()))
        self.reject(root)
        root = valid_trx()
        root.find(TAG("TestDefinitions")).append(root.find(TAG("TestDefinitions"))[0])
        self.reject(root)

    def test_class_method_or_theory_cardinality_drift_rejected(self):
        for key, value in (("className", FOCUS.CLASS + "Other"), ("name", "UnexpectedMethod")):
            root = valid_trx()
            root.find(TAG("TestDefinitions"))[0].find(TAG("TestMethod")).set(key, value)
            self.reject(root)
        root = valid_trx()
        definitions = root.find(TAG("TestDefinitions"))
        definitions[0].find(TAG("TestMethod")).set("name", list(FOCUS.EXPECTED)[1])
        self.reject(root)

    def test_conflicting_or_invalid_test_identity_rejected(self):
        root = valid_trx()
        definitions = root.find(TAG("TestDefinitions"))
        definitions[2].set("id", definitions[0].get("id"))
        self.reject(root)
        root = valid_trx()
        root.find(TAG("Results"))[0].set("testId", "not-a-guid")
        self.reject(root)

    def test_repeated_theory_variant_with_fresh_guids_rejected(self):
        root = valid_trx()
        definitions, results = root.find(TAG("TestDefinitions")), root.find(TAG("Results"))
        # Both rows have genuinely distinct execution GUIDs; repeating the display
        # variant still cannot substitute for the second parameterized case.
        new_test, new_execution = str(uuid.uuid4()), str(uuid.uuid4())
        definitions[1].set("id", new_test)
        definitions[1].find(TAG("Execution")).set("id", new_execution)
        definitions[1].set("name", definitions[0].get("name"))
        results[1].set("testId", new_test)
        results[1].set("executionId", new_execution)
        results[1].set("testName", results[0].get("testName"))
        self.reject(root)

    def test_display_names_must_be_present_nonempty_and_bound(self):
        for value in (None, "", " ", "private-different-variant"):
            root = valid_trx()
            result = root.find(TAG("Results"))[0]
            if value is None:
                del result.attrib["testName"]
            else:
                result.set("testName", value)
            self.reject(root)
        for value in (None, "", " "):
            root = valid_trx()
            definition = root.find(TAG("TestDefinitions"))[0]
            if value is None:
                del definition.attrib["name"]
            else:
                definition.set("name", value)
            self.reject(root)

    def test_guids_must_be_canonical_lowercase_nonzero(self):
        for value in ("00000000-0000-0000-0000-000000000000", "ABCDEFAB-1234-1234-1234-123456789ABC", "{abcdefab-1234-1234-1234-123456789abc}", "abcdefab123412341234123456789abc", "malformed"):
            for attribute in ("testId", "executionId"):
                root = valid_trx()
                root.find(TAG("Results"))[0].set(attribute, value)
                self.reject(root)
            root = valid_trx()
            root.find(TAG("TestDefinitions"))[0].set("id", value)
            self.reject(root)
            root = valid_trx()
            root.find(TAG("TestDefinitions"))[0].find(TAG("Execution")).set("id", value)
            self.reject(root)

    def test_unexpected_direct_definition_or_result_children_rejected(self):
        for container in ("TestDefinitions", "Results"):
            root = valid_trx()
            ET.SubElement(root.find(TAG(container)), TAG("Unexpected"))
            self.reject(root)
            root = valid_trx()
            ET.SubElement(root.find(TAG(container))[0], TAG("Unexpected"))
            self.reject(root)


if __name__ == "__main__":
    unittest.main()
