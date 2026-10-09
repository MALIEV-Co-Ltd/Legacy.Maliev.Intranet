import importlib.util
import json
import os
import pathlib
import tempfile
import types
import unittest
from unittest import mock

spec = importlib.util.spec_from_file_location("retainer", pathlib.Path(__file__).resolve().parents[3] / "scripts/verify-quotation-company-focus.py")
retainer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(retainer)


class Controls(unittest.TestCase):
    def xml(self, names=None, outcome="Passed", counter=None, summary="Completed"):
        names = retainer.EXPECTED if names is None else names
        counters = dict(total="7", executed="7", passed="7", **dict.fromkeys(retainer.ZERO, "0"))
        if counter: counters.update(counter)
        attrs = " ".join(f'{key}="{value}"' for key, value in counters.items())
        rows = "".join(f'<UnitTestResult testName="{name}" outcome="{outcome}"/>' for name in names)
        return f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>{rows}</Results><ResultSummary outcome="{summary}"><Counters {attrs}/></ResultSummary></TestRun>'

    def check(self, value):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "result.trx"
            path.write_text(value)
            return retainer.read_result(path)

    def test_exact_seven_passed_native_results(self):
        self.assertTrue(self.check(self.xml())["complete"])

    def test_omitted_or_duplicate_case_is_rejected(self):
        for name in retainer.EXPECTED:
            self.assertFalse(self.check(self.xml([other for other in retainer.EXPECTED if other != name]))["complete"])
            self.assertFalse(self.check(self.xml(retainer.EXPECTED + (name,)))["complete"])

    def test_nonzero_terminal_pending_or_incorrect_counts_rejected(self):
        for key in retainer.ZERO:
            self.assertFalse(self.check(self.xml(counter={key:"1"}))["complete"], key)
        for key in ("total", "executed", "passed"):
            self.assertFalse(self.check(self.xml(counter={key:"8"}))["complete"], key)

    def test_nonpassed_or_aborted_summary_is_rejected(self):
        for outcome in retainer.OUTCOMES - {"Passed"}:
            self.assertFalse(self.check(self.xml(outcome=outcome))["complete"])
        self.assertFalse(self.check(self.xml(summary="Aborted"))["complete"])

    def test_missing_or_malformed_file_is_incomplete(self):
        self.assertFalse(retainer.read_result(pathlib.Path("nonexistent-owned-result.trx"))["complete"])
        self.assertFalse(self.check("{")["complete"])

    def test_foreign_result_and_unknown_outcome_never_publish_arbitrary_text(self):
        value = self.check(self.xml(retainer.EXPECTED + ("foreign-secret-principal",)))
        self.assertFalse(value["complete"])
        self.assertNotIn("foreign-secret-principal", json.dumps(value))
        value = self.check(self.xml(outcome="private-token"))
        self.assertFalse(value["complete"])
        self.assertNotIn("private-token", json.dumps(value))

    def test_missing_counters_and_extra_counter_do_not_pass(self):
        self.assertFalse(self.check(self.xml().replace(' failed="0"', ''))["complete"])
        self.assertFalse(self.check(self.xml(counter={"unknown":"0"}))["complete"])

    def test_success_flags_cannot_replace_native_results_or_exact_source(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            head = "a"*40
            env = dict(FOCUS_RESULTS=str(root), FOCUS_EVIDENCE=str(root/"evidence"), EXPECTED_SOURCE_REVISION=head,
                       BUILD_OUTCOME="success", EXECUTION_OUTCOME="success", FORMAT_OUTCOME="success")
            with mock.patch.dict(os.environ, env), mock.patch.object(retainer.subprocess,"run",return_value=types.SimpleNamespace(stdout=head)):
                with self.assertRaises(SystemExit): retainer.main()
                self.assertFalse(json.loads((root/"evidence/proof.json").read_text())["complete"])
            (root/"quotation-company.trx").write_text(self.xml())
            with mock.patch.dict(os.environ, env), mock.patch.object(retainer.subprocess,"run",return_value=types.SimpleNamespace(stdout="b"*40)):
                with self.assertRaises(SystemExit): retainer.main()
            with mock.patch.dict(os.environ, dict(env,EXECUTION_OUTCOME="skipped")), mock.patch.object(retainer.subprocess,"run",return_value=types.SimpleNamespace(stdout=head)):
                with self.assertRaises(SystemExit): retainer.main()


if __name__ == "__main__":
    unittest.main()
