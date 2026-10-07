"""Actual finite Linux control-service smoke; never starts SDK/Docker/app hosts."""
import argparse
import json
import os
from pathlib import Path
import signal
import sys
import time
import bootstrap_creation_join as bootstrap
import run_creation_join as lane


def marker_write(path, value):
    temporary = path.with_suffix(".tmp")
    with temporary.open("w") as writer:
        writer.write(json.dumps(value))
        writer.flush()
        os.fsync(writer.fileno())
    temporary.replace(path)
    lane.sync_directory(path.parent)


def payload(marker, seconds):
    identity = lane.process_identity(os.getpid())
    identity["cgroup"] = Path("/proc/self/cgroup").read_text().strip()
    def stopped(*_):
        marker_write(marker.with_suffix(".exit.json"), {"pid": os.getpid(), "signal": "TERM"})
        raise SystemExit(0)
    signal.signal(signal.SIGTERM, stopped)
    marker_write(marker, identity)
    time.sleep(seconds)
    marker_write(marker.with_suffix(".exit.json"), {"pid": os.getpid(), "signal": "normal"})


class Smoke(bootstrap.Bootstrap):
    def __init__(self, root, evidence, scenario):
        super().__init__(root, evidence)
        self.scenario = scenario
        self.marker = self.evidence / "payload.json"

    def inspect_native_exit(self):
        # This specialized smoke allocates ONLY this finite control service.
        self.quiescent(self.group)
        if not self.marker.exists() or not self.marker.with_suffix(".exit.json").exists():
            raise RuntimeError("Actual payload start/exit markers missing")
        identity = json.loads(self.marker.read_text())
        if identity["cgroup"] != "0::" + self.group or identity["executable"] != str(Path(sys.executable).resolve()):
            raise RuntimeError("Actual payload ownership fence mismatch")
        try:
            current = lane.process_identity(identity["pid"])
        except FileNotFoundError:
            current = None
        if current is not None and all(current[key] == identity[key] for key in ("pid", "startTicks", "executable")):
            raise RuntimeError("Actual smoke payload still alive")
        exit_value = json.loads(self.marker.with_suffix(".exit.json").read_text())
        if exit_value["pid"] != identity["pid"]:
            raise RuntimeError("Actual exit marker identity mismatch")
        self.records["actualPayload"] = identity
        self.records["actualExit"] = exit_value

    def run_smoke(self):
        runtime = 3 if self.scenario == "expiry" else 30
        duration = 1 if self.scenario == "normal" else 25
        args = ["sudo", "-n", "systemd-run", "--quiet", "--service-type=exec", "--unit=" + self.unit,
                "--property=Description=" + self.records["description"], "--property=RuntimeMaxSec=" + str(runtime),
                "--property=TimeoutStopSec=5", "--property=KillMode=control-group", "--property=SendSIGKILL=yes",
                "--property=Delegate=no", "--property=MemoryMax=64M", "--property=CPUQuota=100%",
                "--property=RemainAfterExit=yes", "--property=User=" + str(os.getuid()),
                "--property=Group=" + str(os.getgid()), "--working-directory=" + str(self.root),
                str(Path(sys.executable).resolve()), "-B", str(Path(__file__).resolve()),
                "--payload", str(self.marker), "--seconds", str(duration)]
        terminal = None
        try:
            self.records["dispatchIntent"] = True
            self.save()
            self.command(args)
            deadline = time.monotonic() + 40
            while not self.marker.exists():
                if time.monotonic() >= deadline:
                    raise RuntimeError("Actual payload never started")
                time.sleep(0.05)
            if self.scenario == "post-dispatch-fault":
                raise RuntimeError("Injected after actual dispatch and payload start")
            if self.scenario == "cancel":
                self.acquire()  # Exact generation/process fence before stop.
                self.command(["sudo", "-n", "systemctl", "stop", self.unit])
            while True:
                terminal = self.acquire()
                if terminal is None or terminal.get("SubState") == "exited" or terminal.get("ActiveState") in ("inactive", "failed"):
                    break
                if time.monotonic() >= deadline:
                    raise RuntimeError("Finite actual smoke observation lease exhausted")
                time.sleep(0.1)
            if self.scenario == "expiry" and (terminal is None or terminal.get("Result") != "timeout"):
                raise RuntimeError("Actual systemd lease expiry missing")
            if self.scenario == "normal" and (terminal is None or terminal.get("Result") != "success" or terminal.get("ExecMainStatus") != "0"):
                raise RuntimeError("Actual normal payload failed")
        except RuntimeError as error:
            if self.scenario != "post-dispatch-fault" or str(error) != "Injected after actual dispatch and payload start":
                raise
        finally:
            self.finish()
        if not self.records["releaseVerified"]:
            raise RuntimeError("Actual smoke resources not released")
        return {"scenario": self.scenario, "scope": "finite-control-service-only", "generation": self.records["generation"],
                "actualProcess": self.records["actualPayload"], "actualExit": self.records["actualExit"],
                "systemdResult": terminal.get("Result") if terminal else None,
                "releaseVerified": True, "sdkStarted": False, "dockerStarted": False, "nativeCreationAcceptance": False}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--payload", type=Path)
    parser.add_argument("--seconds", type=float, default=1)
    parser.add_argument("--root", type=Path)
    parser.add_argument("--evidence", type=Path)
    args = parser.parse_args()
    if args.payload:
        payload(args.payload, args.seconds)
    else:
        signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))
        available = int(next(line for line in Path("/proc/meminfo").read_text().splitlines() if line.startswith("MemAvailable:")).split()[1])
        lane.admission(os.environ, available)
        results = [Smoke(args.root, args.evidence, scenario).run_smoke() for scenario in ("normal", "cancel", "expiry", "post-dispatch-fault")]
        (args.evidence / "control-smoke-proof.json").write_text(json.dumps(results, indent=2))
        print(json.dumps(results, indent=2))
