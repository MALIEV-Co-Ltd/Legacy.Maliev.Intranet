"""Opt-in hosted synthetic diagnosis; never publish a heap or unparsed SOS output.

Review before activation. --self-test uses synthetic inputs and one isolated Python
entrypoint subprocess, without dotnet, network, or heap collection. Normal execution
retains the original failing Fact.
Official pin: github.com/dotnet/diagnostics/releases/tag/v10.0.745401.
The SHA512 below is the immutable NuGet catalog packageHash, retrieved 2026-10-05.
"""

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import signal
import subprocess
import sys
import tempfile
import time
import unittest
import urllib.request
import xml.etree.ElementTree as ET


PIN = "10.0.745401"
PACKAGE = "https://api.nuget.org/v3-flatcontainer/dotnet-dump/10.0.745401/dotnet-dump.10.0.745401.nupkg"
CATALOG = "https://api.nuget.org/v3/catalog0/data/2026.09.10.18.35.53/dotnet-dump.10.0.745401.json"
PACKAGE_SHA512 = "oxVrDv0pAVHe5+wIbu71GFssgX7+DefQwAoTyDt6oweR7GY72zjg/ObH7otpqK6NRZjrtyzFIbmVeMUzaj5EUw=="
PACKAGE_SIZE = 10453685
FACT = "Legacy.Maliev.Intranet.Tests.PooledTestHostReleaseContractTests.DisposedOrdinaryBffHost_ReleasesProviderAndTransportAfterObservedPoolExpiry"
WITNESS = "Legacy.Maliev.Intranet.Tests.PooledTestHostReleaseContractTests+RetainedHost"
FACTORY = "Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`1+DelegatedWebApplicationFactory"
PROVIDER = "Microsoft.Extensions.DependencyInjection.ServiceProvider"
FAILURE = "Disposed ordinary BFF factory remained rooted beyond observed handler expiry and cleanup."
HEX = r"[0-9a-fA-F]{1,16}"
FAMILIES = ("System.", "Microsoft.", "Legacy.Maliev.", "Maliev.Aspire.ServiceDefaults.",
            "Polly.", "OpenTelemetry.", "Xunit.", "xunit.", "Grpc.", "Program+")
MAX_TEXT = 1024 * 1024


class Unavailable(Exception):
    """Only fixed stage codes, never raw exception messages, leave this program."""


def require(condition, code):
    if not condition:
        raise Unavailable(code)


def verify_checkout(head, checkout, parents):
    """Accept only the reviewed head or GitHub's two-parent tested merge."""
    require(all(re.fullmatch(r"[0-9a-f]{40}", value) for value in (head, checkout, *parents)),
            "checkout-hash-format")
    require(checkout == head or (len(parents) == 2 and parents[1] == head),
            "checkout-head-provenance")


def commit_parents(text):
    """Read immutable object headers, unaffected by Git's shallow traversal boundary."""
    require(len(text.encode("utf-8")) <= MAX_TEXT and "\x00" not in text,
            "commit-output-bound")
    header, separator, _ = text.partition("\n\n")
    require(separator, "commit-header-boundary")
    rows = header.split("\n")
    require(re.fullmatch(r"tree [0-9a-f]{40}", rows[0]), "commit-tree-header")
    parents = []
    other_headers = False
    for row in rows[1:]:
        if row.startswith("parent"):
            require(not other_headers and re.fullmatch(r"parent [0-9a-f]{40}", row),
                    "commit-parent-header")
            parents.append(row[7:])
        else:
            require(re.fullmatch(r"[a-z][a-z0-9-]* .+", row) or (other_headers and row.startswith(" ")),
                    "commit-header-format")
            require(not row.startswith("tree "), "commit-tree-header")
            other_headers = True
    return parents


def metadata(value):
    require(len(value) <= 2048 and (value == "Program" or value.startswith(FAMILIES)), "unknown-metadata-identity")
    require(re.fullmatch(r"[A-Za-z0-9_.$`+<>\[\](), =!&?*\-]+", value) is not None,
            "unknown-metadata-grammar")
    canonical = re.sub(r", Version=\d+(?:\.\d+){3}", "", value)
    canonical = re.sub(r", Culture=neutral", "", canonical)
    canonical = re.sub(r", PublicKeyToken=(?:[0-9a-fA-F]{16}|null)", "", canonical)
    require("=" not in canonical and " " not in canonical.replace(", ", ""), "metadata-value-injection")
    return value


def lines(text):
    require(len(text.encode("utf-8")) <= MAX_TEXT and "\x00" not in text, "output-bound")
    return [line.strip() for line in text.splitlines() if line.strip()]


def addresses(text):
    result = lines(text)
    require(result and all(re.fullmatch(HEX, row) for row in result), "heap-address-format")
    require(len(result) == len(set(result)) and len(result) <= 64, "heap-address-count")
    return result


def object_fields(text, expected_type, expected_fields, module):
    """Read only reviewed record/WeakReference fields, not host/provider contents."""
    result = {}
    found_name = False
    for row in lines(text):
        if row.startswith("Name:"):
            require(not found_name and row[5:].strip() == expected_type, "object-type")
            found_name = True
        elif re.fullmatch(r"(?:MethodTable|EEClass):\s*" + HEX, row):
            pass
        elif re.fullmatch(r"Size:\s*\d+\(0x[0-9a-fA-F]+\) bytes", row):
            pass
        elif row.startswith("File:"):
            value = row[5:].strip()
            require(value.startswith("/") and len(value) <= 1024
                    and value.endswith("/" + module), "object-module")
        elif row == "Fields:" or re.fullmatch(r"MT\s+Field\s+Offset\s+Type\s+VT\s+Attr\s+Value\s+Name", row):
            pass
        else:
            match = re.fullmatch(
                rf"{HEX}\s+{HEX}\s+{HEX}\s+(\S+)\s+[01]\s+instance\s+({HEX})\s+(\S+)", row)
            require(match is not None, "object-field-format")
            field_type, value, name = match.groups()
            require(name in expected_fields and name not in result, "object-field-identity")
            # SOS may left-truncate a field type; only these exact suffixes are reviewed.
            require(field_type in ("System.WeakReference", "System.TimeSpan", "System.IntPtr")
                    or (name == "<Disposal>k__BackingField" and field_type.endswith("DisposalCount")),
                    "object-field-type")
            result[name] = value.lower()
    require(found_name and set(result) == set(expected_fields), "object-field-completeness")
    return result


def weak_handles(text):
    result = {}
    in_statistics = False
    for row in lines(text):
        if row.startswith("Statistics:"):
            in_statistics = True
        elif re.fullmatch(r"Handle\s+Type\s+Object\s+Size\s+Data\s+Type", row):
            pass
        elif match := re.fullmatch(rf"({HEX})\s+WeakShort\s+({HEX})\s+\d+\s+(.+)", row):
            require(not in_statistics, "handle-order")
            handle, target, kind = match.groups()
            require(handle.lower() not in result, "duplicate-handle")
            result[handle.lower()] = (target.lower(), metadata(kind))
        elif in_statistics and (re.fullmatch(r"MT\s+Count\s+TotalSize\s+Class Name", row)
                                or re.fullmatch(r"Total\s+[\d,]+ objects, [\d,]+ bytes", row)
                                or re.fullmatch(r"Weak Short Handles:\s*[\d,]+", row)):
            pass
        elif in_statistics and (match := re.fullmatch(rf"{HEX}\s+[\d,]+\s+[\d,]+\s+(.+)", row)):
            metadata(match.group(1))
        else:
            raise Unavailable("unknown-handle-output")
    require(result and len(result) <= 10000, "handle-count")
    return result


def root_paths(text, target, non_stack=False):
    paths = []
    current = None
    summary = None
    context = None
    for row in lines(text):
        if row in ("Caching GC roots, this may take a while.",
                   "Caching GC stack roots, this may take a while.",
                   "Subsequent runs of this command will be faster."):
            continue
        if match := re.fullmatch(r"Thread (" + HEX + r"):", row):
            require(not non_stack, "unexpected-stack-root")
            context = {"kind": "stack", "thread": match.group(1).lower()}
            current = None
        elif row in ("HandleTable:", "Finalizer Queue:"):
            context = {"kind": "handle" if row == "HandleTable:" else "finalizer"}
            current = None
        elif match := re.fullmatch(rf"({HEX}) \((strong handle|pinned handle|async pinned handle|sized ref handle|dependent handle|finalizer root)\)", row):
            require(context is not None and context["kind"] != "stack", "root-header")
            current = {**context, "root": match.group(1).lower(), "handle_kind": match.group(2), "nodes": []}
            paths.append(current)
        elif match := re.fullmatch(rf"({HEX})(?:\s+{HEX})?\s+(.+)", row):
            require(context is not None and context["kind"] == "stack", "stack-frame-context")
            method = match.group(2)
            # Source paths are not exported; do not permit locals/argument values.
            source = re.search(r" \[(/[^\r\n\[\]]+\.cs) @ (\d+)\]$", method)
            if source:
                method = method[:source.start()]
            frame = re.match(r"\[(HelperMethodFrame(?:_[A-Z0-9]+)?)\] (.+)$", method)
            if frame:
                context["frame"] = frame.group(1)
                method = frame.group(2)
            context["method"] = metadata(method)
        elif match := re.fullmatch(r"(r(?:[abcd]x|[sd]i|[sb]p|[89]|1[0-5])):\s*(?:\(interior\))?", row):
            require(context is not None and context.get("kind") == "stack" and "method" in context,
                    "stack-register-context")
            current = {**context, "register": match.group(1), "nodes": []}
            paths.append(current)
        elif match := re.fullmatch(rf"->\s+({HEX})\s+(.+)", row):
            require(current is not None and len(current["nodes"]) < 128, "root-node-context")
            kind = match.group(2)
            edge = None
            if kind.endswith(" (dependent handle)"):
                kind, edge = kind[:-19], "dependent"
            elif " (static variable: " in kind and kind.endswith(")"):
                kind, edge = kind[:-1].split(" (static variable: ", 1)
                edge = metadata(edge)
            current["nodes"].append({"id": match.group(1).lower(), "type": metadata(kind), "edge": edge})
        elif match := re.fullmatch(r"Found (\d+) unique roots\.", row):
            require(summary is None, "duplicate-root-summary")
            summary = int(match.group(1))
        else:
            raise Unavailable("unknown-root-output")
    require(summary is not None and summary == len(paths) and summary <= 32, "root-summary")
    require(all(p["nodes"] and p["nodes"][-1]["id"] == target.lower() for p in paths), "root-target")
    require(non_stack or paths, "no-retained-root")
    return {"paths": paths, "capped": summary == 32}


def transcript(text, commands, dump):
    """Strip only exact tool framing; command result parsers reject everything else."""
    sections = []
    active = None
    for row in text.splitlines():
        if row == f"Loading core dump: {dump}":
            continue
        if row in ("Ready to process analysis commands. Type 'help' to list available commands or 'help [command]' to get detailed help on a command.",
                   "Type 'quit' or 'exit' to exit the session.") or not row.strip():
            continue
        if row.startswith("> "):
            command = row[2:]
            require(command in commands and command not in [c for c, _ in sections], "unexpected-sos-command")
            active = []
            sections.append((command, active))
        else:
            require(active is not None, "unknown-sos-framing")
            active.append(row)
    require([c for c, _ in sections] == commands, "sos-command-order")
    return {command: "\n".join(rows) for command, rows in sections}


def verify_original_trx(trx_bytes):
    require(len(trx_bytes) < 128 * 1024 and b"<!DOCTYPE" not in trx_bytes, "trx-bound")
    xml = ET.fromstring(trx_bytes)
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counts = xml.find(".//t:Counters", ns).attrib
    expected = dict.fromkeys(("passed", "error", "timeout", "aborted", "inconclusive",
                              "passedButRunAborted", "notRunnable", "notExecuted", "disconnected",
                              "warning", "completed", "inProgress", "pending"), "0")
    expected.update(total="1", executed="1", failed="1")
    require(counts == expected, "trx-counts")
    results = xml.findall(".//t:UnitTestResult", ns)
    require(len(results) == 1 and results[0].attrib["testName"] == FACT
            and results[0].attrib["outcome"] == "Failed"
            and results[0].find(".//t:Message", ns).text == FAILURE, "trx-failure-identity")


def run_private(args, env, cwd, private, label, seconds=60):
    log = private / (label + ".log")
    with log.open("wb") as stream:
        process = subprocess.Popen(args, cwd=cwd, env=env, stdout=stream,
                                   stderr=subprocess.STDOUT, start_new_session=True)
        try:
            deadline = time.monotonic() + seconds
            while process.poll() is None:
                if log.stat().st_size > MAX_TEXT:
                    raise Unavailable(label + "-output-bound")
                if time.monotonic() >= deadline:
                    raise subprocess.TimeoutExpired(args, seconds)
                time.sleep(0.1)
            code = process.returncode
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGTERM)
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait(timeout=5)
            raise Unavailable(label + "-timeout") from None
        except Unavailable:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=5)
            raise
    require(log.stat().st_size <= MAX_TEXT, label + "-output-bound")
    require(code == 0, label + "-failed")
    return log.read_text(encoding="utf-8", errors="strict")


def download(url, limit):
    # No operator proxy/authentication configuration is consulted.
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open(url, timeout=20) as response:
        require(response.geturl() == url, "package-source-redirect")
        value = response.read(limit + 1)
    require(len(value) <= limit, "package-size-bound")
    return value


def clean_environment(private, dotnet, workspace, head):
    home, tmp = private / "home", private / "tmp"
    home.mkdir(mode=0o700)
    tmp.mkdir(mode=0o700)
    return {
        "PATH": f"{dotnet.parent}:/usr/bin:/bin", "HOME": str(home), "TMPDIR": str(tmp),
        "DOTNET_ROOT": str(dotnet.parent), "DOTNET_CLI_HOME": str(home),
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
        "DOTNET_NOLOGO": "1", "DOTNET_PROCESSOR_COUNT": "1", "LANG": "C.UTF-8",
        "GITHUB_ACTIONS": "false", "GITHUB_TOKEN": "", "RUNNER_OS": "Linux",
        "MalievWorkspaceRoot": str(workspace / ".dependencies"),
        "MALIEV_POOLED_HOST_ROOT_DIAGNOSTICS": "synthetic-isolated-v1",
        "MALIEV_POOLED_HOST_ROOT_HEAD": head,
    }


def capture(args, private):
    require(platform.system() == "Linux" and platform.machine() == "x86_64", "runner-platform")
    require(re.fullmatch(r"[0-9a-f]{40}", args.head), "head-format")
    workspace, dotnet = Path(args.workspace).resolve(), Path(args.dotnet).resolve()
    require(dotnet.is_file() and dotnet.name == "dotnet", "dotnet-identity")
    project = workspace / "Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj"
    assembly = project.parent / "bin/Release/net10.0/Legacy.Maliev.Intranet.Tests.dll"
    require(project.is_file() and assembly.is_file(), "built-release-prerequisite")
    env = clean_environment(private, dotnet, workspace, args.head)
    checkout = run_private(["/usr/bin/git", "rev-parse", "HEAD"], env, workspace, private, "checkout").strip()
    require(re.fullmatch(r"[0-9a-f]{40}", checkout), "checkout-format")
    commit = run_private(["/usr/bin/git", "cat-file", "-p", checkout],
                         env, workspace, private, "head-provenance")
    verify_checkout(args.head, checkout, commit_parents(commit))
    catalog = json.loads(download(CATALOG, 128 * 1024))
    require(catalog["id"] == "dotnet-dump" and catalog["version"] == PIN
            and catalog["authors"] == "Microsoft" and catalog["packageHashAlgorithm"] == "SHA512"
            and catalog["packageHash"] == PACKAGE_SHA512 and catalog["packageSize"] == PACKAGE_SIZE,
            "package-catalog-identity")
    package = download(PACKAGE, PACKAGE_SIZE)
    require(len(package) == PACKAGE_SIZE and base64.b64encode(hashlib.sha512(package).digest()).decode() == PACKAGE_SHA512,
            "package-checksum")
    feed = private / "feed"
    feed.mkdir(mode=0o700)
    (feed / f"dotnet-dump.{PIN}.nupkg").write_bytes(package)
    config = private / "NuGet.Config"
    config.write_text(f'<configuration><packageSources><clear/><add key="verified-local" value="{feed}"/></packageSources></configuration>', encoding="utf-8")
    tools = private / "tools"
    run_private([str(dotnet), "tool", "install", "dotnet-dump", "--version", PIN,
                 "--tool-path", str(tools), "--configfile", str(config)], env, private, private, "tool-install")
    tool = tools / "dotnet-dump"
    version = run_private([str(tool), "--version"], env, private, private, "tool-version").strip()
    require(re.fullmatch(re.escape(PIN) + r"(?:\+[0-9a-f]+)?", version), "tool-version-identity")
    trx_dir = private / "trx"
    log = private / "isolated-test.log"
    with log.open("wb") as output:
        test = subprocess.Popen([str(dotnet), "test", str(project), "--configuration", "Release",
                                 "--no-build", "--no-restore", "-p:UseLocalMalievDependencies=true",
                                 "--filter", "FullyQualifiedName=" + FACT,
                                 "--logger", "trx;LogFileName=isolated-pooled-host.trx",
                                 "--results-directory", str(trx_dir)],
                                env=env, cwd=workspace, stdout=output, stderr=subprocess.STDOUT,
                                start_new_session=True)
        try:
            deadline = time.monotonic() + 360
            ready = None
            while time.monotonic() < deadline and test.poll() is None:
                require(log.stat().st_size <= MAX_TEXT, "isolated-test-output-bound")
                markers = list(Path(env["TMPDIR"]).glob("maliev-intranet-pooled-root-*.ready"))
                require(len(markers) <= 1, "ambiguous-signal")
                if markers:
                    ready = markers[0]
                    break
                time.sleep(0.2)
            require(ready is not None, "no-post-observation-signal")
            require(ready.stat().st_size <= 4096, "signal-size-bound")
            # File.WriteAllLines may still be completing when the marker first appears.
            for _ in range(5):
                if len(ready.read_text(encoding="utf-8").splitlines()) == 5:
                    break
                time.sleep(0.1)
            rows = lines(ready.read_text(encoding="utf-8"))
            require(len(rows) == 5 and rows[1:] == [f"head={args.head}", f"factory={FACTORY}",
                                                   f"provider={PROVIDER}", f"witness={WITNESS}"], "signal-identity")
            require(re.fullmatch(r"pid=[1-9]\d{0,9}", rows[0]), "signal-pid")
            pid = int(rows[0][4:])
            require(ready.name == f"maliev-intranet-pooled-root-{pid}.ready", "signal-filename")
            proc = Path("/proc") / str(pid)
            status = (proc / "status").read_text()
            require(re.search(rf"^Uid:\s+{os.getuid()}\s+{os.getuid()}\s+{os.getuid()}\s+{os.getuid()}$", status, re.M), "target-owner")
            parent = pid
            for _ in range(16):
                if parent == test.pid:
                    break
                s = (Path("/proc") / str(parent) / "status").read_text()
                parent = int(re.search(r"^PPid:\s+(\d+)$", s, re.M).group(1))
            require(parent == test.pid and os.readlink(proc / "exe") == str(dotnet), "target-process-identity")
            require(any(Path(v.decode()).name == "testhost.dll"
                        for v in (proc / "cmdline").read_bytes().split(b"\0") if v), "target-testhost")
            target_env = dict(v.split(b"=", 1) for v in (proc / "environ").read_bytes().split(b"\0") if v)
            allowed = {k.encode(): v.encode() for k, v in env.items()}
            allowed[b"DOTNET_HOST_PATH"] = str(dotnet).encode()
            allowed[b"DOTNET_ROOT_X64"] = str(dotnet.parent).encode()
            require(all(k in allowed and allowed[k] == v for k, v in target_env.items()), "target-environment-not-allowlisted")
            core_paths = {line.split()[-1] for line in (proc / "maps").read_text().splitlines()
                          if line.endswith("/libcoreclr.so")}
            require(len(core_paths) == 1, "runtime-map-identity")
            runtime_dir = Path(core_paths.pop()).parent
            require(runtime_dir.name == "10.0.12" and runtime_dir.parent.name == "Microsoft.NETCore.App",
                    "runtime-version")
            require(runtime_dir.is_relative_to(dotnet.parent), "runtime-location")
            dac = runtime_dir / "libmscordaccore.so"
            require(dac.is_file(), "matching-dac-prerequisite")
            dump = private / "isolated.heap"
            run_private([str(tool), "collect", "--process-id", str(pid), "--type", "Heap",
                         "--output", str(dump)], env, private, private, "heap-collection", seconds=30)
            # Post-collection size rejection, not a hard live collection/disk cap.
            require(dump.is_file() and dump.stat().st_size < 512 * 1024 * 1024, "heap-size-bound")
            while test.poll() is None and time.monotonic() < deadline:
                require(log.stat().st_size <= MAX_TEXT, "isolated-test-output-bound")
                time.sleep(0.1)
            require(test.poll() is not None, "isolated-test-timeout")
            test_code = test.returncode
            require(log.stat().st_size <= MAX_TEXT, "isolated-test-output-bound")
            require(test_code == 1, "original-test-failure-not-preserved")
        finally:
            if test.poll() is None:
                os.killpg(test.pid, signal.SIGTERM)
                try:
                    test.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    os.killpg(test.pid, signal.SIGKILL)
                    test.wait(timeout=5)

    def sos(commands, label):
        all_commands = [f"setclrpath {runtime_dir}", *commands, "exit"]
        command = [str(tool), "analyze", str(dump)]
        for value in all_commands:
            command.extend(["--command", value])
        text = run_private(command, env, private, private, label)
        sections = transcript(text, all_commands, dump)
        require(not sections[all_commands[0]].strip() and not sections["exit"].strip(), "sos-setup-output")
        return sections

    initial = sos(["eeversion", f"dumpheap -type {WITNESS} -short", "gchandles -type WeakShort"], "sos-inventory")
    version_rows = lines(initial["eeversion"])
    product = r"10\.0\.12(?:\+[0-9a-f]+| @Commit: ?[0-9a-f]+)?"
    require(any(re.fullmatch(product, v) for v in version_rows), "dump-runtime-version")
    require(all(re.fullmatch(r"(?:\d+(?:\.\d+){3}|" + product
                            + r"|SOS Version: [0-9.]+(?:\+[0-9a-f]+| @Commit: ?[0-9a-f]+)?"
                            + r"|Server mode with \d+ gc heaps|Workstation mode|DATAS \d+)", v)
                for v in version_rows), "unknown-version-output")
    witness_ids = addresses(initial[f"dumpheap -type {WITNESS} -short"])
    require(len(witness_ids) == 1, "ambiguous-witness")
    witness_id = witness_ids[0]
    fields_command = "dumpobj " + witness_id
    fields = object_fields(sos([fields_command], "sos-witness")[fields_command], WITNESS,
                           {f"<{n}>k__BackingField" for n in ("Factory", "Provider", "Transport", "HandlerLifetime", "Disposal")},
                           "Legacy.Maliev.Intranet.Tests.dll")
    handle_table = weak_handles(initial["gchandles -type WeakShort"])
    mappings = {}
    roots = {}
    for name, expected in (("Factory", FACTORY), ("Provider", PROVIDER)):
        ref = fields[f"<{name}>k__BackingField"]
        command = "dumpobj " + ref
        handle = object_fields(sos([command], "sos-weak-" + name)[command], "System.WeakReference",
                               {"_taggedHandle"}, "System.Private.CoreLib.dll")["_taggedHandle"]
        require(int(handle, 16) != 0 and int(handle, 16) & 3 == 0, "unsupported-weak-handle-tags")
        require(handle in handle_table, "weak-handle-not-found")
        target, kind = handle_table[handle]
        require(kind == expected or (name == "Factory" and kind.startswith(FACTORY + "[")), "weak-target-identity")
        mappings[name] = {"weak_reference": ref, "weak_handle": handle, "target": target, "type": kind}
        commands = ["gcroot -limit 32 " + target, "gcroot -nostacks -limit 32 " + target]
        sections = sos(commands, "sos-roots-" + name)
        roots[name] = {"all": root_paths(sections[commands[0]], target),
                       "non_stack": root_paths(sections[commands[1]], target, non_stack=True)}
    trx = trx_dir / "isolated-pooled-host.trx"
    require(trx.stat().st_size < 128 * 1024, "trx-bound")
    verify_original_trx(trx.read_bytes())
    return {"status": "evidence", "head": args.head, "checkout": checkout, "pid": pid,
            "tool": PIN, "package_source": PACKAGE, "package_sha512": PACKAGE_SHA512,
            "runtime": "10.0.12", "dac_sha256": hashlib.sha256(dac.read_bytes()).hexdigest(),
            "test_assembly_sha256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
            "original_test": {"executed": 1, "failed": 1}, "witness": witness_id,
            "mappings": mappings, "roots": roots}


class ParserControls(unittest.TestCase):
    def rejects(self, function, *args):
        with self.assertRaises(Unavailable):
            function(*args)

    def test_checkout_provenance(self):
        head, checkout, base = "a" * 40, "b" * 40, "c" * 40
        verify_checkout(head, head, [])
        verify_checkout(head, checkout, [base, head])
        for candidate, parents in ((checkout, [base]), (checkout, [base, base]),
                                   (checkout, [base, base, head]), (checkout, [head, base]),
                                   ("invalid", [base, head]), (checkout, ["invalid", head])):
            self.rejects(verify_checkout, head, candidate, parents)
        self.rejects(verify_checkout, "invalid", head, [])

    def test_commit_object_headers(self):
        head, checkout, base, tree = "a" * 40, "b" * 40, "c" * 40, "d" * 40
        identities = "author Synthetic <synthetic@example.invalid> 1 +0000\ncommitter Synthetic <synthetic@example.invalid> 1 +0000"
        # The raw two-parent object retains parents even when traversal sees a shallow root.
        text = f"tree {tree}\nparent {base}\nparent {head}\n{identities}\n\nSynthetic merge\n"
        self.assertEqual(commit_parents(text), [base, head])
        verify_checkout(head, checkout, commit_parents(text))
        exact = f"tree {tree}\n{identities}\n\nSynthetic head\n"
        verify_checkout(head, head, commit_parents(exact))
        extra = text.replace(f"parent {head}\n", f"parent {base}\nparent {head}\n")
        self.rejects(verify_checkout, head, checkout, commit_parents(extra))
        for bad in (text.replace(f"parent {head}", "parent invalid"),
                    text.replace(f"tree {tree}", "tree invalid"),
                    text.replace(f"tree {tree}\n", ""), "", text.replace("\n\n", "\n"),
                    text.replace(f"parent {head}\n", "").replace("\n\n", f"\nparent {head}\n\n")):
            self.rejects(commit_parents, bad)
        # Neither a body 'parent' line nor a body 'head' line can prove provenance.
        for body in (f"parent {head}", f"head {head}"):
            spoof = exact + body + "\n"
            self.assertEqual(commit_parents(spoof), [])
            self.rejects(verify_checkout, head, checkout, commit_parents(spoof))

    def test_actual_entrypoint_generic_failure(self):
        # Trigger the actual __main__ outer catch without invoking capture or tooling.
        code = ("import argparse, runpy, sys\n"
                "def fail(*args, **kwargs):\n"
                "    raise RuntimeError('synthetic sensitive failure')\n"
                "argparse.ArgumentParser.parse_args = fail\n"
                "runpy.run_path(sys.argv[1], run_name='__main__')\n")
        env = {"SystemRoot": os.environ["SystemRoot"]} if "SystemRoot" in os.environ else {}
        result = subprocess.run([sys.executable, "-I", "-c", code, str(Path(__file__).resolve())],
                                env=env, capture_output=True, text=True, timeout=5)
        self.assertEqual(result.returncode, 1)
        self.assertEqual(result.stdout,
                         '{"status":"diagnostic-unavailable","stage":"unexpected-failure"}\n')
        self.assertEqual(result.stderr, "")

    def test_addresses(self):
        self.assertEqual(addresses("000abc\n000def"), ["000abc", "000def"])
        for bad in ("", "secret=value", "abc\nabc", "abc\nString: token"):
            self.rejects(addresses, bad)

    def test_metadata(self):
        self.assertEqual(metadata("System.Threading.TimerQueueTimer"), "System.Threading.TimerQueueTimer")
        for bad in ("Unknown.Root", "System.String /secret", "System.Type\ncredential", "System.Type\"secret",
                    "System.String token=secret", "System.Method(System.String token)"):
            self.rejects(metadata, bad)
        self.assertEqual(metadata("System.List`1[[System.String, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]"),
                         "System.List`1[[System.String, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]")

    def test_weak_field(self):
        text = "Name: System.WeakReference\nMethodTable: abc\nEEClass: def\nSize: 24(0x18) bytes\nFile: /runtime/System.Private.CoreLib.dll\nFields:\nMT Field Offset Type VT Attr Value Name\nabc 4000001 8 System.IntPtr 1 instance 1000 _taggedHandle"
        self.assertEqual(object_fields(text, "System.WeakReference", {"_taggedHandle"}, "System.Private.CoreLib.dll"), {"_taggedHandle": "1000"})
        for bad in (text.replace("System.WeakReference", "System.String"), text + "\nString: secret",
                    text.replace("_taggedHandle", "unreviewed"), text + "\nabc 4000001 8 System.IntPtr 1 instance 1000 _taggedHandle"):
            self.rejects(object_fields, bad, "System.WeakReference", {"_taggedHandle"}, "System.Private.CoreLib.dll")

    def test_handles(self):
        text = "Handle Type Object Size Data Type\n1000 WeakShort 2000 24 System.WeakReference\nStatistics:\nMT Count TotalSize Class Name\nabc 1 24 System.WeakReference\nTotal 1 objects, 24 bytes\nWeak Short Handles: 1"
        self.assertEqual(weak_handles(text)["1000"], ("2000", "System.WeakReference"))
        for bad in (text.replace("WeakShort", "Strong"), text + "\nSecret: token", text.replace("System.WeakReference", "Unknown.Object")):
            self.rejects(weak_handles, bad)

    def test_root_paths(self):
        text = "HandleTable:\n1000 (strong handle)\n-> 2000 System.Object[]\n-> 3000 System.Threading.TimerQueueTimer (static variable: System.Threading.TimerQueue.s_queue)\n-> 4000 Microsoft.Extensions.DependencyInjection.ServiceProvider\nFound 1 unique roots."
        self.assertEqual(root_paths(text, "4000")["paths"][0]["nodes"][-1]["id"], "4000")
        for bad in (text.replace("4000", "5000"), text + "\nString: secret", text.replace("System.Object[]", "Unknown.Root"), text.replace("Found 1", "Found 2"), text.replace("strong handle", "weak short handle")):
            self.rejects(root_paths, bad, "4000")
        self.assertEqual(root_paths(text.replace(" (static variable: System.Threading.TimerQueue.s_queue)", " (dependent handle)"), "4000")["paths"][0]["nodes"][1]["edge"], "dependent")

    def test_stack_and_framing(self):
        text = "Thread ab:\n1000 2000 System.Threading.Timer.Callback()\nrdi:\n-> 4000 Microsoft.Extensions.DependencyInjection.ServiceProvider\nFound 1 unique roots."
        self.assertEqual(root_paths(text, "4000")["paths"][0]["kind"], "stack")
        self.rejects(root_paths, text, "4000", True)
        self.rejects(root_paths, "Found 0 unique roots.", "4000")
        self.assertEqual(root_paths("Found 0 unique roots.", "4000", True)["paths"], [])
        self.assertEqual(transcript("> dumpheap\nabc\n> exit\n", ["dumpheap", "exit"], Path("/private/heap"))["dumpheap"], "abc")
        self.rejects(transcript, "unexpected secret\n> exit", ["exit"], Path("/private/heap"))
        self.rejects(transcript, "> dumpobj\nsecret\n> exit", ["exit"], Path("/private/heap"))

    def test_original_trx_failure(self):
        zero = ("passed", "error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
                "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending")
        attributes = 'total="1" executed="1" failed="1" ' + " ".join(f'{key}="0"' for key in zero)
        text = (f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                f'<Counters {attributes}/><UnitTestResult testName="{FACT}" outcome="Failed">'
                f'<Message>{FAILURE}</Message></UnitTestResult></TestRun>')
        verify_original_trx(text.encode())
        for bad in (text.replace('failed="1"', 'failed="0"'), text.replace('timeout="0"', 'timeout="1"'),
                    text.replace(FACT, "Unknown.Test"), text.replace(FAILURE, "Unknown failure"),
                    '<!DOCTYPE unsafe>' + text):
            self.rejects(verify_original_trx, bad.encode())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--workspace")
    parser.add_argument("--dotnet")
    parser.add_argument("--head")
    args = parser.parse_args()
    if args.self_test:
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(ParserControls)
        return 0 if unittest.TextTestRunner().run(suite).wasSuccessful() else 1
    require(args.workspace and args.dotnet and args.head, "arguments")
    os.umask(0o077)
    # Exactly one owned temporary subtree; no user home, cache, or shared data deletion.
    runner_temp = os.environ.get("RUNNER_TEMP")
    require(platform.system() == "Linux" and runner_temp
            and os.environ.get("RUNNER_OS") == "Linux"
            and os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted"
            and re.fullmatch(r"[1-9]\d+", os.environ.get("GITHUB_RUN_ID", "")), "hosted-runner")
    parent = Path(runner_temp).resolve()
    require(parent.is_dir() and parent != Path("/"), "runner-temp")
    report = {"status": "diagnostic-unavailable", "stage": "unexpected-failure"}
    with tempfile.TemporaryDirectory(prefix="maliev-pooled-root-", dir=parent) as directory:
        private = Path(directory).resolve()
        require(private.parent == parent and private.stat().st_uid == os.getuid()
                and private.stat().st_mode & 0o777 == 0o700, "private-temp-ownership")
        try:
            report = capture(args, private)
        except Unavailable as failure:
            report = {"status": "diagnostic-unavailable", "stage": str(failure)}
        except Exception:
            # Never expose exception details, unfiltered logs, or partial root results.
            pass
    payload = json.dumps(report, sort_keys=True)
    require(len(payload.encode()) <= 128 * 1024, "report-bound")
    print(payload)
    # This characterization never replaces or clears the original validation RED.
    return 1


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Unavailable:
        print('{"status":"diagnostic-unavailable","stage":"preflight"}')
        raise SystemExit(1)
    except Exception:
        print('{"status":"diagnostic-unavailable","stage":"unexpected-failure"}')
        raise SystemExit(1)
