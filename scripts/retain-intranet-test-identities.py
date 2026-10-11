"""Retain actual TRX identity joins without display names, output, or parameters."""
import argparse
import collections
import hashlib
import json
import pathlib
import re
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
                require({pathlib.Path(row["path"]).name for row in rows} == {project + ".dll", project + ".pdb"})
            groups[name] = sorted(rows, key=lambda row: row["path"])
        groups["origin"] = "MSBuild CoreCompile item snapshot and build-output capture"
        groups["compiledMembershipCertified"] = False
        groups["generatedInclusiveCertified"] = False
        captured[project] = groups
    return captured


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--trx", type=pathlib.Path, required=True)
    parser.add_argument("--baseline", type=pathlib.Path, required=True)
    parser.add_argument("--workspace", type=pathlib.Path, required=True)
    parser.add_argument("--source-revision", required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    baseline = json.loads(args.baseline.read_text(encoding="utf-8-sig"))
    verify_sources(args.workspace, baseline)
    evidence = retain(args.trx.read_bytes(), baseline, args.source_revision)
    evidence["compilerCustody"] = retain_compiler_custody(args.workspace)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(evidence, sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")
    print("Verified privacy-safe original definition/entry/result joins and frozen focused source roster.")


if __name__ == "__main__":
    try:
        main()
    except (InvalidEvidence, KeyError, ValueError, OSError, ET.ParseError):
        raise SystemExit("Test identity retention failed; no accepted identity proof emitted.")
