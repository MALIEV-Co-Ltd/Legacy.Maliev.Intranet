import datetime as dt
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import recover_creation_owner as recovery


class RecoveryControls(unittest.TestCase):
    def receipt(self, temporary, mutate=lambda value: None):
        root = Path.cwd().resolve()
        evidence = Path(temporary)
        run, generation = "1" * 32, "2" * 32
        unit = "codex-creation-supervisor-" + "3" * 32 + ".service"
        daemon = "codex-creation-" + run + "-daemon.service"
        scratch = evidence / run
        scratch.mkdir()
        value = {"run": run, "root": str(root), "slice": "creation" + run + ".slice",
                 "sliceStarted": True, "sliceIntent": True, "sliceDescription": "CodexCreationSlice:" + run + ":" + "4" * 32,
                 "sliceGeneration": "5" * 32, "bridgeIntent": True, "bridgeAlias": "CodexCreationBridge:" + run + ":" + "6" * 32,
                 "bridgeIndex": 123, "expiresUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
                 "controlService": {"unit": unit, "generation": generation, "controlGroup": "/system.slice/" + unit},
                 "cleanupFailures": [{"unit": daemon, "error": "CaptureError"}], "controlProcesses": [{"pid": 123, "settled": False}],
                 "ownedUnits": {daemon: {"generation": "7" * 32, "identity": None, "cgroup": "/creation" + run + ".slice/" + daemon,
                                         "description": "CodexCreation:" + run + ":" + "8" * 32 + ":" + "9" * 64,
                                         "commandSha256": "9" * 64, "executable": str(Path(recovery.os.sys.executable).resolve())}}}
        mutate(value)
        raw = json.dumps(value).encode()
        (scratch / "ownership.json").write_bytes(raw)
        return root, evidence, run, hashlib.sha256(raw).hexdigest(), unit, generation

    def test_exact_receipt_preserves_original_and_starts_cleanup_only(self):
        with tempfile.TemporaryDirectory() as temporary:
            args = self.receipt(temporary)
            owner = recovery.Recovery.load(*args)
            self.assertTrue(owner.cleanup_only)
            self.assertFalse(owner.creator_verified_empty)
            self.assertEqual([], owner.control_children)
            self.assertEqual(1, len(owner.records["historicalCleanupFailures"]))
            self.assertEqual(1, len(owner.records["expiredControlProcesses"]))
            self.assertEqual(args[3], hashlib.sha256((owner.recovery_evidence / ("ownership.creator-" + args[3] + ".json")).read_bytes()).hexdigest())
            with self.assertRaisesRegex(RuntimeError, "cannot launch"):
                owner.launch("build", "dotnet", [], "64M")

    def test_receipt_hash_and_creator_generation_must_match(self):
        for position, wrong in [(3, "a" * 64), (5, "a" * 32)]:
            with tempfile.TemporaryDirectory() as temporary:
                args = list(self.receipt(temporary))
                args[position] = wrong
                with self.assertRaises(RuntimeError):
                    recovery.Recovery.load(*args)

    def test_foreign_unit_lineage_or_command_fence_rejected(self):
        for field, wrong in [("cgroup", "/foreign"), ("description", "foreign"), ("commandSha256", "a" * 64), ("generation", "foreign")]:
            with tempfile.TemporaryDirectory() as temporary:
                args = self.receipt(temporary, lambda value: next(iter(value["ownedUnits"].values())).__setitem__(field, wrong))
                with self.assertRaises(RuntimeError):
                    recovery.Recovery.load(*args)

    def test_live_or_changed_creator_never_allows_native_cleanup(self):
        for state in [{"LoadState": "loaded", "Transient": "yes", "InvocationID": "2" * 32, "ActiveState": "active"},
                      {"LoadState": "loaded", "Transient": "yes", "InvocationID": "f" * 32, "ActiveState": "failed"}]:
            with tempfile.TemporaryDirectory() as temporary:
                owner = recovery.Recovery.load(*self.receipt(temporary))
                text = "\n".join(key + "=" + value for key, value in state.items())
                with patch.object(owner, "command", return_value=text), self.assertRaises(RuntimeError):
                    owner.verify_expired_creator()
                self.assertFalse(owner.creator_verified_empty)
                with patch.object(owner, "control_barrier"), patch.object(recovery.lane.Supervisor, "cleanup") as native:
                    with self.assertRaisesRegex(RuntimeError, "cleanup prohibited"):
                        owner.cleanup()
                    native.assert_not_called()

    def test_expired_creator_empty_exact_group_unlocks_cleanup_only(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = recovery.Recovery.load(*self.receipt(temporary))
            creator = owner.records["expiredControlService"]
            text = "LoadState=loaded\nTransient=yes\nInvocationID=" + creator["generation"] + "\nActiveState=failed\nControlGroup=" + creator["controlGroup"]
            with patch.object(owner, "command", return_value=text):
                owner.verify_expired_creator()
            self.assertTrue(owner.creator_verified_empty)
            with patch.object(recovery.lane.Supervisor, "cleanup") as native:
                owner.cleanup()
                native.assert_called_once()

    def test_creator_changed_control_lineage_never_unlocks_cleanup(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = recovery.Recovery.load(*self.receipt(temporary))
            text = "LoadState=loaded\nTransient=yes\nInvocationID=" + "2" * 32 + "\nActiveState=failed\nControlGroup=/foreign"
            with patch.object(owner, "command", return_value=text), self.assertRaisesRegex(RuntimeError, "lineage changed"):
                owner.verify_expired_creator()
            self.assertFalse(owner.creator_verified_empty)

    def test_admission_save_never_rewrites_unverified_creator_receipt(self):
        with tempfile.TemporaryDirectory() as temporary:
            args = self.receipt(temporary)
            original = args[1] / args[2] / "ownership.json"
            before = original.read_bytes()
            owner = recovery.Recovery.load(*args)
            owner.save()
            self.assertEqual(before, original.read_bytes())
            self.assertTrue((owner.recovery_evidence / "ownership.json").exists())
            with self.assertRaisesRegex(RuntimeError, "Unverified recovery"):
                owner.publish_recovered_receipt()
            self.assertEqual(before, original.read_bytes())

    def test_failed_creator_preflight_and_finally_preserve_original_receipt(self):
        with tempfile.TemporaryDirectory() as temporary:
            args = self.receipt(temporary)
            original = args[1] / args[2] / "ownership.json"
            before = original.read_bytes()
            owner = recovery.Recovery.load(*args)
            def admission():
                owner.save()
            text = "LoadState=loaded\nTransient=yes\nInvocationID=" + "2" * 32 + "\nActiveState=active"
            with patch.object(owner, "verify_control_lease", side_effect=admission), patch.object(owner, "command", return_value=text):
                with self.assertRaisesRegex(RuntimeError, "cleanup prohibited"):
                    owner.recover()
            self.assertEqual(before, original.read_bytes())
            self.assertFalse(owner.creator_verified_empty)
            self.assertFalse((owner.scratch / "recovery-proof.json").exists())

    def test_real_control_lease_pipeline_does_not_rewrite_live_creator_checkpoint(self):
        import time
        with tempfile.TemporaryDirectory() as temporary:
            args = self.receipt(temporary)
            owner = recovery.Recovery.load(*args)
            original = owner.scratch / "ownership.json"
            before = original.read_bytes()
            unit = "codex-creation-supervisor-" + "a" * 32 + ".service"
            fence = "b" * 32
            group = "/system.slice/" + unit
            executable = str(Path(recovery.os.sys.executable).resolve())
            control = {"Transient": "yes", "Description": "CodexCreationControl:" + fence, "InvocationID": "c" * 32,
                       "ControlGroup": group, "ExecMainPID": "123", "ExecStart": "{ path=" + executable + " ; argv[]=owned ; }",
                       "KillMode": "control-group", "SendSIGKILL": "yes", "Delegate": "no"}
            observed = []
            def capture(arguments, **kwargs):
                self.assertEqual(before, original.read_bytes())
                if "get-property" in arguments:
                    key = arguments[-1]
                    data = {"RuntimeMaxUSec": 1560000000, "ExecMainStartTimestampMonotonic": int(time.monotonic() * 1000000) - 1000000,
                            "TimeoutStopUSec": 20000000, "MemoryMax": 134217728}[key]
                    return json.dumps({"type": "t", "data": data}), {}
                if unit in arguments:
                    observed.append("new-control-lease")
                    return "\n".join(key + "=" + value for key, value in control.items()), {}
                observed.append("old-creator-live")
                return "LoadState=loaded\nTransient=yes\nInvocationID=" + "2" * 32 + "\nActiveState=active", {}
            read_text = Path.read_text
            def read(path, *arguments, **kwargs):
                return "0::" + group if str(path).replace("\\", "/") == "/proc/self/cgroup" else read_text(path, *arguments, **kwargs)
            with patch.dict(recovery.os.environ, {"MALIEV_CREATION_CONTROL_UNIT": unit, "MALIEV_CREATION_CONTROL_FENCE": fence}), patch.object(Path, "read_text", read), patch.object(recovery.lane.os, "getpid", return_value=123), patch.object(recovery.lane, "process_identity", return_value={"pid": 123, "startTicks": "actual-model", "executable": executable}), patch.object(recovery.lane.owned_io, "capture", side_effect=capture):
                with self.assertRaisesRegex(RuntimeError, "cleanup prohibited"):
                    owner.recover()
            self.assertEqual(["new-control-lease", "old-creator-live"], observed)
            self.assertEqual(before, original.read_bytes())
            self.assertFalse(owner.creator_verified_empty)

    def test_changed_creator_receipt_cannot_be_overwritten_after_cleanup(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = recovery.Recovery.load(*self.receipt(temporary))
            owner.creator_verified_empty = True
            owner.records["cleanupOnlyRecoveryVerified"] = True
            original = owner.scratch / "ownership.json"
            original.write_bytes(b"new owner")
            with patch.object(owner, "verify_expired_creator"), self.assertRaisesRegex(RuntimeError, "changed during recovery"):
                owner.publish_recovered_receipt()
            self.assertEqual(b"new owner", original.read_bytes())

    def test_absent_creator_pending_job_never_unlocks_cleanup(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = recovery.Recovery.load(*self.receipt(temporary))
            unit = owner.records["expiredControlService"]["unit"]
            with patch.object(owner, "command", side_effect=["LoadState=not-found", "123 " + unit + " start running"]), self.assertRaisesRegex(RuntimeError, "pending job"):
                owner.verify_expired_creator()
            self.assertFalse(owner.creator_verified_empty)


if __name__ == "__main__":
    unittest.main()
