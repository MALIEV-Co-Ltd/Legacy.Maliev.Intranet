"""Offline controls for the actual-context receipt verifier; no Docker substitute."""
import importlib.util
import io
from pathlib import Path
import tarfile
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location(
    "admission", ROOT / "scripts/verify-intranet-build-context.py")
admission = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(admission)


def archive(entries):
    stream = io.BytesIO()
    with tarfile.open(fileobj=stream, mode="w") as output:
        for name, body in entries:
            info = tarfile.TarInfo(name)
            info.size = len(body)
            output.addfile(info, io.BytesIO(body))
    stream.seek(0)
    return stream


class ContextAdmissionTests(unittest.TestCase):
    def check(self, entries, required=None, forbidden=("nested/.git/sentinel",)):
        return admission.verify_archive(archive(entries),
            required or {"build/nuget-locks/library/packages.lock.json": admission.digest(b"lock")},
            forbidden)

    def test_exact_lock_bytes_pass(self):
        self.check([("context/build/nuget-locks/library/packages.lock.json", b"lock")])

    def test_missing_required_lock_rejects(self):
        with self.assertRaises(ValueError):
            self.check([])

    def test_modified_required_lock_rejects(self):
        with self.assertRaises(ValueError):
            self.check([("context/build/nuget-locks/library/packages.lock.json", b"changed")])

    def test_missing_asset_rejects(self):
        with self.assertRaises(ValueError):
            self.check([], {"Client/wwwroot/app.css": admission.digest(b"css")})

    def test_admitted_forbidden_path_rejects(self):
        with self.assertRaises(ValueError):
            self.check([("context/build/nuget-locks/library/packages.lock.json", b"lock"),
                        ("context/nested/.git/sentinel", b"synthetic")])

    def test_duplicate_required_path_rejects(self):
        with self.assertRaises(ValueError):
            self.check([("context/build/nuget-locks/library/packages.lock.json", b"lock")] * 2)

    def test_unsafe_archive_path_rejects(self):
        with self.assertRaises(ValueError):
            self.check([("context/../escape", b"synthetic")])

    def test_build_warning_rejects(self):
        with self.assertRaises(ValueError):
            admission.verify_build_output("Build succeeded.\n1 Warning(s)\n0 Error(s)")

    def test_compiler_warning_rejects(self):
        with self.assertRaises(ValueError):
            admission.verify_build_output("source.cs: warning CS1234: synthetic")

    def test_clean_build_output_passes(self):
        admission.verify_build_output("Build succeeded.\n0 Warning(s)\n0 Error(s)")

    def test_native_failure_cannot_satisfy_negative_control(self):
        def unavailable():
            raise ValueError("native_command_failed")
        with self.assertRaises(ValueError):
            admission.assert_reject(unavailable, "required_context_bytes_mismatch")

    def test_required_symlink_rejects(self):
        stream = io.BytesIO()
        with tarfile.open(fileobj=stream, mode="w") as output:
            info = tarfile.TarInfo("context/build/nuget-locks/library/packages.lock.json")
            info.type = tarfile.SYMTYPE
            info.linkname = "elsewhere"
            output.addfile(info)
        stream.seek(0)
        with self.assertRaises(ValueError):
            admission.verify_archive(stream,
                {"build/nuget-locks/library/packages.lock.json": admission.digest(b"lock")}, ())

    def test_negative_receipt_retains_exact_sanitized_diagnostics_and_exit(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            output = ("private-unrelated-output\n" + str(directory) +
                      "/source: error NU1004: lock mismatch https://synthetic.invalid/path\n").encode()
            receipt = admission.retain_lock_negative(output, 1, "Bff/Dockerfile", 1,
                                                      directory, directory)
            log = (directory / receipt["sanitizedDiagnosticLog"]).read_bytes()
            self.assertEqual(admission.digest(log), receipt["sanitizedDiagnosticLogSha256"])
            self.assertEqual(admission.digest(output), receipt["nativeOutputSha256"])
            self.assertEqual(1, receipt["nativeExitCode"])
            self.assertEqual("build", receipt["dockerTarget"])
            self.assertEqual("Bff/Dockerfile", receipt["dockerfile"])
            self.assertIn(b"NU1004", log)
            self.assertNotIn(b"private-unrelated-output", log)
            self.assertNotIn(b"https://", log)
            self.assertNotIn(str(directory).encode(), log)

    def test_failed_native_command_without_nu1004_is_not_lock_evidence(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            with self.assertRaises(ValueError):
                admission.retain_lock_negative(b"network unavailable", 1, "Bff/Dockerfile", 1,
                                                directory, directory)

    def test_success_with_nu1004_text_is_not_lock_evidence(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            with self.assertRaises(ValueError):
                admission.retain_lock_negative(b"NU1004", 0, "Bff/Dockerfile", 1,
                                                directory, directory)


if __name__ == "__main__":
    unittest.main()
