"""Synthetic TRX parser controls only; never producer-join execution evidence."""
import copy
import contextlib
import io
import json
import os
import pathlib
import tempfile
import unittest
import uuid
from types import SimpleNamespace
import xml.etree.ElementTree as ET
from unittest import mock
import verify_join as verifier

def report(shared_theory_id=False):
    q = lambda name: "{" + verifier.NS["t"] + "}" + name
    root = ET.Element(q("TestRun"))
    definitions, results = ET.SubElement(root, q("TestDefinitions")), ET.SubElement(root, q("Results"))
    index = 0
    for method, count in verifier.METHODS.items():
        for case in range(count):
            index += 1
            identifier = str(uuid.UUID(int=1000 if shared_theory_id and count == 2 else index))
            execution = str(uuid.UUID(int=100 + index))
            definition = ET.SubElement(definitions, q("UnitTest"), id=identifier)
            ET.SubElement(definition, q("TestMethod"), className=verifier.CLASS, name=method)
            ET.SubElement(definition, q("Execution"), id=execution)
            name = verifier.CLASS + "." + method
            if count == 2:
                name += '(changed: "' + ("profile" if case == 0 else "identity") + '")'
            if count in (3, 6):
                name += f"(case: {case})"
            ET.SubElement(results, q("UnitTestResult"), testId=identifier, executionId=execution, testName=name, outcome="Passed")
    summary = ET.SubElement(root, q("ResultSummary"), outcome="Completed")
    counters = dict.fromkeys(("total", "executed", "passed"), "14")
    counters.update(dict.fromkeys(("failed", "error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
        "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending"), "0"))
    ET.SubElement(summary, q("Counters"), counters)
    return ET.ElementTree(root)

class ParserControls(unittest.TestCase):
    def test_valid(self):
        self.assertEqual(14, sum(verifier.validate(report()).values()))

    def test_shared_theory_test_id_distinct_execution_ids(self):
        self.assertEqual(14, sum(verifier.validate(report(True)).values()))

    def test_single_shared_theory_definition(self):
        value = report(True)
        definitions = value.find("./t:TestDefinitions", verifier.NS)
        definitions.remove(list(definitions)[2])
        self.assertEqual(14, sum(verifier.validate(value).values()))

    def test_shared_definition_without_execution_anchor(self):
        value = report(True)
        definitions = value.find("./t:TestDefinitions", verifier.NS)
        for definition in definitions:
            definition.remove(definition.find("./t:Execution", verifier.NS))
        definitions.remove(list(definitions)[2])
        self.assertEqual(14, sum(verifier.validate(value).values()))

    def test_identical_repeated_definition(self):
        value = report(True)
        definitions = value.find("./t:TestDefinitions", verifier.NS)
        definitions.append(copy.deepcopy(list(definitions)[0]))
        self.assertEqual(14, sum(verifier.validate(value).values()))

    def test_same_test_id_conflicting_method(self):
        value = report(True)
        definitions = value.find("./t:TestDefinitions", verifier.NS)
        definitions[2].find("./t:TestMethod", verifier.NS).set("name", next(iter(verifier.METHODS)))
        with self.assertRaises(ValueError):
            verifier.validate(value)

    def test_parameter_display_not_retained(self):
        value = report()
        value.findall("./t:Results/t:UnitTestResult", verifier.NS)[1].set("testName",
            verifier.CLASS + '.StaleOriginalVersion_RejectsBeforeEitherWrite(PRIVATE-CONTROL-PARAMETER)')
        self.assertNotIn("PRIVATE-CONTROL", str(verifier.validate(value)))

    def test_canonical_guid_case_normalizes(self):
        value = str(uuid.uuid4())
        self.assertEqual(value, verifier.guid(value.upper()))

    def test_execution_guid_aliases_reject(self):
        value = str(uuid.uuid4())
        for alias in (value.replace("-", ""), "{" + value + "}", "urn:uuid:" + value, " " + value):
            with self.subTest(alias=alias), self.assertRaises(ValueError):
                verifier.guid(alias)

    def test_test_id_alias_rejects(self):
        value = report()
        row = value.findall("./t:Results/t:UnitTestResult", verifier.NS)[0]
        row.set("testId", row.get("testId").replace("-", ""))
        with self.assertRaises(ValueError):
            verifier.validate(value)

    def test_duplicate_logical_execution_case_alias(self):
        value = report()
        rows = value.findall("./t:Results/t:UnitTestResult", verifier.NS)
        rows[1].set("executionId", rows[0].get("executionId").upper())
        with self.assertRaises(ValueError):
            verifier.validate(value)

    def test_nil_identity_rejects(self):
        with self.assertRaises(ValueError):
            verifier.guid(str(uuid.UUID(int=0)))

    def test_root_rejects(self):
        value = report()
        value.getroot().tag = "Other"
        with self.assertRaises(ValueError):
            verifier.validate(value)

    def test_noncompleted_summary_rejects(self):
        value = report()
        value.find("./t:ResultSummary", verifier.NS).set("outcome", "Failed")
        with self.assertRaises(ValueError):
            verifier.validate(value)

    def test_duplicate_summary_rejects(self):
        value = report()
        value.getroot().append(copy.deepcopy(value.find("./t:ResultSummary", verifier.NS)))
        with self.assertRaises(ValueError):
            verifier.validate(value)

    def test_missing_counter_rejects(self):
        value = report()
        del value.find("./t:ResultSummary/t:Counters", verifier.NS).attrib["warning"]
        with self.assertRaises(ValueError):
            verifier.validate(value)

    def test_definition_execution_mismatch(self):
        value = report()
        value.findall("./t:TestDefinitions/t:UnitTest/t:Execution", verifier.NS)[0].set("id", "wrong")
        with self.assertRaises(ValueError):
            verifier.validate(value)

    def test_duplicate_execution(self):
        value = report()
        rows = value.findall("./t:Results/t:UnitTestResult", verifier.NS)
        rows[1].set("executionId", rows[0].get("executionId"))
        with self.assertRaises(ValueError):
            verifier.validate(value)

    def test_missing_case(self):
        value = report()
        results = value.find("./t:Results", verifier.NS)
        results.remove(list(results)[0])
        with self.assertRaises(ValueError):
            verifier.validate(value)

    def test_nonpassing(self):
        value = report()
        value.findall("./t:Results/t:UnitTestResult", verifier.NS)[0].set("outcome", "Failed")
        with self.assertRaises(ValueError):
            verifier.validate(value)

    def test_skipped_counter(self):
        value = report()
        value.find("./t:ResultSummary/t:Counters", verifier.NS).set("notExecuted", "1")
        with self.assertRaises(ValueError):
            verifier.validate(value)

    def test_unknown_class(self):
        value = report()
        value.findall("./t:TestDefinitions/t:UnitTest/t:TestMethod", verifier.NS)[0].set("className", "Other.Class")
        with self.assertRaises(ValueError):
            verifier.validate(value)

    def test_unused_definition(self):
        value = report()
        definitions = value.find("./t:TestDefinitions", verifier.NS)
        extra = copy.deepcopy(list(definitions)[0])
        extra.set("id", "unused")
        definitions.append(extra)
        with self.assertRaises(ValueError):
            verifier.validate(value)

    def test_duplicate_display_name(self):
        value = report()
        rows = value.findall("./t:Results/t:UnitTestResult", verifier.NS)
        rows[2].set("testName", rows[1].get("testName"))
        with self.assertRaises(ValueError):
            verifier.validate(value)

class EvidenceBoundaryControls(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="customer-join-parser-")
        self.addCleanup(self.temporary.cleanup)
        self.root = pathlib.Path(self.temporary.name)
        self.path = self.root / "actual.trx"
        self.data = ET.tostring(report().getroot())
        self.path.write_bytes(self.data)

    def test_bounded_owned_read(self):
        data, parsed = verifier.read_evidence(self.root, self.path)
        self.assertEqual(self.data, data)
        self.assertEqual(14, sum(verifier.validate(parsed).values()))

    def test_outside_results_rejects(self):
        child = self.root / "owned"
        child.mkdir()
        with self.assertRaises(ValueError):
            verifier.read_evidence(child, self.path)

    def test_root_symlink_rejects(self):
        with mock.patch.object(pathlib.Path, "is_symlink", side_effect=lambda: True):
            with self.assertRaises(ValueError):
                verifier.read_evidence(self.root, self.path)

    def test_ancestor_symlink_rejects(self):
        ancestor = self.root.parent
        with mock.patch.object(pathlib.Path, "is_symlink", autospec=True, side_effect=lambda path: path == ancestor):
            with self.assertRaises(ValueError):
                verifier.read_evidence(self.root, self.path)

    def test_file_symlink_rejects(self):
        with mock.patch.object(pathlib.Path, "is_symlink", autospec=True, side_effect=lambda path: path == self.path):
            with self.assertRaises(ValueError):
                verifier.read_evidence(self.root, self.path)

    def test_empty_bytes_reject(self):
        with self.assertRaises(ValueError):
            verifier.parse_bytes(b"")

    def test_oversize_bytes_reject(self):
        with self.assertRaises(ValueError):
            verifier.parse_bytes(b"x" * (verifier.MAX_BYTES + 1))

    def test_dtd_rejects_before_parser(self):
        with mock.patch.object(verifier.ET, "fromstring", side_effect=AssertionError("Unsafe input reached parser")):
            with self.assertRaises(ValueError):
                verifier.parse_bytes(b"<!DOCTYPE TestRun><TestRun />")

    def test_entity_rejects_before_parser(self):
        with mock.patch.object(verifier.ET, "fromstring", side_effect=AssertionError("Unsafe input reached parser")):
            with self.assertRaises(ValueError):
                verifier.parse_bytes(b'<!ENTITY unsafe "PRIVATE"><TestRun />')

    def test_utf16_dtd_rejects_before_parser(self):
        with mock.patch.object(verifier.ET, "fromstring", side_effect=AssertionError("Unsafe input reached parser")):
            with self.assertRaises(ValueError):
                verifier.parse_bytes("<!DOCTYPE TestRun><TestRun />".encode("utf-16"))

    def test_empty_file_rejects(self):
        self.path.write_bytes(b"")
        with self.assertRaises(ValueError):
            verifier.read_evidence(self.root, self.path)

    def test_oversized_file_rejects_before_open(self):
        original = pathlib.Path.stat
        def stat(path, *args, **kwargs):
            if path == self.path:
                return SimpleNamespace(st_mode=0o100600, st_size=verifier.MAX_BYTES + 1)
            return original(path, *args, **kwargs)
        with mock.patch.object(pathlib.Path, "stat", autospec=True, side_effect=stat), mock.patch.object(pathlib.Path, "open", side_effect=AssertionError("Oversize file opened")):
            with self.assertRaises(ValueError):
                verifier.read_evidence(self.root, self.path)

    def test_same_file_size_race_remains_bounded(self):
        original = pathlib.Path.stat
        def stat(path, *args, **kwargs):
            if path == self.path:
                return SimpleNamespace(st_mode=0o100600, st_size=10)
            return original(path, *args, **kwargs)
        with mock.patch.object(verifier, "MAX_BYTES", 10), mock.patch.object(pathlib.Path, "stat", autospec=True, side_effect=stat):
            with self.assertRaises(ValueError):
                verifier.read_evidence(self.root, self.path)

    def test_main_only_retains_sanitized_proof(self):
        value = report()
        output = ET.SubElement(value.getroot(), "Output")
        output.text = "PRIVATE-SNAPSHOT-HASH-STAMP"
        self.path.write_bytes(ET.tostring(value.getroot()))
        evidence = self.root / "sanitized"
        heads = ["a" * 40] + [pin for _, pin in verifier.PINS.values()]
        with mock.patch.dict(os.environ, {"JOIN_RESULTS": str(self.root), "JOIN_EVIDENCE": str(evidence)}), mock.patch.object(verifier.subprocess, "check_output", side_effect=heads):
            with contextlib.redirect_stdout(io.StringIO()):
                verifier.main()
        files = list(evidence.iterdir())
        self.assertEqual(["customer-administration-producer-join-proof.json"], [path.name for path in files])
        proof = json.loads(files[0].read_text())
        self.assertEqual(verifier.METHODS, proof["methods"])
        self.assertEqual(14, proof["passed"])
        self.assertEqual(verifier.hashlib.sha256(self.path.read_bytes()).hexdigest(), proof["trxSha256"])
        self.assertNotIn("PRIVATE", files[0].read_text())
        self.assertNotIn("testName", files[0].read_text())
        self.assertNotIn("executionId", files[0].read_text())

    def test_failed_input_creates_no_proof_directory(self):
        self.path.write_bytes(b"<!DOCTYPE TestRun><TestRun />")
        evidence = self.root / "sanitized"
        with mock.patch.dict(os.environ, {"JOIN_RESULTS": str(self.root), "JOIN_EVIDENCE": str(evidence)}):
            with self.assertRaises(ValueError):
                verifier.main()
        self.assertFalse(evidence.exists())

    def test_workflow_uploads_only_sanitized_directory(self):
        workflow = pathlib.Path(__file__).resolve().parents[2] / ".github/workflows/customer-administration-producer-join.yml"
        text = workflow.read_text()
        upload = text[text.index("      - name: Preserve sanitized join proof only"):]
        self.assertIn("path: ${{ runner.temp }}/customer-administration-producer-join-evidence/", upload)
        self.assertNotIn("-results/", upload)
        self.assertIn("always() && steps.customer_join_proof.outcome == 'success'", upload)

if __name__ == "__main__":
    unittest.main()
