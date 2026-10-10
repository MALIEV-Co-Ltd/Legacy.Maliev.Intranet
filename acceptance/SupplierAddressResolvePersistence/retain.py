"""Retain names/outcomes and revisions only; never token, principal, database or assertion data."""
import json
import os
import pathlib
import re
import subprocess
import xml.etree.ElementTree as ET

EXPECTED = "SupplierCatalogPersistence.Acceptance.SupplierAddressResolvePersistenceTests.ExistingSupplier_PastedAddressRequiresReviewAndApply_RealSaveReadbackAndReload"
CONTROL_PREFIX = "SupplierCatalogPersistence.Acceptance.SupplierResourceControls."
EXPECTED_CONTROLS = tuple(CONTROL_PREFIX + name for name in (
    "PartialStartup_StillReleasesAllocatedHostBeforeBackend",
    "FailedShutdown_PreservesBackendUntilSuccessfulRetry",
    "UnsettledStartup_FencesBackendRemoval",
    "ExpiredOwner_RejectsNewAcquisition",
    "ExpiredPreregisteredStartup_NeverDispatchesCallback",
    "AllocationFailure_RemainsReachableAndSettlesWithoutReleaseOfUnknownChild",
    "SynchronouslyBlockedClientAllocation_IsBoundedAndLateResourceRemainsOwned",
    "SynchronouslyBlockedBackendAllocation_IsBoundedAndLateResourceRemainsOwned",
    "SynchronouslyBlockedStartup_IsBoundedAndFencesDependencies",
    "SynchronouslyBlockedRelease_IsBoundedAndRetryAwaitsSameAttempt",
    "TerminalBeforeQueuedAllocation_NeverInvokesCallback",
    "TerminalBeforeQueuedStartup_NeverInvokesCallback",
    "TerminalBeforeAllocationReturns_RejectsResourceHandoff",
))
ZERO_COUNTERS = ("error", "failed", "timeout", "aborted", "inconclusive", "passedButRunAborted",
                 "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending")
OUTCOMES = {"Passed", "Failed", "NotExecuted", "Aborted", "Timeout", "Error", "Inconclusive", "NotRunnable"}
PINS = {
    "Legacy.Maliev.CatalogService": "3f426723743570a6c20d2c014499445abb0774e1",
    "Legacy.Maliev.ProcurementService": "614a157ab5c8739c144554797a387c45990ba7fe",
    "Legacy.Maliev.ServiceDefaults": "4517cf16f5f1159318e184969732d46eae4a8308",
    "Legacy.Maliev.CompatibilityContracts": "99529ad665503b227184c0baa946ad4e62db978a",
}

def revision(path):
    try:
        result = subprocess.run(["git", "-C", str(path), "rev-parse", "--show-toplevel", "HEAD"],
                                text=True, capture_output=True, check=False)
        values = result.stdout.splitlines()
        if (result.returncode == 0 and len(values) == 2 and
                pathlib.Path(values[0]).resolve() == pathlib.Path(path).resolve() and
                re.fullmatch(r"[0-9a-f]{40}", values[1])):
            return values[1]
    except OSError:
        pass
    return None

def merge_parents(path):
    try:
        result = subprocess.run(["git", "-C", str(path), "show", "-s", "--format=%P", "HEAD"],
                                text=True, capture_output=True, check=False)
        values = result.stdout.split()
        if result.returncode == 0 and all(re.fullmatch(r"[0-9a-f]{40}", value) for value in values):
            return values
    except OSError:
        pass
    return []

def actual_result(path):
    try:
        document = ET.parse(path)
    except (OSError, ET.ParseError):
        return {"outcomes": [], "complete": False}
    namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    results = document.findall(".//t:UnitTestResult", namespace)
    matches = [result.get("outcome") if result.get("outcome") in OUTCOMES else "Unavailable"
               for result in results if result.get("testName") == EXPECTED]
    counters = document.findall(".//t:ResultSummary/t:Counters", namespace)
    summaries = document.findall(".//t:ResultSummary", namespace)
    expected = {"total": 1, "executed": 1, "passed": 1, **dict.fromkeys(ZERO_COUNTERS, 0)}
    complete = (len(results) == 1 and matches == ["Passed"] and len(counters) == 1 and
                counters[0].attrib == {key: str(value) for key, value in expected.items()} and
                len(summaries) == 1 and summaries[0].get("outcome") == "Completed")
    return {"outcomes": matches, "complete": complete}

def actual_controls(path, names=EXPECTED_CONTROLS):
    sanitized = {"tests": [{"name": name, "outcomes": []} for name in names], "complete": False}
    try:
        document = ET.parse(path)
        namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
        results = document.findall(".//t:UnitTestResult", namespace)
        for test in sanitized["tests"]:
            test["outcomes"] = [row.get("outcome") if row.get("outcome") in OUTCOMES else "Unavailable"
                                for row in results if row.get("testName") == test["name"]]
        summaries = document.findall(".//t:ResultSummary", namespace)
        counters = document.findall(".//t:ResultSummary/t:Counters", namespace)
        expected = {"total": len(names), "executed": len(names),
                    "passed": len(names), **dict.fromkeys(ZERO_COUNTERS, 0)}
        sanitized["complete"] = (len(results) == len(names) and
                                 all(test["outcomes"] == ["Passed"] for test in sanitized["tests"]) and
                                 len(summaries) == 1 and summaries[0].get("outcome") == "Completed" and
                                 len(counters) == 1 and counters[0].attrib == {key: str(value) for key, value in expected.items()})
    except (OSError, ET.ParseError):
        pass
    return sanitized

def bound_candidate(executed, candidate, parents):
    return (re.fullmatch(r"[0-9a-f]{40}", candidate or "") is not None and
            (executed == candidate or (len(parents) == 2 and parents[1] == candidate)))

# Exact healthy journey inventory from the frozen real supplier test registrations.
# Actual-host names are derived from Program assembly names in RuntimeFactory.CreateHost.
EXPECTED_CLIENT_PHASES = {
    "authority-startup": 0, "catalog-startup": 0, "catalog-http": 0,
    "procurement-startup": 0, "procurement-http": 0, "bff-startup": 0,
    "bff-http": 0, "chromium": 0, "browser-context": 0,
    "address-browser-response-observer": 0,
    "playwright-driver": 1,
    "Legacy.Maliev.CatalogService.Api-actual-host": 2,
    "Legacy.Maliev.ProcurementService.Api-actual-host": 2,
    "Legacy.Maliev.Intranet.Bff-actual-host": 2,
    "catalog-factory": 3, "procurement-factory": 3, "bff-factory": 3,
    "authority-host": 4, "signing-key": 5, "protection-key": 5, "certificate": 5,
}
EXPECTED_BACKENDS = frozenset(("postgres-and-redis", "backend-startup"))
EXPECTED_BACKEND_COMMANDS = {
    "postgres": "exec timeout -k 5 900 docker-entrypoint.sh postgres",
    "redis": "exec timeout -k 5 900 docker-entrypoint.sh redis-server --save '' --appendonly no",
}

def exact_participants(row):
    clients, backends = row["clients"], row["backends"]
    if not isinstance(clients, list) or not isinstance(backends, list):
        return False
    if len(clients) != len(EXPECTED_CLIENT_PHASES) or len(backends) != len(EXPECTED_BACKENDS):
        return False
    names = [item["Name"] for item in clients]
    backend_names = [item["Name"] for item in backends]
    return (len(set(names)) == len(names) and set(names) == set(EXPECTED_CLIENT_PHASES) and
            all(type(item["Phase"]) is int and item["Phase"] == EXPECTED_CLIENT_PHASES[item["Name"]]
                for item in clients) and
            len(set(backend_names)) == len(backend_names) and set(backend_names) == EXPECTED_BACKENDS)

def same_participants(left, right):
    # Teardown changes release flags, never the fenced participant names or phases.
    return (sorted((item["Name"], item["Phase"]) for item in left["clients"]) ==
            sorted((item["Name"], item["Phase"]) for item in right["clients"]) and
            sorted(item["Name"] for item in left["backends"]) ==
            sorted(item["Name"] for item in right["backends"]))

def resource_release(directory):
    try:
        resources = [json.loads(line) for line in (directory / "resources.jsonl").read_text().splitlines()]
        scopes = [json.loads(line) for line in (directory / "scope.jsonl").read_text().splitlines()]
        created = [row for row in resources if row.get("state") == "created"]
        absent = [row for row in resources if row.get("state") == "verified-absent"]
        if len(resources) != 4 or len(created) != 2 or len(absent) != 2 or len({row["Id"] for row in created}) != 2:
            return False
        owner = created[0]["owner"]
        run = created[0]["run"]
        if not re.fullmatch(r"[a-f0-9]{32}", owner) or not re.fullmatch(r"[a-f0-9]{32}", run):
            return False
        rows = resources + scopes
        sequences = [row["sequence"] for row in rows]
        if (not scopes or any(row["owner"] != owner for row in rows) or
                any(type(value) is not int or value <= 0 for value in sequences) or len(set(sequences)) != len(sequences) or
                any(row["run"] != run for row in resources) or
                len({row["daemon"] for row in created}) != 1 or not isinstance(created[0]["daemon"], str) or not created[0]["daemon"].strip() or
                any(row.get("state") not in {"clients-quiescent", "retained", "released"} for row in scopes) or
                [row["sequence"] for row in resources] != sorted(row["sequence"] for row in resources) or
                [row["sequence"] for row in scopes] != sorted(row["sequence"] for row in scopes) or
                {row["Name"] for row in created} != {f"supplier-proof-{run}-postgres", f"supplier-proof-{run}-redis"}):
            return False
        for birth in created:
            if not re.fullmatch(r"[a-f0-9]{64}", birth["Id"]):
                return False
            signature = json.loads(birth["ownershipSignature"])
            role = "postgres" if birth["Name"].endswith("-postgres") else "redis"
            if (not isinstance(signature, dict) or signature.get("Init") is not True or
                    signature.get("ID") != birth["Id"] or signature.get("Name") != "/" + birth["Name"] or
                    signature.get("Entrypoint") != ["/bin/sh", "-c"] or
                    signature.get("Cmd") != [EXPECTED_BACKEND_COMMANDS[role]]):
                return False
            matches = [row for row in absent if all(row.get(key) == birth.get(key) for key in ("owner", "run", "daemon", "Name", "Id", "expires"))]
            if len(matches) != 1 or birth["persistentData"] is not False or matches[0]["persistentData"] is not False:
                return False
        quiescent = [row for row in scopes if row.get("state") == "clients-quiescent"]
        released = [row for row in scopes if row.get("state") == "released"]
        ids = sorted(row["Id"] for row in created)
        binding = [{"Name": "postgres-and-redis", "Run": run, "ids": ids}]
        def settled(items):
            return bool(items) and all(item["Released"] is True and item["startupSettled"] is True for item in items)
        return (all(exact_participants(row) for row in scopes) and
                len(released) == 1 and scopes[-1] == released[0] and
                released[0]["sequence"] > max(row["sequence"] for row in resources) and
                released[0]["backendBindings"] == binding and
                {item["Name"] for item in released[0]["backends"]} == {"postgres-and-redis", "backend-startup"} and
                settled(released[0]["clients"]) and settled(released[0]["backends"]) and
                any(same_participants(row, released[0]) and
                    row["backendBindings"] == binding and settled(row["clients"]) and
                    all(item["startupSettled"] is True for item in row["backends"]) and
                    max(birth["sequence"] for birth in created) < row["sequence"] < min(end["sequence"] for end in absent)
                    for row in quiescent))
    except (OSError, ValueError, KeyError, TypeError, AttributeError):
        return False


OBSERVER_TESTS = tuple("SupplierCatalogPersistence.Acceptance.SupplierAddressBrowserObserverControls."+name for name in (
    "ActualPassiveCloneContractsAndFailureSettlement", "PendingCloneRetainsExactEvaluationUntilOwnedRealmClosesAndSameOwnerRetries", "CanceledInstallationRetainsOriginalRpcBeforeCloseAndOwnedRealmRetry"))
OBSERVER_CASES = ["same-promise-response-args-receiver","restored-pass-through-no-new-task","wrong-origin","wrong-path","unexpected-query","wrong-method",
                  "url-fragment","url-credentials","wrong-response-url","wrong-response-status","byte-cap","invalid-json","clone-read-error",
                  "duplicate-exact-request","original-fetch-rejection","deadline-pending-tee-until-original-close","detach-identity-interference"]

def browser_observation(row, successful=True):
    try:
        keys={"schema","source","observerId","maxBytes","deadlineMs","matchingRequests","capturedResponses","capturedBytes","status",
              "captureSucceeded","exactRequestAndResponseUrl","method","closed","fetchIdentityRestored","activeTasks","activeTimers","readSettled","cancelSettled"}
        if not (isinstance(row,dict) and set(row)==keys and type(row["schema"]) is int and row["schema"]==1
                and row["source"]=="actual-browser-fetch-clone" and re.fullmatch(r"[a-f0-9]{32}",row["observerId"])
                and type(row["maxBytes"]) is int and row["maxBytes"]==65536 and type(row["deadlineMs"]) is int and row["deadlineMs"]==5000
                and row["method"]=="POST" and all(row[k] is True for k in ("closed","fetchIdentityRestored","readSettled","cancelSettled"))
                and all(type(row[k]) is int and row[k]==0 for k in ("activeTasks","activeTimers"))
                and all(type(row[k]) is int for k in ("matchingRequests","capturedResponses","capturedBytes","status"))
                and type(row["captureSucceeded"]) is bool and type(row["exactRequestAndResponseUrl"]) is bool):
            return False
        return (all(row[k]==v for k,v in {"matchingRequests":1,"capturedResponses":1,"status":200}.items())
                and 1<=row["capturedBytes"]<=65536 and row["captureSucceeded"] is True and row["exactRequestAndResponseUrl"] is True) if successful else (
                    0<=row["matchingRequests"]<=2 and 0<=row["capturedResponses"]<=1 and 0<=row["capturedBytes"]<=65536 and 0<=row["status"]<=599)
    except (ValueError,KeyError,TypeError):
        return False

def browser_observer_release(directory):
    try:
        rows=[json.loads(line) for line in (directory/"browser-observer.jsonl").read_text().splitlines()]
        row,=rows
        resources=[json.loads(line) for line in (directory/"resources.jsonl").read_text().splitlines()]
        scopes=[json.loads(line) for line in (directory/"scope.jsonl").read_text().splitlines()]
        keys={"schema","state","observerId","installationDispatched","installationEvaluationSettled","retainedEvaluationSettled","ownedPageClosed","joinedReaderReceipt","runId","runAttempt","owner","sequence"}
        installed=row["installationDispatched"] is True and row["installationEvaluationSettled"] is True
        unborn=(row["state"]=="observer-never-dispatched" and row["installationDispatched"] is False and row["installationEvaluationSettled"] is False and row["ownedPageClosed"] is False and row["joinedReaderReceipt"] is None)
        realm=(installed and row["state"]=="owned-realm-destroyed-after-settled-evaluation" and row["ownedPageClosed"] is True and row["joinedReaderReceipt"] is None)
        joined=(installed and row["state"]=="reader-joined-fetch-restored" and row["ownedPageClosed"] is False and browser_observation(row["joinedReaderReceipt"],False)
                and row["observerId"]==row["joinedReaderReceipt"]["observerId"])
        all_events=resources+scopes
        return (set(row)==keys and type(row["schema"]) is int and row["schema"]==1 and row["retainedEvaluationSettled"] is True and (realm or joined or unborn)
                and re.fullmatch(r"[a-f0-9]{32}",row["observerId"]) and re.fullmatch(r"[a-f0-9]{32}",row["owner"])
                and row["runId"]==os.environ.get("GITHUB_RUN_ID") and row["runAttempt"]==os.environ.get("GITHUB_RUN_ATTEMPT")
                and type(row["sequence"]) is int and resources and scopes and all(event["owner"]==row["owner"] for event in all_events)
                and row["sequence"] not in {event["sequence"] for event in all_events}
                and max(event["sequence"] for event in resources if event["state"]=="created") < row["sequence"] < min(event["sequence"] for event in resources if event["state"]=="verified-absent")
                and any(event["state"]=="clients-quiescent" and row["sequence"] < event["sequence"] for event in scopes))
    except (OSError,ValueError,KeyError,TypeError):
        return False

def browser_controls_receipts(directory):
    try:
        row=json.loads((directory/"browser-observer-controls.json").read_text())
        realms=[json.loads((directory/name).read_text()) for name in ("browser-observer-realm-control.json","browser-observer-installation-control.json")]
        keys={"schema","state","observerId","installationDispatched","installationEvaluationSettled","retainedEvaluationSettled","ownedPageClosed","joinedReaderReceipt","runId","runAttempt"}
        return (row==dict(schema=1,nativeBrowserObserverControls=True,cases=OBSERVER_CASES,realNetworkAllocated=False)
                and type(row["schema"]) is int and row["nativeBrowserObserverControls"] is True and row["realNetworkAllocated"] is False
                and all(set(realm)==keys and type(realm["schema"]) is int and realm["schema"]==1
                    and realm["state"]=="owned-realm-destroyed-after-settled-evaluation"
                    and re.fullmatch(r"[a-f0-9]{32}",realm["observerId"])
                    and all(realm[k] is True for k in ("installationDispatched","installationEvaluationSettled","retainedEvaluationSettled","ownedPageClosed"))
                    and realm["joinedReaderReceipt"] is None and realm["runId"]==os.environ.get("GITHUB_RUN_ID") and realm["runAttempt"]==os.environ.get("GITHUB_RUN_ATTEMPT") for realm in realms)
                and len({realm["observerId"] for realm in realms})==2)
    except (OSError,ValueError,KeyError,TypeError):
        return False



def resolve_journey(directory):
    try:
        row = json.loads((directory / "resolve-journey.json").read_text())
        resources = [json.loads(line) for line in (directory / "resources.jsonl").read_text().splitlines()]
        observer, = [json.loads(line) for line in (directory / "browser-observer.jsonl").read_text().splitlines()]
        identifiers = ("supplierId", "originalAddressId", "persistedSupplierId", "persistedAddressId", "profileAddressId")
        flags = ("directAndBffResolutionMatched", "noAutoApply", "candidateSelectionDidNotApply",
                 "editedPreviewDidNotApply", "explicitApplyMatched", "reviewedDetailPersisted",
                 "address2AndBuildingPreserved", "countryPreserved", "taxPreserved",
                 "deniedResolvePreservedOriginal", "reloadMatched", "singleSupplierAndAddress")
        return (isinstance(row, dict) and type(row.get("schema")) is int and row["schema"] == 1 and
                re.fullmatch(r"[a-f0-9]{32}", row.get("owner", "")) is not None and
                resources and all(resource.get("owner") == row["owner"] for resource in resources) and
                re.fullmatch(r"[0-9]+", row.get("runId", "")) is not None and
                re.fullmatch(r"[0-9]+", row.get("runAttempt", "")) is not None and
                row["runId"] == os.environ.get("GITHUB_RUN_ID") and
                row["runAttempt"] == os.environ.get("GITHUB_RUN_ATTEMPT") and
                all(type(row.get(key)) is int and row[key] > 0 for key in identifiers) and
                row["supplierId"] == row["persistedSupplierId"] and
                row["originalAddressId"] == row["persistedAddressId"] == row["profileAddressId"] and
                row.get("initialPostcode") == "10110" and
                row.get("selectedPostcode") == row.get("persistedPostcode") == "11120" and
                row.get("resolutionOutcome") == "exact" and
                type(row.get("candidateCount")) is int and row["candidateCount"] == 1 and
                isinstance(row.get("datasetVersion"), str) and row["datasetVersion"].startswith("thailand-geography-json:") and
                all(type(row.get(key)) is int and row[key] == value for key, value in {
                    "directResolveStatus": 200, "resolveStatus": 200, "updateStatus": 204, "addressReadStatus": 200, "profileReadStatus": 200,
                    "csrfDeniedStatus": 400, "lookupDeniedStatus": 403, "manualSaveStatus": 204,
                }.items()) and all(row.get(key) is True for key in flags)
                and browser_observation(row["browserBodyObservation"]) and observer["owner"] == row["owner"]
                and observer["state"] == "reader-joined-fetch-restored" and observer["joinedReaderReceipt"] == row["browserBodyObservation"])
    except (OSError, ValueError, TypeError, KeyError):
        return False

def main(root=None):
    root = pathlib.Path(root).resolve() if root is not None else pathlib.Path(__file__).resolve().parents[2]
    graph = {name: revision(root / ".dependencies" / name) for name in PINS}
    result = actual_result(pathlib.Path(os.environ["PROOF_RESULTS"]) / "supplier-address-resolve-persistence.trx")
    controls = actual_controls(pathlib.Path(os.environ["PROOF_RESULTS"]) / "supplier-resource-controls.trx")
    observer = actual_controls(pathlib.Path(os.environ["PROOF_RESULTS"]) / "supplier-address-browser-observer.trx", OBSERVER_TESTS)
    head = os.environ.get("CANDIDATE_HEAD", "")
    executed = revision(root)
    parents = merge_parents(root)
    journey_complete = resolve_journey(pathlib.Path(os.environ["PROOF_EVIDENCE"]))
    cleanup_complete = resource_release(pathlib.Path(os.environ["PROOF_EVIDENCE"])) and browser_observer_release(pathlib.Path(os.environ["PROOF_EVIDENCE"]))
    complete = (result["complete"] and controls["complete"] and observer["complete"] and browser_controls_receipts(pathlib.Path(os.environ["PROOF_RESULTS"]))
                and os.environ.get("OBSERVER_CONTROLS_OUTCOME") == "success" and graph == PINS and bound_candidate(executed, head, parents) and
                os.environ.get("BUILD_OUTCOME") == "success" and
                os.environ.get("EXECUTION_OUTCOME") == "success" and cleanup_complete and journey_complete and
                os.environ.get("RESOURCE_CONTROLS_OUTCOME") == "success")
    evidence = {
        "schema": 1, "candidateHead": head, "executedSource": executed, "mergeParents": parents, "producers": graph,
        "unavailableProducers": [name for name, value in graph.items() if value is None],
        "runId": os.environ.get("GITHUB_RUN_ID"), "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
        "tests": [{"name": EXPECTED, "outcomes": result["outcomes"]}], "complete": complete,
        "resourceControls": controls, "browserObserverControls": observer, "resolveJourneyComplete": journey_complete,
        "covered": ["Existing supplier pasted-address exact resolution (initial ordinary create prerequisite)", "real Catalog resolve/explicit candidate/edited detail Apply", "ordinary supplier/address update with original IDs preserved", "independent domain API readback", "page reload", "resolve CSRF/missing only Catalog read denial with manual save permitted"],
        "excluded": ["Creden company selection", "real AuthService issuance", "AppHost orchestration", "customer", "billing", "shipping", "purchase order", "quotation"],
        "authority": "disposable synthetic RSA issuer; normal JWT and signed permission fallback",
        "cleanup": {"observedExactBackendAbsenceAndOwnerRelease": cleanup_complete},
    }
    destination = pathlib.Path(os.environ["PROOF_EVIDENCE"])
    destination.mkdir(parents=True, exist_ok=True)
    (destination / "proof.json").write_text(json.dumps(evidence, indent=2) + "\n")
    if not complete:
        raise SystemExit("Supplier persistence proof incomplete; sanitized actual result retained")

if __name__ == "__main__":
    main()
