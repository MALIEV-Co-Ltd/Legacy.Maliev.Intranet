"""Retain actual TRX identity joins without display names, output, or parameters."""
import argparse
import shutil
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
    # Direct sections must be the only sections anywhere in the document.
    for name in ("TestRun", "TestDefinitions", "TestEntries", "Results", "ResultSummary", "Counters"):
        require(sum(local_tag(node) == name for node in root.iter()) == 1)
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
    for parent, name in ((definitions, "UnitTest"), (entries, "TestEntry"), (results, "UnitTestResult")):
        require([node for node in root.iter() if local_tag(node) == name] == list(parent))
    for name in ("Execution", "TestMethod"):
        require(sum(local_tag(node) == name for node in root.iter()) == total)
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


FOCUSED_BASELINE = {'schemaVersion': 1, 'sourceFiles': {'Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrderCreate.resx': ['5e0539c8a6841cb52c0d95107ab86efab8aff39d3cb47b1287e0a9ce854c99e3', 'dddb860be2a4dbb7e69aeae16791cf0238f3754d12fa5d346905d008f5e69902'], 'Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrderCreate.th.resx': ['548d3f13817ad7db9036b3ed3c1a655b555ec13880842f21ec8858dca4948f40', '81527eaddebfd766225b7faacbfec2842e7daf0afd160f05a99175236a410e30'], 'Legacy.Maliev.Intranet.Tests/PurchaseOrderFailureMessageContractTests.cs': ['7178588b7e3acf59ba26e1084c11ba1a8624a90bb901df7fc8a620e149508454', '9060949dc7d736c584204fbf90a64c0c57b73bb891ad55c33f2f2a943dfb750b']}, 'focusedMethods': {'a713d277e17873eab119e46482aee03366ec2451fc0c5fa4d1d5cc2b274e978a': 2}, 'focusedCases': 2}

def verify_focused_output(root):
    summary=one_child(root,"ResultSummary")
    for output in root.iter():
        if local_tag(output)!="Output":
            continue
        require(not output.attrib and not (output.text or "").strip() and not (output.tail or "").strip())
        seen=set()
        for child in output:
            tag=local_tag(child)
            require(tag in {"StdOut","StdErr","DebugTrace"} and tag not in seen)
            seen.add(tag)
            require(not child.attrib and len(child)==0 and not (child.tail or "").strip())
            require(output not in list(summary) or tag=="StdOut")
    outputs=[node for node in summary if local_tag(node)=="Output"]
    require(len(outputs)<=1)
    summary_stdout=[] if not outputs else [node for node in outputs[0] if local_tag(node)=="StdOut"]
    require(len(summary_stdout)<=1)
    prefix=r"\[xUnit\.net [0-9]{2}:[0-5][0-9]:[0-5][0-9]\.[0-9]{2}\] "
    patterns=[
        prefix+r"xUnit\.net VSTest Adapter v3\.1\.4\+50e68bbb8b \(64-bit \.NET 10\.0\.13\)",
        prefix+r"  Discovering: Legacy\.Maliev\.Intranet\.Tests",
        prefix+r"  Discovered:  Legacy\.Maliev\.Intranet\.Tests",
        prefix+r"  Starting:    Legacy\.Maliev\.Intranet\.Tests",
        prefix+r"  Finished:    Legacy\.Maliev\.Intranet\.Tests",
    ]
    for node in root.iter():
        if local_tag(node) not in {"StdOut","StdErr","DebugTrace"} or not (node.text or "").strip():
            continue
        require(len(summary_stdout)==1 and node is summary_stdout[0])
        text=node.text or ""
        require(len(text)<=1024)
        lines=text.splitlines()
        require(len(lines)==len(patterns))
        require(all(re.fullmatch(pattern,line) for pattern,line in zip(patterns,lines)))


def retain_focus(raw, workspace, source_revision):
    verify_sources(workspace, FOCUSED_BASELINE)
    proof=retain(raw, FOCUSED_BASELINE, source_revision)
    require(len(proof["definitions"]) == 2 and len(proof["methodRoster"]) == 1)
    require(len({row["canonicalNameSha256"] for row in proof["definitions"]}) == 2)
    # Preserve known pinned adapter status metadata; reject diagnostic payloads.
    verify_focused_output(ET.fromstring(raw))
    return proof


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--trx",type=pathlib.Path,required=True)
    parser.add_argument("--workspace",type=pathlib.Path,required=True)
    parser.add_argument("--source-revision",required=True)
    parser.add_argument("--output-directory",type=pathlib.Path,required=True)
    args=parser.parse_args()
    require(not args.output_directory.exists())
    raw=args.trx.read_bytes()
    proof=retain_focus(raw,args.workspace,args.source_revision)
    args.output_directory.mkdir(parents=True)
    (args.output_directory/"original-focused.trx").write_bytes(raw)
    (args.output_directory/"identity-proof.json").write_text(json.dumps(proof,sort_keys=True,separators=(",",":"))+"\n",encoding="utf-8")
    print("Verified two actual localized executions and original identity joins.")


if __name__=="__main__":
    try:main()
    except (InvalidEvidence,KeyError,ValueError,OSError,ET.ParseError):
        raise SystemExit("Purchase-order message focused evidence rejected; no accepted proof emitted.")
