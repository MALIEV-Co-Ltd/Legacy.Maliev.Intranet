"""Retain names/outcomes and revisions only; never token, principal, database or assertion data."""
import json
import os
import pathlib
import re
import subprocess
import xml.etree.ElementTree as ET
from datetime import datetime

EXPECTED = "SupplierCatalogPersistence.Acceptance.SupplierCompanyCatalogPersistenceTests.ExistingSupplier_RealCompanySelection_OrdinaryUpdateReadbackAndReload"
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
    "Legacy.Maliev.ServiceDefaults": "7b3099bf67d0f17e56cfdb3dcf36541304abaac2",
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
    complete = (strict_result_ids(results) and len(results) == 1 and matches == ["Passed"] and len(counters) == 1 and
                counters[0].attrib == {key: str(value) for key, value in expected.items()} and
                len(summaries) == 1 and summaries[0].get("outcome") == "Completed")
    return {"outcomes": matches, "complete": complete}

def actual_controls(path):
    sanitized = {"tests": [{"name": name, "outcomes": []} for name in EXPECTED_CONTROLS], "complete": False}
    try:
        document = ET.parse(path)
        namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
        results = document.findall(".//t:UnitTestResult", namespace)
        for test in sanitized["tests"]:
            test["outcomes"] = [row.get("outcome") if row.get("outcome") in OUTCOMES else "Unavailable"
                                for row in results if row.get("testName") == test["name"]]
        summaries = document.findall(".//t:ResultSummary", namespace)
        counters = document.findall(".//t:ResultSummary/t:Counters", namespace)
        expected = {"total": len(EXPECTED_CONTROLS), "executed": len(EXPECTED_CONTROLS),
                    "passed": len(EXPECTED_CONTROLS), **dict.fromkeys(ZERO_COUNTERS, 0)}
        sanitized["complete"] = (strict_result_ids(results) and len(results) == len(EXPECTED_CONTROLS) and
                                 all(test["outcomes"] == ["Passed"] for test in sanitized["tests"]) and
                                 len(summaries) == 1 and summaries[0].get("outcome") == "Completed" and
                                 len(counters) == 1 and counters[0].attrib == {key: str(value) for key, value in expected.items()})
    except (OSError, ET.ParseError):
        pass
    return sanitized

def strict_result_ids(results):
    pattern = r"[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}"
    return bool(results) and all(
        len({row.get(key) for row in results}) == len(results)
        and all(re.fullmatch(pattern, row.get(key, "")) is not None for row in results)
        for key in ("testId", "executionId"))

def bound_candidate(executed, candidate, parents):
    return (re.fullmatch(r"[0-9a-f]{40}", candidate or "") is not None and
            (executed == candidate or (len(parents) == 2 and parents[1] == candidate)))

# Exact healthy journey inventory from the frozen real supplier test registrations.
# Actual-host names are derived from Program assembly names in RuntimeFactory.CreateHost.
EXPECTED_CLIENT_PHASES = {
    "authority-startup": 0, "catalog-startup": 0, "catalog-http": 0,
    "procurement-startup": 0, "procurement-http": 0, "bff-startup": 0,
    "bff-http": 0, "chromium": 0, "browser-context": 0,
    "playwright-driver": 1,
    "Legacy.Maliev.CatalogService.Api-actual-host": 2,
    "Legacy.Maliev.ProcurementService.Api-actual-host": 2,
    "Legacy.Maliev.Intranet.Bff-actual-host": 2,
    "catalog-factory": 3, "procurement-factory": 3, "bff-factory": 3,
    "company-provider-startup": 0, "company-provider-transport": 3, "company-provider-host": 4,
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

def backend_resource_release(directory):
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
            if not exact_backend_envelope(birth, signature, role, scopes):
                return False
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

def utc_timestamp(value):
    if not isinstance(value,str) or not re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)",value):
        raise ValueError("UTC timestamp required")
    return datetime.fromisoformat(value.replace("Z","+00:00"))

def exact_backend_envelope(birth, signature, role, scopes):
    try:
        memory,cpu,mount=(536870912,1000000000,"/var/lib/postgresql") if role=="postgres" else (134217728,500000000,"/data")
        created=utc_timestamp(birth["Created"]);expiry=utc_timestamp(birth["expires"])
        scope_expiries=[utc_timestamp(scope["expires"]) for scope in scopes]
        labels=signature["labels"];keys=[pair["Key"] for pair in labels]
        label_values={pair["Key"]:pair["Value"] for pair in labels}
        return (set(signature)=={"ID","Created","Image","Name","labels","mounts","tmpfs","ports","Memory","NanoCPUs","Init","Cmd","Entrypoint"}
                and birth["localEndpoint"]=="unix:///var/run/docker.sock" and signature["Created"]==birth["Created"]
                and signature["Image"]==birth["Image"] and re.fullmatch(r"sha256:[a-f0-9]{64}",birth["Image"]) is not None
                and type(birth["Memory"]) is int and birth["Memory"]==memory==signature["Memory"]
                and type(birth["Cpu"]) is int and birth["Cpu"]==cpu==signature["NanoCPUs"]
                and type(signature["Memory"]) is int and type(signature["NanoCPUs"]) is int
                and created < expiry and scope_expiries and len(set(scope_expiries))==1 and created < scope_expiries[0] < expiry
                and len(keys)==len(set(keys)) and all(set(pair)=={"Key","Value"} and isinstance(pair["Value"],str) for pair in labels)
                and label_values.get("maliev.owner")=="intranet-supplier-proof" and label_values.get("maliev.run")==birth["run"]
                and label_values.get("maliev.expires-utc")==birth["expires"]
                and signature["tmpfs"]==[{"Key":mount,"Value":"rw,size=268435456"}]
                and isinstance(signature["mounts"],list) and all(set(item)=={"Type","Source","Destination","RW"} and item["Type"]=="tmpfs" and item["Destination"]==mount and item["RW"] is True for item in signature["mounts"])
                and isinstance(signature["ports"],list) and len(signature["ports"])==1
                and signature["ports"][0]["Key"]==("5432/tcp" if role=="postgres" else "6379/tcp")
                and len(signature["ports"][0]["Value"])==1 and signature["ports"][0]["Value"][0]["HostIp"]=="127.0.0.1"
                and (signature["ports"][0]["Value"][0]["HostPort"]=="" or
                     re.fullmatch(r"[0-9]+",signature["ports"][0]["Value"][0]["HostPort"]) is not None
                     and 1024 < int(signature["ports"][0]["Value"][0]["HostPort"]) <= 65535))
    except (ValueError,KeyError,TypeError,AttributeError,IndexError):
        return False


TRANSPORT_TEST = "SupplierCatalogPersistence.Acceptance.SupplierCompanyTransportControls.ActualTransportRejectsElevenInvalidContractsBeforeAnyPhysicalAttempt"
PROVIDER_TEST = "SupplierCatalogPersistence.Acceptance.SupplierCompanyProviderControls.FailedStartupBeforeTransportBirth_StopsOwnedProviderAndReleasesScope"
OBSERVATION = dict(logicalUri="https://data.creden.co/sapi/search/get_suggestion", physicalLoopback=True,
                   method="POST", typeSearch="prefix", query="Synthetic supplier company", language="en", status=200)

def provider_release(directory):
    try:
        rows = [json.loads(line) for line in (directory / "provider.jsonl").read_text().splitlines()]
        scopes = [json.loads(line) for line in (directory / "scope.jsonl").read_text().splitlines()]
        resources = [json.loads(line) for line in (directory / "resources.jsonl").read_text().splitlines()]
        row, = rows
        keys = {"schema", "state", "admittedRequests", "completedRequests", "activeRequests", "transportAllocated",
                "exactTransportDisposed", "runId", "runAttempt", "owner", "sequence"}
        return (set(row) == keys and type(row["schema"]) is int and row["schema"] == 1 and row["state"] == "stopped-disposed"
                and all(type(row[k]) is int and row[k] == v for k,v in {"admittedRequests":1,"completedRequests":1,"activeRequests":0}.items())
                and row["transportAllocated"] is True and row["exactTransportDisposed"] is True
                and row["runId"] == os.environ.get("GITHUB_RUN_ID") and row["runAttempt"] == os.environ.get("GITHUB_RUN_ATTEMPT")
                and re.fullmatch(r"[0-9]+", row["runId"]) is not None and re.fullmatch(r"[0-9]+", row["runAttempt"]) is not None
                and re.fullmatch(r"[a-f0-9]{32}", row["owner"]) is not None
                and type(row["sequence"]) is int and row["sequence"] > 0
                and resources and all(resource["owner"]==row["owner"] for resource in resources)
                and row["sequence"] not in {event["sequence"] for event in resources+scopes}
                and len([event for event in resources if event["state"]=="created"])==2
                and row["sequence"] > max(event["sequence"] for event in resources if event["state"]=="created")
                and scopes and all(scope["owner"] == row["owner"] for scope in scopes)
                and any(scope["state"] == "clients-quiescent" and row["sequence"] < scope["sequence"] for scope in scopes))
    except (OSError, ValueError, KeyError, TypeError):
        return False

def resource_release(directory):
    return backend_resource_release(directory) and provider_release(directory)

def edit_journey(directory):
    try:
        row=json.loads((directory/"company-journey.json").read_text())
        resources=[json.loads(line) for line in (directory/"resources.jsonl").read_text().splitlines()]
        identifiers=("supplierId","originalAddressId","persistedSupplierId","persistedAddressId","profileAddressId")
        flags=("manualAddressPreserved","countryPreserved","addressTuplePreserved","deniedUpdatePreservedOriginal","reloadMatched","singleSupplierAndAddress")
        statuses={"lookupStatus":200,"updateStatus":204,"addressReadStatus":200,"profileReadStatus":200,"csrfDeniedStatus":400,
                  "employeeUpdateDeniedStatus":403,"workloadUpdateDeniedStatus":403,"employeeLookupDeniedStatus":403,"workloadLookupDeniedStatus":403,"deniedLookupPhysicalAttempts":0}
        names=("selectedName","persistedName","catalogName")
        taxes=("selectedTaxNumber","persistedTaxNumber","catalogTaxNumber")
        keys={"schema","owner","runId","runAttempt","catalogOutcome","catalogProvider","catalogCapability","observations",*identifiers,*flags,*statuses,*names,*taxes}
        return (isinstance(row,dict) and set(row)==keys and type(row["schema"]) is int and row["schema"]==1
                and re.fullmatch(r"[a-f0-9]{32}",row["owner"]) is not None and resources and all(r["owner"]==row["owner"] for r in resources)
                and re.fullmatch(r"[0-9]+",row["runId"]) is not None and re.fullmatch(r"[0-9]+",row["runAttempt"]) is not None
                and row["runId"]==os.environ.get("GITHUB_RUN_ID") and row["runAttempt"]==os.environ.get("GITHUB_RUN_ATTEMPT")
                and all(type(row[k]) is int and row[k]>0 for k in identifiers)
                and row["supplierId"]==row["persistedSupplierId"] and row["originalAddressId"]==row["persistedAddressId"]==row["profileAddressId"]
                and all(row[k]=="Synthetic Company Limited" for k in names) and all(row[k]=="1234567890123" for k in taxes)
                and (row["catalogOutcome"],row["catalogProvider"],row["catalogCapability"])==("matches","creden","suggestion")
                and row["observations"]==[OBSERVATION] and type(row["observations"][0]["status"]) is int
                and row["observations"][0]["physicalLoopback"] is True
                and all(type(row[k]) is int and row[k]==v for k,v in statuses.items()) and all(row[k] is True for k in flags))
    except (OSError,ValueError,KeyError,TypeError):
        return False

def extra_native_result(path, expected):
    global EXPECTED
    original=EXPECTED
    try:
        EXPECTED=expected
        return actual_result(path)
    finally:
        EXPECTED=original

def transport_receipt(root):
    try:
        row=json.loads((pathlib.Path(os.environ["PROOF_RESULTS"])/"transport-controls.json").read_text())
        expected=dict(nativeTransportControls=True, rejectedCases=[f"physical-origin-{index}" for index in range(4)] +
                      ["logical-http","logical-other-host","logical-other-path","logical-query","method","body","authorization"],
                      rejectedPhysicalAttempts=0, validPhysicalAttempts=1, realNetworkAllocated=False, validLoopbackUriVerified=True)
        return row==expected and all(row[k] is expected[k] for k in ("nativeTransportControls","realNetworkAllocated","validLoopbackUriVerified")) and all(type(row[k]) is int for k in ("rejectedPhysicalAttempts","validPhysicalAttempts"))
    except (OSError,ValueError,KeyError,TypeError):
        return False


def main(root=None):
    root = pathlib.Path(root).resolve() if root is not None else pathlib.Path(__file__).resolve().parents[2]
    graph = {name: revision(root / ".dependencies" / name) for name in PINS}
    result = actual_result(pathlib.Path(os.environ["PROOF_RESULTS"]) / "supplier-company-persistence.trx")
    controls = actual_controls(pathlib.Path(os.environ["PROOF_RESULTS"]) / "supplier-resource-controls.trx")
    transport = extra_native_result(pathlib.Path(os.environ["PROOF_RESULTS"]) / "supplier-company-transport.trx", TRANSPORT_TEST)
    provider = extra_native_result(pathlib.Path(os.environ["PROOF_RESULTS"]) / "supplier-company-provider.trx", PROVIDER_TEST)
    head = os.environ.get("CANDIDATE_HEAD", "")
    executed = revision(root)
    parents = merge_parents(root)
    journey_complete = edit_journey(pathlib.Path(os.environ["PROOF_EVIDENCE"]))
    cleanup_complete = resource_release(pathlib.Path(os.environ["PROOF_EVIDENCE"]))
    complete = (result["complete"] and controls["complete"] and transport["complete"] and provider["complete"] and transport_receipt(root) and graph == PINS and bound_candidate(executed, head, parents) and
                os.environ.get("BUILD_OUTCOME") == "success" and
                os.environ.get("EXECUTION_OUTCOME") == "success" and cleanup_complete and journey_complete and
                os.environ.get("RESOURCE_CONTROLS_OUTCOME") == "success" and os.environ.get("TRANSPORT_CONTROLS_OUTCOME") == "success" and os.environ.get("PROVIDER_CONTROLS_OUTCOME") == "success")
    evidence = {
        "schema": 1, "candidateHead": head, "executedSource": executed, "mergeParents": parents, "producers": graph,
        "unavailableProducers": [name for name, value in graph.items() if value is None],
        "runId": os.environ.get("GITHUB_RUN_ID"), "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
        "tests": [{"name": EXPECTED, "outcomes": result["outcomes"]}], "complete": complete,
        "resourceControls": controls, "transportControls": transport, "providerControls": provider, "editJourneyComplete": journey_complete,
        "covered": ["Intranet supplier EDIT (initial ordinary create prerequisite)", "actual Catalog company suggestion via owned physical synthetic provider", "ordinary supplier/address update with original IDs preserved", "independent domain API readback", "page reload", "CSRF and employee/workload rejection controls"],
        "excluded": ["live Creden and 503/429 behavior", "real AuthService issuance", "AppHost orchestration", "customer", "billing", "shipping", "purchase order", "quotation"],
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
