"""Retain actual TRX identity joins without display names, output, or parameters."""
import argparse
import collections
import hashlib
import json
import pathlib
import re
import traceback
import uuid
import xml.etree.ElementTree as ET


class InvalidEvidence(ValueError):
    pass


def require(condition):
    if not condition:
        raise InvalidEvidence("Invalid test identity evidence.")


def digest(value):
    return hashlib.sha256(value).hexdigest()


def text_hash(value):
    require(isinstance(value, str) and value != "")
    return digest(value.encode("utf-8"))


def guid(value):
    require(isinstance(value, str) and re.fullmatch(r"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}", value))
    parsed = uuid.UUID(value)
    require(parsed.int != 0)
    return value.lower()


def local_tag(node):
    return node.tag.rsplit("}", 1)[-1]


def one_child(node, name):
    found = [child for child in node if local_tag(child) == name]
    require(len(found) == 1)
    return found[0]


def method_hash(class_name, method_name):
    # Assembly qualification is metadata, not part of the declared CLR name.
    canonical_class = class_name.split(",", 1)[0].strip()
    require(re.fullmatch(r"[A-Za-z_][A-Za-z0-9_.+`]*", canonical_class))
    require(re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", method_name))
    return text_hash(canonical_class + "\n" + method_name)


def retain(raw, baseline, source_revision):
    require(0 < len(raw) <= 64 * 1024 * 1024)
    require(re.fullmatch(r"[0-9a-f]{40}", source_revision))
    require(b"<!DOCTYPE" not in raw.upper() and b"<!ENTITY" not in raw.upper())
    root = ET.fromstring(raw)
    require(local_tag(root) == "TestRun")
    definitions = one_child(root, "TestDefinitions")
    entries = one_child(root, "TestEntries")
    results = one_child(root, "Results")
    summary = one_child(root, "ResultSummary")
    require(summary.attrib.get("outcome") == "Completed")
    # Adverse diagnostics cannot become a passing identity receipt.
    require(not any(local_tag(node) == "ErrorInfo" for node in root.iter()))
    require(not any(local_tag(node) == "RunInfo" for node in root.iter()))
    counters = one_child(summary, "Counters")
    required_counters = {"total", "executed", "passed", "failed", "error", "timeout",
                         "aborted", "inconclusive", "passedButRunAborted", "notRunnable",
                         "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending"}
    require(set(counters.attrib) == required_counters)
    require(all(re.fullmatch(r"[0-9]+", value) for value in counters.attrib.values()))
    total = int(counters.attrib["total"])
    require(0 < total <= 100000 and int(counters.attrib["executed"]) == total)
    require(int(counters.attrib["passed"]) == total and int(counters.attrib["failed"]) == 0)
    for name, value in counters.attrib.items():
        if name not in {"total", "executed", "passed"}:
            require(int(value) == 0)
    declared = {}
    retained_definitions = []
    roster = collections.Counter()
    for definition in definitions:
        require(local_tag(definition) == "UnitTest")
        key = guid(definition.attrib["id"])
        require(key not in declared)
        execution = one_child(definition, "Execution")
        test_method = one_child(definition, "TestMethod")
        execution_key = guid(execution.attrib["id"])
        identity = method_hash(test_method.attrib["className"], test_method.attrib["name"])
        name_hash = text_hash(definition.attrib["name"])
        declared[key] = (execution_key, identity, name_hash)
        roster[identity] += 1
        retained_definitions.append({
            "testId": definition.attrib["id"], "executionId": execution.attrib["id"],
            "methodSha256": identity, "rawClassNameSha256": text_hash(test_method.attrib["className"]),
            "rawMethodNameSha256": text_hash(test_method.attrib["name"]), "canonicalNameSha256": name_hash,
        })
    joined_entries = {}
    retained_entries = []
    for entry in entries:
        require(local_tag(entry) == "TestEntry")
        key = guid(entry.attrib["testId"])
        execution_key = guid(entry.attrib["executionId"])
        require(key in declared and key not in joined_entries and declared[key][0] == execution_key)
        joined_entries[key] = execution_key
        retained_entries.append({"testId": entry.attrib["testId"], "executionId": entry.attrib["executionId"]})
    joined_results = {}
    retained_results = []
    for result in results:
        require(local_tag(result) == "UnitTestResult")
        key = guid(result.attrib["testId"])
        execution_key = guid(result.attrib["executionId"])
        name_hash = text_hash(result.attrib["testName"])
        require(key in declared and key not in joined_results and joined_entries.get(key) == execution_key)
        require(declared[key][0] == execution_key and declared[key][2] == name_hash)
        require(result.attrib["outcome"] == "Passed")
        joined_results[key] = execution_key
        retained_results.append({
            "testId": result.attrib["testId"], "executionId": result.attrib["executionId"],
            "canonicalNameSha256": name_hash, "outcome": result.attrib["outcome"],
        })
    require(len(declared) == len(joined_entries) == len(joined_results) == total)
    require(len(set(joined_results.values())) == total)
    require(set(declared) == set(joined_entries) == set(joined_results))
    require(set(baseline) == {"schemaVersion", "sourceFiles", "focusedMethods", "focusedCases"})
    require(baseline["schemaVersion"] == 1)
    focused = baseline["focusedMethods"]
    require(all(roster.get(key) == count for key, count in focused.items()))
    require(sum(focused.values()) == baseline["focusedCases"])
    return {
        "schemaVersion": 2, "checkoutSourceRevision": source_revision, "rawTrxSha256": digest(raw),
        "baselineSha256": digest(json.dumps(baseline, sort_keys=True, separators=(",", ":")).encode()),
        "counters": {key: int(value) for key, value in counters.attrib.items()},
        "definitions": sorted(retained_definitions, key=lambda value: value["testId"].lower()),
        "entries": sorted(retained_entries, key=lambda value: value["testId"].lower()),
        "results": sorted(retained_results, key=lambda value: value["testId"].lower()),
        "methodRoster": dict(sorted(roster.items())), "focusedMethodCount": len(focused),
        "focusedCaseCount": sum(focused.values()), "rawDefinitionEntryResultJoinVerified": True,
        "passingRunSummaryVerified": True,
        "exactCaseIdentityPreservationVerified": False,
        "exactCaseIdentityLimitation": "No historical canonical case-name baseline is available; method counts do not prove case identity preservation.",
        "compiledMembershipCertified": False, "generatedInclusiveCertified": False,
    }


def verify_sources(workspace, baseline):
    workspace = workspace.resolve()
    for relative, allowed in baseline["sourceFiles"].items():
        path = workspace / relative
        require(not path.is_symlink() and path.resolve().is_relative_to(workspace))
        data = path.read_bytes()
        require(digest(data) in allowed)


def retain_compiler_custody(workspace):
    workspace = workspace.resolve()
    projects = ("Legacy.Maliev.Intranet.Bff", "Legacy.Maliev.Intranet.Server",
                "Legacy.Maliev.Intranet.Contracts", "Legacy.Maliev.Intranet.Tests")
    captured = {}
    for project in projects:
        directory = workspace / project / "obj/Release/net10.0/intranet-validation"
        groups = {}
        policy = directory / "symbol-policy.txt"
        require(policy.is_file() and not policy.is_symlink() and policy.stat().st_size <= 128)
        fields = policy.read_text(encoding="utf-8-sig").strip().split("|")
        require(len(fields) == 3)
        debug_type, debug_symbols, coverage_symbols = (field.lower() for field in fields)
        require(coverage_symbols in ("", "true", "false"))
        without_symbols = debug_type == "none" and debug_symbols == "false"
        if without_symbols:
            require(project == "Legacy.Maliev.Intranet.Contracts" and coverage_symbols != "true")
        else:
            require(debug_type in ("portable", "full", "pdbonly") and debug_symbols == "true")
        groups["symbolPolicy"] = {"debugType": debug_type, "debugSymbols": debug_symbols,
                                  "enableCoverageSymbols": coverage_symbols}
        for name in ("compiler-inputs", "compiler-outputs", "emitted-sources"):
            manifest = directory / (name + ".sha256")
            require(manifest.is_file() and not manifest.is_symlink() and manifest.stat().st_size <= 1024 * 1024)
            rows = []
            seen = set()
            for line in manifest.read_text(encoding="utf-8-sig").splitlines():
                if not line:
                    continue
                path_text, separator, expected = line.rpartition("|")
                require(separator and re.fullmatch(r"[0-9a-fA-F]{64}", expected))
                path = pathlib.Path(path_text)
                require(path.is_absolute() and not path.is_symlink() and path.resolve().is_relative_to(workspace))
                relative = path.resolve().relative_to(workspace).as_posix()
                require(relative not in seen and path.stat().st_size <= 64 * 1024 * 1024)
                actual = digest(path.read_bytes())
                require(actual == expected.lower())
                seen.add(relative)
                rows.append({"path": relative, "sha256": actual})
            require(name == "emitted-sources" or len(rows) > 0)
            if name == "compiler-outputs":
                expected_names = {project + ".dll"}
                if not without_symbols:
                    expected_names.add(project + ".pdb")
                require({pathlib.Path(row["path"]).name for row in rows} == expected_names)
            groups[name] = sorted(rows, key=lambda row: row["path"])
        groups["origin"] = "MSBuild CoreCompile item snapshot and build-output capture"
        groups["compiledMembershipCertified"] = False
        groups["generatedInclusiveCertified"] = False
        captured[project] = groups
    return captured


def entry_join_diagnostics(raw):
    """Describe rejected joins only; never substitute for the acceptance validator."""
    try:
        require(0 < len(raw) <= 64 * 1024 * 1024)
        require(b"<!DOCTYPE" not in raw.upper() and b"<!ENTITY" not in raw.upper())
        root = ET.fromstring(raw)
        require(local_tag(root) == "TestRun")
        definitions = one_child(root, "TestDefinitions")
        entries = one_child(root, "TestEntries")
        require(len(definitions) <= 100000 and len(entries) <= 100000)
        declared = {}
        for definition in definitions:
            require(local_tag(definition) == "UnitTest")
            key = guid(definition.attrib["id"])
            require(key not in declared)
            execution = guid(one_child(definition, "Execution").attrib["id"])
            method = one_child(definition, "TestMethod")
            declared[key] = {
                "declaredExecutionId": execution,
                "methodSha256": method_hash(method.attrib["className"], method.attrib["name"]),
                "canonicalNameSha256": text_hash(definition.attrib["name"]),
            }
        counts = collections.Counter()
        anomalies = []

        def record(reason, key=None, execution=None):
            counts[reason] += 1
            if len(anomalies) < 16:
                row = {"reason": reason}
                if key is not None:
                    row["testId"] = key
                    row.update(declared.get(key, {}))
                if execution is not None:
                    row["executionId"] = execution
                anomalies.append(row)

        seen = set()
        for entry in entries:
            try:
                require(local_tag(entry) == "TestEntry")
                key = guid(entry.attrib["testId"])
                execution = guid(entry.attrib["executionId"])
            except (InvalidEvidence, KeyError, ValueError):
                record("invalid-entry-identity")
                continue
            if key not in declared:
                record("undeclared-test-id", key, execution)
            if key in seen:
                record("duplicate-test-id", key, execution)
            if key in declared and declared[key]["declaredExecutionId"] != execution:
                record("execution-mismatch", key, execution)
            seen.add(key)
        for key in declared:
            if key not in seen:
                record("missing-entry", key)
        return {"definitionCount": len(definitions), "entryCount": len(entries),
                "reasonCounts": dict(sorted(counts.items())), "anomalies": anomalies,
                "truncated": sum(counts.values()) > len(anomalies)}
    except (InvalidEvidence, KeyError, ValueError, ET.ParseError):
        return {"unavailable": "invalid-or-unbounded-input"}


def compiler_path_diagnostics(workspace):
    """Describe path refusals without admitting inputs or retaining their names."""
    counts = collections.Counter()
    anomalies = []
    unavailable = 0
    try:
        workspace = workspace.resolve()
    except (OSError, RuntimeError):
        return {"reasonCounts": {}, "anomalies": [], "unavailableManifests": 12, "truncated": False}
    for project in ("Legacy.Maliev.Intranet.Bff", "Legacy.Maliev.Intranet.Server",
                    "Legacy.Maliev.Intranet.Contracts", "Legacy.Maliev.Intranet.Tests"):
        directory = workspace / project / "obj/Release/net10.0/intranet-validation"
        for kind in ("compiler-inputs", "compiler-outputs", "emitted-sources"):
            manifest = directory / (kind + ".sha256")
            try:
                require(manifest.is_file() and not manifest.is_symlink()
                        and manifest.resolve().is_relative_to(workspace)
                        and manifest.stat().st_size <= 1024 * 1024)
                with manifest.open("rb") as stream:
                    raw = stream.read(1024 * 1024 + 1)
                require(len(raw) <= 1024 * 1024)
                lines = raw.decode("utf-8-sig").splitlines()
                require(len(lines) <= 10000)
                manifest_hash = digest(raw)
                for index, line in enumerate(lines, 1):
                    if not line:
                        continue
                    path_text, separator, expected = line.rpartition("|")
                    require(separator and re.fullmatch(r"[0-9a-fA-F]{64}", expected)
                            and len(path_text) <= 4096)
                    path = pathlib.Path(path_text)
                    absolute = path.is_absolute()
                    symlink = path.is_symlink() if absolute else None
                    inside = path.resolve().is_relative_to(workspace) if absolute and not symlink else None
                    if not absolute:
                        reason = "non-absolute"
                    elif symlink:
                        reason = "leaf-symlink"
                    elif not inside:
                        reason = "resolved-outside-workspace"
                    else:
                        continue
                    counts[reason] += 1
                    if len(anomalies) < 16:
                        suffix = path_text.replace("\\", "/").split("/")[-5:]
                        known_sdk = suffix == ["microsoft.net.test.sdk", "17.14.1", "build", "net8.0",
                                               "Microsoft.NET.Test.Sdk.Program.cs"]
                        anomalies.append({
                            "project": project, "manifestKind": kind, "rowIndex": index, "reason": reason,
                            "pathSha256": digest(path_text.encode("utf-8")), "manifestSha256": manifest_hash,
                            "isAbsolute": absolute, "leafIsSymlink": symlink, "resolvedInsideWorkspace": inside,
                            "inputKind": "test-sdk-entrypoint" if known_sdk else "other",
                        })
            except (InvalidEvidence, OSError, ValueError, RuntimeError):
                unavailable += 1
    return {"reasonCounts": dict(sorted(counts.items())), "anomalies": anomalies,
            "unavailableManifests": unavailable, "truncated": sum(counts.values()) > len(anomalies)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--trx", type=pathlib.Path, required=True)
    parser.add_argument("--baseline", type=pathlib.Path, required=True)
    parser.add_argument("--workspace", type=pathlib.Path, required=True)
    parser.add_argument("--source-revision", required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    stage = "baseline"
    raw = None
    try:
        baseline = json.loads(args.baseline.read_text(encoding="utf-8-sig"))
        stage = "source-pins"
        verify_sources(args.workspace, baseline)
        stage = "original-trx-read"
        raw = args.trx.read_bytes()
        stage = "original-trx-identity"
        evidence = retain(raw, baseline, args.source_revision)
        stage = "compiler-custody"
        evidence["compilerCustody"] = retain_compiler_custody(args.workspace)
        stage = "proof-write"
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(evidence, sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")
    except (InvalidEvidence, KeyError, ValueError, OSError, ET.ParseError) as error:
        # Emit only bounded fixed labels, identifiers and opaque hashes.
        # Never retain exception text, test names, output or credential values.
        lines = [frame.lineno for frame in traceback.extract_tb(error.__traceback__)
                 if pathlib.Path(frame.filename).resolve() == pathlib.Path(__file__).resolve()]
        failure = {"schemaVersion": 1, "accepted": False, "stage": stage,
                   "validatorLines": lines, "rawTrxSha256": digest(raw) if raw is not None else None}
        if stage == "original-trx-identity" and raw is not None:
            failure["entryJoinDiagnostics"] = entry_join_diagnostics(raw)
        if stage == "compiler-custody":
            failure["compilerPathDiagnostics"] = compiler_path_diagnostics(args.workspace)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.with_name("identity-rejection.json").write_text(
            json.dumps(failure, sort_keys=True) + "\n", encoding="utf-8")
        raise
    print("Verified privacy-safe original definition/entry/result joins and frozen focused source roster.")


if __name__ == "__main__":
    try:
        main()
    except (InvalidEvidence, KeyError, ValueError, OSError, ET.ParseError):
        raise SystemExit("Test identity retention failed; no accepted identity proof emitted.")
