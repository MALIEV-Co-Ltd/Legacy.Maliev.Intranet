"""Retain names/outcomes and revisions only; never token, principal, database or assertion data."""
import json
import os
import pathlib
import re
import subprocess
import xml.etree.ElementTree as ET

EXPECTED = "SupplierCatalogPersistence.Acceptance.SupplierCatalogPersistenceTests.CatalogPostcodeSelection_RealSupplierSave_ApiReadbackAndReloadPreserveAddress"
PINS = {
    "Legacy.Maliev.CatalogService": "3f426723743570a6c20d2c014499445abb0774e1",
    "Legacy.Maliev.ProcurementService": "614a157ab5c8739c144554797a387c45990ba7fe",
    "Legacy.Maliev.ServiceDefaults": "7b3099bf67d0f17e56cfdb3dcf36541304abaac2",
    "Legacy.Maliev.CompatibilityContracts": "99529ad665503b227184c0baa946ad4e62db978a",
}

def revision(path):
    value = subprocess.check_output(["git", "-C", str(path), "rev-parse", "HEAD"], text=True).strip()
    if not re.fullmatch(r"[0-9a-f]{40}", value):
        raise ValueError("Invalid source identity")
    return value

def actual_result(path):
    if not path.exists():
        return {"outcomes": [], "complete": False}
    document = ET.parse(path)
    namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    results = document.findall(".//t:UnitTestResult", namespace)
    matches = [result.get("outcome") for result in results if result.get("testName") == EXPECTED]
    counters = document.find(".//t:ResultSummary/t:Counters", namespace)
    summary = document.find(".//t:ResultSummary", namespace)
    complete = (len(results) == 1 and matches == ["Passed"] and counters is not None and
                all(counters.get(key) == str(value) for key, value in
                    {"total": 1, "executed": 1, "passed": 1, "failed": 0, "notExecuted": 0}.items()) and
                summary is not None and summary.get("outcome") == "Completed")
    return {"outcomes": matches, "complete": complete}

def bound_candidate(executed, candidate, parents):
    return (re.fullmatch(r"[0-9a-f]{40}", candidate or "") is not None and
            (executed == candidate or (len(parents) == 2 and parents[1] == candidate)))

def main():
    root = pathlib.Path(__file__).resolve().parents[2]
    graph = {name: revision(root / ".dependencies" / name) for name in PINS}
    result = actual_result(pathlib.Path(os.environ["PROOF_RESULTS"]) / "supplier-persistence.trx")
    head = os.environ.get("CANDIDATE_HEAD", "")
    executed = revision(root)
    parents = subprocess.check_output(["git", "-C", str(root), "show", "-s", "--format=%P", "HEAD"], text=True).split()
    complete = (result["complete"] and graph == PINS and bound_candidate(executed, head, parents) and
                os.environ.get("BUILD_OUTCOME") == "success" and
                os.environ.get("EXECUTION_OUTCOME") == "success")
    evidence = {
        "schema": 1, "candidateHead": head, "executedSource": executed, "mergeParents": parents, "producers": graph,
        "runId": os.environ.get("GITHUB_RUN_ID"), "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
        "tests": [{"name": EXPECTED, "outcomes": result["outcomes"]}], "complete": complete,
        "covered": ["Intranet supplier create", "Catalog Thai postcode selection", "Procurement supplier and address writes", "independent domain API readback", "page reload", "CSRF and employee/workload rejection controls"],
        "excluded": ["Creden company selection", "real AuthService issuance", "AppHost orchestration", "supplier edit", "customer", "billing", "shipping", "purchase order", "quotation"],
        "authority": "disposable synthetic RSA issuer; normal JWT and signed permission fallback",
        "cleanup": "passing test completes awaited factory/browser/authority/container disposal",
    }
    destination = pathlib.Path(os.environ["PROOF_EVIDENCE"])
    destination.mkdir(parents=True, exist_ok=True)
    (destination / "proof.json").write_text(json.dumps(evidence, indent=2) + "\n")
    if not complete:
        raise SystemExit("Supplier persistence proof incomplete; sanitized actual result retained")

if __name__ == "__main__":
    main()

