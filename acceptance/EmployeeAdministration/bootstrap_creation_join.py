"""Hosted-only finite control-service bootstrap; source-only CI never invokes it."""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import sys
import signal
import time
import uuid
import owned_creation_io as io
import run_creation_join as lane


class Bootstrap:
    def __init__(self, root, evidence):
        self.root = root.resolve(strict=True)
        self.run = uuid.uuid4().hex
        self.evidence = evidence.resolve() / ("bootstrap-" + self.run)
        self.evidence.mkdir(parents=True, mode=0o700)
        self.unit = "codex-creation-supervisor-" + self.run + ".service"
        self.fence = uuid.uuid4().hex
        self.group = "/system.slice/" + self.unit
        self.children = []
        self.records = {"run": self.run, "unit": self.unit, "description": "CodexCreationControl:" + self.fence,
                        "dispatchIntent": False, "generation": None, "identity": None, "controlProcesses": [],
                        "releaseVerified": False, "expiresUtc": (dt.datetime.now(dt.timezone.utc) + dt.timedelta(minutes=30)).isoformat()}
        self.failed = False
        self.save()

    def save(self):
        lane.write_durable_receipt(self.evidence / "ownership.json", json.dumps(self.records, indent=2))

    def register(self, resource):
        self.children.append(resource)
        self.records["controlProcesses"].append(resource.receipt)
        self.save()

    def settle(self):
        return all([resource.receipt.get("settled") or resource.settle() for resource in self.children])

    def command(self, arguments):
        if not self.settle():
            raise RuntimeError("Bootstrap control helper unresolved")
        try:
            return io.capture(arguments, env=dict(os.environ), timeout=15, register=self.register)[0]
        finally:
            self.save()

    def state(self):
        return dict(line.split("=", 1) for line in self.command([
            "sudo", "-n", "systemctl", "show", self.unit, "-p", "LoadState", "-p", "Transient",
            "-p", "Description", "-p", "InvocationID", "-p", "ControlGroup", "-p", "ExecStart",
            "-p", "ExecMainPID", "-p", "ActiveState", "-p", "SubState", "-p", "Result", "-p", "ExecMainStatus",
        ]).splitlines() if "=" in line)

    def acquire(self):
        state = self.state()
        if state.get("LoadState") == "not-found":
            jobs = self.command(["sudo", "-n", "systemctl", "list-jobs", "--all", "--no-legend"])
            if any(self.unit in line.split() for line in jobs.splitlines()):
                raise RuntimeError("Bootstrap dispatch job still pending")
            self.quiescent(self.group)
            return None
        if state.get("Transient") != "yes" or state.get("Description") != self.records["description"]:
            raise RuntimeError("Bootstrap unit fence mismatch")
        if not state.get("ExecStart", "").startswith("{ path=" + str(Path(sys.executable).resolve()) + " ;"):
            raise RuntimeError("Bootstrap executable fence mismatch")
        generation = state.get("InvocationID")
        if not generation or self.records["generation"] not in (None, generation):
            raise RuntimeError("Bootstrap generation mismatch")
        self.records["generation"] = generation
        if state.get("ControlGroup") not in ("", self.group):
            raise RuntimeError("Bootstrap control cgroup mismatch")
        terminal = state.get("SubState") == "exited" or state.get("ActiveState") in ("inactive", "failed")
        if terminal:
            self.quiescent(self.group)
        else:
            identity = lane.process_identity(int(state["ExecMainPID"]))
            if identity["executable"] != str(Path(sys.executable).resolve()) or self.records["identity"] not in (None, identity):
                raise RuntimeError("Bootstrap process identity mismatch")
            if "0::" + self.group not in (Path("/proc") / str(identity["pid"]) / "cgroup").read_text().splitlines():
                raise RuntimeError("Bootstrap process escaped control service")
            self.records["identity"] = identity
        self.save()
        return state

    def quiescent(self, group):
        root = Path("/sys/fs/cgroup") / group.lstrip("/")
        if root.exists() and any(p.read_text().strip() for p in root.rglob("cgroup.procs")):
            raise RuntimeError("Bootstrap owned cgroup is not empty")

    def inspect_native_exit(self):
        scratch = self.evidence / "native" / self.run
        receipt = scratch / "ownership.json"
        if not receipt.exists():
            raise RuntimeError("Native ownership receipt unavailable; preserve evidence")
        value = json.loads(receipt.read_text())
        if value.get("run") != self.run or value.get("slice") != "creation" + self.run + ".slice":
            raise RuntimeError("Native ownership receipt mismatch")
        self.quiescent("/" + value["slice"])
        if (Path("/sys/class/net") / ("cj" + self.run[:12])).exists():
            raise RuntimeError("Native bridge remains; no release claim")
        if value.get("cleanupFailures") or any(not row.get("settled") for row in value.get("controlProcesses", [])):
            raise RuntimeError("Native cleanup receipt incomplete")
        if (scratch / "data").exists() or (scratch / "exec").exists():
            raise RuntimeError("Native private daemon directories retained; no release claim")
        self.records["nativeOwnershipSha256"] = hashlib.sha256(receipt.read_bytes()).hexdigest()

    def finish(self):
        # Encompass helpers created DURING final observations/stops as well.
        sticky = None
        while True:
            try:
                if not self.settle():
                    raise RuntimeError("Bootstrap reaping pending")
                if self.records["dispatchIntent"]:
                    state = self.acquire()
                    if state is not None:
                        self.command(["sudo", "-n", "systemctl", "stop", self.unit])
                        stopped = self.acquire()
                        if stopped is not None and stopped.get("ActiveState") != "inactive":
                            raise RuntimeError("Bootstrap service did not stop")
                        self.quiescent(self.group)
                    self.inspect_native_exit()
                if not self.settle():
                    raise RuntimeError("Bootstrap final helper pending")
            except BaseException as error:
                self.failed = True
                if any(not child.receipt.get("settled") for child in self.children):
                    sticky = sticky or error
                    try:
                        time.sleep(0.05)
                    except BaseException:
                        pass
                    continue
                raise
            if sticky is not None:
                raise RuntimeError("Bootstrap cleanup settled after uncertainty; proof invalid") from None
            self.records["releaseVerified"] = True
            self.save()
            return

    def execute(self, seal, seal_sha):
        lane.proof_reader.validate_source_seal(self.root, seal, seal_sha)
        arguments = ["sudo", "-n", "systemd-run", "--quiet", "--service-type=exec", "--unit=" + self.unit,
                     "--property=Description=" + self.records["description"], "--property=RuntimeMaxSec=26min",
                     "--property=TimeoutStopSec=20", "--property=KillMode=control-group", "--property=SendSIGKILL=yes",
                     "--property=Delegate=no", "--property=MemoryMax=128M", "--property=CPUQuota=100%",
                     "--property=RemainAfterExit=yes", "--property=User=" + str(os.getuid()),
                     "--property=Group=" + str(os.getgid()), "--working-directory=" + str(self.root)]
        values = {"MALIEV_CREATION_RUN": self.run, "MALIEV_CREATION_CONTROL_UNIT": self.unit,
                  "MALIEV_CREATION_CONTROL_FENCE": self.fence, "RUNNER_ENVIRONMENT": "github-hosted",
                  "MALIEV_CREATION_HOSTED_ADMISSION": "root-owned-sole-lane"}
        arguments += ["--setenv=" + key + "=" + value for key, value in values.items()]
        arguments += [str(Path(sys.executable).resolve()), "-B", str(self.root / "acceptance/EmployeeAdministration/run_creation_join.py"),
                      "--root", str(self.root), "--evidence", str(self.evidence / "native"),
                      "--source-seal", str(seal.resolve()), "--source-seal-sha256", seal_sha]
        try:
            self.records["dispatchIntent"] = True
            self.records["commandSha256"] = hashlib.sha256(json.dumps(arguments).encode()).hexdigest()
            self.save()
            self.command(arguments)
            deadline = time.monotonic() + 27 * 60
            while True:
                state = self.acquire()
                if state is None:
                    raise RuntimeError("Bootstrap service vanished before completion")
                if state.get("SubState") == "exited" or state.get("ActiveState") in ("inactive", "failed"):
                    if state.get("Result") != "success" or state.get("ExecMainStatus") != "0":
                        raise RuntimeError("Native supervisor failed or lease expired")
                    break
                if time.monotonic() >= deadline:
                    raise RuntimeError("Finite bootstrap observation lease expired")
                time.sleep(1)
        finally:
            self.finish()
        proof = self.evidence / "native" / self.run / "proof.json"
        if not proof.exists() or self.failed:
            raise RuntimeError("Native proof unavailable or bootstrap failed")


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--source-seal", type=Path, required=True)
    parser.add_argument("--source-seal-sha256", required=True)
    args = parser.parse_args()
    signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))
    available = int(next(line for line in Path("/proc/meminfo").read_text().splitlines() if line.startswith("MemAvailable:")).split()[1])
    lane.admission(os.environ, available)
    Bootstrap(args.root, args.evidence).execute(args.source_seal, args.source_seal_sha256)
