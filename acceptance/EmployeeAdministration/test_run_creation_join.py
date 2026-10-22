import tempfile
from pathlib import Path
import unittest
import sys
from unittest.mock import patch
import run_creation_join as lane


def requested(owner, unit, generation=None, identity=None):
    return {"generation": generation, "identity": identity, "cgroup": "/" + owner.slice + "/" + unit,
            "description": "owned:" + unit, "executable": "dotnet", "commandSha256": "sealed"}


def observed(owner, unit, generation="current", pid="0"):
    return {"Transient": "yes", "LoadState": "loaded", "Description": "owned:" + unit,
            "ExecStart": "{ path=dotnet ; argv[]=dotnet ; }", "ControlGroup": "/" + owner.slice + "/" + unit,
            "InvocationID": generation, "ExecMainPID": pid}


class AdmissionTests(unittest.TestCase):
    def test_exact_minimum_admission(self):
        lane.admission({"RUNNER_ENVIRONMENT": "github-hosted",
                        "MALIEV_CREATION_HOSTED_ADMISSION": "root-owned-sole-lane"}, 4194304)

    def test_rejects_missing_owner_nonhosted_and_low_memory(self):
        for env, memory in [
            ({}, 4194304),
            ({"RUNNER_ENVIRONMENT": "self-hosted", "MALIEV_CREATION_HOSTED_ADMISSION": "root-owned-sole-lane"}, 4194304),
            ({"RUNNER_ENVIRONMENT": "github-hosted"}, 4194304),
            ({"RUNNER_ENVIRONMENT": "github-hosted", "MALIEV_CREATION_HOSTED_ADMISSION": "root-owned-sole-lane"}, 4194303),
        ]:
            with self.subTest(env=env, memory=memory), self.assertRaises(RuntimeError):
                lane.admission(env, memory)


class OwnershipTests(unittest.TestCase):
    def container(self, label="run", mount="tmpfs"):
        return {"Id": "abc", "Created": "2026-10-08T00:00:00Z",
                "Config": {"Labels": {lane.LABEL: label}, "Image": "postgres:18-alpine"},
                "Mounts": [{"Type": mount}]}

    def test_owned_disposable_mount_is_accepted(self):
        self.assertEqual("abc", lane.owned_container(self.container(), "run")["id"])

    def test_foreign_label_or_persistent_mount_is_preserved(self):
        for value in [self.container(label="other"), self.container(mount="bind"), self.container(mount="volume")]:
            with self.subTest(value=value), self.assertRaises(RuntimeError):
                lane.owned_container(value, "run")

    def test_exact_unified_cgroup_required(self):
        lane.verify_lineage("0::/owned.slice/docker-abc.scope\n", "owned.slice", "docker-abc.scope")
        for value in ["0::/foreign/owned.slice/docker-abc.scope", "0::/owned.slice/docker-other.scope", "1:memory:/owned.slice/docker-abc.scope"]:
            with self.subTest(value=value), self.assertRaises(RuntimeError):
                lane.verify_lineage(value, "owned.slice", "docker-abc.scope")

    def test_forwarding_requires_loopback_and_no_active_client(self):
        value = self.container()
        value["NetworkSettings"] = {"Ports": {"5432/tcp": [{"HostIp": "0.0.0.0", "HostPort": "5555"}]}}
        with self.assertRaises(RuntimeError):
            lane.owned_container(value, "run")
        value["NetworkSettings"]["Ports"]["5432/tcp"][0]["HostIp"] = "127.0.0.1"
        self.assertEqual(["5555"], lane.owned_container(value, "run")["hostPorts"])
        lane.no_active_clients(["5555"], "")
        with self.assertRaises(RuntimeError):
            lane.no_active_clients(["5555"], "0 0 127.0.0.1:45000 127.0.0.1:5555")

    def test_pid_reuse_preserves_exact_resource(self):
        with tempfile.TemporaryDirectory(prefix="creation-supervisor-unit-") as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            owner.units["unit-test.service"] = requested(owner, "unit-test.service", "same", {"pid": 123, "startTicks": "old", "executable": "dotnet"})
            commands = []
            with patch.object(owner, "state", return_value=observed(owner, "unit-test.service", "same", "123")), patch.object(lane, "process_identity", return_value={"pid": 123, "startTicks": "new", "executable": "dotnet"}), patch.object(owner, "command", side_effect=lambda args, **kw: commands.append(args) or ""):
                with self.assertRaises(RuntimeError):
                    owner.cleanup()
            self.assertEqual([], commands)

    def test_generation_change_stops_wait_without_native_commands(self):
        with tempfile.TemporaryDirectory(prefix="creation-supervisor-unit-") as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            owner.units["unit-test.service"] = requested(owner, "unit-test.service", "old")
            with patch.object(owner, "state", return_value=observed(owner, "unit-test.service", "new")), self.assertRaises(ValueError):
                owner.wait("unit-test.service")

    def test_changed_sdk_generation_preserves_backend(self):
        with tempfile.TemporaryDirectory(prefix="creation-supervisor-unit-") as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            daemon = "unit-daemon.service"
            sdk = "unit-test.service"
            owner.units[daemon] = requested(owner, daemon, "d")
            owner.units[sdk] = requested(owner, sdk, "old")
            commands = []
            def state(unit):
                return observed(owner, unit, "new" if unit == sdk else "d")
            with patch.object(owner, "state", side_effect=state), patch.object(owner, "command", side_effect=lambda args, **kw: commands.append(args) or ""):
                with self.assertRaises(RuntimeError):
                    owner.cleanup()
            self.assertFalse(any(args[0] == "docker" for args in commands))
            self.assertEqual(2, len(owner.records["cleanupFailures"]))

    def test_partial_dispatch_state_failure_is_reacquired_before_cleanup(self):
        with tempfile.TemporaryDirectory(prefix="creation-supervisor-unit-") as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            unit = "unit-test.service"
            owner.units[unit] = requested(owner, unit)
            states = iter([RuntimeError("Injected post-dispatch state failure"), observed(owner, unit)])
            def state(_):
                result = next(states, observed(owner, unit))
                if isinstance(result, Exception):
                    raise result
                return result
            commands = []
            with patch.object(owner, "state", side_effect=state), patch.object(owner, "command", side_effect=lambda args, **kw: commands.append(args) or ""):
                owner.cleanup()
            self.assertEqual("current", owner.units[unit]["generation"])
            self.assertIn(["sudo", "-n", "systemctl", "stop", unit], commands)
            self.assertEqual([], owner.records["cleanupFailures"])

    def test_partial_dispatch_timeout_with_foreign_fence_never_adopts_unit(self):
        with tempfile.TemporaryDirectory(prefix="creation-supervisor-unit-") as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            unit = "unit-test.service"
            owner.units[unit] = requested(owner, unit)
            state = observed(owner, unit)
            state["Description"] = "foreign"
            commands = []
            with patch.object(owner, "state", return_value=state), patch.object(owner, "command", side_effect=lambda args, **kw: commands.append(args) or ""):
                with self.assertRaises(RuntimeError):
                    owner.cleanup()
            self.assertEqual([], commands)

    def test_dispatch_timeout_keeps_durable_fence_and_same_owner_recovers(self):
        with tempfile.TemporaryDirectory(prefix="creation-supervisor-unit-") as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            commands = []
            def command(args, **_):
                commands.append(args)
                if "systemd-run" in args:
                    raise RuntimeError("Injected timeout after manager accepted dispatch")
                return ""
            def state(unit):
                record = owner.units[unit]
                return {"Transient": "yes", "LoadState": "loaded", "Description": record["description"],
                        "ExecStart": "{ path=" + record["executable"] + " ; argv[]=owned ; }",
                        "ControlGroup": record["cgroup"], "InvocationID": "recovered", "ExecMainPID": "0"}
            with patch.object(lane.os, "mkfifo", side_effect=lambda p, mode: Path(p).touch(), create=True), patch.object(lane.os, "O_NONBLOCK", 0, create=True), patch.object(owner, "command", side_effect=command), patch.object(owner, "state", side_effect=state):
                with self.assertRaises(RuntimeError):
                    owner.launch("daemon", sys.executable, ["owned"], "64M")
                unit = next(iter(owner.units))
                receipt = (owner.scratch / "ownership.json").read_text()
                self.assertIn(owner.units[unit]["description"], receipt)
                self.assertIsNone(owner.units[unit]["generation"])
                owner.cleanup()
            self.assertEqual("recovered", owner.units[unit]["generation"])
            self.assertIn(["sudo", "-n", "systemctl", "stop", unit], commands)
            self.assertEqual({}, owner.streams)

    def test_terminal_historical_pid_is_not_read_or_killed(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            unit = "unit-build.service"
            owner.units[unit] = requested(owner, unit, "current")
            state = observed(owner, unit, pid="123")
            state.update(SubState="exited", ActiveState="active", Result="success", ExecMainStatus="0")
            commands = []
            with patch.object(owner, "state", return_value=state), patch.object(lane, "process_identity", side_effect=AssertionError("Historical PID must not be inspected")), patch.object(owner, "command", side_effect=lambda args, **kw: commands.append(args) or ""):
                owner.wait(unit)
                owner.cleanup()
            self.assertIn(["sudo", "-n", "systemctl", "stop", unit], commands)

    def test_continuous_overflow_fifo_drain_returns_with_bounded_reads(self):
        import io
        with tempfile.TemporaryDirectory() as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            owner.streams["stream"] = {"fd": 999, "file": io.BytesIO(), "bytes": 8 * 1024 * 1024}
            with patch.object(lane.os, "read", return_value=b"x" * 16384) as read:
                owner.drain_streams()
            self.assertLessEqual(read.call_count, 8)
            self.assertTrue(owner.stream_overflow)
            self.assertEqual(b"", owner.streams["stream"]["file"].getvalue())
            owner.streams.clear()

    def test_slice_timeout_after_effect_recovers_immutable_fence(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            commands = []
            def command(args, **kw):
                commands.append(args)
                if "StartTransientUnit" in args:
                    receipt = (owner.scratch / "ownership.json").read_text()
                    self.assertIn(owner.slice_description, receipt)
                    self.assertIn('"sliceIntent": true', receipt)
                    raise RuntimeError("Timeout after slice manager accepted")
                return ""
            state = {"LoadState": "loaded", "Transient": "yes", "Description": owner.slice_description, "InvocationID": "slice-generation", "ActiveState": "inactive"}
            with patch.object(owner, "verify_control_lease"), patch.object(lane.proof_reader, "validate_source_seal"), patch.object(lane.shutil, "which", return_value="owned"), patch.object(owner, "command", side_effect=command), patch.object(owner, "state", return_value=state):
                with self.assertRaisesRegex(RuntimeError, "Timeout after"):
                    owner.execute(Path("seal"), "sealed")
            self.assertEqual("slice-generation", owner.slice_generation)
            self.assertIn(["sudo", "-n", "systemctl", "stop", owner.slice], commands)
            self.assertEqual([], owner.records["cleanupFailures"])

    def test_bridge_timeout_after_effect_recovers_alias_before_delete(self):
        import json
        with tempfile.TemporaryDirectory() as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            interface = Path(temporary) / "interface"
            interface.mkdir()
            (interface / "ifindex").write_text("456")
            (interface / "ifalias").write_text(owner.bridge_alias)
            (interface / "brif").mkdir()
            original_path = lane.Path
            commands = []
            def path(value):
                return Path(temporary) if str(value) == "/sys/class/net" else original_path(value)
            # Rename the fake interface to the unique actual name; no native network commands run.
            interface.rename(Path(temporary) / owner.bridge)
            interface = Path(temporary) / owner.bridge
            exists = [False]
            original_exists = Path.exists
            def observed_exists(value):
                return exists[0] if value == interface else original_exists(value)
            def command(args, **kw):
                commands.append(args)
                if "add" in args:
                    receipt = json.loads((owner.scratch / "ownership.json").read_text())
                    self.assertTrue(receipt["bridgeIntent"])
                    self.assertEqual(owner.bridge_alias, receipt["bridgeAlias"])
                    exists[0] = True
                    raise RuntimeError("Timeout after bridge kernel effect")
                if "delete" in args:
                    exists[0] = False
                return ""
            with patch.object(lane, "Path", side_effect=path), patch.object(Path, "exists", observed_exists), patch.object(owner, "command", side_effect=command):
                with self.assertRaisesRegex(RuntimeError, "Timeout after"):
                    owner.start_bridge()
                self.assertIsNone(owner.bridge_index)
                owner.cleanup()
            self.assertEqual(456, owner.bridge_index)
            self.assertIn(["sudo", "-n", "ip", "link", "delete", owner.bridge], commands)
            self.assertEqual([], owner.records["cleanupFailures"])

    def test_unreaped_control_barrier_blocks_all_native_cleanup(self):
        from unittest.mock import Mock
        with tempfile.TemporaryDirectory() as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            resource = Mock()
            resource.receipt = {"settled": False, "exited": False, "readerClosed": False}
            resource.settle.return_value = False
            owner.register_control(resource)
            with patch.object(owner, "command") as command, patch.object(owner, "state") as state:
                with self.assertRaisesRegex(RuntimeError, "Control reaping unverified"):
                    owner.cleanup()
                command.assert_not_called()
                state.assert_not_called()
            self.assertIs(resource, owner.control_children[0])
            resource.settle.return_value = True
            owner.control_barrier()

    def test_log_open_failure_keeps_registered_fifo_for_cleanup(self):
        import os
        with tempfile.TemporaryDirectory() as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            original_open = Path.open
            def open_path(path, *args, **kwargs):
                if path.name.endswith(".private.log"):
                    raise OSError("Injected private log open failure")
                return original_open(path, *args, **kwargs)
            with patch.object(lane.os, "mkfifo", side_effect=lambda path, mode: Path(path).touch(), create=True), patch.object(lane.os, "O_NONBLOCK", 0, create=True), patch.object(Path, "open", open_path):
                with self.assertRaisesRegex(OSError, "Injected private log"):
                    owner.launch("build", sys.executable, [], "64M")
            stream = next(iter(owner.streams.values()))
            fd = stream["fd"]
            self.assertIsNone(stream["file"])
            os.fstat(fd)
            owner.cleanup()
            with self.assertRaises(OSError):
                os.fstat(fd)
            self.assertEqual({}, owner.streams)

    def test_file_fsync_failure_preserves_prior_receipt_and_prevents_dispatch(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            receipt = owner.scratch / "ownership.json"
            prior = receipt.read_bytes()
            with patch.object(lane.os, "fsync", side_effect=OSError("Injected file fsync failure")), patch.object(owner, "command") as command:
                with self.assertRaisesRegex(OSError, "Injected file fsync"):
                    owner.start_bridge()
                command.assert_not_called()
            self.assertEqual(prior, receipt.read_bytes())
            self.assertTrue(owner.bridge_intent)
            owner.save()

    def test_directory_sync_failure_preserves_prior_receipt_before_dispatch(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            receipt = owner.scratch / "ownership.json"
            prior = receipt.read_bytes()
            with patch.object(lane, "sync_directory", side_effect=OSError("Injected directory fsync failure")), patch.object(owner, "command") as command:
                with self.assertRaisesRegex(OSError, "Injected directory fsync"):
                    owner.start_bridge()
                command.assert_not_called()
            self.assertEqual(prior, receipt.read_bytes())
            self.assertEqual(prior, (owner.scratch / "ownership.previous.json").read_bytes())
            owner.save()

    def test_linux_directory_sync_closes_exact_descriptor_on_failure(self):
        with patch.object(lane.os, "name", "posix"), patch.object(lane.os, "O_DIRECTORY", 0, create=True), patch.object(lane.os, "open", return_value=654) as opened, patch.object(lane.os, "fsync", side_effect=OSError("Injected actual directory descriptor failure")) as sync, patch.object(lane.os, "close") as closed:
            with self.assertRaises(OSError):
                lane.sync_directory(Path("owned"))
            opened.assert_called_once()
            sync.assert_called_once_with(654)
            closed.assert_called_once_with(654)

    def test_execute_finally_keeps_actual_owner_until_failed_reap_settles(self):
        import os
        import datetime as dt
        import owned_creation_io as owned
        import test_owned_creation_io as controls
        with tempfile.TemporaryDirectory() as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            original_settle = owned.OwnedChild.settle
            attempts = []
            native_cleanup = []
            def settle(resource):
                attempts.append(resource.process.pid)
                if len(attempts) <= 2:
                    self.assertIsNone(resource.process.poll())
                    return False
                self.assertTrue(owner.cleanup_only)
                return original_settle(resource)
            def command(args, **kwargs):
                if "StartTransientUnit" in args:
                    # The actual child remains alive after the command failure and first retry.
                    owner.deadline = dt.datetime.now(dt.timezone.utc) - dt.timedelta(seconds=1)
                    return owned.capture([sys.executable, "-c", "import time;time.sleep(60)"], env=dict(os.environ), timeout=0.05, register=owner.register_control)[0]
                self.assertTrue(all(r.receipt["settled"] for r in owner.control_children))
                native_cleanup.append(args)
                return ""
            state = {"LoadState": "loaded", "Transient": "yes", "Description": owner.slice_description,
                     "InvocationID": "same-owner", "ActiveState": "inactive"}
            def observe(_):
                self.assertTrue(all(r.receipt["settled"] for r in owner.control_children))
                return state
            try:
                with patch.object(owner, "verify_control_lease"), patch.object(lane.proof_reader, "validate_source_seal"), patch.object(lane.shutil, "which", return_value="owned"), patch.object(owner, "command", side_effect=command), patch.object(owner, "state", side_effect=observe), patch.object(owned.OwnedChild, "settle", settle):
                    with self.assertRaisesRegex(RuntimeError, "native proof remains invalid"):
                        owner.execute(Path("seal"), "sealed")
                self.assertGreaterEqual(len(attempts), 3)
                self.assertTrue(owner.records["cleanupOnly"])
                self.assertTrue(owner.records["nativeLeaseExpired"])
                self.assertIn(["sudo", "-n", "systemctl", "stop", owner.slice], native_cleanup)
                self.assertFalse((owner.scratch / "proof.json").exists())
                with self.assertRaisesRegex(RuntimeError, "cannot renew"):
                    owner.execute(Path("seal"), "sealed")
            finally:
                for resource in owner.control_children:
                    self.assertTrue(original_settle(resource))
                    controls.RECEIPTS.append(resource.receipt)
                controls.tearDownModule()

    def test_control_service_requires_exact_fence_and_finite_containment(self):
        actual = {"pid": 123, "executable": "/usr/bin/python"}
        state = {"Transient": "yes", "Description": "CodexCreationControl:nonce", "InvocationID": "generation",
                 "ControlGroup": "/system.slice/unit", "ExecMainPID": "123", "ExecStart": "{ path=/usr/bin/python ; argv[]=owned ; }",
                 "RuntimeMaxUSec": "600000000", "ExecMainStartTimestampMonotonic": "1000000",
                 "KillMode": "control-group", "SendSIGKILL": "yes", "Delegate": "no", "TimeoutStopUSec": "20000000", "MemoryMax": "134217728"}
        lane.validate_control_lease(state, "unit", "nonce", "/system.slice/unit", actual, 2)
        for key, value in [("Description", "foreign"), ("InvocationID", ""), ("ControlGroup", "/foreign"), ("ExecMainPID", "456"), ("ExecStart", "foreign"), ("RuntimeMaxUSec", "infinity"), ("RuntimeMaxUSec", "1560000001"), ("ExecMainStartTimestampMonotonic", "3000000"), ("KillMode", "process"), ("SendSIGKILL", "no"), ("Delegate", "yes"), ("TimeoutStopUSec", "20000001"), ("MemoryMax", "134217729")]:
            with self.subTest(key=key, value=value), self.assertRaises((RuntimeError, ValueError)):
                lane.validate_control_lease({**state, key: value}, "unit", "nonce", "/system.slice/unit", actual, 2)
        with self.assertRaisesRegex(RuntimeError, "reserve exhausted"):
            lane.validate_control_lease(state, "unit", "nonce", "/system.slice/unit", actual, 500)

    def test_cleanup_only_no_live_helpers_does_not_loop_on_broken_receipt_disk(self):
        with tempfile.TemporaryDirectory() as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            with patch.object(owner, "save", side_effect=OSError("Disk unavailable")), patch.object(owner, "command") as command:
                with self.assertRaises(OSError):
                    owner.recover_cleanup()
                command.assert_not_called()
            self.assertEqual([], owner.control_children)

    def test_busctl_property_uint64_is_scalar_and_strict(self):
        self.assertEqual(600000000, lane.typed_uint64({"type": "t", "data": 600000000}))
        for value in [{"type": "t", "data": [600000000]}, {"type": "t", "data": True}, {"type": "s", "data": "600000000"}, {"type": "t", "data": -1}, {"type": "t", "data": 18446744073709551616}, {}]:
            with self.subTest(value=value), self.assertRaises(RuntimeError):
                lane.typed_uint64(value)

    def test_cleanup_created_actual_child_cannot_escape_execute_finally(self):
        import os
        import owned_creation_io as owned
        import test_owned_creation_io as controls
        with tempfile.TemporaryDirectory() as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            original_settle = owned.OwnedChild.settle
            attempts = []
            observations = []
            dispatched = []
            def settle(resource):
                attempts.append(resource.process.pid)
                if len(attempts) <= 2:
                    return False
                return original_settle(resource)
            def command(args, **kwargs):
                if "StartTransientUnit" in args:
                    raise RuntimeError("Original native phase failed")
                if not dispatched:
                    dispatched.append(True)
                    return owned.capture([sys.executable, "-c", "import time;time.sleep(60)"], env=dict(os.environ), timeout=0.05, register=owner.register_control)[0]
                self.assertTrue(all(r.receipt["settled"] for r in owner.control_children))
                return ""
            def state(_):
                # The first actual helper is created from inside cleanup.
                owner.command(["owned", "state"])
                observations.append("settled")
                return {"LoadState": "not-found"}
            try:
                with patch.object(owner, "verify_control_lease"), patch.object(lane.proof_reader, "validate_source_seal"), patch.object(lane.shutil, "which", return_value="owned"), patch.object(owner, "command", side_effect=command), patch.object(owner, "state", side_effect=state), patch.object(owned.OwnedChild, "settle", settle):
                    with self.assertRaisesRegex(RuntimeError, "native proof remains invalid"):
                        owner.execute(Path("seal"), "sealed")
                self.assertGreaterEqual(len(attempts), 3)
                self.assertEqual(["settled"], observations)
                self.assertTrue(owner.records["cleanupAttemptFailed"])
                self.assertFalse((owner.scratch / "proof.json").exists())
            finally:
                for resource in owner.control_children:
                    self.assertTrue(original_settle(resource))
                    controls.RECEIPTS.append(resource.receipt)
                controls.tearDownModule()

    def test_missing_owned_cgroup_never_authorizes_backend_cleanup(self):
        with tempfile.TemporaryDirectory(prefix="creation-supervisor-unit-") as temporary:
            owner = lane.Supervisor(Path.cwd(), Path(temporary))
            owner.units["unit"] = {"cgroup": "/foreign/test"}
            with self.assertRaises(RuntimeError):
                owner.quiescent("unit")


if __name__ == "__main__":
    unittest.main()
