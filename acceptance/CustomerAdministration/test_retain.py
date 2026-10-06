"""Synthetic adversarial controls, never native test results."""
import copy
import pathlib
import tempfile
import unittest
import uuid
import xml.etree.ElementTree as ET
from unittest import mock

import retain

T = "{" + retain.NS["t"] + "}"
RAW = b'<coverage><packages><package><classes><class><lines><line number="1" hits="1" /></lines></class></classes></package></packages></coverage>'


def fixture():
    root = ET.Element(T + "TestRun")
    summary = ET.SubElement(root, T + "ResultSummary", outcome="Completed")
    ET.SubElement(summary, T + "Counters", {k: str(v) for k, v in retain.COUNTERS.items()})
    definitions, results = ET.SubElement(root, T + "TestDefinitions"), ET.SubElement(root, T + "Results")
    for identity, count in retain.METHODS.items():
        cls, method = identity.rsplit(".", 1)
        identifier = str(uuid.uuid4())
        definition = ET.SubElement(definitions, T + "UnitTest", id=identifier)
        ET.SubElement(definition, T + "TestMethod", className=cls, name=method)
        for index in range(count):
            ET.SubElement(results, T + "UnitTestResult", testId=identifier, executionId=str(uuid.uuid4()),
                          outcome="Passed", testName=identity + "(PRIVATE-PARAMETER-MUST-NOT-RETAIN=" + str(index) + ")")
    return root


class RetainerControls(unittest.TestCase):
    def setUp(self):
        self.root = fixture()

    def verify(self, raw=RAW):
        return retain.verify(ET.tostring(self.root), raw)

    def reject(self, raw=RAW):
        with self.assertRaises((ValueError, ET.ParseError)):
            self.verify(raw)

    def test_valid_shared_theory_ids_and_no_parameter_retention(self):
        proof = self.verify()
        self.assertEqual(111, len(proof["executionIds"]))
        self.assertNotIn("PRIVATE-PARAMETER", str(proof))

    def test_identical_duplicate_definition_allowed(self):
        definitions = self.root.find(T + "TestDefinitions")
        definitions.append(copy.deepcopy(definitions[0]))
        self.verify()

    def test_conflicting_definition_rejected(self):
        definitions = self.root.find(T + "TestDefinitions")
        duplicate = copy.deepcopy(definitions[0])
        duplicate.find(T + "TestMethod").set("name", "UNEXPECTED")
        definitions.append(duplicate)
        self.reject()

    def test_duplicate_execution_rejected(self):
        rows = self.root.find(T + "Results")
        rows[1].set("executionId", rows[0].get("executionId"))
        self.reject()

    def test_duplicate_case_with_new_execution_rejected(self):
        rows = self.root.find(T + "Results")
        rows[2].set("testName", rows[1].get("testName"))
        self.reject()

    def test_missing_display_name_rejected(self):
        self.root.find(T + "Results")[0].set("testName", "")
        self.reject()

    def test_alternate_guid_alias_rejected(self):
        rows = self.root.find(T + "Results")
        rows[1].set("executionId", rows[0].get("executionId").replace("-", ""))
        self.reject()

    def test_canonical_guid_case_normalized(self):
        value = str(uuid.uuid4())
        self.assertEqual(value, retain.guid(value.upper()))

    def test_missing_case_rejected(self):
        rows = self.root.find(T + "Results")
        rows.remove(rows[0])
        self.reject()

    def test_wrong_method_cardinality_rejected(self):
        rows = self.root.find(T + "Results")
        rows[0].set("testId", rows[-1].get("testId"))
        self.reject()

    def test_failed_result_rejected(self):
        self.root.find(T + "Results")[0].set("outcome", "Failed")
        self.reject()

    def test_skipped_result_rejected(self):
        self.root.find(T + "Results")[0].set("outcome", "NotExecuted")
        self.reject()

    def test_bad_guid_rejected(self):
        self.root.find(T + "Results")[0].set("executionId", "not-guid")
        self.reject()

    def test_missing_counter_rejected(self):
        del self.root.find(T + "ResultSummary").find(T + "Counters").attrib["warning"]
        self.reject()

    def test_nonzero_counter_rejected(self):
        self.root.find(T + "ResultSummary").find(T + "Counters").set("failed", "1")
        self.reject()

    def test_missing_raw_rejected(self):
        self.reject(b"")

    def test_missing_raw_hits_rejected(self):
        self.reject(b'<coverage><packages /></coverage>')

    def test_dtd_rejected(self):
        self.reject(b'<!DOCTYPE coverage><coverage />')

    def test_source_inventory_matches_current_authored_cases(self):
        root = pathlib.Path(__file__).resolve().parents[2]
        hashes = retain.source_map(root)
        self.assertEqual(len(retain.SOURCES), len(hashes))

    def test_source_revision_mismatch_rejected(self):
        with self.assertRaises(ValueError):
            retain.source_identity("a" * 40, "b" * 40, retain.SOURCES, "")

    def test_untracked_source_rejected(self):
        with self.assertRaises(ValueError):
            retain.source_identity("a" * 40, "a" * 40, retain.SOURCES[:-1], "")

    def test_dirty_source_rejected(self):
        with self.assertRaises(ValueError):
            retain.source_identity("a" * 40, "a" * 40, retain.SOURCES, " M owned-source")


class CoverageCopyControls(unittest.TestCase):
    def setUp(self):
        self.fixture = tempfile.TemporaryDirectory(prefix="owned-controls-", dir=pathlib.Path(__file__).parent)
        self.addCleanup(self.fixture.cleanup)
        self.results = pathlib.Path(self.fixture.name)
        self.collector = self.results / str(uuid.uuid4()) / "coverage.cobertura.xml"
        self.attachment = self.results / "_machine_2026-10-06_12_00_00" / "In" / "machine" / "coverage.cobertura.xml"

    def write(self, path, data=RAW):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    def test_single_collector(self):
        self.assertEqual(RAW, retain.select_coverage(self.results, [self.write(self.collector)]))

    def test_identical_known_copies(self):
        self.assertEqual(RAW, retain.select_coverage(self.results, [self.write(self.collector), self.write(self.attachment)]))

    def test_conflicting_copies(self):
        with self.assertRaises(ValueError):
            retain.select_coverage(self.results, [self.write(self.collector), self.write(self.attachment, RAW.replace(b'hits="1"', b'hits="2"'))])

    def test_extra_copy(self):
        with self.assertRaises(ValueError):
            retain.select_coverage(self.results, [self.write(self.collector), self.write(self.attachment), self.write(self.results / "extra" / "coverage.cobertura.xml")])

    def test_unknown_single_layout(self):
        with self.assertRaises(ValueError):
            retain.select_coverage(self.results, [self.write(self.results / "unknown" / "coverage.cobertura.xml")])

    def test_mismatched_machine(self):
        bad = self.attachment.parent.parent / "other" / "coverage.cobertura.xml"
        with self.assertRaises(ValueError):
            retain.select_coverage(self.results, [self.write(self.collector), self.write(bad)])

    def test_unsafe_dtd_copy(self):
        with self.assertRaises(ValueError):
            retain.select_coverage(self.results, [self.write(self.collector, b'<!DOCTYPE coverage><coverage />')])

    def test_symlink_rejection_without_creating_symlink(self):
        self.write(self.collector)
        with mock.patch.object(pathlib.Path, "is_symlink", return_value=True):
            with self.assertRaises(ValueError):
                retain.select_coverage(self.results, [self.collector])

    def test_oversized_copy(self):
        self.write(self.collector)
        with mock.patch.object(pathlib.Path, "read_bytes", return_value=b"x" * (32 * 1024 * 1024 + 1)):
            with self.assertRaises(ValueError):
                retain.select_coverage(self.results, [self.collector])

    def test_path_outside_results(self):
        self.write(self.collector)
        restricted = self.results / "restricted"
        restricted.mkdir()
        with self.assertRaises(ValueError):
            retain.read_evidence(restricted, self.collector)


if __name__ == "__main__":
    unittest.main()
