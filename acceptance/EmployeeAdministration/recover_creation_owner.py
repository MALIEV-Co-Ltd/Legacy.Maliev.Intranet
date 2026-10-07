"""Cleanup-only reconstruction of an exact sealed creation ownership receipt."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import uuid
import run_creation_join as lane


class Recovery(lane.Supervisor):
    @classmethod
    def load(cls, root, evidence, run, expected_sha, creator_unit, creator_generation):
        if not re.fullmatch(r"[0-9a-f]{32}", run) or not re.fullmatch(r"[0-9a-f]{64}", expected_sha):
            raise RuntimeError("Invalid immutable recovery identity")
        evidence = evidence.resolve(strict=True)
        scratch = evidence / run
        path = scratch / "ownership.json"
        if scratch.is_symlink() or path.is_symlink() or scratch.resolve(strict=True).parent != evidence:
            raise RuntimeError("Linked or escaped ownership receipt")
        raw = path.read_bytes()
        if len(raw) > 8 * 1024 * 1024 or hashlib.sha256(raw).hexdigest() != expected_sha:
            raise RuntimeError("Ownership receipt hash or size mismatch")
        value = json.loads(raw)
        root = root.resolve(strict=True)
        if value.get("run") != run or value.get("root") != str(root) or value.get("slice") != "creation" + run + ".slice":
            raise RuntimeError("Ownership root/run/slice mismatch")
        creator = value.get("controlService", {})
        if creator.get("unit") != creator_unit or creator.get("generation") != creator_generation:
            raise RuntimeError("Original creator generation mismatch")
        if not re.fullmatch(r"codex-creation-supervisor-[0-9a-f]{32}\.service", creator_unit) or not re.fullmatch(r"[0-9a-f]{32}", creator_generation):
            raise RuntimeError("Invalid original creator identity")
        if creator.get("controlGroup") != "/system.slice/" + creator_unit:
            raise RuntimeError("Original creator cgroup mismatch")
        units = value.get("ownedUnits")
        if not isinstance(units, dict):
            raise RuntimeError("Owned unit inventory missing")
        for unit, record in units.items():
            if not re.fullmatch("codex-creation-" + run + r"-(daemon|build|test)\.service", unit):
                raise RuntimeError("Unexpected owned unit name")
            if record.get("cgroup") != "/" + value["slice"] + "/" + unit:
                raise RuntimeError("Owned unit lineage mismatch")
            if not re.fullmatch("CodexCreation:" + run + r":[0-9a-f]{32}:[0-9a-f]{64}", record.get("description", "")):
                raise RuntimeError("Owned unit acquisition fence missing")
            if record.get("description", "").rsplit(":", 1)[-1] != record.get("commandSha256"):
                raise RuntimeError("Owned immutable command seal mismatch")
            if not Path(record.get("executable", "")).is_absolute():
                raise RuntimeError("Owned executable identity missing")
            if record.get("generation") is not None and not re.fullmatch(r"[0-9a-f]{32}", record["generation"]):
                raise RuntimeError("Invalid owned invocation identity")
        owner = cls.__new__(cls)
        owner.root, owner.evidence, owner.run, owner.scratch = root, evidence, run, scratch
        owner.socket = scratch / "docker.sock"
        owner.slice = value["slice"]
        owner.slice_started, owner.slice_intent = bool(value.get("sliceStarted")), bool(value.get("sliceIntent"))
        owner.slice_description, owner.slice_generation = value.get("sliceDescription"), value.get("sliceGeneration")
        if owner.slice_intent and not re.fullmatch("CodexCreationSlice:" + run + r":[0-9a-f]{32}", owner.slice_description or ""):
            raise RuntimeError("Owned slice acquisition fence missing")
        owner.bridge = "cj" + run[:12]
        owner.bridge_index, owner.bridge_intent = value.get("bridgeIndex"), bool(value.get("bridgeIntent"))
        owner.bridge_alias = value.get("bridgeAlias")
        if owner.bridge_intent and not re.fullmatch("CodexCreationBridge:" + run + r":[0-9a-f]{32}", owner.bridge_alias or ""):
            raise RuntimeError("Owned bridge acquisition fence missing")
        owner.units, owner.streams, owner.stream_overflow = units, {}, False
        owner.control_children, owner.control_group, owner.cleanup_only = [], None, True
        owner.records = value
        owner.records["expiredControlService"] = dict(creator)
        owner.records["recoveryReceiptSha256"] = expected_sha
        owner.records["historicalCleanupFailures"] = list(value.get("cleanupFailures", []))
        owner.records["cleanupFailures"] = []
        owner.records["expiredControlProcesses"] = list(value.get("controlProcesses", []))
        owner.records["controlProcesses"] = []
        owner.deadline = dt.datetime.fromisoformat(value["expiresUtc"])
        owner.creator_verified_empty = False
        owner.recovery_evidence = evidence / ("cleanup-owner-" + uuid.uuid4().hex)
        owner.recovery_evidence.mkdir(mode=0o700)
        snapshot = owner.recovery_evidence / ("ownership.creator-" + expected_sha + ".json")
        if snapshot.exists():
            if snapshot.is_symlink() or hashlib.sha256(snapshot.read_bytes()).hexdigest() != expected_sha:
                raise RuntimeError("Original immutable receipt preservation mismatch")
        else:
            with snapshot.open("xb") as writer:
                writer.write(raw)
                writer.flush()
                os.fsync(writer.fileno())
            lane.sync_directory(owner.recovery_evidence)
        return owner

    def save(self):
        # Admission, observations and failed recovery never rewrite the creator ledger.
        self.records["ownedUnits"] = self.units
        self.records["root"] = str(self.root)
        self.records["slice"] = self.slice
        self.records["sliceStarted"] = self.slice_started
        self.records["sliceIntent"] = self.slice_intent
        self.records["sliceDescription"] = self.slice_description
        self.records["sliceGeneration"] = self.slice_generation
        self.records["bridgeIndex"] = self.bridge_index
        self.records["bridgeIntent"] = self.bridge_intent
        self.records["bridgeAlias"] = self.bridge_alias
        lane.write_durable_receipt(self.recovery_evidence / "ownership.json", json.dumps(self.records, indent=2))

    def publish_recovered_receipt(self):
        if not self.creator_verified_empty or not self.records.get("cleanupOnlyRecoveryVerified"):
            raise RuntimeError("Unverified recovery cannot replace original creator receipt")
        self.verify_expired_creator()
        original = self.scratch / "ownership.json"
        if original.is_symlink() or hashlib.sha256(original.read_bytes()).hexdigest() != self.records["recoveryReceiptSha256"]:
            raise RuntimeError("Original creator receipt changed during recovery; preserve both owners")
        lane.write_durable_receipt(original, json.dumps(self.records, indent=2))

    def verify_expired_creator(self):
        creator = self.records["expiredControlService"]
        text = self.command(["sudo", "-n", "systemctl", "show", creator["unit"],
                             "-p", "LoadState", "-p", "Transient", "-p", "InvocationID",
                             "-p", "ControlGroup", "-p", "ActiveState"])
        state = dict(line.split("=", 1) for line in text.splitlines() if "=" in line)
        if state.get("LoadState") == "not-found":
            jobs = self.command(["sudo", "-n", "systemctl", "list-jobs", "--all", "--no-legend"])
            if any(creator["unit"] in line.split() for line in jobs.splitlines()):
                raise RuntimeError("Original creator has a pending job")
        elif state.get("Transient") != "yes" or state.get("InvocationID") != creator["generation"] or state.get("ActiveState") not in ("inactive", "failed"):
            raise RuntimeError("Original creator is live or generation changed")
        if state.get("ControlGroup", "") not in ("", creator["controlGroup"]):
            raise RuntimeError("Original creator control lineage changed")
        group = Path("/sys/fs/cgroup") / creator["controlGroup"].lstrip("/")
        if group.exists() and any(p.read_text().strip() for p in group.rglob("cgroup.procs")):
            raise RuntimeError("Original creator helpers remain; preserve native resources")
        self.creator_verified_empty = True
        self.records["expiredCreatorQuiescenceVerified"] = True
        self.save()

    def observe_containers(self):
        self.verify_daemon_socket()
        super().observe_containers()

    def cleanup(self):
        if not self.creator_verified_empty:
            self.control_barrier()
            raise RuntimeError("Original creator expiry unverified; native cleanup prohibited")
        super().cleanup()

    def recover(self):
        try:
            self.verify_control_lease()
            self.verify_expired_creator()
        finally:
            self.recover_cleanup()
        self.records["cleanupOnlyRecoveryVerified"] = True
        self.save()
        self.publish_recovered_receipt()
        proof = {"scope": "exact-owner-cleanup-only", "run": self.run,
                 "originalGeneration": self.records["expiredControlService"]["generation"],
                 "receiptSha256": self.records["recoveryReceiptSha256"],
                 "cleanupVerified": True, "nativeCreationAcceptance": False}
        (self.scratch / "recovery-proof.json").write_text(json.dumps(proof, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    for name in ("root", "evidence", "source-seal"):
        parser.add_argument("--" + name, type=Path, required=True)
    for name in ("run", "receipt-sha256", "creator-unit", "creator-generation", "source-seal-sha256"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    if os.geteuid() != 0:
        raise RuntimeError("Privileged finite recovery control service required")
    signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))
    available = int(next(line for line in Path("/proc/meminfo").read_text().splitlines() if line.startswith("MemAvailable:")).split()[1])
    lane.admission(os.environ, available)
    lane.proof_reader.validate_source_seal(args.root, args.source_seal, args.source_seal_sha256)
    Recovery.load(args.root, args.evidence, args.run, args.receipt_sha256, args.creator_unit, args.creator_generation).recover()
