"""New PO retainer: exact TRX and unchanged supplier cleanup verifier derived from reviewed resolve source; no native result forecast."""
"""Retain names/outcomes and revisions only; never token, principal, database or assertion data."""
import json
import os
import pathlib
import re
import subprocess
import xml.etree.ElementTree as ET

EXPECTED = "SupplierCatalogPersistence.Acceptance.PurchaseOrderCompanyCatalogPersistenceTests.ShippingAndBilling_EnglishAndThai_CatalogSelectionOrdinarySavePdfFileReadbackAndReload"
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
PINS = {'Legacy.Maliev.CatalogService': '3f426723743570a6c20d2c014499445abb0774e1', 'Legacy.Maliev.ProcurementService': '614a157ab5c8739c144554797a387c45990ba7fe', 'Legacy.Maliev.ServiceDefaults': '7b3099bf67d0f17e56cfdb3dcf36541304abaac2', 'Legacy.Maliev.CompatibilityContracts': '99529ad665503b227184c0baa946ad4e62db978a', 'Legacy.Maliev.DocumentService': 'b12b9499d3f33113d2f0be196db831759ea3a03c', 'Legacy.Maliev.FileService': 'd478d2674b57a25c939f62aa838efbedd8591055', 'Legacy.Maliev.EmployeeService': '5ace8e2b7b1de498100c2e3cd7d2327451578ea7'}

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
        sanitized["complete"] = (len(results) == len(EXPECTED_CONTROLS) and
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
EXPECTED_CLIENT_PHASES = {'signing-key': 5, 'protection-key': 5, 'certificate': 5, 'authority-host': 4, 'authority-startup': 0, 'po-company-provider-host': 4, 'po-company-provider-startup': 0, 'po-company-provider-transport': 3, 'po-file-protocol-cancellation': 3, 'po-file-sdk-signing-identity': 3, 'po-file-signed-read-listener': 1, 'po-file-instream-listener': 1, 'po-file-protocol-task-settlement': 2, 'po-file-strict-storage-sdk': 3, 'playwright-driver': 1, 'chromium': 0, 'create-denied-context': 0, 'manual-denied-context': 0, 'manual-denied-page': 0, 'catalog-factory': 3, 'catalog-startup': 0, 'catalog-http': 0, 'Legacy.Maliev.CatalogService.Api-actual-host': 2, 'procurement-factory': 3, 'procurement-startup': 0, 'procurement-http': 0, 'Legacy.Maliev.ProcurementService.Api-actual-host': 2, 'employee-factory': 3, 'employee-startup': 0, 'employee-http': 0, 'Legacy.Maliev.EmployeeService.Api-actual-host': 2, 'document-factory': 3, 'document-startup': 0, 'document-http': 0, 'Legacy.Maliev.DocumentService.Api-actual-host': 2, 'file-factory': 3, 'file-startup': 0, 'file-http': 0, 'Legacy.Maliev.FileService.Api-actual-host': 2, 'bff-factory': 3, 'bff-startup': 0, 'bff-http': 0, 'Legacy.Maliev.Intranet.Bff-actual-host': 2, 'case-shipping-en-context': 0, 'case-shipping-en-page': 0, 'case-shipping-th-context': 0, 'case-shipping-th-page': 0, 'case-billing-en-context': 0, 'case-billing-en-page': 0, 'case-billing-th-context': 0, 'case-billing-th-page': 0}
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


def exact_rows(path, names):
    try:
        namespace = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
        document = ET.parse(path)
        results = document.findall('.//t:UnitTestResult', namespace)
        counters = document.findall('.//t:ResultSummary/t:Counters', namespace)
        summaries = document.findall('.//t:ResultSummary', namespace)
        expected = {'total': len(names), 'executed': len(names), 'passed': len(names), **dict.fromkeys(ZERO_COUNTERS, 0)}
        return (len(results) == len(names) and sorted(row.get('testName') for row in results) == sorted(names) and
                all(row.get('outcome') == 'Passed' for row in results) and
                len(counters) == 1 and counters[0].attrib == {key: str(value) for key, value in expected.items()} and
                len(summaries) == 1 and summaries[0].get('outcome') == 'Completed')
    except (OSError, ET.ParseError, TypeError):
        return False


def component_rows(path):
    try:
        namespace = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
        document = ET.parse(path)
        rows = document.findall('.//t:UnitTestResult', namespace)
        expected = set()
        prefix = 'PurchaseOrderCompanyCatalogPersistence.Acceptance.PurchaseOrderCompanyHooksComponentTests.'
        first = 'Selection_NotifiesOnlyItsDocumentPartyAndPreservesManualState'
        second = 'CapturedSelectionDeliveredAfterSubmitBegins_CannotAlterOrdinaryRequest'
        third = 'OversizedSuggestion_RemainsUntruncatedAndOrdinaryValidationStopsSave'
        for party in ('shipping','billing'):
            for lang in ('en','th'):
                expected.add((first,party,lang))
            expected.add((second,party,None))
            expected.add((third,party,None))
        actual = []
        for row in rows:
            name=row.get('testName','')
            if not name.startswith(prefix) or row.get('outcome')!='Passed':
                return False
            method=name[len(prefix):].split('(')[0]
            party=re.search(r'party: "(shipping|billing)"',name)
            lang=re.search(r'language: "(en|th)"',name)
            if party is None or method not in (first,second,third) or (method==first and lang is None):
                return False
            actual.append((method,party[1],lang[1] if lang else None))
        counters=document.findall('.//t:ResultSummary/t:Counters',namespace)
        summaries=document.findall('.//t:ResultSummary',namespace)
        expected_counters={'total':8,'executed':8,'passed':8,**dict.fromkeys(ZERO_COUNTERS,0)}
        return (len(actual)==8 and set(actual)==expected and len(counters)==1 and
                counters[0].attrib=={key:str(value) for key,value in expected_counters.items()} and
                len(summaries)==1 and summaries[0].get('outcome')=='Completed')
    except (OSError, ET.ParseError, TypeError):
        return False


def main(root=None):
    from po_receipt import journey
    root = pathlib.Path(root).resolve() if root is not None else pathlib.Path(__file__).resolve().parents[2]
    results = pathlib.Path(os.environ['PROOF_RESULTS'])
    directory = pathlib.Path(os.environ['PROOF_EVIDENCE'])
    graph = {name: revision(root / '.dependencies' / name) for name in PINS}
    head = os.environ.get('CANDIDATE_HEAD', '')
    executed = revision(root)
    parents = merge_parents(root)
    native = actual_result(results / 'po-company-persistence.trx')
    controls = actual_controls(results / 'supplier-resource-controls.trx')
    transport = exact_rows(results / 'po-transport-controls.trx', ['SupplierCatalogPersistence.Acceptance.PoOriginalCatalogTransportControls.InvalidLogicalOrPhysicalRequests_NeverReachExternalFixture'])
    file_controls = exact_rows(results / 'po-file-controls.trx', ['SupplierCatalogPersistence.Acceptance.PoOwnedFileFixtureControls.SyntheticFileProtocols_CompleteScanConditionalPromotionSignedReadAndDenialReleaseOwner'])
    component = component_rows(results / 'po-component-controls.trx')
    cleanup = resource_release(directory)
    business = journey(directory)
    gates = {key: os.environ.get(key) == 'success' for key in ('BUILD_OUTCOME','FORMAT_OUTCOME','COMPONENT_OUTCOME','TRANSPORT_OUTCOME','FILE_CONTROLS_OUTCOME','EXECUTION_OUTCOME','RESOURCE_CONTROLS_OUTCOME')}
    complete = (native['complete'] and controls['complete'] and transport and file_controls and component and cleanup and business and
                all(gates.values()) and graph == PINS and bound_candidate(executed, head, parents))
    report = {
        'schema':1, 'candidateHead':head, 'executedSource':executed, 'mergeParents':parents,
        'producers':graph, 'runId':os.environ.get('GITHUB_RUN_ID'), 'runAttempt':os.environ.get('GITHUB_RUN_ATTEMPT'),
        'complete':complete, 'nativeJourney':native, 'resourceControls':controls, 'gates':gates,
        'transportControlsComplete':transport, 'fileProtocolControlsComplete':file_controls,
        'componentControlsComplete':component,
        'businessReceiptComplete':business, 'observedExactBackendAbsenceAndOwnerRelease':cleanup,
        'covered':['PO shipping/billing company selection in English/Thai', 'ordinary PO create and distinct address/contact/item persistence',
                   'actual QuestPDF party names', 'normal File scan/promotion/journal/metadata', 'independent API/result/download/reload', 'CSRF/create/company permission denials with manual save'],
        'excluded':['live IAM authority','live Creden','live Google Cloud Storage','real ClamAV malware engine','all other PO variants','invoice','quotation','customer','AppHost orchestration','production deployment'],
    }
    directory.mkdir(parents=True, exist_ok=True)
    (directory / 'proof.json').write_text(json.dumps(report, indent=2)+'\n')
    if not complete:
        raise SystemExit('PO company proof incomplete; sanitized actual result retained')


if __name__ == '__main__':
    main()
