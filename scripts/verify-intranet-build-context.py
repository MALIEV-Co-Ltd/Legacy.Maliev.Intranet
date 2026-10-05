"""Hosted-only admission of this repository's actual Docker context and images.

No publication, provider access, application requests or live data. Offline archive
controls test the verifier, not consumer acceptance. All Docker evidence is derived
from an exact committed scratch checkout with synthetic private-boundary sentinels.
"""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import tarfile
import tempfile
import uuid

DOCKERFILES = ("Legacy.Maliev.Intranet/Dockerfile", "Legacy.Maliev.Intranet.Bff/Dockerfile")
LOCKS = (
    "build/nuget-locks/Legacy.Maliev.ServiceDefaults/packages.lock.json",
    "build/nuget-locks/Legacy.Maliev.CompatibilityContracts/packages.lock.json",
)
DEPENDENCY_REFS = (
    "7edcd961024868513fd5f373cab3dcb261197f77",
    "78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7",
)
FORBIDDEN = tuple(
    prefix + suffix for prefix in ("", "nested/") for suffix in (
        ".git/admission-sentinel", ".worktrees/admission-sentinel",
        ".codex/admission-sentinel", ".codex-staging-admission/admission-sentinel",
        ".superpowers/admission-sentinel", ".agents/admission-sentinel",
        ".qwen/admission-sentinel", "logs/admission.log", "archive/admission.bak",
        "archive/admission.backup", "bin/admission.dll", "obj/admission.cache",
        "TestResults/admission.txt", "node_modules/admission.js",
    )
)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def verify_archive(stream, required, forbidden):
    found = {}
    with tarfile.open(fileobj=stream, mode="r:*") as archive:
        for entry in archive:
            path = PurePosixPath(entry.name)
            if path.is_absolute() or ".." in path.parts or "\\" in entry.name:
                raise ValueError("unsafe_archive_path")
            name = path.as_posix()
            if not name.startswith("context/"):
                continue
            name = name.removeprefix("context/")
            if name in forbidden:
                raise ValueError("forbidden_context_input")
            if name in required:
                if name in found or not entry.isfile() or entry.size > 128 * 1024 * 1024:
                    raise ValueError("invalid_required_context_input")
                found[name] = digest(archive.extractfile(entry).read())
    if found != required:
        raise ValueError("required_context_bytes_mismatch")
    return found


def verify_build_output(output):
    if re.search(r"\b(?:warning|error)\s+[A-Z]+\d+\b", output, re.I):
        raise ValueError("compiler_diagnostic")
    if re.search(r"\b[1-9]\d*\s+(?:Warning|Error)\(s\)", output):
        raise ValueError("nonzero_build_diagnostics")


def native(arguments, timeout=1800):
    result = subprocess.run(arguments, capture_output=True, timeout=timeout)
    if len(result.stdout) + len(result.stderr) > 64 * 1024 * 1024:
        raise ValueError("native_output_bound_exceeded")
    return result.returncode, result.stdout + result.stderr


def require_native(arguments):
    code, output = native(arguments)
    if code:
        raise ValueError("native_command_failed")
    return output


def assert_reject(action, expected):
    try:
        action()
    except ValueError as failure:
        if str(failure) == expected:
            return
        raise
    raise ValueError("negative_control_not_rejected")


def retain_lock_negative(output, code, dockerfile, index, evidence, context):
    if code == 0 or b"NU1004" not in output:
        raise ValueError("locked_restore_negative_not_rejected")
    # Retain only the controlled lock-mismatch diagnostics, not arbitrary native
    # output. No actual credentials/private logs are introduced into this context.
    lines = [line for line in output.decode("utf-8", errors="replace").splitlines()
             if "NU1004" in line]
    if not lines or len(lines) > 80:
        raise ValueError("negative_diagnostic_bound_exceeded")
    text = "\n".join(lines).replace(str(context), "<context>")
    text = text.replace("/dependencies/", "<dependencies>/")
    text = re.sub(r"https?://\S+", "<url-redacted>", text) + "\n"
    data = text.encode("utf-8")
    if len(data) > 16384:
        raise ValueError("negative_diagnostic_bound_exceeded")
    name = "locked-restore-negative-" + str(index) + ".log"
    (evidence / name).write_bytes(data)
    return {"control": "locked-restore-rejected-image-" + str(index),
            "dockerfile": dockerfile, "dockerTarget": "build",
            "nativeExitCode": code, "requiredDiagnostic": "NU1004",
            "sanitizedDiagnosticLog": name, "sanitizedDiagnosticLogSha256": digest(data),
            "nativeOutputSha256": digest(output),
            "logScope": "NU1004 diagnostic excerpt only; not full native output"}


def run(root, evidence):
    if os.environ.get("GITHUB_ACTIONS") != "true" or os.name != "posix":
        raise ValueError("hosted_linux_required")
    sha = require_native(["git", "-C", str(root), "rev-parse", "HEAD"]).decode().strip()
    if not re.fullmatch(r"[0-9a-f]{40}", sha) or sha != os.environ.get("GITHUB_SHA"):
        raise ValueError("checkout_identity_mismatch")
    evidence.mkdir(parents=True, exist_ok=True)
    receipt = {"sourceRevision": sha, "status": "pending", "negativeControls": []}
    tags, containers = [], []
    try:
        with tempfile.TemporaryDirectory(prefix="intranet-context-") as temporary:
            scratch = Path(temporary)
            context = scratch / "source"
            context.mkdir()
            payload = require_native(["git", "-C", str(root), "archive", "--format=tar", sha])
            with tarfile.open(fileobj=io.BytesIO(payload)) as archive:
                archive.extractall(context, filter="data")
            required = {}
            for path in context.rglob("*"):
                name = path.relative_to(context).as_posix()
                if path.is_file() and (name in LOCKS or name in ("nuget.config", "Directory.Build.props")
                        or (name.startswith("Legacy.Maliev.Intranet") and
                            ("/wwwroot/" in name or path.suffix in (".cs", ".csproj", ".razor", ".resx", ".json")))):
                    required[name] = digest(path.read_bytes())
            assets = sorted(name for name in required if "/wwwroot/" in name)
            if not assets or any(name not in required for name in LOCKS):
                raise ValueError("required_corpus_incomplete")
            for name in FORBIDDEN:
                path = context / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"SYNTHETIC-ADMISSION-SENTINEL-NOT-PRIVATE-DATA")
            dependency = ".dependencies/admission/source.cs"
            (context / dependency).parent.mkdir(parents=True)
            (context / dependency).write_bytes(b"// synthetic retained dependency source\n")
            required[dependency] = digest((context / dependency).read_bytes())
            policy = (context / ".dockerignore").read_bytes()
            receipt["ignoreSha256"] = digest(policy)
            probe = scratch / "context.Dockerfile"
            probe.write_text("FROM scratch\nCOPY . /context/\n", encoding="utf-8")

            def export():
                tag = "intranet-context:" + uuid.uuid4().hex
                tags.append(tag)
                require_native(["docker", "build", "--file", str(probe), "--tag", tag, str(context)])
                container = require_native(["docker", "create", tag, "/not-executed"]).decode().strip()
                containers.append(container)
                destination = scratch / (uuid.uuid4().hex + ".tar")
                require_native(["docker", "export", "--output", str(destination), container])
                if destination.stat().st_size > 2 * 1024 ** 3:
                    raise ValueError("context_export_bound_exceeded")
                with destination.open("rb") as stream:
                    return verify_archive(stream, required, FORBIDDEN)

            export()
            for label, rule in (("required-lock-excluded", LOCKS[0]),
                                ("required-asset-excluded", assets[0])):
                (context / ".dockerignore").write_bytes(policy + b"\n" + rule.encode() + b"\n")
                assert_reject(export, "required_context_bytes_mismatch")
                receipt["negativeControls"].append(label)
            (context / ".dockerignore").write_bytes(policy + b"\n!nested/logs/admission.log\n")
            assert_reject(export, "forbidden_context_input")
            receipt["negativeControls"].append("forbidden-admitted")
            (context / ".dockerignore").write_bytes(policy)
            lock = context / LOCKS[0]
            original_lock = lock.read_bytes()
            lock.write_bytes(original_lock + b"\n")
            assert_reject(export, "required_context_bytes_mismatch")
            receipt["negativeControls"].append("required-lock-bytes-modified")
            lock.write_bytes(original_lock)
            export()
            receipt["requiredSha256"] = required
            receipt["forbiddenSentinelCount"] = len(FORBIDDEN)
            receipt["images"] = []
            for index, dockerfile in enumerate(DOCKERFILES):
                dockerfile_bytes = (context / dockerfile).read_bytes()
                if (any(ref.encode() not in dockerfile_bytes for ref in DEPENDENCY_REFS)
                        or b"--locked-mode" not in dockerfile_bytes
                        or any(name.encode() not in dockerfile_bytes for name in LOCKS)):
                    raise ValueError("frozen_consumer_graph_mismatch")
                tag = "intranet-admission:" + uuid.uuid4().hex
                tags.append(tag)
                code, output = native(
                    ["docker", "build", "--progress=plain", "--file", str(context / dockerfile),
                     "--tag", tag, str(context)], timeout=2400)
                text = output.decode("utf-8", errors="replace")
                (evidence / ("image-" + str(index) + ".log")).write_text(text, encoding="utf-8")
                if code:
                    raise ValueError("consumer_image_build_failed")
                verify_build_output(text)
                image = require_native(["docker", "image", "inspect", tag, "--format", "{{.Id}}"])
                receipt["images"].append({"dockerfile": dockerfile, "imageId": image.decode().strip(),
                                          "dockerfileSha256": digest(dockerfile_bytes),
                                          "dependencyRefs": DEPENDENCY_REFS,
                                          "compilerWarnings": 0, "compilerErrors": 0})
            changed = json.loads(original_lock)
            directs = [package for framework in changed["dependencies"].values()
                       for package in framework.values() if package.get("type") == "Direct"]
            if not directs:
                raise ValueError("lock_negative_corpus_incomplete")
            directs[0]["requested"] = "[0.0.0, )"
            lock.write_text(json.dumps(changed), encoding="utf-8")
            receipt["lockedRestoreNegatives"] = []
            for index, dockerfile in enumerate(DOCKERFILES):
                tag = "intranet-lock-negative:" + uuid.uuid4().hex
                tags.append(tag)
                code, output = native(["docker", "build", "--progress=plain", "--target", "build",
                                       "--file", str(context / dockerfile), "--tag", tag, str(context)])
                negative = retain_lock_negative(output, code, dockerfile, index, evidence, context)
                receipt["lockedRestoreNegatives"].append(negative)
                receipt["negativeControls"].append(negative["control"])
            receipt["status"] = "accepted-context-and-image-build-only"
    finally:
        cleanup_ok = True
        for container in containers:
            code, _ = native(["docker", "rm", "--force", container], timeout=120)
            cleanup_ok &= code == 0
        for tag in tags:
            # Failed builds have no resulting image and therefore need no removal.
            if native(["docker", "image", "inspect", tag], timeout=120)[0] == 0:
                cleanup_ok &= native(["docker", "image", "rm", "--force", tag], timeout=120)[0] == 0
        receipt["ownedDockerResourcesCleaned"] = cleanup_ok
        if not cleanup_ok:
            receipt["status"] = "cleanup-failed"
        (evidence / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
        if not cleanup_ok:
            raise ValueError("owned_resource_cleanup_failed")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    args = parser.parse_args()
    try:
        run(Path(__file__).resolve().parents[1], args.evidence.resolve())
    except Exception:
        raise SystemExit("intranet_context_admission_failed; inspect bounded retained receipt")
