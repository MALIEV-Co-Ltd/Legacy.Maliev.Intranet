import json
import os
import pathlib
import subprocess
import tempfile
import unittest
from unittest import mock
import retain

class RetainerTests(unittest.TestCase):
    def result(self, outcome="Passed", count=1, name=None, summary="Completed", failed=0, skipped=0):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "result.trx"
            result = f'<UnitTestResult testName="{name or retain.EXPECTED}" outcome="{outcome}" />'
            path.write_text(f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>{result * count}</Results><ResultSummary outcome="{summary}"><Counters total="{count}" executed="{count}" passed="{count}" failed="{failed}" notExecuted="{skipped}" /></ResultSummary></TestRun>')
            return retain.actual_result(path)["complete"]

    def test_one_actual_named_pass(self):
        self.assertTrue(self.result())

    def test_failure_skip_duplicate_wrong_name_or_aborted_rejected(self):
        for arguments in [dict(outcome="Failed"), dict(outcome="NotExecuted"), dict(count=2), dict(count=0), dict(name="another.test"), dict(summary="Aborted"), dict(failed=1), dict(skipped=1)]:
            with self.subTest(arguments=arguments):
                self.assertFalse(self.result(**arguments))

    def test_candidate_binds_only_exact_checkout_or_second_merge_parent(self):
        a, b, c = "a" * 40, "b" * 40, "c" * 40
        self.assertTrue(retain.bound_candidate(a, a, []))
        self.assertTrue(retain.bound_candidate(a, b, [c, b]))
        self.assertFalse(retain.bound_candidate(a, b, []))
        self.assertFalse(retain.bound_candidate(a, b, [b, c]))
        self.assertFalse(retain.bound_candidate(a, "", []))

    def test_missing_file_rejected(self):
        self.assertFalse(retain.actual_result(pathlib.Path("nonexistent-owned-result.trx"))["complete"])

    def test_missing_checkout_writes_incomplete_receipt_despite_other_pass_conditions(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            missing = "Legacy.Maliev.CatalogService"
            head = "a" * 40
            result = root / "supplier-persistence.trx"
            result.write_text(f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testName="{retain.EXPECTED}" outcome="Passed" /></Results><ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" notExecuted="0" /></ResultSummary></TestRun>')
            actual_revision = retain.revision

            def available_revision(path):
                if path == root:
                    return head
                return actual_revision(path) if path.name == missing else retain.PINS[path.name]

            environment = {"PROOF_RESULTS": str(root), "PROOF_EVIDENCE": str(root / "evidence"),
                           "CANDIDATE_HEAD": head, "BUILD_OUTCOME": "success", "EXECUTION_OUTCOME": "success"}
            with mock.patch.dict(os.environ, environment), mock.patch("retain.revision", side_effect=available_revision):
                with self.assertRaises(SystemExit):
                    retain.main(root)
            evidence = json.loads((root / "evidence" / "proof.json").read_text())
            self.assertFalse(evidence["complete"])
            self.assertEqual(evidence["tests"][0]["outcomes"], ["Passed"])
            self.assertEqual(evidence["unavailableProducers"], [missing])
            self.assertIsNone(evidence["producers"][missing])
            self.assertEqual(evidence["executedSource"], head)
            self.assertEqual({name: value for name, value in evidence["producers"].items() if name != missing},
                             {name: value for name, value in retain.PINS.items() if name != missing})

    def test_revision_rejects_missing_checkout_and_parent_repository(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            subprocess.run(["git", "init", "-q", str(root)], check=True, capture_output=True)
            subprocess.run(["git", "-C", str(root), "-c", "user.name=Synthetic fixture",
                            "-c", "user.email=fixture@example.invalid", "commit", "--allow-empty", "-qm", "Fixture"],
                           check=True, capture_output=True)
            child = root / "empty-checkout"
            child.mkdir()
            self.assertIsNotNone(retain.revision(root))
            self.assertIsNone(retain.revision(child))
            self.assertIsNone(retain.revision(root / "missing-checkout"))

if __name__ == "__main__":
    unittest.main()
