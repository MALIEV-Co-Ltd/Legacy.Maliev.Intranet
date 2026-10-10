"""Bounded lock artifact custody; this helper never executes an SDK or pushes."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

DEFAULTS = "4517cf16f5f1159318e184969732d46eae4a8308"
CONTRACTS = "78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7"
OUTPUTS = (
    "build/nuget-locks/Legacy.Maliev.ServiceDefaults/packages.lock.json",
    "Legacy.Maliev.Intranet/packages.lock.json",
    "Legacy.Maliev.Intranet.Bff/packages.lock.json",
)
PRODUCER_LOCK = ".dependencies/Legacy.Maliev.ServiceDefaults/src/Legacy.Maliev.ServiceDefaults/packages.lock.json"
CONTRACT_LOCK = ".dependencies/Legacy.Maliev.CompatibilityContracts/src/Legacy.Maliev.CompatibilityContracts/packages.lock.json"
RELATED = {
    "MassTransit", "MassTransit.Abstractions", "MassTransit.RabbitMQ", "Scalar.AspNetCore",
    "Microsoft.AspNet.WebApi.Client", "Newtonsoft.Json", "Newtonsoft.Json.Bson",
    "System.Memory", "System.Threading.Tasks.Extensions", "legacy.maliev.servicedefaults",
}
PINS = {
    "MassTransit": "9.2.3", "MassTransit.Abstractions": "9.2.3",
    "MassTransit.RabbitMQ": "9.2.3", "Scalar.AspNetCore": "2.17.13",
    "Microsoft.AspNet.WebApi.Client": "6.0.0",
}
POLICY = json.loads(Path(__file__).with_name("intranet-lock-policy.json").read_text())
PROJECT_POLICY = json.loads(Path(__file__).with_name("intranet-qualified-project.json").read_text())


def check_owner(path):
    if json.loads(read(path / "owner.json")) != owner():
        raise ValueError("Foreign custody ownership")


def read(path):
    if path.is_symlink() or not path.is_file() or not 0 < path.stat().st_size <= 4 * 1024 * 1024:
        raise ValueError("Invalid bounded artifact")
    return path.read_bytes()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def owner():
    values = {key: os.environ.get(key, "") for key in
              ("GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "EXPECTED_SOURCE_SHA")}
    if not values["GITHUB_RUN_ID"].isdigit() or not values["GITHUB_RUN_ATTEMPT"].isdigit():
        raise ValueError("Missing finite hosted run ownership")
    if len(values["EXPECTED_SOURCE_SHA"]) != 40:
        raise ValueError("Missing source ownership")
    return values


def cleanup_owned(path):
    if path.is_symlink():
        raise ValueError("Invalid cleanup ownership")
    if path.exists():
        if json.loads(read(path / "owner.json")) != owner():
            raise ValueError("Foreign cleanup ownership")
        shutil.rmtree(path)


def lock(data):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("Duplicate lock key")
            result[key] = value
        return result
    result = json.loads(data, object_pairs_hook=unique)
    if set(result) != {"version", "dependencies"} or result["version"] != 1:
        raise ValueError("Unexpected lock schema")
    if not isinstance(result["dependencies"], dict) or not result["dependencies"]:
        raise ValueError("Missing dependency graph")
    return result


def validate_delta(before, after):
    old, new = lock(before)["dependencies"], lock(after)["dependencies"]
    if set(old) != set(new):
        raise ValueError("Framework drift")
    changes = []
    for framework in old:
        for name in sorted(set(old[framework]) | set(new[framework])):
            previous, candidate = old[framework].get(name), new[framework].get(name)
            if previous == candidate:
                continue
            if name not in RELATED or candidate is None:
                raise ValueError("Unrelated dependency drift: " + name)
            if name == "legacy.maliev.servicedefaults":
                if set(candidate) != {"type", "dependencies"} or candidate.get("type") != "Project":
                    raise ValueError("Unexpected exact Project schema")
                if candidate["dependencies"] != PROJECT_POLICY["dependencies"]:
                    raise ValueError("Unrelated project dependency edge closure")
                changes.append({"framework": framework, "package": name, "before": previous, "after": candidate})
                continue
            if name in POLICY:
                expected = POLICY[name]
                if set(candidate) - {"type", "requested", "resolved", "contentHash", "dependencies"}:
                    raise ValueError("Unexpected package schema")
                if candidate.get("resolved") != expected["resolved"]:
                    raise ValueError("Unexpected qualified package version: " + name)
                if candidate.get("contentHash") != expected["contentHash"]:
                    raise ValueError("Unexpected package content hash: " + name)
                if candidate.get("dependencies", {}) != expected["dependencies"]:
                    raise ValueError("Unexpected exact package dependency edges: " + name)
                if candidate.get("type") not in ("Direct", "Transitive"):
                    raise ValueError("Invalid package type")
                if candidate.get("type") == "Direct" and candidate.get("requested") != "[" + expected["resolved"] + ", )":
                    raise ValueError("Unexpected requested package version")
            if name in PINS and candidate.get("resolved") != PINS[name]:
                raise ValueError("Unexpected qualified package version: " + name)
            if name not in PINS and name != "legacy.maliev.servicedefaults" and previous is not None:
                if previous.get("resolved") != candidate.get("resolved"):
                    raise ValueError("Unrelated existing transitive version change: " + name)
            previous_dependencies = (previous or {}).get("dependencies", {})
            candidate_dependencies = candidate.get("dependencies", {})
            for child in set(previous_dependencies) | set(candidate_dependencies):
                if name not in POLICY and previous_dependencies.get(child) != candidate_dependencies.get(child) and child not in RELATED:
                    raise ValueError("Unrelated dependency edge: " + child)
                if name == "legacy.maliev.servicedefaults" and child in POLICY:
                    value = candidate_dependencies.get(child)
                    if value not in (POLICY[child]["resolved"], "[" + POLICY[child]["resolved"] + ", )"):
                        raise ValueError("Unexpected project package edge version")
            changes.append({"framework": framework, "package": name, "before": previous, "after": candidate})
    return changes


def git(root, *args):
    return subprocess.check_output(["git", "-C", str(root), *args], timeout=15).decode().strip()


def identity(root):
    expected = os.environ.get("EXPECTED_SOURCE_SHA", "")
    if len(expected) != 40 or any(c not in "0123456789abcdef" for c in expected):
        raise ValueError("Missing exact proposed source")
    if git(root, "rev-parse", "HEAD") != expected:
        raise ValueError("Source identity mismatch")
    for name, sha in (("Legacy.Maliev.ServiceDefaults", DEFAULTS), ("Legacy.Maliev.CompatibilityContracts", CONTRACTS)):
        if git(root / ".dependencies" / name, "rev-parse", "HEAD") != sha:
            raise ValueError("Dependency identity mismatch")
    return expected


def inventory(root):
    paths = set(git(root, "ls-files").splitlines())
    for name in ("Legacy.Maliev.ServiceDefaults", "Legacy.Maliev.CompatibilityContracts"):
        dep = root / ".dependencies" / name
        paths.update(".dependencies/" + name + "/" + p for p in git(dep, "ls-files").splitlines())
    for p in root.rglob("packages.lock.json"):
        relative = p.relative_to(root)
        if "obj" not in relative.parts and "bin" not in relative.parts and ".git" not in relative.parts:
            paths.add(relative.as_posix())
    return {p: digest(read(root / p)) for p in sorted(paths)
            if p.endswith((".csproj", ".props", ".targets", "packages.lock.json")) or p.endswith("nuget.config")}


def prepare(root, custody):
    sha = identity(root)
    if custody.exists():
        raise ValueError("Custody already exists")
    custody.mkdir()
    (custody / "owner.json").write_text(json.dumps(owner()))
    for source, destination in ((OUTPUTS[0], PRODUCER_LOCK),
            ("build/nuget-locks/Legacy.Maliev.CompatibilityContracts/packages.lock.json", CONTRACT_LOCK)):
        target = root / destination
        target.write_bytes(read(root / source))
    for p in OUTPUTS:
        target = custody / "baseline" / p
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(read(root / p))
    (custody / "before.json").write_text(json.dumps({"source": sha, "inputs": inventory(root)}, indent=2))


def retain(root, custody, evidence):
    sha = identity(root)
    check_owner(custody)
    before = json.loads(read(custody / "before.json"))
    if before["source"] != sha or evidence.exists():
        raise ValueError("Custody mismatch")
    (root / OUTPUTS[0]).write_bytes(read(root / PRODUCER_LOCK))
    after = inventory(root)
    allowed = set(OUTPUTS) | {PRODUCER_LOCK}
    for p in set(before["inputs"]) | set(after):
        if before["inputs"].get(p) != after.get(p) and p not in allowed:
            raise ValueError("Unexpected project/lock drift: " + p)
    evidence.mkdir()
    (evidence / "owner.json").write_text(json.dumps(owner()))
    rows = []
    for p in OUTPUTS:
        data = read(root / p)
        changes = validate_delta(read(custody / "baseline" / p), data)
        destination = evidence / p
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(data)
        rows.append({"path": p, "bytes": len(data), "sha256": digest(data), "changes": changes})
    receipt = {"classification": "GENERATED_LOCKS_NOT_ACCEPTED", "source": sha,
        "defaults": DEFAULTS, "contracts": CONTRACTS, "files": rows,
        "contractsLockUnchanged": True, "lockedRestore": "PENDING", "inputInventory": after}
    (evidence / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")


def verified(root, custody, evidence):
    sha = identity(root)
    check_owner(custody)
    check_owner(evidence)
    receipt = json.loads(read(evidence / "receipt.json"))
    if receipt["source"] != sha or receipt["defaults"] != DEFAULTS or receipt["contracts"] != CONTRACTS:
        raise ValueError("Receipt identity mismatch")
    if inventory(root) != receipt["inputInventory"]:
        raise ValueError("Inputs changed during strict restore")
    for row in receipt["files"]:
        if digest(read(root / row["path"])) != row["sha256"]:
            raise ValueError("Lock changed during strict restore")
    receipt["lockedRestore"] = "PASS_BOTH_PROJECTS"
    (evidence / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")


def rejected(root, custody, evidence):
    """Retain bounded raw failure evidence without changing any admission rule."""
    sha = identity(root)
    check_owner(custody)
    if evidence.exists():
        check_owner(evidence)
    else:
        evidence.mkdir()
        (evidence / "owner.json").write_text(json.dumps(owner()))
    before = json.loads(read(custody / "before.json"))
    if before["source"] != sha:
        raise ValueError("Rejected receipt source mismatch")
    after = inventory(root)
    changed = {p: {"before": before["inputs"].get(p), "after": after.get(p)}
               for p in sorted(set(before["inputs"]) | set(after))
               if before["inputs"].get(p) != after.get(p)}
    rows = []
    for p in (*OUTPUTS, PRODUCER_LOCK, CONTRACT_LOCK):
        if (root / p).exists():
            data = read(root / p)
            target = evidence / "rejected-raw" / p
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            rows.append({"path": p, "bytes": len(data), "sha256": digest(data)})
    baselines = []
    for p in OUTPUTS:
        original_path = custody / "baseline" / p
        if original_path.exists():
            original_data = read(original_path)
            target = evidence / "original-baseline" / p
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(original_data)
            baselines.append({"path": p, "bytes": len(original_data), "sha256": digest(original_data)})
    contracts_before = root / "build/nuget-locks/Legacy.Maliev.CompatibilityContracts/packages.lock.json"
    original = read(contracts_before)
    if digest(original) != before["inputs"][contracts_before.relative_to(root).as_posix()]:
        raise ValueError("Committed Contracts baseline changed")
    (evidence / "contracts-before.json").write_bytes(original)
    receipt = {"classification": "REJECTED_GENERATION_NOT_ACCEPTED", "source": sha,
        "defaults": DEFAULTS, "contracts": CONTRACTS, "changedInputs": changed,
        "rawFiles": rows, "baselineFiles": baselines, "inputBefore": before["inputs"], "inputAfter": after,
        "lockedRestore": "NOT_ACCEPTED", "admissionRulesUnchanged": True}
    (evidence / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("phase", choices=("prepare", "retain", "verified", "rejected", "cleanup"))
    args = parser.parse_args()
    root = Path.cwd().resolve()
    temp = Path(os.environ["RUNNER_TEMP"]).resolve()
    custody, evidence = temp / "intranet-lock-custody", temp / "intranet-lock-artifact"
    if args.phase == "prepare":
        prepare(root, custody)
    elif args.phase == "retain":
        retain(root, custody, evidence)
    elif args.phase == "verified":
        verified(root, custody, evidence)
    elif args.phase == "rejected":
        rejected(root, custody, evidence)
    else:
        # Only these exact job-local paths; retained GitHub artifact survives cleanup.
        for path in (custody, evidence):
            if path.parent != temp:
                raise ValueError("Invalid cleanup ownership")
            cleanup_owned(path)


if __name__ == "__main__":
    main()
