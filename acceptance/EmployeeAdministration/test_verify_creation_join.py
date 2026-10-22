import collections
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import uuid
from xml.etree import ElementTree as ET
import verify_creation_join as parser
import verify_join as old


def report(names=None):
    root = ET.Element("{" + old.NS["t"] + "}TestRun")
    def element(parent, tag, **attributes):
        return ET.SubElement(parent, "{" + old.NS["t"] + "}" + tag, attributes)
    definitions, results = element(root, "TestDefinitions"), element(root, "Results")
    for name in sorted(parser.EXPECTED if names is None else names):
        identifier, execution = str(uuid.uuid4()), str(uuid.uuid4())
        base = name.split("(", 1)[0]
        class_name, method = base.rsplit(".", 1)
        definition = element(definitions, "UnitTest", id=identifier)
        element(definition, "TestMethod", className=class_name, name=method)
        element(results, "UnitTestResult", testId=identifier, executionId=execution, testName=name, outcome="Passed")
    summary = element(root, "ResultSummary", outcome="Completed")
    counters = dict.fromkeys(("total", "executed", "passed"), "22")
    counters.update(dict.fromkeys(("failed", "error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
                                  "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending"), "0"))
    element(summary, "Counters", **counters)
    return root


class ProofControls(unittest.TestCase):
    def test_exact_22_synthetic_controls_are_accepted(self):
        self.assertEqual(22, len(parser.EXPECTED))
        proof = parser.validate(ET.tostring(report()))
        self.assertEqual(22, proof["passed"])
        self.assertFalse(proof["genuineIamAcceptance"])
        self.assertFalse(proof["fullApplicationSuiteAcceptance"])

    def test_old_six_cannot_be_creation_proof(self):
        with self.assertRaises(ValueError):
            parser.validate(ET.tostring(report(old.EXPECTED_CASES)))

    def test_skipped_duplicate_and_wrong_definition_are_rejected(self):
        for change in ("skip", "duplicate", "class"):
            root = report()
            rows = root.findall("./t:Results/t:UnitTestResult", old.NS)
            if change == "skip":
                rows[0].set("outcome", "NotExecuted")
            elif change == "duplicate":
                rows[1].set("testName", rows[0].get("testName"))
            else:
                root.find("./t:TestDefinitions/t:UnitTest/t:TestMethod", old.NS).set("className", "FakeProgram")
            with self.subTest(change=change), self.assertRaises(ValueError):
                parser.validate(ET.tostring(root))

    def test_false_counters_and_entity_document_are_rejected(self):
        root = report()
        root.find("./t:ResultSummary/t:Counters", old.NS).set("passed", "23")
        with self.assertRaises(ValueError):
            parser.validate(ET.tostring(root))
        with self.assertRaises(ValueError):
            parser.validate(b'<!DOCTYPE x [<!ENTITY leak SYSTEM "file:///secret">]><x/>')

    def test_unreviewed_or_tampered_source_seal_is_rejected(self):
        with tempfile.TemporaryDirectory(prefix="creation-seal-unit-") as temporary:
            root = Path(temporary)
            seal = root / "seal.json"
            seal.write_text(json.dumps({"status": "unreviewed", "files": []}))
            digest = hashlib.sha256(seal.read_bytes()).hexdigest()
            for expected in ("0" * 64, digest):
                with self.subTest(expected=expected), self.assertRaises(ValueError):
                    parser.validate_source_seal(root, seal, expected)

    def test_parent_escape_in_source_seal_is_rejected(self):
        with tempfile.TemporaryDirectory(prefix="creation-seal-unit-") as temporary:
            root = Path(temporary)
            seal = root / "seal.json"
            seal.write_text(json.dumps({"status": "source-reviewed-native-pending", "files": [{"path": "../foreign", "sha256": "0" * 64}]}))
            with self.assertRaises(ValueError):
                parser.validate_source_seal(root, seal, hashlib.sha256(seal.read_bytes()).hexdigest())


if __name__ == "__main__":
    unittest.main()
