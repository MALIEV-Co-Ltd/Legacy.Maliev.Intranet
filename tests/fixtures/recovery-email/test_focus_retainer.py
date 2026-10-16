"""Offline controls for exact mailbox-case identity admission; no HTTP or SDK."""

import copy
import importlib.util
from pathlib import Path
import tempfile
import unittest
import uuid
import xml.etree.ElementTree as ET

REPOSITORY = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location(
    "recovery_email_focus", REPOSITORY / "scripts/verify-recovery-email-focus.py"
)
GUARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GUARD)


def fixture():
    tag = lambda value: f"{{{GUARD.NAMESPACE}}}{value}"
    root = ET.Element(tag("TestRun"))
    definitions = ET.SubElement(root, tag("TestDefinitions"))
    results = ET.SubElement(root, tag("Results"))
    entries = ET.SubElement(root, tag("TestEntries"))
    summary = ET.SubElement(root, tag("ResultSummary"), outcome="Completed")
    ET.SubElement(summary, tag("Counters"), **{
        key: str(11 if key in {"total", "executed", "passed"} else 0)
        for key in GUARD.COUNTERS
    })
    for method, count in GUARD.EXPECTED.items():
        for index in range(count):
            test_id, execution_id = str(uuid.uuid4()), str(uuid.uuid4())
            display = f"{GUARD.CLASS}.{method}(case: {index})"
            definition = ET.SubElement(definitions, tag("UnitTest"), id=test_id, name=display)
            ET.SubElement(definition, tag("Execution"), id=execution_id)
            ET.SubElement(definition, tag("TestMethod"), className=GUARD.CLASS, name=method)
            ET.SubElement(results, tag("UnitTestResult"), testId=test_id,
                          executionId=execution_id, testName=display, outcome="Passed")
            ET.SubElement(entries, tag("TestEntry"), testId=test_id, executionId=execution_id)
    return root, tag


class RecoveryEmailFocusTests(unittest.TestCase):
    def test_accepts_exact_ten_plus_one_actual_case_bindings(self):
        root, tag = fixture()
        run_infos = ET.SubElement(root, tag("RunInfos"))
        ET.SubElement(run_infos, tag("RunInfo"), outcome="Information")
        self.assertEqual(11, len(GUARD.verify_trx(root)))

    def test_rejects_missing_duplicate_mismatched_and_unexpected_evidence(self):
        baseline, tag = fixture()
        for mutation in (
            "missing-entry", "duplicate-entry", "wrong-entry-id", "missing-definition",
            "duplicate-definition-id",
            "duplicate-result", "wrong-display", "wrong-method", "failed-result",
            "skipped-counter", "missing-summary", "missing-result",
            "nested-summary", "nested-counters", "passed-output-error-info", "wrapped-error-run-info",
        ):
            with self.subTest(mutation=mutation):
                root = copy.deepcopy(baseline)
                entries = root.find(tag("TestEntries"))
                definitions = root.find(tag("TestDefinitions"))
                results = root.find(tag("Results"))
                if mutation == "missing-entry":
                    entries.remove(entries[0])
                elif mutation == "duplicate-entry":
                    entries.append(copy.deepcopy(entries[0]))
                elif mutation == "wrong-entry-id":
                    entries[0].set("executionId", str(uuid.uuid4()))
                elif mutation == "missing-definition":
                    definitions.remove(definitions[0])
                elif mutation == "duplicate-definition-id":
                    original_id = definitions[1].get("id")
                    duplicate_id = definitions[0].get("id")
                    definitions[1].set("id", duplicate_id)
                    for container, key in ((results, "testId"), (entries, "testId")):
                        for node in container:
                            if node.get(key) == original_id:
                                node.set(key, duplicate_id)
                elif mutation == "duplicate-result":
                    results.append(copy.deepcopy(results[0]))
                elif mutation == "wrong-display":
                    results[0].set("testName", "unrelated")
                elif mutation == "wrong-method":
                    definitions[0].find(tag("TestMethod")).set("name", "unrelated")
                elif mutation == "failed-result":
                    results[0].set("outcome", "Failed")
                elif mutation == "skipped-counter":
                    root.find(f"{tag('ResultSummary')}/{tag('Counters')}").set("notExecuted", "1")
                elif mutation == "missing-summary":
                    root.remove(root.find(tag("ResultSummary")))
                elif mutation == "missing-result":
                    results.remove(results[0])
                elif mutation == "nested-summary":
                    wrapper = ET.SubElement(root, tag("Wrapper"))
                    wrapper.append(copy.deepcopy(root.find(tag("ResultSummary"))))
                elif mutation == "nested-counters":
                    wrapper = ET.SubElement(root, tag("Wrapper"))
                    wrapper.append(copy.deepcopy(root.find(f"{tag('ResultSummary')}/{tag('Counters')}")))
                elif mutation == "passed-output-error-info":
                    output = ET.SubElement(results[0], tag("Output"))
                    wrapper = ET.SubElement(output, tag("Wrapper"))
                    ET.SubElement(wrapper, tag("ErrorInfo"))
                elif mutation == "wrapped-error-run-info":
                    wrapper = ET.SubElement(root, tag("Wrapper"))
                    run_info = ET.SubElement(wrapper, tag("RunInfo"), outcome="Error")
                    ET.SubElement(run_info, tag("Text")).text = "Controlled run error."
                with self.assertRaises(ValueError):
                    GUARD.verify_trx(root)


    def test_utf8_and_utf8_bom_input_is_admitted(self):
        root, _ = fixture()
        payload = ET.tostring(root, encoding="utf-8", xml_declaration=True)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "focus.trx"
            for prefix in (b"", b"\xef\xbb\xbf"):
                path.write_bytes(prefix + payload)
                parsed, raw = GUARD.read_trx(path)
                self.assertEqual(prefix + payload, raw)
                self.assertEqual(11, len(GUARD.verify_trx(parsed)))

    def test_utf16_dtd_non_utf8_declaration_nul_and_oversize_are_rejected(self):
        unsafe = '<?xml version="1.0" encoding="utf-16"?><!DOCTYPE x [<!ENTITY e "x">]><x>&e;</x>'
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "focus.trx"
            for payload in (
                unsafe.encode("utf-16"),
                unsafe.encode("utf-16-le"),
                unsafe.encode("utf-8"),
                b'<?xml version="1.0" encoding="iso-8859-1"?><x/>',
                b"<x>\x00</x>",
                b'<?xml version="1.0"?><!DOCTYPE x [<!ENTITY e "x">]><x>&e;</x>',
            ):
                with self.subTest(payload_kind=payload[:8]):
                    path.write_bytes(payload)
                    with self.assertRaises((ValueError, UnicodeDecodeError)):
                        GUARD.read_trx(path)
            with path.open("wb") as stream:
                stream.truncate(GUARD.MAX_TRX_BYTES + 1)
            with self.assertRaises(ValueError):
                GUARD.read_trx(path)


    def test_failed_warning_unknown_or_missing_run_info_is_rejected(self):
        for outcome in ("Failed", "Warning", "Unexpected", None):
            with self.subTest(outcome=outcome):
                root, tag = fixture()
                run_infos = ET.SubElement(root, tag("RunInfos"))
                info = ET.SubElement(run_infos, tag("RunInfo"))
                if outcome is not None:
                    info.set("outcome", outcome)
                ET.SubElement(info, tag("Text")).text = "Controlled contradictory diagnostic."
                with self.assertRaises(ValueError):
                    GUARD.verify_trx(root)

    def test_passed_run_info_is_noncontradictory(self):
        root, tag = fixture()
        run_infos = ET.SubElement(root, tag("RunInfos"))
        ET.SubElement(run_infos, tag("RunInfo"), outcome="Passed")
        self.assertEqual(11, len(GUARD.verify_trx(root)))


if __name__ == "__main__":
    unittest.main()
