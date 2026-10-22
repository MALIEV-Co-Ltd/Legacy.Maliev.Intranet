import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import smoke_creation_control as smoke


class SmokeControls(unittest.TestCase):
    def test_failure_diagnostic_copy_omits_private_metadata_and_never_qualifies(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = smoke.Smoke(Path.cwd(), Path(temporary), "normal")
            owner.records["private"] = "password SQL customer@example.invalid"
            owner.records["controlProcesses"] = [{"pid": 123, "stage": "creation-identity",
                "settled": True, "private": owner.records["private"]}]
            target = owner.retain_diagnostics()
            value = json.loads(target.read_text())
            self.assertNotIn(owner.records["private"], target.read_text())
            self.assertTrue(value["diagnosticOnly"])
            self.assertFalse(value["nativeCreationAcceptance"])
            self.assertFalse(value["releaseVerified"])
            self.assertEqual(target.parent, owner.evidence.parent)

    def test_command_failure_retains_diagnostic_and_original_error(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = smoke.Smoke(Path.cwd(), Path(temporary), "normal")
            original = RuntimeError("Original command failure")
            with patch.object(owner, "command", side_effect=original), patch.object(owner, "finish"), \
                    patch.object(smoke.os, "getuid", return_value=0, create=True), \
                    patch.object(smoke.os, "getgid", return_value=0, create=True):
                with self.assertRaises(RuntimeError) as error:
                    owner.run_smoke()
            self.assertIs(original, error.exception)
            self.assertTrue((owner.evidence.parent / ("control-smoke-" + owner.run + ".json")).exists())

    def test_retention_failure_never_masks_original_failure(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = smoke.Smoke(Path.cwd(), Path(temporary), "normal")
            original = RuntimeError("Original command failure")
            with patch.object(owner, "command", side_effect=original), patch.object(owner, "finish"), \
                    patch.object(owner, "retain_diagnostics", side_effect=OSError("Private error")), \
                    patch.object(smoke.os, "getuid", return_value=0, create=True), \
                    patch.object(smoke.os, "getgid", return_value=0, create=True), \
                    patch("builtins.print"):
                with self.assertRaises(RuntimeError) as error:
                    owner.run_smoke()
            self.assertIs(original, error.exception)

    def test_missing_exit_marker_never_qualifies_actual_release(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = smoke.Smoke(Path.cwd(), Path(temporary), "cancel")
            with self.assertRaisesRegex(RuntimeError, "markers missing"):
                owner.inspect_native_exit()
            self.assertFalse(owner.records["releaseVerified"])

    def test_same_birth_still_live_never_qualifies_release(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = smoke.Smoke(Path.cwd(), Path(temporary), "cancel")
            identity = {"pid": 123, "startTicks": "same", "executable": str(Path(smoke.sys.executable).resolve()), "cgroup": "0::" + owner.group}
            owner.marker.write_text(json.dumps(identity))
            owner.marker.with_suffix(".exit.json").write_text(json.dumps({"pid": 123, "signal": "TERM"}))
            with patch.object(smoke.lane, "process_identity", return_value={key: identity[key] for key in ("pid", "startTicks", "executable")}), self.assertRaisesRegex(RuntimeError, "still alive"):
                owner.inspect_native_exit()

    def test_foreign_cgroup_marker_never_qualifies_release(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = smoke.Smoke(Path.cwd(), Path(temporary), "cancel")
            owner.marker.write_text(json.dumps({"pid": 123, "startTicks": "same", "executable": str(Path(smoke.sys.executable).resolve()), "cgroup": "0::/foreign"}))
            owner.marker.with_suffix(".exit.json").write_text(json.dumps({"pid": 123, "signal": "TERM"}))
            with self.assertRaisesRegex(RuntimeError, "fence mismatch"):
                owner.inspect_native_exit()

    def test_marker_fsync_failure_does_not_publish_ready(self):
        with tempfile.TemporaryDirectory() as temporary:
            target = Path(temporary) / "payload.json"
            with patch.object(smoke.os, "fsync", side_effect=OSError("Injected marker fsync failure")), self.assertRaises(OSError):
                smoke.marker_write(target, {"pid": 123})
            self.assertFalse(target.exists())


if __name__ == "__main__":
    unittest.main()
