"""Opt-in hosted synthetic diagnosis; never publish a heap or unparsed SOS output.

Review before activation. --self-test uses synthetic inputs, disposable synthetic
files, and one isolated Python entrypoint subprocess, without dotnet, network, or
heap collection. Normal execution retains the original failing Fact.
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
from unittest import mock
import urllib.request
import xml.etree.ElementTree as ET


PIN = "10.0.745401"
PACKAGE = "https://api.nuget.org/v3-flatcontainer/dotnet-dump/10.0.745401/dotnet-dump.10.0.745401.nupkg"
CATALOG = "https://api.nuget.org/v3/catalog0/data/2026.09.10.18.35.53/dotnet-dump.10.0.745401.json"
PACKAGE_SHA512 = "oxVrDv0pAVHe5+wIbu71GFssgX7+DefQwAoTyDt6oweR7GY72zjg/ObH7otpqK6NRZjrtyzFIbmVeMUzaj5EUw=="
PACKAGE_SIZE = 10453685
FACT = "Legacy.Maliev.Intranet.Tests.PooledTestHostReleaseContractTests.DisposedOrdinaryBffHost_ReleasesProviderAndTransportAfterObservedPoolExpiry"
COHORT_CLASSES = tuple("Legacy.Maliev.Intranet.Tests." + name for name in (
    "PooledTestHostReleaseContractTests", "TestHostLimiterDisposalContractTests", "OrderInitialStatusHttpTests"))
COHORT_FILTER = "|".join("FullyQualifiedName~" + name for name in COHORT_CLASSES)
COHORT_TOTAL = 48
WITNESS = "Legacy.Maliev.Intranet.Tests.PooledTestHostReleaseContractTests+RetainedHost"
FACTORY = "Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`1+DelegatedWebApplicationFactory"
PROVIDER = "Microsoft.Extensions.DependencyInjection.ServiceProvider"
FAILURE = "Disposed ordinary BFF factory remained rooted beyond observed handler expiry and cleanup."
HEX = r"[0-9a-fA-F]{1,16}"
FAMILIES = ("System.", "Microsoft.", "Legacy.Maliev.", "Maliev.Aspire.ServiceDefaults.",
            "Polly.", "OpenTelemetry.", "Xunit.", "xunit.", "Grpc.", "Program+")
MAX_TEXT = 1024 * 1024
SDK_VERSION = "10.0.401"
CAPTURE_DEADLINE = None
FRAME_NAMES = frozenset(("GCFrame", "InlinedCallFrame", "TailCallFrame", "ResumableFrame",
    "RedirectedThreadFrame", "FaultingExceptionFrame", "SoftwareExceptionFrame", "FuncEvalFrame",
    "ComMethodFrame", "CLRToCOMMethodFrame", "ComPrestubMethodFrame", "PInvokeCalliFrame",
    "HijackFrame", "PrestubMethodFrame", "CallCountingHelperFrame", "StubDispatchFrame",
    "ExternalMethodFrame", "DynamicHelperFrame", "ProtectValueClassFrame", "DebuggerClassInitMarkFrame",
    "DebuggerExitFrame", "DebuggerU2MCatchHandlerFrame", "ExceptionFilterFrame", "InterpreterFrame"))
PRIMITIVES = frozenset(("Void", "Boolean", "SByte", "Byte", "Int16", "UInt16", "Char", "IntPtr",
                       "UIntPtr", "Int32", "UInt32", "Int64", "UInt64", "Single", "Double", "TypedReference"))


class Unavailable(Exception):
    """Only fixed stage codes, never raw exception messages, leave this program."""


class EnvironmentUnavailable(Unavailable):
    """Fixed enum counts and name-only digests; never retain names or any values."""

    def __init__(self, counts, unknown_name_sha256=()):
        super().__init__("target-environment-not-allowlisted")
        self.counts = counts
        self.unknown_name_sha256 = sorted(set(unknown_name_sha256))


class ObservationUnavailable(Unavailable):
    """Actual bounded TRX counters and fixed reason codes, never test output."""

    def __init__(self, observation, reason):
        super().__init__(reason)
        self.observation = observation


class MetadataUnavailable(Unavailable):
    """Fixed source context and numeric grammar discriminators, never rejected text."""

    def __init__(self, code, context, value):
        super().__init__(code)
        self.context = context
        self.shape = {"length": min(len(value), 2049), "spaces": min(value.count(" "), 2049),
                      "equals": min(value.count("="), 2049),
                      "leading_space": value.startswith(" "), "trailing_space": value.endswith(" "),
                      "assembly_attributes": "Version=" in value,
                      "byref_suffix": " ByRef" in value, "function_pointer_separator": " (" in value}


def sdk_environment_paths(dotnet, sdk_dir, version):
    """Exact path values only; never normalize an observed environment value."""
    require(version == SDK_VERSION, "selected-sdk-version")
    require(dotnet.is_absolute() and dotnet.name == "dotnet"
            and sdk_dir == dotnet.parent / "sdk" / SDK_VERSION, "sdk-root-layout")
    return {b"MSBuildExtensionsPath": (str(sdk_dir) + "/").encode(),
            b"MSBuildSDKsPath": str(sdk_dir / "Sdks").encode()}


def verified_sdk_environment(dotnet, version):
    sdk_dir = dotnet.parent / "sdk" / SDK_VERSION
    expected = sdk_environment_paths(dotnet, sdk_dir, version)
    directories = (dotnet.parent, sdk_dir.parent, sdk_dir, sdk_dir / "Sdks")
    files = (dotnet, sdk_dir / "dotnet.dll", sdk_dir / "MSBuild.dll")
    require(all(path.is_dir() for path in directories) and all(path.is_file() for path in files),
            "sdk-installed-layout")
    for path in directories + files:
        require(not path.is_symlink() and path.resolve(strict=True) == path, "sdk-link-escape")
    return expected


def launch_environment_expectations(dotnet):
    """Exact SDK/VMR launch literals for this Linux x64, clean-environment lane."""
    require(dotnet.is_absolute() and dotnet.name == "dotnet", "launch-dotnet-identity")
    # SDK v10.0.401 Program.cs:59 and VSTestForwardingApp.cs:50-51;
    # VMR 981be135... XMake.cs:787,2254. No inherited operator settings consulted.
    fixed = {b"MSBUILDFAILONDRIVEENUMERATINGWILDCARD": b"1",
             b"MSBUILDENSURESTDOUTFORTASKPROCESSES": b"1",
             b"MSBuildLoadMicrosoftTargetsReadOnly": b"true",
             b"VSTEST_DOTNET_ROOT_PATH": str(dotnet.parent).encode(),
             b"VSTEST_DOTNET_ROOT_ARCHITECTURE": b"X64",
             # SDK 10.0.401's VMR 981be135... TestTaskUtils.cs:216-219,
             # also Test.Sdk 17.14.1:215-218: --nologo writes exactly this literal.
             b"VSTEST_MSBUILD_NOLOGO": b"1"}
    # XMake writes precisely these two literals according to the chosen logger.
    enumerated = {b"_MSBUILDTLENABLED": frozenset((b"0", b"1"))}
    return fixed, enumerated


def verify_target_environment(target, allowed, enumerated=None):
    # Classification grants no permission: anything outside exact expected values fails closed.
    # Exact SDK v10.0.401 launch sources: Program, MSBuildForwardingApp[WithoutLogging],
    # Commands/Test/VSTest/{TestCommand,VSTestForwardingApp}. Its bundled MSBuild
    # XMake and BuildEnvironmentHelper at dotnet/dotnet 981be135426277fabd550b94fa14cdaf7271b9c2
    # set/read the remaining exact identities. Conditional/read-only identities are
    # classified too, never treated as proof that their values are safe or expected.
    known = {b"MSBuildExtensionsPath": "sdk-extensions-path",
             b"MSBuildSDKsPath": "sdk-sdks-path", b"MSBUILDUSESERVER": "sdk-build-server",
             b"MSBUILDFAILONDRIVEENUMERATINGWILDCARD": "sdk-drive-wildcard",
             b"MSBUILDENSURESTDOUTFORTASKPROCESSES": "sdk-task-stdout",
             b"VSTEST_DOTNET_ROOT_PATH": "vstest-root-path",
             b"VSTEST_DOTNET_ROOT_ARCHITECTURE": "vstest-root-architecture",
             b"VSTEST_MSBUILD_NOLOGO": "vstest-msbuild-no-logo",
             b"DOTNET_CLI_TELEMETRY_SESSIONID": "sdk-telemetry-session",
             b"MSBuildLoadMicrosoftTargetsReadOnly": "msbuild-readonly-targets",
             b"_MSBUILDTLENABLED": "msbuild-terminal-logger",
             b"MSBUILD_EXE_PATH": "msbuild-executable-path",
             # Exact read-only identities from pinned VSTestTask/Task2 and SDK UI flow.
             # Classification only, including debug keys; never permission to enable them.
             b"VSTEST_BUILD_TRACE": "vstest-build-trace",
             b"VSTEST_BUILD_DEBUG": "vstest-build-debug",
             b"VSTEST_DISABLE_UTF8_CONSOLE_ENCODING": "vstest-encoding-toggle",
             b"DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION": "runtime-ansi-redirection",
             b"DOTNET_CLI_UI_LANGUAGE": "sdk-ui-language",
             b"VSLANG": "sdk-vs-language", b"PreferredUILang": "sdk-preferred-ui-language"}
    require(len(target) <= 256 and sum(len(k) + len(v) for k, v in target.items()) <= 64 * 1024,
            "target-environment-bound")
    counts = {}
    unknown_name_sha256 = []
    enumerated = enumerated or {}
    for key, value in target.items():
        if key in allowed and allowed[key] == value:
            continue
        if key in enumerated and value in enumerated[key]:
            continue
        category = ("allowlisted-value-changed" if key in allowed or key in enumerated
                    else known.get(key, "unknown-addition"))
        counts[category] = counts.get(category, 0) + 1
        if category == "unknown-addition":
            unknown_name_sha256.append(hashlib.sha256(key).hexdigest())
    if counts:
        raise EnvironmentUnavailable(counts, unknown_name_sha256)


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


def signature_type(value):
    value = value.removesuffix(" ByRef")
    base = re.sub(r"(?:\[,*\]|[&*])+$", "", value)
    if base not in PRIMITIVES:
        metadata(value, "signature-type")


def metadata(value, context="type"):
    try:
        require(len(value) <= 2048, "unknown-metadata-identity")
        require(re.fullmatch(r"[A-Za-z0-9_.$`+<>\[\](), =!&?*\-]+", value) is not None,
                "unknown-metadata-grammar")
        canonical = re.sub(r", Version=\d+(?:\.\d+){3}", "", value)
        canonical = re.sub(r", Culture=neutral", "", canonical)
        canonical = re.sub(r", PublicKeyToken=(?:[0-9a-fA-F]{16}|null)", "", canonical)
        canonical = canonical.replace(", ", ",")
        identity = canonical
        if context == "stack-method":
            # DAC GetMethodDescName -> TypeString.AppendMethodInternal ->
            # SigFormat.GetCStringParmsOnly: ordinary return prefixes are absent.
            # Only source-defined spacing inside parameter types is admitted.
            # AddTypeString emits exactly ' ByRef'; no local/argument names.
            canonical = re.sub(r"(?<=[A-Za-z0-9_\]>*&]) ByRef(?=[,\)\]>*&]|$)", "&", canonical)
            # ELEMENT_TYPE_FNPTR emits its validated return type plus ' ('.
            def function_pointer(match):
                signature_type(match.group(1))
                return match.group(1) + "("
            canonical = re.sub(r"([A-Za-z0-9_.$`+<>\[\],!&?*\-]+) \(", function_pointer, canonical)
            require(canonical.endswith(")"), "stack-method-signature")
        require(identity == "Program" or identity.startswith(FAMILIES)
                or (context == "stack-method" and identity.startswith("Program.")), "unknown-metadata-identity")
        require("=" not in canonical and " " not in canonical, "metadata-value-injection")
        return value
    except MetadataUnavailable:
        raise
    except Unavailable as failure:
        raise MetadataUnavailable(str(failure), context, value) from None


def lines(text):
    require(len(text.encode("utf-8")) <= MAX_TEXT and "\x00" not in text, "output-bound")
    return [line.strip() for line in text.splitlines() if line.strip()]


def addresses(text):
    rows = lines(text)
    require(rows and all(re.fullmatch(HEX, row) for row in rows), "heap-address-format")
    result = [pointer_id(row) for row in rows]
    require(len(result) == len(set(result)) and len(result) <= 64, "heap-address-count")
    return result


def pointer_id(value, allow_zero=False):
    require(re.fullmatch(HEX, value) is not None and (allow_zero or int(value, 16) != 0),
            "pointer-identity-format")
    return format(int(value, 16), "x")


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
        elif match := re.fullmatch(rf"({HEX})\s+WeakShort\s+({HEX})\s+(\d+)\s+(.+)", row):
            require(not in_statistics, "handle-order")
            handle, target, size, kind = match.groups()
            handle = pointer_id(handle)
            target = pointer_id(target, allow_zero=True)
            require(handle not in result, "duplicate-handle")
            # GCHandlesImpl emits literal sentinels for cleared/unreadable/free
            # weak targets. Never bind these rows as a reviewed live witness.
            if kind in ("<error>", "<free>"):
                require(size == "0", "handle-sentinel-size")
            else:
                metadata(kind, "weak-handle-type")
            result[handle] = (target, kind)
        elif in_statistics and (re.fullmatch(r"MT\s+Count\s+TotalSize\s+Class Name", row)
                                or re.fullmatch(r"Total\s+\d+ objects", row)):
            pass
        elif in_statistics and (match := re.fullmatch(rf"{HEX}\s+\d+\s+\d+\s+(.+)", row)):
            if match.group(1) not in ("Free", "UNKNOWN"):
                metadata(match.group(1), "weak-handle-stat-type")
        else:
            raise Unavailable("unknown-handle-output")
    require(result and len(result) <= 10000, "handle-count")
    return result


def root_paths(text, target, non_stack=False):
    target = pointer_id(target)
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
        elif match := re.fullmatch(rf"({HEX}) \((strong handle|pinned handle|async pinned handle|sized ref handle|dependent handle|finalizer root|ref counted handle RefCount: [\d,]+)\)", row):
            require(context is not None and context["kind"] != "stack", "root-header")
            kind = match.group(2)
            if kind.startswith("ref counted handle RefCount:"):
                kind = "ref counted handle"
            current = {**context, "root": pointer_id(match.group(1)), "handle_kind": kind, "nodes": []}
            paths.append(current)
        elif match := re.fullmatch(rf"({HEX})(?:\s+{HEX})?\s+(.+)", row):
            require(context is not None and context["kind"] == "stack", "stack-frame-context")
            context.pop("method", None)
            context.pop("frame", None)
            method = match.group(2)
            # Source paths are not exported; do not permit locals/argument values.
            source = re.search(r" \[(/[^\r\n\[\]]+\.cs) @ (\d+)\]$", method)
            if source:
                method = method[:source.start()]
            frame = re.fullmatch(r"\[([A-Za-z0-9_]+)\](?: {1,2}(.+))?", method)
            if frame:
                # Exact pinned runtime FrameTypes.h/frames.h identities plus
                # the reviewed helper-frame family; no arbitrary frame labels.
                require(frame.group(1) in FRAME_NAMES or re.fullmatch(
                    r"HelperMethodFrame(?:_[A-Z0-9]+)?", frame.group(1)), "unknown-stack-frame")
                context["frame"] = frame.group(1)
                method = frame.group(2)
                # Pinned GCRootCommand.GetFrameOutput wraps a managed method
                # in parentheses when emitting its explicit helper-frame name.
                if method is not None and method.startswith("(") and method.endswith(")"):
                    method = method[1:-1]
            if method is not None:
                context["method"] = metadata(method, "stack-method")
        elif match := re.fullmatch(rf"(r(?:[abcd]x|[sd]i|[sb]p|[89]|1[0-5])|\?\?\?)([+-]{HEX})?:\s*({HEX})?", row):
            require(context is not None and context.get("kind") == "stack" and ("method" in context or "frame" in context),
                    "stack-register-context")
            # GetRegisterOutput emits only register, optional signed hex offset,
            # and optional stack-slot address. Never parse a local value/name.
            current = {**context, "register": match.group(1), "offset": match.group(2),
                       "stack_slot": match.group(3), "nodes": []}
            paths.append(current)
        elif re.fullmatch(HEX, row):
            # GetRegisterOutput prints only the x16 stack slot when both
            # RegisterName and RegisterOffset are absent. It remains private.
            require(context is not None and context.get("kind") == "stack" and ("method" in context or "frame" in context),
                    "stack-slot-context")
            current = {**context, "stack_slot": row, "nodes": []}
            paths.append(current)
        elif match := re.fullmatch(rf"->\s+({HEX})\s+(.+)", row):
            require(current is not None and len(current["nodes"]) < 128, "root-node-context")
            kind = match.group(2)
            edge = None
            if kind.endswith(" (dependent handle)"):
                kind, edge = kind[:-19], "dependent"
            elif " (static variable: " in kind and kind.endswith(")"):
                kind, edge = kind[:-1].split(" (static variable: ", 1)
                edge = metadata(edge, "static-edge")
            current["nodes"].append({"id": pointer_id(match.group(1)), "type": metadata(kind, "root-node-type"), "edge": edge})
        elif match := re.fullmatch(r"Found (\d+) unique roots\.", row):
            require(summary is None, "duplicate-root-summary")
            summary = int(match.group(1))
        else:
            raise Unavailable("unknown-root-output")
    require(summary is not None and summary == len(paths) and summary <= 32, "root-summary")
    # Native GCHandles ObjectPtr is x16; pinned managed Formats.Pointer uses x12
    # on x64. Both represent the same pointer; bind numeric identity, never a
    # textual prefix/suffix or a different target merely matching its type.
    require(all(p["nodes"] and int(p["nodes"][-1]["id"], 16) == int(target, 16) for p in paths), "root-target")
    require(non_stack or paths, "no-retained-root")
    return {"paths": paths, "capped": summary == 32}


def command_output(text, dump, runtime_dir):
    """Exact pinned noninteractive framing; result parsers reject every other line."""
    # v10.0.745401 Analyzer.cs:42,114-123 runs --command without REPL echoes;
    # SetClrPathCommand.cs:60 prints this exact acknowledgement for an argument.
    rows = lines(text)
    require(len(rows) >= 2 and rows[:2] == [f"Loading core dump: {dump} ...",
            f"Set load path for DAC/DBI to '{runtime_dir}'"], "unknown-sos-framing")
    return "\n".join(rows[2:])


def verify_dump_version(text):
    """Verify native FileVersion's encoded product identity, not a product string."""
    # Runtime v10.0.12 clrversion.h uses RuntimeFile*Version. Its pinned Arcade
    # at VMR 6f10c96a... CalculateAssemblyAndFileVersions.cs:102-107 encodes
    # minor*100 + patch//100; (patch%100)*100 + yy; mm*5000 + dd*100 + r.
    # This remains additional to mapped runtime10.0.12 and exact DAC checks.
    found = False
    suffix = r"(?:\+[0-9a-f]+| @Commit: ?[0-9a-f]+)?"
    for row in lines(text):
        if match := re.fullmatch(r"(\d{1,5}(?:\.\d{1,5}){2,3})" + suffix, row):
            version = tuple(map(int, match.group(1).split(".")))
            if len(version) == 3:
                require(version == (10, 0, 12), "dump-runtime-version")
            else:
                major, minor, build, revision = version
                month, remainder = divmod(revision, 5000)
                require(major == 10 and minor == 0 and build // 100 == 12
                        and 1 <= month <= 12 and revision <= 65535
                        and any(0 <= remainder - day * 100 <= 199 for day in range(1, 32)),
                        "dump-runtime-version")
            found = True
        elif re.fullmatch(r"SOS Version: [0-9.]+" + suffix
                          + r"|Server mode with \d+ gc heaps|Workstation mode|DATAS \d+", row):
            pass
        else:
            raise Unavailable("unknown-version-output")
    require(found, "dump-runtime-version")


def signal_handle_ids(rows, head):
    require(len(rows) == 7 and rows[1:5] == [f"head={head}", f"factory={FACTORY}",
            f"provider={PROVIDER}", f"witness={WITNESS}"], "signal-identity")
    handles = {}
    for name, row in zip(("Factory", "Provider"), rows[5:]):
        match = re.fullmatch(name.lower() + r"-handle=([0-9a-f]{16})", row)
        require(match is not None, "signal-weak-handle-format")
        handle = match.group(1)
        require(int(handle, 16) != 0 and int(handle, 16) & 3 == 0, "unsupported-weak-handle-tags")
        handles[name] = pointer_id(handle)
    require(len(set(handles.values())) == 2, "signal-weak-handle-alias")
    return handles


def bind_weak_target(handle, table, expected):
    handle = pointer_id(handle)
    require(handle in table, "weak-handle-not-found")
    target, kind = table[handle]
    require(int(target, 16) != 0 and (kind == expected or (expected == FACTORY and kind.startswith(FACTORY + "["))),
            "weak-target-identity")
    return target, kind


def public_root_paths(parsed):
    # Retain type/ownership relationships only. Addresses, thread IDs and stack
    # slots remain in the private parser state and are never published.
    return {"capped": parsed["capped"], "paths": [
        {**{k: p[k] for k in ("kind", "handle_kind", "frame", "method", "register") if k in p},
         "nodes": [{"type": n["type"], "edge": n["edge"]} for n in p["nodes"]]}
        for p in parsed["paths"]]}


def observed_trx(trx_bytes, cohort=False):
    require(len(trx_bytes) < 128 * 1024 and b"<!DOCTYPE" not in trx_bytes, "trx-bound")
    xml = ET.fromstring(trx_bytes)
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counters = xml.findall(".//t:Counters", ns)
    require(len(counters) == 1, "trx-counter-identity")
    counts = counters[0].attrib
    zero = dict.fromkeys(("error", "timeout", "aborted", "inconclusive",
                              "passedButRunAborted", "notRunnable", "notExecuted", "disconnected",
                              "warning", "completed", "inProgress", "pending"), "0")
    total = COHORT_TOTAL if cohort else 1
    require(set(counts) == set(zero) | {"total", "executed", "passed", "failed"}
            and all(counts[key] == value for key, value in zero.items())
            and counts["total"] == counts["executed"] == str(total)
            and all(re.fullmatch(r"0|[1-9]\d{0,3}", counts[key]) for key in ("passed", "failed"))
            and int(counts["passed"]) + int(counts["failed"]) == total, "trx-counts")
    results = xml.findall(".//t:UnitTestResult", ns)
    require(len(results) == total
            and all(result.attrib.get("outcome") in ("Passed", "Failed") for result in results)
            and sum(result.attrib["outcome"] == "Failed" for result in results) == int(counts["failed"]),
            "trx-result-counts")
    require(all(result.attrib.get("testName", "").startswith(tuple(name + "." for name in COHORT_CLASSES))
                if cohort else result.attrib.get("testName") == FACT for result in results), "trx-class-identity")
    original = [result for result in results if result.attrib.get("testName") == FACT]
    require(len(original) == 1, "trx-original-identity")
    outcome = original[0].attrib["outcome"]
    if outcome == "Failed":
        messages = original[0].findall(".//t:Message", ns)
        require(len(messages) == 1 and messages[0].text == FAILURE, "trx-failure-identity")
    class_counts = {}
    for name in COHORT_CLASSES:
        selected = [result for result in results if result.attrib["testName"].startswith(name + ".")]
        if selected:
            class_counts[name] = {"executed": len(selected),
                                 "passed": sum(result.attrib["outcome"] == "Passed" for result in selected),
                                 "failed": sum(result.attrib["outcome"] == "Failed" for result in selected)}
    return {"total": total, "executed": total, "passed": int(counts["passed"]),
            "failed": int(counts["failed"]), "original_outcome": outcome,
            "class_counts": class_counts}


def verify_original_trx(trx_bytes, cohort=False):
    observed = observed_trx(trx_bytes, cohort)
    require(observed["original_outcome"] == "Failed" and observed["failed"] == 1,
            "trx-original-failure-not-preserved")
    return observed


def no_signal_reason(observation, text):
    if observation["original_outcome"] == "Passed":
        return "isolated-no-reproduction" if observation["total"] == 1 else "cohort-no-reproduction"
    reasons = {"exact weak-handle ownership metadata was not available.": "weak-handle-guard-unavailable",
               "hosted diagnostic prerequisites were not satisfied.": "diagnostic-prerequisite-unavailable",
               "signal file could not be written.": "diagnostic-signal-write-unavailable"}
    found = [code for message, code in reasons.items()
             if "Synthetic root capture unavailable: " + message in text]
    return found[0] if len(found) == 1 else "failed-without-diagnostic-signal"


def run_private(args, env, cwd, private, label, seconds=60):
    log = private / (label + ".log")
    with log.open("wb") as stream:
        process = subprocess.Popen(args, cwd=cwd, env=env, stdout=stream,
                                   stderr=subprocess.STDOUT, start_new_session=True)
        try:
            deadline = time.monotonic() + seconds
            if CAPTURE_DEADLINE is not None:
                deadline = min(deadline, CAPTURE_DEADLINE)
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


def capture(args, private, cohort=False):
    require(platform.system() == "Linux" and platform.machine() == "x86_64", "runner-platform")
    require(re.fullmatch(r"[0-9a-f]{40}", args.head), "head-format")
    workspace, dotnet = Path(args.workspace).resolve(), Path(args.dotnet).resolve()
    require(dotnet.is_file() and dotnet.name == "dotnet", "dotnet-identity")
    project = workspace / "Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj"
    assembly = project.parent / "bin/Release/net10.0/Legacy.Maliev.Intranet.Tests.dll"
    require(project.is_file() and assembly.is_file(), "built-release-prerequisite")
    env = clean_environment(private, dotnet, workspace, args.head)
    selected_sdk = run_private([str(dotnet), "--version"], env, workspace, private,
                               "selected-sdk", seconds=15).strip()
    sdk_expected = verified_sdk_environment(dotnet, selected_sdk)
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
                                 "--filter", COHORT_FILTER if cohort else "FullyQualifiedName=" + FACT,
                                 "--logger", "trx;LogFileName=isolated-pooled-host.trx",
                                 "--results-directory", str(trx_dir)],
                                env=env, cwd=workspace, stdout=output, stderr=subprocess.STDOUT,
                                start_new_session=True)
        try:
            deadline = min(time.monotonic() + 360, CAPTURE_DEADLINE)
            ready = None
            while time.monotonic() < deadline and test.poll() is None:
                require(log.stat().st_size <= MAX_TEXT, "isolated-test-output-bound")
                markers = list(Path(env["TMPDIR"]).glob("maliev-intranet-pooled-root-*.ready"))
                require(len(markers) <= 1, "ambiguous-signal")
                if markers:
                    ready = markers[0]
                    break
                time.sleep(0.2)
            if ready is None:
                require(test.poll() is not None, "diagnostic-test-timeout")
                trx = trx_dir / "isolated-pooled-host.trx"
                require(trx.is_file(), "no-signal-trx-unavailable")
                observation = observed_trx(trx.read_bytes(), cohort)
                require(test.returncode == (1 if observation["failed"] else 0), "trx-exit-consistency")
                raise ObservationUnavailable(observation, no_signal_reason(
                    observation, log.read_text(encoding="utf-8", errors="strict")))
            require(ready.stat().st_size <= 4096, "signal-size-bound")
            # File.WriteAllLines may still be completing when the marker first appears.
            for _ in range(5):
                if len(ready.read_text(encoding="utf-8").splitlines()) == 7:
                    break
                time.sleep(0.1)
            rows = lines(ready.read_text(encoding="utf-8"))
            witness_handles = signal_handle_ids(rows, args.head)
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
            allowed.update(sdk_expected)
            launch_fixed, launch_enumerated = launch_environment_expectations(dotnet)
            allowed.update(launch_fixed)
            verify_target_environment(target_env, allowed, launch_enumerated)
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

    trx = trx_dir / "isolated-pooled-host.trx"
    require(trx.is_file(), "trx-unavailable")
    observation = observed_trx(trx.read_bytes(), cohort)
    # Preserve actual observation before any offline parser can reject output.
    args.observed_counts = observation
    args.capture_filter = "focused-cohort" if cohort else "original-fact"
    verify_original_trx(trx.read_bytes(), cohort)

    def sos(commands, label):
        sections = {}
        # One result per process avoids inferring boundaries between unframed
        # --command outputs. Exact DAC acknowledgement remains mandatory.
        for index, value in enumerate(commands):
            command = [str(tool), "analyze", str(dump), "--command", f"setclrpath {runtime_dir}",
                       "--command", value, "--command", "exit"]
            text = run_private(command, env, private, private, f"{label}-{index}")
            sections[value] = command_output(text, dump, runtime_dir)
        return sections

    initial = sos(["eeversion", f"dumpheap -type {WITNESS} -short", "gchandles -type WeakShort"], "sos-inventory")
    verify_dump_version(initial["eeversion"])
    witness_ids = addresses(initial[f"dumpheap -type {WITNESS} -short"])
    require(len(witness_ids) == 1, "ambiguous-witness")
    handle_table = weak_handles(initial["gchandles -type WeakShort"])
    mappings = {}
    roots = {}
    for name, expected in (("Factory", FACTORY), ("Provider", PROVIDER)):
        target, kind = bind_weak_target(witness_handles[name], handle_table, expected)
        mappings[name] = {"type": kind}
        commands = ["gcroot -limit 32 " + target, "gcroot -nostacks -limit 32 " + target]
        sections = sos(commands, "sos-roots-" + name)
        roots[name] = {"all": public_root_paths(root_paths(sections[commands[0]], target)),
                       "non_stack": public_root_paths(root_paths(sections[commands[1]], target, non_stack=True))}
    return {"status": "evidence", "head": args.head, "checkout": checkout, "pid": pid,
            "tool": PIN, "package_source": PACKAGE, "package_sha512": PACKAGE_SHA512,
            "runtime": "10.0.12", "dac_sha256": hashlib.sha256(dac.read_bytes()).hexdigest(),
            "test_assembly_sha256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
            "original_test": {"executed": 1, "failed": 1}, "observed_test_counts": observation,
            "capture_filter": "focused-cohort" if cohort else "original-fact", "witness_count": len(witness_ids),
            "mappings": mappings, "roots": roots}


class ParserControls(unittest.TestCase):
    def rejects(self, function, *args):
        with self.assertRaises(Unavailable):
            function(*args)

    def test_environment_classification_privacy(self):
        allowed = {b"HOME": b"/private/home", b"GITHUB_TOKEN": b""}
        verify_target_environment(dict(allowed), allowed)
        target = dict(allowed)
        target.update({b"MSBuildExtensionsPath": b"sensitive SDK path",
                       b"MSBuildSDKsPath": b"sensitive SDK path", b"MSBUILDUSESERVER": b"0"})
        target[b"GITHUB_TOKEN"] = b"synthetic-secret-value"
        target[b"UNKNOWN_SYNTHETIC_SECRET_NAME"] = b"synthetic-secret-value"
        with self.assertRaises(EnvironmentUnavailable) as caught:
            verify_target_environment(target, allowed)
        self.assertEqual(caught.exception.counts,
                         {"sdk-extensions-path": 1, "sdk-sdks-path": 1, "sdk-build-server": 1,
                          "allowlisted-value-changed": 1, "unknown-addition": 1})
        payload = json.dumps({"stage": str(caught.exception), "environment_issues": caught.exception.counts})
        for sensitive in ("sensitive SDK path", "synthetic-secret-value", "UNKNOWN_SYNTHETIC_SECRET_NAME",
                          "MSBuildExtensionsPath", "MSBuildSDKsPath", "GITHUB_TOKEN"):
            self.assertNotIn(sensitive, payload)
        self.rejects(verify_target_environment, {b"x": b"x" * (64 * 1024)}, {})
        self.rejects(verify_target_environment, {str(i).encode(): b"" for i in range(257)}, {})

    def test_fixed_launch_environment_categories(self):
        additions = {b"MSBUILDFAILONDRIVEENUMERATINGWILDCARD": "sdk-drive-wildcard",
                     b"MSBUILDENSURESTDOUTFORTASKPROCESSES": "sdk-task-stdout",
                     b"VSTEST_DOTNET_ROOT_PATH": "vstest-root-path",
                     b"VSTEST_DOTNET_ROOT_ARCHITECTURE": "vstest-root-architecture",
                     b"DOTNET_CLI_TELEMETRY_SESSIONID": "sdk-telemetry-session",
                     b"MSBuildLoadMicrosoftTargetsReadOnly": "msbuild-readonly-targets",
                     b"_MSBUILDTLENABLED": "msbuild-terminal-logger",
                     b"MSBUILD_EXE_PATH": "msbuild-executable-path",
                     b"VSTEST_BUILD_TRACE": "vstest-build-trace",
                     b"VSTEST_BUILD_DEBUG": "vstest-build-debug",
                     b"VSTEST_DISABLE_UTF8_CONSOLE_ENCODING": "vstest-encoding-toggle",
                     b"DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION": "runtime-ansi-redirection",
                     b"DOTNET_CLI_UI_LANGUAGE": "sdk-ui-language",
                     b"VSLANG": "sdk-vs-language", b"PreferredUILang": "sdk-preferred-ui-language"}
        for name, category in additions.items():
            # Even source-known keys with synthetic malicious values must still reject.
            with self.assertRaises(EnvironmentUnavailable) as caught:
                verify_target_environment({name: b"synthetic-secret-value"}, {})
            self.assertEqual(caught.exception.counts, {category: 1})
            self.assertNotIn(name.decode(), json.dumps(caught.exception.counts))
            with self.assertRaises(EnvironmentUnavailable) as unknown:
                verify_target_environment({name + b"_UNREVIEWED_SUFFIX": b"synthetic-secret-value"}, {})
            self.assertEqual(unknown.exception.counts, {"unknown-addition": 1})
        with self.assertRaises(EnvironmentUnavailable) as changed:
            verify_target_environment({b"DOTNET_HOST_PATH": b"/untrusted/dotnet"},
                                      {b"DOTNET_HOST_PATH": b"/verified/dotnet"})
        self.assertEqual(changed.exception.counts, {"allowlisted-value-changed": 1})

    def test_exact_sdk_path_values(self):
        with tempfile.TemporaryDirectory(prefix="synthetic-sdk-control-") as directory:
            root = Path(directory).resolve()
            dotnet = root / "dotnet"
            sdk_dir = root / "sdk" / SDK_VERSION
            (sdk_dir / "Sdks").mkdir(parents=True)
            for path in (dotnet, sdk_dir / "dotnet.dll", sdk_dir / "MSBuild.dll"):
                path.write_bytes(b"synthetic, never executed")
            expected = verified_sdk_environment(dotnet, SDK_VERSION)
            self.assertEqual(expected[b"MSBuildExtensionsPath"], (str(sdk_dir) + "/").encode())
            self.assertEqual(expected[b"MSBuildSDKsPath"], str(sdk_dir / "Sdks").encode())
            verify_target_environment(expected, expected)
            for key, value in expected.items():
                for mutation in (value + b"/", value + b"../escape", b"/untrusted/path", value + b"\n"):
                    self.rejects(verify_target_environment, {key: mutation}, expected)
            self.rejects(verify_target_environment,
                         {b"MSBuildExtensionsPath": str(sdk_dir).encode()}, expected)
            self.rejects(sdk_environment_paths, dotnet, root.parent / "sdk" / SDK_VERSION, SDK_VERSION)
            self.rejects(sdk_environment_paths, dotnet, sdk_dir, "10.0.400")
            self.rejects(sdk_environment_paths, dotnet, sdk_dir / ".." / SDK_VERSION, SDK_VERSION)
            self.rejects(sdk_environment_paths, Path("relative/dotnet"), sdk_dir, SDK_VERSION)
            # A selected executable from another root cannot authorize this SDK directory.
            self.rejects(sdk_environment_paths, root.parent / "dotnet", sdk_dir, SDK_VERSION)
            # Pure link-negative control avoids platform-dependent symlink privileges.
            with mock.patch.object(Path, "is_symlink", return_value=True):
                self.rejects(verified_sdk_environment, dotnet, SDK_VERSION)
            (sdk_dir / "MSBuild.dll").unlink()
            self.rejects(verified_sdk_environment, dotnet, SDK_VERSION)

    def test_unknown_environment_diagnosis_retains_name_digests_only(self):
        name = b"SYNTHETIC_UNKNOWN_NAME"
        value = b"synthetic-secret-value"
        with self.assertRaises(EnvironmentUnavailable) as caught:
            verify_target_environment({name: value}, {})
        failure = caught.exception
        self.assertEqual(failure.counts, {"unknown-addition": 1})
        self.assertEqual(failure.unknown_name_sha256, [hashlib.sha256(name).hexdigest()])
        exported = json.dumps({"counts": failure.counts, "names": failure.unknown_name_sha256})
        self.assertNotIn(name.decode(), exported)
        self.assertNotIn(value.decode(), exported)
        self.assertNotIn(hashlib.sha256(value).hexdigest(), exported)
        # Known rejected values remain fixed categories and acquire no unknown-name digest.
        with self.assertRaises(EnvironmentUnavailable) as known:
            verify_target_environment({b"VSTEST_BUILD_DEBUG": value}, {})
        self.assertEqual(known.exception.counts, {"vstest-build-debug": 1})
        self.assertEqual(known.exception.unknown_name_sha256, [])

    def test_exact_launch_values(self):
        dotnet = Path(tempfile.gettempdir()).resolve() / "synthetic-root" / "dotnet"
        fixed, enumerated = launch_environment_expectations(dotnet)
        self.assertEqual(fixed[b"VSTEST_DOTNET_ROOT_PATH"], str(dotnet.parent).encode())
        self.assertEqual(fixed[b"VSTEST_DOTNET_ROOT_ARCHITECTURE"], b"X64")
        self.assertEqual(fixed[b"VSTEST_MSBUILD_NOLOGO"], b"1")
        for bad in (b"0", b"true", b"false", b"01", b"1 ", b"1\n"):
            self.rejects(verify_target_environment, {b"VSTEST_MSBUILD_NOLOGO": bad}, fixed, enumerated)
        for terminal in (b"0", b"1"):
            verify_target_environment({**fixed, b"_MSBUILDTLENABLED": terminal}, fixed, enumerated)
        for key, value in fixed.items():
            for bad in (value + b" ", value + b"\n", b"synthetic-secret", value + b"/../escape"):
                self.rejects(verify_target_environment, {key: bad}, fixed, enumerated)
        for bad in (b"true", b"false", b"2", b"01", b"1 ", b"1\n"):
            self.rejects(verify_target_environment, {b"_MSBUILDTLENABLED": bad}, fixed, enumerated)
        for key, bad in ((b"MSBuildLoadMicrosoftTargetsReadOnly", b"TRUE"),
                         (b"VSTEST_DOTNET_ROOT_ARCHITECTURE", b"x64"),
                         (b"VSTEST_DOTNET_ROOT_PATH", str(dotnet.parent).encode() + b"/")):
            self.rejects(verify_target_environment, {key: bad}, fixed, enumerated)
        for key in (b"MSBUILDUSESERVER", b"DOTNET_CLI_TELEMETRY_SESSIONID", b"UNKNOWN_PRIVATE_NAME"):
            self.rejects(verify_target_environment, {key: b"synthetic-secret"}, fixed, enumerated)
        self.rejects(launch_environment_expectations, Path("relative/dotnet"))

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
        self.assertEqual(addresses("000abc\n000def"), ["abc", "def"])
        for bad in ("", "secret=value", "abc\nabc", "abc\n000ABC", "abc\nString: token", "0"):
            self.rejects(addresses, bad)

    def test_metadata(self):
        self.assertEqual(metadata("System.Threading.TimerQueueTimer"), "System.Threading.TimerQueueTimer")
        for bad in ("Unknown.Root", "System.String /secret", "System.Type\ncredential", "System.Type\"secret",
                    "System.String token=secret", "System.Method(System.String token)"):
            self.rejects(metadata, bad)
        self.assertEqual(metadata("System.List`1[[System.String, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]"),
                         "System.List`1[[System.String, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]")

    def test_pinned_runtime_signature_types_and_spacing(self):
        signatures = (
            "System.Threading.Timer.Callback(System.Int32 ByRef, Boolean)",
            "System.Threading.Timer.Callback(Void (System.String, Int32))",
            "System.Threading.Timer.Callback(System.List`1[[System.String, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]] ByRef)")
        for value in signatures:
            self.assertEqual(metadata(value, "stack-method"), value)
            self.rejects(metadata, value, "root-node-type")
        for bad in ("secret System.Threading.Timer.Callback()",
                    "System.String token System.Threading.Timer.Callback()",
                    "Void Unknown.Timer.Callback()", "System.Threading.Timer.Callback(System.String token)",
                    "System.Threading.Timer.Callback(bool arg)",
                    "System.Threading.Timer.Callback(System.String ByRef secret)",
                    "System.Threading.Timer.Callback(secret (System.String))",
                    "System.Threading.Tasks.Task System.Threading.Timer.Callback(System.Object)",
                    "System.Threading.Timer.Callback(token=value)"):
            self.rejects(metadata, bad, "stack-method")

    def test_metadata_context_shape_excludes_rejected_text(self):
        value = "System.String synthetic-sensitive-value"
        with self.assertRaises(MetadataUnavailable) as caught:
            metadata(value, "root-node-type")
        failure = caught.exception
        self.assertEqual(failure.context, "root-node-type")
        self.assertEqual(failure.shape["spaces"], 1)
        serialized = json.dumps({"context": failure.context, "shape": failure.shape})
        self.assertNotIn(value, serialized)
        self.assertNotIn("synthetic-sensitive-value", serialized)
        self.assertNotIn(hashlib.sha256(value.encode()).hexdigest(), serialized)

    def test_weak_field(self):
        text = "Name: System.WeakReference\nMethodTable: abc\nEEClass: def\nSize: 24(0x18) bytes\nFile: /runtime/System.Private.CoreLib.dll\nFields:\nMT Field Offset Type VT Attr Value Name\nabc 4000001 8 System.IntPtr 1 instance 1000 _taggedHandle"
        self.assertEqual(object_fields(text, "System.WeakReference", {"_taggedHandle"}, "System.Private.CoreLib.dll"), {"_taggedHandle": "1000"})
        for bad in (text.replace("System.WeakReference", "System.String"), text + "\nString: secret",
                    text.replace("_taggedHandle", "unreviewed"), text + "\nabc 4000001 8 System.IntPtr 1 instance 1000 _taggedHandle"):
            self.rejects(object_fields, bad, "System.WeakReference", {"_taggedHandle"}, "System.Private.CoreLib.dll")

    def test_handles(self):
        text = "Handle Type Object Size Data Type\n1000 WeakShort 2000 24 System.WeakReference\nStatistics:\nMT Count TotalSize Class Name\nabc 1 24 System.WeakReference\nTotal 1 objects"
        self.assertEqual(weak_handles(text)["1000"], ("2000", "System.WeakReference"))
        for bad in (text.replace("WeakShort", "Strong"), text + "\nSecret: token", text.replace("System.WeakReference", "Unknown.Object")):
            self.rejects(weak_handles, bad)

    def test_pinned_native_handle_sentinels_and_summary(self):
        text = "Handle Type Object Size Data Type\n1000 WeakShort 2000 24 System.WeakReference\n3000 WeakShort 0000 0 <error>\n5000 WeakShort 6000 0 <free>\nStatistics:\nMT Count TotalSize Class Name\nabc 1 24 System.WeakReference\ndef 1 16 Free\nfed 1 32 UNKNOWN\nTotal 3 objects"
        table = weak_handles(text)
        self.assertEqual(table["3000"], ("0", "<error>"))
        self.rejects(bind_weak_target, "3000", table, FACTORY)
        for bad in (text.replace("0 <error>", "1 <error>"), text.replace("<free>", "<unreviewed>"),
                    text.replace("Total 3 objects", "Total 3 objects, 72 bytes"),
                    text + "\nWeak Short Handles: 3", text + "\nSecret: value"):
            self.rejects(weak_handles, bad)

    def test_root_paths(self):
        text = "HandleTable:\n1000 (strong handle)\n-> 2000 System.Object[]\n-> 3000 System.Threading.TimerQueueTimer (static variable: System.Threading.TimerQueue.s_queue)\n-> 4000 Microsoft.Extensions.DependencyInjection.ServiceProvider\nFound 1 unique roots."
        self.assertEqual(root_paths(text, "4000")["paths"][0]["nodes"][-1]["id"], "4000")
        for bad in (text.replace("4000", "5000"), text + "\nString: secret", text.replace("System.Object[]", "Unknown.Root"), text.replace("Found 1", "Found 2"), text.replace("strong handle", "weak short handle")):
            self.rejects(root_paths, bad, "4000")
        self.assertEqual(root_paths(text.replace(" (static variable: System.Threading.TimerQueue.s_queue)", " (dependent handle)"), "4000")["paths"][0]["nodes"][1]["edge"], "dependent")

    def test_native_and_managed_pointer_padding_preserves_identity(self):
        text = "HandleTable:\n0000000000001000 (strong handle)\n-> 000000004000 Microsoft.Extensions.DependencyInjection.ServiceProvider\nFound 1 unique roots."
        parsed = root_paths(text, "0000000000004000")
        self.assertEqual(parsed["paths"][0]["nodes"][-1]["type"], PROVIDER)
        for wrong in ("0000000000004001", "0000000100004000", "0000000000000000", "4000-secret"):
            self.rejects(root_paths, text, wrong)
        self.assertNotIn("000000004000", json.dumps(public_root_paths(parsed)))
        table_text = f"Handle Type Object Size Data Type\n0000000000001000 WeakShort 000000004000 24 {PROVIDER}\nStatistics:\nMT Count TotalSize Class Name\nabc 1 24 {PROVIDER}\nTotal 1 objects"
        table = weak_handles(table_text)
        self.assertEqual(bind_weak_target("000000001000", table, PROVIDER), ("4000", PROVIDER))
        self.rejects(bind_weak_target, "000000001001", table, PROVIDER)
        self.rejects(bind_weak_target, "000000001000", table, FACTORY)
        duplicate = table_text.replace("\nStatistics:", f"\n1000 WeakShort 4000 24 {PROVIDER}\nStatistics:")
        self.rejects(weak_handles, duplicate)

    def test_stack_and_framing(self):
        text = "Thread ab:\n1000 2000 System.Threading.Timer.Callback()\nrdi:\n-> 4000 Microsoft.Extensions.DependencyInjection.ServiceProvider\nFound 1 unique roots."
        self.assertEqual(root_paths(text, "4000")["paths"][0]["kind"], "stack")
        helper = text.replace("System.Threading.Timer.Callback()", "[HelperMethodFrame_1OBJ] (System.Threading.Timer.Callback())")
        helper = helper.replace("rdi:", "rsp+28: 3000")
        path = root_paths(helper, "4000")["paths"][0]
        self.assertEqual(path["method"], "System.Threading.Timer.Callback()")
        self.assertEqual((path["register"], path["offset"], path["stack_slot"]), ("rsp", "+28", "3000"))
        # Pinned GetFrameOutput adds a second space before a framed method;
        # GetRegisterOutput can emit only a slot, or a literal unknown register.
        self.assertEqual(root_paths(helper.replace("] (", "]  ("), "4000")["paths"][0]["method"], path["method"])
        self.assertEqual(root_paths(text.replace("rdi:", "0000000000003000"), "4000")["paths"][0]["stack_slot"],
                         "0000000000003000")
        self.assertEqual(root_paths(helper.replace("rsp+28:", "???+28:"), "4000")["paths"][0]["register"], "???")
        frame_only = text.replace("System.Threading.Timer.Callback()", "[GCFrame]")
        self.assertEqual(root_paths(frame_only, "4000")["paths"][0]["frame"], "GCFrame")
        self.assertNotIn("method", root_paths(frame_only, "4000")["paths"][0])
        self.rejects(root_paths, frame_only.replace("GCFrame", "UnreviewedFrame"), "4000")
        for bad in (helper.replace("rsp+28: 3000", "rsp+secret: 3000"),
                    helper.replace("rsp+28: 3000", "rsp+28: secret"),
                    helper.replace("rsp+28: 3000", "rsp+28: 3000 secret=value")):
            self.rejects(root_paths, bad, "4000")
        self.rejects(root_paths, text, "4000", True)
        self.rejects(root_paths, "Found 0 unique roots.", "4000")
        self.assertEqual(root_paths("Found 0 unique roots.", "4000", True)["paths"], [])
        dump, runtime = Path("/private/heap"), Path("/pinned/runtime")
        framed = f"Loading core dump: {dump} ...\nSet load path for DAC/DBI to '{runtime}'\nabc\n"
        self.assertEqual(command_output(framed, dump, runtime), "abc")
        for bad in (framed.replace("heap ...", "heap"), framed.replace(str(runtime), str(Path("/wrong/runtime"))),
                    "unexpected secret\n" + framed, framed.replace("Set load path", "Unknown acknowledgement"),
                    "> dumpheap\nabc\n> exit\n"):
            self.rejects(command_output, bad, dump, runtime)

    def test_pinned_ref_counted_roots_keep_only_kind(self):
        text = "HandleTable:\n1000 (ref counted handle RefCount: 1,234)\n-> 4000 Microsoft.Extensions.DependencyInjection.ServiceProvider\nFound 1 unique roots."
        parsed = root_paths(text, "4000")
        self.assertEqual(parsed["paths"][0]["handle_kind"], "ref counted handle")
        self.assertNotIn("1,234", json.dumps(public_root_paths(parsed)))
        self.rejects(root_paths, text.replace("1,234", "secret=value"), "4000")

    def test_encoded_native_runtime_version(self):
        native = "10.0.1226.42604\n10.0.1226.42604 @Commit: " + "a" * 40
        verify_dump_version(native + "\nWorkstation mode\nSOS Version: 10.0.745401+abc")
        verify_dump_version("10.0.12+abc\nServer mode with 1 gc heaps\nDATAS 1")
        for bad in (native.replace("1226", "1126"), native.replace("10.0", "10.1"),
                    native.replace("42604", "99999"), native.replace("42604", "4999"),
                    native + "\nsecret=value", "Workstation mode\nSOS Version: 10.0.745401",
                    "10.0.12 @Commit: secret=value"):
            self.rejects(verify_dump_version, bad)

    def test_exact_signal_handles_and_target_binding(self):
        head = "a" * 40
        rows = ["pid=123", f"head={head}", f"factory={FACTORY}", f"provider={PROVIDER}", f"witness={WITNESS}",
                "factory-handle=0000000000001000", "provider-handle=0000000000002000"]
        handles = signal_handle_ids(rows, head)
        table = {handles["Factory"]: ("3000", FACTORY), handles["Provider"]: ("4000", PROVIDER)}
        self.assertEqual(bind_weak_target(handles["Factory"], table, FACTORY), ("3000", FACTORY))
        for wrong in ("0000000000001001", "0000000000000000", "0000000000002000", "secret", "0000000000001000 secret=value"):
            self.rejects(signal_handle_ids, rows[:5] + ["factory-handle=" + wrong, rows[6]], head)
        self.rejects(signal_handle_ids, rows, "b" * 40)
        self.rejects(signal_handle_ids, rows[:2] + ["factory=Unknown.Type"] + rows[3:], head)
        self.rejects(bind_weak_target, "5000", table, FACTORY)
        self.rejects(bind_weak_target, handles["Provider"], table, FACTORY)

    def test_public_root_metadata_excludes_addresses(self):
        text = "HandleTable:\n1000 (strong handle)\n-> 2000 System.Object[]\n-> 4000 Microsoft.Extensions.DependencyInjection.ServiceProvider\nFound 1 unique roots."
        public = public_root_paths(root_paths(text, "4000"))
        serialized = json.dumps(public)
        for private in ("1000", "2000", "4000", '"id"', '"root"', '"stack_slot"', '"thread"'):
            self.assertNotIn(private, serialized)
        self.assertEqual(public["paths"][0]["nodes"][-1]["type"], PROVIDER)

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

    def test_no_signal_actual_results_and_reason_privacy(self):
        zero = ("error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
                "notRunnable", "notExecuted", "disconnected", "warning", "completed",
                "inProgress", "pending")
        attributes = 'total="1" executed="1" passed="1" failed="0" ' + " ".join(f'{key}="0"' for key in zero)
        text = (f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                f'<Counters {attributes}/><UnitTestResult testName="{FACT}" outcome="Passed"/></TestRun>')
        observation = observed_trx(text.encode())
        self.assertEqual(observation, {"total": 1, "executed": 1, "passed": 1, "failed": 0,
                                      "original_outcome": "Passed", "class_counts": {
                                          COHORT_CLASSES[0]: {"executed": 1, "passed": 1, "failed": 0}}})
        self.assertEqual(no_signal_reason(observation, "synthetic-private-value"), "isolated-no-reproduction")
        self.rejects(verify_original_trx, text.encode())
        failed = text.replace('passed="1" failed="0"', 'passed="0" failed="1"').replace(
            'outcome="Passed"/>', f'outcome="Failed"><Message>{FAILURE}</Message></UnitTestResult>')
        observation = observed_trx(failed.encode())
        reason = no_signal_reason(observation, "Synthetic root capture unavailable: exact weak-handle ownership metadata was not available. synthetic-private-value")
        self.assertEqual(reason, "weak-handle-guard-unavailable")
        self.assertNotIn("synthetic-private-value", json.dumps({"reason": reason, "counts": observation}))
        for bad in (text.replace('outcome="Passed"', 'outcome="Failed"'),
                    text.replace('executed="1"', 'executed="0"'), text.replace(FACT, "Unknown.Test"),
                    text.replace('failed="0"', 'failed="01"')):
            self.rejects(observed_trx, bad.encode())

    def test_focused_cohort_preserves_original_failure(self):
        zero = ("error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
                "notRunnable", "notExecuted", "disconnected", "warning", "completed",
                "inProgress", "pending")
        attributes = f'total="{COHORT_TOTAL}" executed="{COHORT_TOTAL}" passed="{COHORT_TOTAL - 1}" failed="1" ' + " ".join(f'{key}="0"' for key in zero)
        results = f'<UnitTestResult testName="{FACT}" outcome="Failed"><Message>{FAILURE}</Message></UnitTestResult>'
        results += "".join(f'<UnitTestResult testName="{COHORT_CLASSES[index % 3]}.Synthetic{index}" outcome="Passed"/>'
                           for index in range(COHORT_TOTAL - 1))
        text = f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Counters {attributes}/>{results}</TestRun>'
        observed = verify_original_trx(text.encode(), cohort=True)
        self.assertEqual((observed["total"], observed["passed"], observed["failed"]), (48, 47, 1))
        for bad in (text.replace(FACT, "Unknown.Test"), text.replace(FAILURE, "Different failure"),
                    text.replace(COHORT_CLASSES[0] + ".Synthetic", "Unknown.Synthetic"),
                    text.replace('total="48"', 'total="47"')):
            self.rejects(verify_original_trx, bad.encode(), True)


def main():
    global CAPTURE_DEADLINE
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
    args.observed_counts = None
    args.capture_filter = None
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
    isolated_control = None
    # Shared finite lease covers both sequential test attempts and every helper.
    # The workflow retains its eight-minute bound; leave time for finally cleanup.
    CAPTURE_DEADLINE = time.monotonic() + 430
    with tempfile.TemporaryDirectory(prefix="maliev-pooled-root-", dir=parent) as directory:
        private = Path(directory).resolve()
        require(private.parent == parent and private.stat().st_uid == os.getuid()
                and private.stat().st_mode & 0o777 == 0o700, "private-temp-ownership")
        try:
            try:
                report = capture(args, private)
            except ObservationUnavailable as failure:
                if (str(failure) != "isolated-no-reproduction"
                        or failure.observation["failed"] != 0):
                    raise
                isolated_control = failure.observation
                # The first owned process has exited and capture's finally ran.
                # Reproduce the same focused cohort before attributing its root.
                cohort_private = private / "focused-cohort"
                cohort_private.mkdir(mode=0o700)
                report = capture(args, cohort_private, cohort=True)
        except ObservationUnavailable as failure:
            report = {"status": "diagnostic-unavailable", "stage": str(failure),
                      "observed_test_counts": failure.observation}
        except MetadataUnavailable as failure:
            report = {"status": "diagnostic-unavailable", "stage": str(failure),
                      "metadata_context": failure.context, "metadata_shape": failure.shape}
        except EnvironmentUnavailable as failure:
            report = {"status": "diagnostic-unavailable", "stage": str(failure),
                      "environment_issues": failure.counts,
                      "unknown_environment_name_sha256": failure.unknown_name_sha256}
        except Unavailable as failure:
            report = {"status": "diagnostic-unavailable", "stage": str(failure)}
        except Exception:
            # Never expose exception details, unfiltered logs, or partial root results.
            pass
        if isolated_control is not None:
            report["isolated_control"] = isolated_control
        if args.observed_counts is not None:
            report["observed_test_counts"] = args.observed_counts
            report["capture_filter"] = args.capture_filter
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
