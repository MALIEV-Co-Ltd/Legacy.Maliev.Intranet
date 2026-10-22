"""Linux hosted-only creation join supervisor. Never uploads raw logs or TRX."""
import datetime as dt
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import time
import uuid
import verify_creation_join as proof_reader
import owned_creation_io as owned_io
import hashlib
import stat
import re
import sys

LABEL = "maliev.validation.employee-join"


def admission(env, available_kib):
    if env.get("RUNNER_ENVIRONMENT") != "github-hosted":
        raise RuntimeError("Dedicated hosted runner required")
    if env.get("MALIEV_CREATION_HOSTED_ADMISSION") != "root-owned-sole-lane":
        raise RuntimeError("Root sole-lane admission required")
    if available_kib < 4194304:
        raise RuntimeError("4096 MiB available-memory guard failed")


def owned_container(value, run):
    if value["Config"]["Labels"].get(LABEL) != run:
        raise RuntimeError("Container ownership label mismatch")
    for mount in value.get("Mounts", []):
        if mount["Type"] != "tmpfs":
            raise RuntimeError("Unexpected persistent/bind mount")
    ports = []
    for bindings in value.get("NetworkSettings", {}).get("Ports", {}).values():
        for binding in bindings or []:
            if binding["HostIp"] != "127.0.0.1":
                raise RuntimeError("Container forwarding is not loopback-only")
            ports.append(binding["HostPort"])
    return {
        "id": value["Id"], "created": value["Created"],
        "image": value["Config"]["Image"], "label": run,
        "mountTypes": [m["Type"] for m in value.get("Mounts", [])],
        "hostPorts": ports,
    }


def no_active_clients(ports, sockets):
    for line in sockets.splitlines():
        addresses = line.split()[-2:]
        if any(address.rsplit(":", 1)[-1] in ports for address in addresses):
            raise RuntimeError("Active local client; preserve forwarding")


def process_identity(pid):
    root = Path("/proc") / str(pid)
    stat = (root / "stat").read_text()
    return {"pid": pid, "startTicks": stat[stat.rfind(")") + 2:].split()[19],
            "executable": str((root / "exe").resolve(strict=True))}


def verify_lineage(lineage, owned_slice, leaf):
    if "0::/" + owned_slice + "/" + leaf not in lineage.splitlines():
        raise RuntimeError("Process outside exact owned cgroup v2 lineage")


def sync_directory(path):
    if os.name == "nt":
        # Windows tests model ownership only; native CLI requires Linux /proc/cgroup v2.
        return False
    fd = os.open(path, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)
    return True


def write_durable_receipt(path, payload):
    temporary = path.with_name("ownership.tmp")
    with temporary.open("w") as writer:
        writer.write(payload)
        writer.flush()
        os.fsync(writer.fileno())
    # Retain the previous immutable inode before replacing the current receipt.
    if path.exists():
        backup = path.with_name("ownership.previous.tmp")
        if backup.exists():
            backup.unlink()
        os.link(path, backup)
        backup.replace(path.with_name("ownership.previous.json"))
        sync_directory(path.parent)
    temporary.replace(path)
    return sync_directory(path.parent)


def typed_uint64(value):
    if value.get("type") != "t" or type(value.get("data")) is not int or not 0 <= value["data"] <= 18446744073709551615:
        raise RuntimeError("Control service typed lease property mismatch")
    return value["data"]


def validate_control_lease(state, unit, fence, group, actual, monotonic):
    if state.get("Transient") != "yes" or state.get("Description") != "CodexCreationControl:" + fence:
        raise RuntimeError("Control service immutable fence mismatch")
    if not state.get("InvocationID") or state.get("ControlGroup") != group or state.get("ExecMainPID") != str(actual["pid"]):
        raise RuntimeError("Control service process/generation mismatch")
    if not state.get("ExecStart", "").startswith("{ path=" + actual["executable"] + " ;"):
        raise RuntimeError("Control service executable mismatch")
    duration = int(state.get("RuntimeMaxUSec", "0"))
    start = int(state.get("ExecMainStartTimestampMonotonic", "0"))
    if not 0 < duration <= 26 * 60 * 1000000 or not 0 < start <= monotonic * 1000000:
        raise RuntimeError("Control service finite lease missing")
    if (start + duration) / 1000000 - monotonic <= 120:
        raise RuntimeError("Control service cleanup reserve exhausted")
    if state.get("KillMode") != "control-group" or state.get("SendSIGKILL") != "yes" or state.get("Delegate") != "no":
        raise RuntimeError("Control service containment/expiry fence mismatch")
    if not 0 < int(state.get("TimeoutStopUSec", "0")) <= 20 * 1000000:
        raise RuntimeError("Control service finite stop lease missing")
    if not 0 < int(state.get("MemoryMax", "0")) <= 128 * 1024 * 1024:
        raise RuntimeError("Control service memory cap missing")


class Supervisor:
    def __init__(self, root, evidence):
        self.root = root.resolve()
        self.evidence = evidence.resolve()
        self.run = os.environ.get("MALIEV_CREATION_RUN", uuid.uuid4().hex)
        if not re.fullmatch(r"[0-9a-f]{32}", self.run):
            raise RuntimeError("Invalid unique creation run")
        self.scratch = self.evidence / self.run
        self.scratch.mkdir(parents=True, mode=0o700)
        self.socket = self.scratch / "docker.sock"
        self.slice = "creation" + self.run + ".slice"
        self.slice_started = False
        self.slice_intent = False
        self.bridge_intent = False
        self.slice_description = "CodexCreationSlice:" + self.run + ":" + uuid.uuid4().hex
        self.bridge_alias = "CodexCreationBridge:" + self.run + ":" + uuid.uuid4().hex
        self.slice_generation = None
        self.bridge = "cj" + self.run[:12]
        self.bridge_index = None
        self.units = {}
        self.streams = {}
        self.control_children = []
        self.control_group = None
        self.cleanup_only = False
        self.stream_overflow = False
        self.records = {"run": self.run, "units": [], "containers": [], "cleanupFailures": []}
        self.deadline = dt.datetime.now(dt.timezone.utc) + dt.timedelta(minutes=24)
        self.records["expiresUtc"] = self.deadline.isoformat()
        self.save()

    def register_control(self, resource):
        self.control_children.append(resource)
        self.records.setdefault("controlProcesses", []).append(resource.receipt)
        if self.control_group is not None and resource.process.poll() is None:
            lineage = (Path("/proc") / str(resource.process.pid) / "cgroup").read_text().splitlines()
            if "0::" + self.control_group not in lineage:
                raise RuntimeError("Control helper escaped finite owner service")
            resource.receipt["controlGroup"] = self.control_group
        self.save()

    def control_barrier(self):
        for resource in self.control_children:
            if not resource.receipt.get("settled") and not resource.settle():
                self.save()
                raise RuntimeError("Owned control process unresolved; preserve all native resources")
        historical = [row for row in self.records["cleanupFailures"] if "controlBarrier" in row]
        if historical:
            self.records.setdefault("resolvedCleanupFailures", []).extend(historical)
            self.records["cleanupFailures"] = [row for row in self.records["cleanupFailures"] if "controlBarrier" not in row]
        self.save()

    def command(self, args, *, docker=False):
        self.control_barrier()
        env = dict(os.environ)
        if docker:
            for key in ("DOCKER_CONTEXT", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH"):
                env.pop(key, None)
            env["DOCKER_CONFIG"] = str(self.scratch / "docker-config")
            env["DOCKER_HOST"] = "unix://" + str(self.socket)
        try:
            text, receipt = owned_io.capture(args, env=env, timeout=15, drain=self.drain_streams, register=self.register_control)
        except owned_io.CaptureError as failure:
            self.save()
            if failure.receipt.get("interrupted"):
                raise KeyboardInterrupt() from None
            raise
        self.save()
        return text

    def drain_streams(self):
        deadline = time.monotonic() + 0.01
        for stream in self.streams.values():
            if stream["fd"] is None or stream["file"] is None:
                continue
            # Bound both discarded bytes and elapsed time so capture can enforce its lease.
            for _ in range(8):
                if time.monotonic() >= deadline:
                    return
                try:
                    chunk = os.read(stream["fd"], 16384)
                except BlockingIOError:
                    break
                if not chunk:
                    break
                remaining = 8 * 1024 * 1024 - stream["bytes"]
                if len(chunk) > remaining:
                    self.stream_overflow = True
                keep = chunk[:max(0, remaining)]
                stream["file"].write(keep)
                stream["bytes"] += len(keep)

    def acquire(self, unit):
        requested = self.units[unit]
        deadline = time.monotonic() + 45
        while True:
            try:
                state = self.state(unit)
                if state.get("LoadState") == "not-found":
                    jobs = self.command(["sudo", "-n", "systemctl", "list-jobs", "--all", "--no-legend"])
                    if any(unit in line.split() for line in jobs.splitlines()):
                        raise RuntimeError("Owned start job still pending")
                    requested_group = Path("/sys/fs/cgroup") / self.slice / unit
                    if requested_group.exists() and any(p.read_text().strip() for p in requested_group.rglob("cgroup.procs")):
                        raise RuntimeError("Owned unit processes still pending")
                    return None
                if state.get("Transient") != "yes" or state.get("Description") != requested["description"]:
                    raise ValueError("Immutable unit acquisition fence mismatch")
                if not state.get("ExecStart", "").startswith("{ path=" + requested["executable"] + " ;"):
                    raise ValueError("Unit executable fence mismatch")
                generation = state["InvocationID"]
                if requested["generation"] is not None and requested["generation"] != generation:
                    raise ValueError("Unit generation changed")
                requested["generation"] = generation
                if state["ControlGroup"] and state["ControlGroup"] != requested["cgroup"]:
                    raise ValueError("Observed cgroup fence mismatch")
                pid = int(state["ExecMainPID"])
                terminal = state.get("SubState") == "exited" or state.get("ActiveState") in ("inactive", "failed")
                if terminal:
                    self.quiescent(unit)
                elif pid:
                    identity = process_identity(pid)
                    if identity["executable"] != requested["executable"]:
                        raise ValueError("Observed executable fence mismatch")
                    if requested["identity"] is not None and requested["identity"] != identity:
                        raise ValueError("Observed process identity changed")
                    verify_lineage((Path("/proc") / str(pid) / "cgroup").read_text(), self.slice, unit)
                    requested["identity"] = identity
                self.save()
                return state
            except (RuntimeError, OSError):
                if time.monotonic() >= deadline:
                    raise RuntimeError("Owned partial-start recovery lease exhausted")
                time.sleep(0.05)

    def save(self):
        self.records["ownedUnits"] = self.units
        self.records["root"] = str(self.root)
        self.records["slice"] = self.slice
        self.records["sliceStarted"] = self.slice_started
        self.records["sliceIntent"] = self.slice_intent
        self.records["bridgeIntent"] = self.bridge_intent
        self.records["sliceDescription"] = self.slice_description
        self.records["sliceGeneration"] = self.slice_generation
        self.records["bridgeAlias"] = self.bridge_alias
        self.records["bridgeIndex"] = self.bridge_index
        write_durable_receipt(self.scratch / "ownership.json", json.dumps(self.records, indent=2))

    def state(self, unit):
        output = self.command(["sudo", "-n", "systemctl", "show", unit,
                               "-p", "Transient", "-p", "ExecMainPID",
                               "-p", "ActiveState", "-p", "ExecMainStatus", "-p", "Result",
                               "-p", "InvocationID", "-p", "ControlGroup", "-p", "SubState",
                               "-p", "Description", "-p", "ExecStart", "-p", "LoadState"])
        return dict(line.split("=", 1) for line in output.splitlines() if "=" in line)

    def launch(self, phase, executable, arguments, memory):
        if self.cleanup_only:
            raise RuntimeError("Cleanup-only owner cannot launch native work")
        unit = "codex-creation-" + self.run + "-" + phase + ".service"
        seconds = int((self.deadline - dt.datetime.now(dt.timezone.utc)).total_seconds())
        if seconds <= 30:
            raise RuntimeError("Owned lease exhausted")
        executable = str(Path(executable).resolve())
        fence = uuid.uuid4().hex
        digest = hashlib.sha256(json.dumps([executable] + arguments).encode()).hexdigest()
        description = "CodexCreation:" + self.run + ":" + fence + ":" + digest
        fifo = self.scratch / (phase + ".fifo")
        os.mkfifo(fifo, 0o600)
        self.streams[unit] = {"fd": None, "bytes": 0, "file": None}
        self.streams[unit]["fd"] = os.open(fifo, os.O_RDWR | os.O_NONBLOCK)
        self.streams[unit]["file"] = (self.scratch / (phase + ".private.log")).open("wb")
        args = ["sudo", "-n", "systemd-run", "--quiet", "--unit=" + unit,
                "--service-type=exec", "--slice=" + self.slice, "--working-directory=" + str(self.root),
                "--property=RuntimeMaxSec=" + str(seconds),
                "--property=RemainAfterExit=yes",
                "--property=TimeoutStopSec=20", "--property=KillMode=control-group",
                "--property=MemoryMax=" + memory, "--property=CPUQuota=200%",
                "--property=Description=" + description,
                "--property=StandardOutput=file:" + str(fifo), "--property=StandardError=inherit"]
        if phase != "daemon":
            args += ["--property=User=" + str(os.getuid()), "--property=Group=" + str(os.getgid())]
        values = {
            "DOCKER_HOST": "unix://" + str(self.socket),
            "TESTCONTAINERS_RYUK_DISABLED": "true", "GITHUB_ACTIONS": "false",
            "MALIEV_EMPLOYEE_JOIN_RUN": self.run,
            "MALIEV_EMPLOYEE_JOIN_SUPERVISOR_RUN": self.run,
            "MALIEV_EMPLOYEE_JOIN_LEASE_UTC": self.deadline.isoformat(),
            "MalievWorkspaceRoot": str(self.root / ".dependencies"),
        }
        args += ["--setenv=" + k + "=" + v for k, v in values.items()]
        self.units[unit] = {"identity": None, "generation": None, "cgroup": "/" + self.slice + "/" + unit,
                            "description": description, "executable": executable, "commandSha256": digest}
        self.save()
        self.command(args + [executable] + arguments)
        if self.acquire(unit) is None:
            raise RuntimeError("Owned unit absent after dispatch")
        self.save()
        return unit

    def wait(self, unit):
        while True:
            state = self.acquire(unit)
            if state is None:
                raise RuntimeError("Owned unit vanished before phase completion")
            if self.stream_overflow:
                raise RuntimeError("Native private-log budget exceeded")
            if state["InvocationID"] != self.units[unit]["generation"]:
                raise RuntimeError("Unit generation changed")
            if unit.endswith("-test.service"):
                self.observe_containers()
            if state.get("SubState") == "exited" or state["ActiveState"] not in ("active", "activating", "deactivating"):
                if state["Result"] != "success" or state["ExecMainStatus"] != "0":
                    raise RuntimeError("Owned native phase failed")
                return
            if dt.datetime.now(dt.timezone.utc) >= self.deadline:
                raise RuntimeError("Supervisor lease expired")
            time.sleep(1)

    def observe_containers(self):
        for container_id in self.command(["docker", "ps", "-q"], docker=True).split():
            value = json.loads(self.command(["docker", "inspect", container_id], docker=True))[0]
            record = owned_container(value, self.run)
            pid = int(value["State"]["Pid"])
            if not pid:
                continue
            identity = process_identity(pid)
            lineage = (Path("/proc") / str(pid) / "cgroup").read_text()
            verify_lineage(lineage, self.slice, "docker-" + record["id"] + ".scope")
            if not any(row["id"] == record["id"] for row in self.records["containers"]):
                self.records["containers"].append({**record, "initProcess": identity, "cgroup": lineage.strip()})
                self.save()

    def quiescent(self, unit):
        group = self.units[unit]["cgroup"]
        if not group or not group.startswith("/" + self.slice + "/"):
            raise RuntimeError("Unit cgroup is not owned")
        root = Path("/sys/fs/cgroup") / group.lstrip("/")
        if root.exists() and any(p.read_text().strip() for p in root.rglob("cgroup.procs")):
            raise RuntimeError("SDK unit has remaining processes")

    def verify_daemon_socket(self):
        unit = next(name for name in self.units if name.endswith("-daemon.service"))
        if self.acquire(unit) is None:
            raise RuntimeError("Owned daemon vanished")
        identity = self.units[unit]["identity"]
        if identity is None or process_identity(identity["pid"]) != identity:
            raise RuntimeError("Owned daemon process changed")
        if self.socket.is_symlink():
            raise RuntimeError("Linked Docker socket")
        socket_stat = self.socket.stat()
        if not stat.S_ISSOCK(socket_stat.st_mode):
            raise RuntimeError("Docker endpoint is not a socket")
        rows = [line.split() for line in Path("/proc/net/unix").read_text().splitlines()[1:]]
        kernels = [row[6] for row in rows if len(row) == 8 and row[3] == "00010000" and row[4] == "0001" and row[7] == str(self.socket)]
        if len(kernels) != 1:
            raise RuntimeError("Ambiguous private Docker socket")
        fds = Path("/proc") / str(identity["pid"]) / "fd"
        if not any(os.readlink(fd) == "socket:[" + kernels[0] + "]" for fd in fds.iterdir()):
            raise RuntimeError("Private socket is not held by observed daemon")
        argv = (Path("/proc") / str(identity["pid"]) / "cmdline").read_bytes().rstrip(b"\0").decode().split("\0")
        argv[0] = str(Path(argv[0]).resolve())
        if hashlib.sha256(json.dumps(argv).encode()).hexdigest() != self.units[unit]["commandSha256"]:
            raise RuntimeError("Daemon immutable boot configuration changed")
        if "--cgroup-parent=" + self.slice not in argv:
            raise RuntimeError("Daemon is capable of allocating outside owned slice")
        self.records["socket"] = {"device": socket_stat.st_dev, "inode": socket_stat.st_ino,
                                  "kernelInode": kernels[0], "daemonPid": identity["pid"],
                                  "bootConfigSha256": self.units[unit]["commandSha256"]}
        self.save()

    def close_streams(self):
        failures = []
        for unit, stream in list(self.streams.items()):
            try:
                if stream["fd"] is not None:
                    os.close(stream["fd"])
                    stream["fd"] = None
                if stream["file"] is not None:
                    stream["file"].close()
                del self.streams[unit]
            except BaseException as error:
                failures.append({"stream": unit, "error": type(error).__name__})
        self.records["cleanupFailures"].extend(failures)

    def cleanup(self):
        try:
            self.control_barrier()
        except BaseException as error:
            self.records["cleanupFailures"].append({"controlBarrier": "unresolved", "error": type(error).__name__})
            self.close_streams()
            self.save()
            raise RuntimeError("Control reaping unverified; preserve all native resources") from None
        # Stop only unique units observed in this supervisor; daemon stops last.
        for unit in reversed(self.units):
            try:
                state = self.acquire(unit)
                if state is None:
                    continue
                if state["InvocationID"] != self.units[unit]["generation"]:
                    raise RuntimeError("Unit generation changed; preserve resource")
                pid = int(state["ExecMainPID"])
                terminal = state.get("SubState") == "exited" or state.get("ActiveState") in ("inactive", "failed")
                if terminal:
                    self.quiescent(unit)
                    pid = 0
                elif pid:
                    current = process_identity(pid)
                    if self.units[unit]["identity"] != current:
                        raise RuntimeError("PID/start-time ownership mismatch; preserve resource")
                if unit.endswith("-daemon.service"):
                    if self.records["cleanupFailures"]:
                        raise RuntimeError("SDK cleanup unverified; preserve backend")
                    for sdk_unit in self.units:
                        if not sdk_unit.endswith("-daemon.service"):
                            self.quiescent(sdk_unit)
                    self.observe_containers()
                    ids = self.command(["docker", "ps", "-aq"], docker=True).split()
                    for container_id in ids:
                        value = json.loads(self.command(["docker", "inspect", container_id], docker=True))[0]
                        record = owned_container(value, self.run)
                        self.records["containers"].append(record)
                        no_active_clients(record["hostPorts"], self.command(["ss", "-Htn", "state", "established"]))
                        self.command(["docker", "stop", "--time", "10", record["id"]], docker=True)
                        self.command(["docker", "rm", record["id"]], docker=True)
                    if self.command(["docker", "ps", "-aq"], docker=True):
                        raise RuntimeError("Owned containers remain")
                    if self.command(["docker", "volume", "ls", "-q"], docker=True):
                        raise RuntimeError("Persistent volume inventory; preserve private daemon data")
                self.command(["sudo", "-n", "systemctl", "stop", unit])
                self.quiescent(unit)
                if pid and (Path("/proc") / str(pid)).exists() and process_identity(pid) == current:
                    raise RuntimeError("Owned process did not exit")
            except Exception as error:
                self.records["cleanupFailures"].append({"unit": unit, "error": type(error).__name__})
        if self.slice_intent:
            try:
                state = self.state(self.slice)
                if state.get("LoadState") == "not-found":
                    jobs = self.command(["sudo", "-n", "systemctl", "list-jobs", "--all", "--no-legend"])
                    if any(self.slice in line.split() for line in jobs.splitlines()):
                        raise RuntimeError("Slice job remains unresolved")
                else:
                    if state.get("Transient") != "yes" or state.get("Description") != self.slice_description:
                        raise RuntimeError("Slice acquisition fence mismatch")
                    generation = state["InvocationID"]
                    if self.slice_generation is not None and generation != self.slice_generation:
                        raise RuntimeError("Slice generation changed")
                    self.slice_generation = generation
                    self.slice_started = True
            except Exception as error:
                self.records["cleanupFailures"].append({"slice": self.slice, "error": type(error).__name__})
        if self.bridge_intent:
            try:
                interface = Path("/sys/class/net") / self.bridge
                if interface.exists():
                    if (interface / "ifalias").read_text().strip() != self.bridge_alias:
                        raise RuntimeError("Bridge acquisition fence mismatch")
                    index = int((interface / "ifindex").read_text())
                    if self.bridge_index is not None and index != self.bridge_index:
                        raise RuntimeError("Bridge identity changed")
                    self.bridge_index = index
            except Exception as error:
                self.records["cleanupFailures"].append({"bridge": self.bridge, "error": type(error).__name__})
        if self.slice_started and not self.records["cleanupFailures"]:
            try:
                procs = Path("/sys/fs/cgroup") / self.slice
                if procs.exists() and any(p.read_text().strip() for p in procs.rglob("cgroup.procs")):
                    raise RuntimeError("Owned slice still has processes")
                self.command(["sudo", "-n", "systemctl", "stop", self.slice])
                stopped = self.state(self.slice)
                if stopped.get("LoadState") != "not-found" and stopped.get("ActiveState") != "inactive":
                    raise RuntimeError("Owned slice did not stop")
                if procs.exists() and any(p.read_text().strip() for p in procs.rglob("cgroup.procs")):
                    raise RuntimeError("Owned slice processes remain after stop")
            except Exception as error:
                self.records["cleanupFailures"].append({"slice": self.slice, "error": type(error).__name__})
        if self.bridge_index is not None and not self.records["cleanupFailures"]:
            try:
                interface = Path("/sys/class/net") / self.bridge
                if interface.exists():
                    if int((interface / "ifindex").read_text()) != self.bridge_index:
                        raise RuntimeError("Owned bridge identity changed")
                    if any((interface / "brif").iterdir()):
                        raise RuntimeError("Owned bridge still has attached endpoints")
                    self.command(["sudo", "-n", "ip", "link", "delete", self.bridge])
                    if interface.exists():
                        raise RuntimeError("Owned bridge remains")
            except Exception as error:
                self.records["cleanupFailures"].append({"bridge": self.bridge, "error": type(error).__name__})
        try:
            self.save()
        finally:
            self.close_streams()
        if self.records["cleanupFailures"]:
            raise RuntimeError("Cleanup incomplete; ownership evidence retained")
        for name in ("data", "exec"):
            disposable = self.scratch / name
            if disposable.exists():
                if disposable.resolve().parent != self.scratch.resolve():
                    raise RuntimeError("Private disposable directory escaped ownership root")
                shutil.rmtree(disposable)

    def start_bridge(self):
        if self.cleanup_only:
            raise RuntimeError("Cleanup-only owner cannot allocate a bridge")
        interface = Path("/sys/class/net") / self.bridge
        if interface.exists():
            raise RuntimeError("Existing bridge; preserve resource")
        self.bridge_intent = True
        self.save()
        self.command(["sudo", "-n", "ip", "link", "add", "name", self.bridge,
                      "alias", self.bridge_alias, "type", "bridge"])
        if (interface / "ifalias").read_text().strip() != self.bridge_alias:
            raise RuntimeError("Bridge acquisition fence mismatch")
        self.bridge_index = int((interface / "ifindex").read_text())
        self.records["bridge"] = {"name": self.bridge, "ifindex": self.bridge_index}
        self.save()

    def verify_control_lease(self):
        unit = os.environ.get("MALIEV_CREATION_CONTROL_UNIT", "")
        fence = os.environ.get("MALIEV_CREATION_CONTROL_FENCE", "")
        if not re.fullmatch(r"codex-creation-supervisor-[0-9a-f]{32}\.service", unit) or not re.fullmatch(r"[0-9a-f]{32}", fence):
            raise RuntimeError("Externally bounded control service required")
        group = "/system.slice/" + unit
        if "0::" + group not in Path("/proc/self/cgroup").read_text().splitlines():
            raise RuntimeError("Supervisor outside exact finite control service")
        self.control_group = group
        actual = process_identity(os.getpid())
        args = ["sudo", "-n", "systemctl", "show", unit]
        for key in ("Transient", "Description", "InvocationID", "ControlGroup", "ExecMainPID", "ExecStart",
                    "RuntimeMaxUSec", "ExecMainStartTimestampMonotonic", "KillMode", "SendSIGKILL",
                    "TimeoutStopUSec", "Delegate", "MemoryMax"):
            args.extend(["-p", key])
        text = self.command(args)
        state = dict(line.split("=", 1) for line in text.splitlines() if "=" in line)
        object_path = "/org/freedesktop/systemd1/unit/" + "".join(char if char.isascii() and char.isalnum() else "_" + format(ord(char), "02x") for char in unit)
        # systemctl formats durations for humans; DBus returns exact typed microseconds.
        for key in ("RuntimeMaxUSec", "ExecMainStartTimestampMonotonic", "TimeoutStopUSec", "MemoryMax"):
            value = json.loads(self.command(["sudo", "-n", "busctl", "--json=short", "get-property",
                                            "org.freedesktop.systemd1", object_path,
                                            "org.freedesktop.systemd1.Service", key]))
            state[key] = str(typed_uint64(value))
        validate_control_lease(state, unit, fence, group, actual, time.monotonic())
        remaining = (int(state["RuntimeMaxUSec"]) + int(state["ExecMainStartTimestampMonotonic"])) / 1000000 - time.monotonic()
        # Native work ends two minutes before the independently enforced control-service lease.
        self.deadline = min(self.deadline, dt.datetime.now(dt.timezone.utc) + dt.timedelta(seconds=remaining - 120))
        self.records["expiresUtc"] = self.deadline.isoformat()
        self.records["controlService"] = {"unit": unit, "generation": state["InvocationID"],
                                          "controlGroup": group, "process": actual,
                                          "runtimeMaxUSec": int(state["RuntimeMaxUSec"])}
        self.save()

    def recover_cleanup(self):
        self.cleanup_only = True
        sticky = None
        while True:
            try:
                self.records["cleanupOnly"] = True
                self.records["nativeLeaseExpired"] = dt.datetime.now(dt.timezone.utc) >= self.deadline
                try:
                    self.save()
                except BaseException:
                    pass
                unresolved = False
                for resource in self.control_children:
                    if not resource.receipt.get("settled") and not resource.settle():
                        unresolved = True
                if unresolved:
                    raise RuntimeError("Cleanup-only helper settlement pending")
                # Reobserve after uncertain prior control effects; keep historical failures.
                pending_failures = [row for row in self.records["cleanupFailures"] if row.get("error") == "CaptureError"]
                if pending_failures:
                    self.records.setdefault("resolvedControlAttempts", []).extend(pending_failures)
                    self.records["cleanupFailures"] = [row for row in self.records["cleanupFailures"] if row.get("error") != "CaptureError"]
                self.cleanup()
            except BaseException as error:
                # cleanup can create NEW helpers. Retain this owner if any is unknown.
                if any(not resource.receipt.get("settled") for resource in self.control_children):
                    sticky = sticky or error
                    self.records["cleanupAttemptFailed"] = True
                else:
                    raise
            else:
                if not any(not resource.receipt.get("settled") for resource in self.control_children):
                    if sticky is not None:
                        raise RuntimeError("Cleanup eventually settled after an uncertain attempt; native proof remains invalid") from None
                    return
            try:
                time.sleep(0.05)
            except BaseException:
                pass

    def execute(self, source_seal, source_seal_sha):
        if self.cleanup_only:
            raise RuntimeError("Cleanup-only owner cannot renew native work")
        proof_reader.validate_source_seal(self.root, source_seal, source_seal_sha)
        self.records["sourceSealSha256"] = source_seal_sha
        self.save()
        dockerd, dotnet = shutil.which("dockerd"), shutil.which("dotnet")
        if not dockerd or not dotnet:
            raise RuntimeError("Required hosted executables unavailable")
        project = "acceptance/EmployeeAdministration/EmployeeAdministration.AcceptanceTests.csproj"
        props = ["-p:UseLocalMalievDependencies=true", "-p:EnableEmployeeCreationSourceJoin=true"]
        try:
            self.verify_control_lease()
            self.slice_intent = True
            self.save()
            # StartTransientUnit applies the immutable ownership fence and cap atomically.
            self.command(["sudo", "-n", "busctl", "call", "org.freedesktop.systemd1",
                          "/org/freedesktop/systemd1", "org.freedesktop.systemd1.Manager",
                          "StartTransientUnit", "ssa(sv)a(sa(sv))", self.slice, "fail", "3",
                          "Description", "s", self.slice_description,
                          "MemoryMax", "t", str(3840 * 1024 * 1024),
                          "CPUQuotaPerSecUSec", "t", "2000000", "0"])
            state = self.state(self.slice)
            if state.get("Transient") != "yes" or state.get("Description") != self.slice_description:
                raise RuntimeError("Slice acquisition fence mismatch")
            self.slice_generation = state["InvocationID"]
            self.slice_started = True
            self.save()
            actual_cap = self.command(["sudo", "-n", "systemctl", "show", self.slice, "-p", "MemoryMax", "--value"])
            if actual_cap != str(3840 * 1024 * 1024):
                raise RuntimeError("Owned aggregate memory cap was not applied")
            self.records["aggregateMemoryBytes"] = int(actual_cap)
            self.start_bridge()
            (self.scratch / "daemon.json").write_text("{}")
            self.launch("daemon", dockerd, ["--host=unix://" + str(self.socket),
                "--config-file=" + str(self.scratch / "daemon.json"),
                "--data-root=" + str(self.scratch / "data"), "--exec-root=" + str(self.scratch / "exec"),
                "--pidfile=" + str(self.scratch / "dockerd.pid"), "--bridge=" + self.bridge,
                "--iptables=false", "--ip-forward=false", "--ip-masq=false",
                "--storage-driver=vfs", "--userland-proxy=true", "--exec-opt=native.cgroupdriver=systemd",
                "--cgroup-parent=" + self.slice], "512M")
            for _ in range(60):
                try:
                    info = json.loads(self.command(["docker", "info", "--format", "{{json .}}"], docker=True))
                    if info["DockerRootDir"] != str(self.scratch / "data") or info["CgroupDriver"] != "systemd" or str(info["CgroupVersion"]) != "2":
                        raise RuntimeError("Private daemon identity/cgroup mismatch")
                    self.records["daemon"] = {key: info[key] for key in ("ID", "DockerRootDir", "CgroupDriver", "CgroupVersion")}
                    self.verify_daemon_socket()
                    self.save()
                    break
                except RuntimeError:
                    time.sleep(1)
            else:
                raise RuntimeError("Private daemon startup failed")
            self.wait(self.launch("build", dotnet,
                ["build", project, "--configuration", "Release", "--warnaserror", "--nologo"] + props, "2048M"))
            self.wait(self.launch("test", dotnet,
                ["test", project, "--configuration", "Release", "--no-build", "--no-restore",
                 "--logger", "trx;LogFileName=creation.trx", "--results-directory",
                 str(self.scratch / "private-results"), "--nologo"] + props, "2048M"))
            if len(self.records["containers"]) != 2:
                raise RuntimeError("Actual private PostgreSQL/Redis ownership observations missing")
            reports = list((self.scratch / "private-results").glob("*.trx"))
            if len(reports) != 1:
                raise RuntimeError("Exactly one native TRX required")
            native_proof = proof_reader.validate(reports[0].read_bytes())
            proof_reader.validate_source_seal(self.root, source_seal, source_seal_sha)
        finally:
            self.recover_cleanup()
        native_proof["sourceSealSha256"] = source_seal_sha
        native_proof["ownershipSha256"] = proof_reader.hashlib.sha256((self.scratch / "ownership.json").read_bytes()).hexdigest()
        (self.scratch / "proof.json").write_text(json.dumps(native_proof, indent=2))


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--source-seal", type=Path, required=True)
    parser.add_argument("--source-seal-sha256", required=True)
    arguments = parser.parse_args()
    signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))
    available = int(next(line for line in Path("/proc/meminfo").read_text().splitlines()
                         if line.startswith("MemAvailable:")).split()[1])
    admission(os.environ, available)
    Supervisor(arguments.root, arguments.evidence).execute(arguments.source_seal, arguments.source_seal_sha256)
