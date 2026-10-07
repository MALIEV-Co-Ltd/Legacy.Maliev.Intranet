import pathlib
import tempfile
import unittest
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

if __name__ == "__main__":
    unittest.main()
