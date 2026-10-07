"""Bounded owned-child capture; no reader workers or unbounded communicate()."""
import ctypes
import datetime as dt
import os
from pathlib import Path
import subprocess
import shutil
import time


class CaptureError(RuntimeError):
    def __init__(self, reason, receipt, resource):
        super().__init__(reason)
        self.receipt = receipt
        self.resource = resource


def creation_identity(process):
    if os.name == "nt":
        from ctypes import wintypes
        values = [wintypes.FILETIME() for _ in range(4)]
        query = ctypes.windll.kernel32.GetProcessTimes
        query.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(wintypes.FILETIME)] * 4
        query.restype = wintypes.BOOL
        if not query(int(process._handle), *(ctypes.byref(v) for v in values)):
            raise OSError("Cannot observe owned process creation")
        ticks = (values[0].dwHighDateTime << 32) | values[0].dwLowDateTime
        buffer = ctypes.create_unicode_buffer(32768)
        length = wintypes.DWORD(len(buffer))
        image_query = ctypes.windll.kernel32.QueryFullProcessImageNameW
        image_query.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
        image_query.restype = wintypes.BOOL
        if not image_query(int(process._handle), 0, buffer, ctypes.byref(length)):
            raise OSError("Cannot observe owned process executable")
        return {"pid": process.pid, "creationFileTime": ticks, "actualExecutable": buffer.value}
    raw = (Path("/proc") / str(process.pid) / "stat").read_text()
    identity = {"pid": process.pid, "startTicks": raw[raw.rfind(")") + 2:].split()[19]}
    try:
        identity["actualExecutable"] = str((Path("/proc") / str(process.pid) / "exe").resolve(strict=True))
    except FileNotFoundError:
        # A zombie retains its birth record but no executable link. The retained
        # Popen handle must independently prove exit; never infer a live identity.
        deadline = time.monotonic() + 0.25
        code = process.poll()
        while code is None and time.monotonic() < deadline:
            time.sleep(0.005)
            code = process.poll()
        if code is None:
            raise
        identity.update(actualExecutable=None, identityObservedAfterExit=True,
                        terminalReturnCode=code)
    return identity


class OwnedChild:
    """Retains the exact Popen handle and reader until exit and closure are proven."""
    def __init__(self, process, receipt):
        self.process = process
        self.receipt = receipt

    def settle(self):
        process, receipt = self.process, self.receipt
        errors = []
        try:
            live = process.poll() is None
        except BaseException as error:
            live = True
            errors.append(type(error).__name__)
        if live:
            for action in (process.terminate, process.kill):
                try:
                    action()
                    process.wait(timeout=3)
                except BaseException as error:
                    errors.append(type(error).__name__)
                try:
                    if process.poll() is not None:
                        break
                except BaseException as error:
                    errors.append(type(error).__name__)
        try:
            receipt["exited"] = process.poll() is not None
        except BaseException as error:
            receipt["exited"] = False
            errors.append(type(error).__name__)
        # A live/unknown child keeps its reader and retained process handle.
        if receipt["exited"]:
            try:
                process.stdout.close()
            except BaseException as error:
                errors.append(type(error).__name__)
        receipt["readerClosed"] = process.stdout.closed
        receipt["settlementErrors"] = errors
        receipt["settled"] = receipt["exited"] and receipt["readerClosed"]
        return receipt["settled"]


def capture(arguments, *, env, timeout=15, max_bytes=262144, drain=lambda: None, register=lambda resource: None):
    started = time.monotonic()
    process = subprocess.Popen(arguments, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env)
    receipt = {"pid": process.pid, "executable": None,
               "purpose": "owned bounded control command", "maxBytes": max_bytes, "timeoutSeconds": timeout,
               "readerWorkers": 0, "exited": False, "readerClosed": False, "settled": False}
    resource = OwnedChild(process, receipt)
    data = bytearray()
    failure = None
    try:
        # Register the retained handle before any fallible identity/read operation.
        receipt["stage"] = "register-owner"
        register(resource)
        receipt["stage"] = "creation-identity"
        receipt.update(creation_identity(process))
        receipt["stage"] = "executable-metadata"
        receipt["executable"] = str(Path(shutil.which(arguments[0]) or arguments[0]).resolve())
        receipt["stage"] = "bounded-reader"
        os.set_blocking(process.stdout.fileno(), False)
        eof = False
        while not eof:
            drain()
            if time.monotonic() - started >= timeout:
                raise TimeoutError("Owned control command timed out")
            try:
                chunk = os.read(process.stdout.fileno(), 16384)
                if chunk:
                    if len(data) + len(chunk) > max_bytes:
                        raise ValueError("Owned command output budget exceeded")
                    data.extend(chunk)
                else:
                    eof = True
            except BlockingIOError:
                time.sleep(0.01)
        code = process.wait(timeout=max(0.01, timeout - (time.monotonic() - started)))
        receipt["stage"] = "exit-result"
        receipt["returnCode"] = code
        if code != 0:
            raise RuntimeError("Owned control command failed")
        result = data.decode("utf-8", errors="strict").strip()
    except BaseException as error:
        failure = error
    finally:
        receipt["capturedBytes"] = len(data)
        resource.settle()
    if failure is not None or not receipt["settled"]:
        receipt["interrupted"] = isinstance(failure, KeyboardInterrupt)
        raise CaptureError(type(failure).__name__ if failure else "UnsettledChild", receipt, resource) from None
    return result, receipt
