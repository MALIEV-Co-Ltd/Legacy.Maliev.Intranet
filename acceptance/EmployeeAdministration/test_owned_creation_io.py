import json
import os
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
import owned_creation_io as io

RECEIPTS = []


class OwnedIOControls(unittest.TestCase):
    def test_missing_linux_executable_requires_exact_owned_handle_exit(self):
        from types import SimpleNamespace
        # Linux birth metadata exists for a zombie; executable symlink does not.
        raw = "123 (owned helper) " + " ".join(["S"] + ["0"] * 18 + ["4321"])
        class ProcPath:
            def __init__(self, value):
                self.value = value
            def __truediv__(self, value):
                return self
            def read_text(self):
                return raw
            def resolve(self, strict):
                raise FileNotFoundError("Exited process executable unavailable")
        with patch.object(io.os, "name", "posix"), patch.object(io, "Path", ProcPath):
            value = io.creation_identity(SimpleNamespace(pid=123, poll=lambda: 0))
            self.assertEqual("4321", value["startTicks"])
            self.assertIsNone(value["actualExecutable"])
            self.assertTrue(value["identityObservedAfterExit"])
            self.assertEqual(0, value["terminalReturnCode"])
            with self.assertRaises(FileNotFoundError):
                io.creation_identity(SimpleNamespace(pid=123, poll=lambda: None))

    def test_missing_linux_birth_record_never_infers_terminal_identity(self):
        from types import SimpleNamespace
        class MissingBirth:
            def __init__(self, _value):
                pass
            def __truediv__(self, _value):
                return self
            def read_text(self):
                raise FileNotFoundError()
        with patch.object(io.os, "name", "posix"), patch.object(io, "Path", MissingBirth):
            with self.assertRaises(FileNotFoundError):
                io.creation_identity(SimpleNamespace(pid=123, poll=lambda: 0))

    def test_terminal_identity_never_converts_failed_command_to_success(self):
        from types import SimpleNamespace
        reader = SimpleNamespace(closed=False, fileno=lambda: 42)
        reader.close = lambda: setattr(reader, "closed", True)
        process = SimpleNamespace(pid=123, stdout=reader, poll=lambda: 7, wait=lambda **kwargs: 7)
        identity = {"pid": 123, "startTicks": "4321", "actualExecutable": None,
                    "identityObservedAfterExit": True, "terminalReturnCode": 7}
        with patch.object(io.subprocess, "Popen", return_value=process), \
                patch.object(io, "creation_identity", return_value=identity), \
                patch.object(io.os, "set_blocking"), patch.object(io.os, "read", return_value=b""), \
                self.assertRaises(io.CaptureError) as failure:
            io.capture([sys.executable], env={})
        self.assertEqual(7, failure.exception.receipt["returnCode"])
        self.assertTrue(failure.exception.receipt["settled"])
        self.assertIsNone(failure.exception.receipt["actualExecutable"])

    def test_normal_capture_observes_actual_exit(self):
        text, receipt = io.capture([sys.executable, "-c", "print('owned')"], env=dict(os.environ), timeout=3)
        RECEIPTS.append(receipt)
        self.assertEqual("owned", text)
        self.assertTrue(receipt["exited"] and receipt["readerClosed"])
        self.assertEqual(0, receipt["readerWorkers"])

    def test_output_overflow_terminates_exact_child_and_closes_reader(self):
        with self.assertRaises(io.CaptureError) as error:
            io.capture([sys.executable, "-c", "import sys,time;sys.stdout.write('x'*4096);sys.stdout.flush();time.sleep(60)"],
                       env=dict(os.environ), timeout=3, max_bytes=32)
        receipt = error.exception.receipt
        RECEIPTS.append(receipt)
        self.assertTrue(receipt["exited"] and receipt["readerClosed"])
        self.assertLessEqual(receipt["capturedBytes"], 32)

    def test_timeout_terminates_exact_child_and_closes_reader(self):
        with self.assertRaises(io.CaptureError) as error:
            io.capture([sys.executable, "-c", "import time;time.sleep(60)"], env=dict(os.environ), timeout=0.2)
        receipt = error.exception.receipt
        RECEIPTS.append(receipt)
        self.assertTrue(receipt["exited"] and receipt["readerClosed"])

    def test_actual_child_failed_reap_keeps_handle_and_reader_until_retry(self):
        retained = []
        def register(resource):
            retained.append(resource)
        try:
            with patch.object(io.subprocess.Popen, "terminate", side_effect=OSError("Injected terminate failure")), patch.object(io.subprocess.Popen, "kill", side_effect=OSError("Injected kill failure")), patch.object(io.subprocess.Popen, "wait", side_effect=io.subprocess.TimeoutExpired("owned", 3)):
                with self.assertRaises(io.CaptureError) as failure:
                    io.capture([sys.executable, "-c", "import time;time.sleep(60)"], env=dict(os.environ), timeout=0.1, register=register)
                resource = failure.exception.resource
                self.assertIs(resource, retained[0])
                self.assertFalse(resource.receipt["exited"])
                self.assertFalse(resource.receipt["readerClosed"])
                self.assertFalse(resource.receipt["settled"])
                self.assertIn("actualExecutable", resource.receipt)
        finally:
            for resource in retained:
                self.assertTrue(resource.settle())
                RECEIPTS.append(resource.receipt)
        self.assertTrue(resource.receipt["exited"] and resource.receipt["readerClosed"])

    def test_failed_birth_observation_still_retains_and_reaps_actual_child(self):
        retained = []
        with patch.object(io, "creation_identity", side_effect=OSError("Injected birth observation failure")), self.assertRaises(io.CaptureError) as failure:
            io.capture([sys.executable, "-c", "import time;time.sleep(60)"], env=dict(os.environ), register=retained.append)
        self.assertIs(failure.exception.resource, retained[0])
        self.assertTrue(retained[0].receipt["settled"])
        RECEIPTS.append(retained[0].receipt)

    def test_actual_child_wait_failure_keeps_owner_until_exact_retry(self):
        retained = []
        try:
            with patch.object(io.subprocess.Popen, "terminate", return_value=None), patch.object(io.subprocess.Popen, "kill", return_value=None), patch.object(io.subprocess.Popen, "wait", side_effect=io.subprocess.TimeoutExpired("owned", 3)):
                with self.assertRaises(io.CaptureError) as failure:
                    io.capture([sys.executable, "-c", "import time;time.sleep(60)"], env=dict(os.environ), timeout=0.1, register=retained.append)
                self.assertIs(failure.exception.resource, retained[0])
                self.assertIn("TimeoutExpired", retained[0].receipt["settlementErrors"])
                self.assertFalse(retained[0].receipt["settled"])
        finally:
            for resource in retained:
                self.assertTrue(resource.settle())
                RECEIPTS.append(resource.receipt)

    def test_actual_child_metadata_failure_is_registered_before_resolution(self):
        retained = []
        with patch.object(io.shutil, "which", side_effect=OSError("Injected metadata resolution failure")), self.assertRaises(io.CaptureError) as failure:
            io.capture([sys.executable, "-c", "import time;time.sleep(60)"], env=dict(os.environ), register=retained.append)
        self.assertIs(failure.exception.resource, retained[0])
        self.assertTrue(retained[0].receipt["settled"])
        self.assertIn("actualExecutable", retained[0].receipt)
        RECEIPTS.append(retained[0].receipt)


def tearDownModule():
    target = os.environ.get("CREATION_IO_RESOURCE_LEDGER")
    if target:
        Path(target).write_text(json.dumps(RECEIPTS, indent=2))


if __name__ == "__main__":
    unittest.main()
