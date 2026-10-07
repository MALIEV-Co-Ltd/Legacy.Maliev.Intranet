import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import bootstrap_creation_join as bootstrap
import owned_creation_io as owned
import test_owned_creation_io as controls


class BootstrapControls(unittest.TestCase):
    def test_foreign_generation_never_authorizes_stop(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = bootstrap.Bootstrap(Path.cwd(), Path(temporary))
            owner.records["dispatchIntent"] = True
            owner.records["generation"] = "old"
            state = {"LoadState": "loaded", "Transient": "yes", "Description": owner.records["description"],
                     "ExecStart": "{ path=" + str(Path(sys.executable).resolve()) + " ; argv[]=owned ; }", "InvocationID": "new"}
            with patch.object(owner, "state", return_value=state), patch.object(owner, "command") as command:
                with self.assertRaisesRegex(RuntimeError, "generation mismatch"):
                    owner.finish()
                command.assert_not_called()
            self.assertFalse(owner.records["releaseVerified"])

    def test_unit_absence_with_pending_start_job_cannot_claim_release(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = bootstrap.Bootstrap(Path.cwd(), Path(temporary))
            with patch.object(owner, "state", return_value={"LoadState": "not-found"}), patch.object(owner, "command", return_value="123 " + owner.unit + " start running"):
                with self.assertRaisesRegex(RuntimeError, "job still pending"):
                    owner.acquire()

    def test_terminal_historical_pid_does_not_trigger_pid_read(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = bootstrap.Bootstrap(Path.cwd(), Path(temporary))
            state = {"LoadState": "loaded", "Transient": "yes", "Description": owner.records["description"],
                     "ExecStart": "{ path=" + str(Path(sys.executable).resolve()) + " ; argv[]=owned ; }",
                     "InvocationID": "same", "ControlGroup": "", "ActiveState": "inactive", "ExecMainPID": "123"}
            with patch.object(owner, "state", return_value=state), patch.object(bootstrap.lane, "process_identity", side_effect=AssertionError("Historical PID")):
                self.assertEqual(state, owner.acquire())

    def test_partial_dispatch_persists_fence_before_manager_call(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = bootstrap.Bootstrap(Path.cwd(), Path(temporary))
            def command(arguments):
                self.assertTrue(owner.records["dispatchIntent"])
                self.assertIn(owner.records["description"], (owner.evidence / "ownership.json").read_text())
                self.assertIn("--property=RuntimeMaxSec=26min", arguments)
                self.assertIn("--property=KillMode=control-group", arguments)
                self.assertIn("--property=MemoryMax=128M", arguments)
                raise RuntimeError("Post-dispatch timeout")
            with patch.object(bootstrap.lane.proof_reader, "validate_source_seal"), patch.object(bootstrap.os, "getuid", return_value=1000, create=True), patch.object(bootstrap.os, "getgid", return_value=1000, create=True), patch.object(owner, "command", side_effect=command), patch.object(owner, "finish") as finish:
                with self.assertRaisesRegex(RuntimeError, "Post-dispatch"):
                    owner.execute(Path("seal"), "sealed")
                finish.assert_called_once()

    def test_actual_cleanup_created_child_keeps_bootstrap_owner_until_exit(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = bootstrap.Bootstrap(Path.cwd(), Path(temporary))
            owner.records["dispatchIntent"] = True
            original_settle = owned.OwnedChild.settle
            attempts = []
            dispatches = []
            def settle(resource):
                attempts.append(resource.process.pid)
                return False if len(attempts) <= 2 else original_settle(resource)
            def acquire():
                if not dispatches:
                    dispatches.append(True)
                    return owned.capture([sys.executable, "-c", "import time;time.sleep(60)"], env=dict(os.environ), timeout=0.05, register=owner.register)[0]
                self.assertTrue(all(child.receipt["settled"] for child in owner.children))
                return None
            try:
                with patch.object(owner, "acquire", side_effect=acquire), patch.object(owner, "inspect_native_exit"), patch.object(owned.OwnedChild, "settle", settle):
                    with self.assertRaisesRegex(RuntimeError, "proof invalid"):
                        owner.finish()
                self.assertGreaterEqual(len(attempts), 3)
                self.assertFalse(owner.records["releaseVerified"])
            finally:
                for child in owner.children:
                    self.assertTrue(original_settle(child))
                    controls.RECEIPTS.append(child.receipt)
                controls.tearDownModule()

    def test_actual_exit_race_reobserves_same_generation_terminal_state(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = bootstrap.Bootstrap(Path.cwd(), Path(temporary))
            state = {"LoadState": "loaded", "Transient": "yes", "Description": owner.records["description"],
                     "ExecStart": "{ path=" + str(Path(sys.executable).resolve()) + " ; argv[]=owned ; }",
                     "InvocationID": "same", "ControlGroup": owner.group, "ActiveState": "active", "SubState": "running", "ExecMainPID": "123"}
            terminal = {**state, "ActiveState": "failed", "SubState": "failed", "Result": "timeout"}
            with patch.object(owner, "state", side_effect=[state, terminal]), patch.object(bootstrap.lane, "process_identity", side_effect=FileNotFoundError("Injected exit between state and proc read")) as identity:
                self.assertEqual(terminal, owner.acquire())
                identity.assert_called_once_with(123)
            self.assertEqual("same", owner.records["generation"])

    def test_failed_empty_expired_service_can_be_verified_stopped(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = bootstrap.Bootstrap(Path.cwd(), Path(temporary))
            owner.records["dispatchIntent"] = True
            state = {"ActiveState": "failed", "Result": "timeout"}
            with patch.object(owner, "acquire", return_value=state), patch.object(owner, "command") as command, patch.object(owner, "inspect_native_exit"), patch.object(owner, "quiescent") as empty:
                owner.finish()
                command.assert_called_once_with(["sudo", "-n", "systemctl", "stop", owner.unit])
                empty.assert_called_once_with(owner.group)
            self.assertTrue(owner.records["releaseVerified"])

    def test_backend_recovery_releases_resources_without_restoring_native_acceptance(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = bootstrap.Bootstrap(Path.cwd(), Path(temporary))
            owner.records["dispatchIntent"] = True
            owner.source_seal = Path("seal")
            owner.source_seal_sha = "a" * 64
            with patch.object(owner, "acquire", return_value=None), patch.object(owner, "inspect_native_exit", side_effect=[RuntimeError("Private backend remains"), None]), patch.object(owner, "recover_native") as recover:
                owner.finish()
                recover.assert_called_once()
            self.assertTrue(owner.records["releaseVerified"])
            self.assertTrue(owner.failed)
            self.assertTrue(owner.recovery_attempted)

    def test_missing_native_receipt_never_dispatches_recovery_owner(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = bootstrap.Bootstrap(Path.cwd(), Path(temporary))
            with patch.object(bootstrap, "CleanupBootstrap") as recovery, self.assertRaisesRegex(RuntimeError, "persisted native owner"):
                owner.recover_native()
            recovery.assert_not_called()

    def test_foreign_persisted_creator_never_dispatches_recovery_owner(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = bootstrap.Bootstrap(Path.cwd(), Path(temporary))
            scratch = owner.evidence / "native" / owner.run
            scratch.mkdir(parents=True)
            (scratch / "ownership.json").write_text('{"run":"foreign","controlService":{}}')
            with patch.object(bootstrap, "CleanupBootstrap") as recovery, self.assertRaisesRegex(RuntimeError, "generation mismatch"):
                owner.recover_native()
            recovery.assert_not_called()

    def test_native_receipt_missing_never_becomes_release(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = bootstrap.Bootstrap(Path.cwd(), Path(temporary))
            with self.assertRaisesRegex(RuntimeError, "receipt unavailable"):
                owner.inspect_native_exit()
            self.assertFalse(owner.records["releaseVerified"])


if __name__ == "__main__":
    unittest.main()
