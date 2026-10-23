"""Exact live producer/consumer storage receipt. Auth/IAM remain synthetic test dependencies."""
import json
import os
import pathlib
import xml.etree.ElementTree as ET
import retain

EXPECTED = "SupplierCatalogPersistence.Acceptance.SupplierCatalogPersistenceTests.PurchaseOrderReplay_RealProducerRowsSurviveLostAcknowledgementAndLaterChildFailure"
CLIENT_PHASES = {
    "procurement-startup": 0, "procurement-http": 0, "purchase-order-consumer-http": 0,
    "Legacy.Maliev.ProcurementService.Api-actual-host": 2, "procurement-factory": 3,
    "signing-key": 5, "protection-key": 5, "certificate": 5,
}


def actual_join(path):
    try:
        document = ET.parse(path)
        ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
        rows = document.findall(".//t:UnitTestResult", ns)
        summaries = document.findall(".//t:ResultSummary", ns)
        counters = document.findall(".//t:ResultSummary/t:Counters", ns)
        expected = {"total": 1, "executed": 1, "passed": 1, **dict.fromkeys(retain.ZERO_COUNTERS, 0)}
        return (len(rows) == 1 and rows[0].get("testName") == EXPECTED and rows[0].get("outcome") == "Passed" and
                len(summaries) == 1 and summaries[0].get("outcome") == "Completed" and len(counters) == 1 and
                counters[0].attrib == {key: str(value) for key, value in expected.items()})
    except (OSError, ET.ParseError):
        return False


def main(root=None):
    root = pathlib.Path(root).resolve() if root is not None else pathlib.Path(__file__).resolve().parents[2]
    results = pathlib.Path(os.environ["PROOF_RESULTS"])
    destination = pathlib.Path(os.environ["PROOF_EVIDENCE"])
    graph = {name: retain.revision(root / ".dependencies" / name) for name in retain.PINS}
    executed = retain.revision(root)
    parents = retain.merge_parents(root)
    head = os.environ.get("CANDIDATE_HEAD", "")
    passed = actual_join(results / "purchase-order-replay.trx")
    controls = retain.actual_controls(results / "supplier-resource-controls.trx")
    cleanup = retain.resource_release(destination, CLIENT_PHASES)
    complete = (passed and controls["complete"] and cleanup and graph == retain.PINS and
                retain.bound_candidate(executed, head, parents) and
                all(os.environ.get(name) == "success" for name in
                    ("BUILD_OUTCOME", "EXECUTION_OUTCOME", "RESOURCE_CONTROLS_OUTCOME")))
    destination.mkdir(parents=True, exist_ok=True)
    (destination / "proof.json").write_text(json.dumps({
        "schema": 1, "candidateHead": head, "executedSource": executed, "mergeParents": parents,
        "producers": graph, "runId": os.environ.get("GITHUB_RUN_ID"), "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
        "test": {"name": EXPECTED, "exactCompletedPass": passed}, "resourceControls": controls,
        "cleanup": {"observedExactBackendAbsenceAndOwnerRelease": cleanup}, "complete": complete,
        "covered": ["actual consumer service and HTTP gateway", "actual Procurement HTTP and PostgreSQL root/item writes",
                    "root replay with identical key/body", "committed child acknowledgement loss", "later child transport rejection",
                    "original and foreign row preservation", "zero automatic DELETE", "live IAM dispatch and forbidden no-write controls"],
        "authority": "existing disposable RSA issuer and exact synthetic IIamServiceClient live allowlist; normal JWT/RequireLiveCheck handlers",
        "excluded": ["real Auth/IAM issuance or operational acceptance", "child idempotency (pinned endpoint creates new rows)",
                     "durable-create activation", "PDF/file workflow", "BFF/browser admission", "deployment or existing databases"],
    }, indent=2) + "\n")
    if not complete:
        raise SystemExit("Purchase-order producer/consumer storage proof incomplete; sanitized actual receipt retained")


if __name__ == "__main__":
    main()
