import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import smoke_creation_control as smoke


class SmokeControls(unittest.TestCase):
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
