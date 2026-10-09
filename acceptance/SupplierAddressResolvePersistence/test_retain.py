import copy
import json
import os
import pathlib
import subprocess
import tempfile
import unittest
from unittest import mock
import retain

class RetainerTests(unittest.TestCase):
    def observation(self):
        return dict(schema=1, source="actual-browser-fetch-clone", observerId="c"*32,
                    maxBytes=65536, deadlineMs=5000, matchingRequests=1, capturedResponses=1,
                    capturedBytes=313, status=200, captureSucceeded=True, exactRequestAndResponseUrl=True,
                    method="POST", closed=True, fetchIdentityRestored=True, activeTasks=0, activeTimers=0,
                    readSettled=True, cancelSettled=True)

    def test_observation_rejects_unjoined_oversized_foreign_method_and_unknown_fields(self):
        row=self.observation()
        self.assertTrue(retain.browser_observation(row))
        mutations=dict(method="GET", maxBytes=65537, deadlineMs=5001, matchingRequests=2,
                       capturedResponses=0, capturedBytes=65537, status=503, observerId="foreign",
                       activeTasks=1, activeTimers=1, readSettled=False, cancelSettled=False,
                       fetchIdentityRestored=False, captureSucceeded=False, exactRequestAndResponseUrl=False)
        for key in row:
            changed=dict(row); changed.pop(key)
            self.assertFalse(retain.browser_observation(changed), key)
        for key,value in mutations.items():
            self.assertFalse(retain.browser_observation(dict(row, **{key:value})), key)
        self.assertFalse(retain.browser_observation(dict(row, unexpected=True)))

    def test_observer_release_requires_same_owner_run_attempt_and_before_quiescence(self):
        with tempfile.TemporaryDirectory() as directory, mock.patch.dict(os.environ,GITHUB_RUN_ID="123",GITHUB_RUN_ATTEMPT="1"):
            root=pathlib.Path(directory)
            births,absent,scopes=self.release_rows()
            # Make a unique sequence slot between backend creation and client quiescence.
            for row in scopes: row["sequence"]+=1
            for row in absent: row["sequence"]+=1
            self.write_release_rows(root,births,absent,scopes)
            row=dict(schema=1,state="reader-joined-fetch-restored",observerId="c"*32,
                     installationDispatched=True,installationEvaluationSettled=True,retainedEvaluationSettled=True,
                     ownedPageClosed=False,joinedReaderReceipt=self.observation(),runId="123",runAttempt="1",owner="c"*32,sequence=3)
            path=root/"browser-observer.jsonl"
            path.write_text(json.dumps(row))
            self.assertTrue(retain.browser_observer_release(root))
            for key,value in dict(owner="b"*32,runId="124",runAttempt="2",sequence=4,
                                  retainedEvaluationSettled=False,installationEvaluationSettled=False,
                                  observerId="d"*32,ownedPageClosed=True).items():
                path.write_text(json.dumps(dict(row,**{key:value})))
                self.assertFalse(retain.browser_observer_release(root),key)
            path.write_text(json.dumps(row)+"\n"+json.dumps(row))
            self.assertFalse(retain.browser_observer_release(root))

    def test_native_observer_trx_requires_exact_three_passed_cases(self):
        with tempfile.TemporaryDirectory() as directory:
            path=pathlib.Path(directory)/"controls.trx"
            path.write_text(self.controls_xml(names=retain.OBSERVER_TESTS))
            self.assertTrue(retain.actual_controls(path,retain.OBSERVER_TESTS)["complete"])
            path.write_text(self.controls_xml(names=retain.OBSERVER_TESTS[:-1]))
            self.assertFalse(retain.actual_controls(path,retain.OBSERVER_TESTS)["complete"])

    def test_native_lifecycle_receipts_reject_stale_nonsettled_and_reused_observers(self):
        with tempfile.TemporaryDirectory() as directory, mock.patch.dict(os.environ,GITHUB_RUN_ID="123",GITHUB_RUN_ATTEMPT="1"):
            root=pathlib.Path(directory)
            controls=dict(schema=1,nativeBrowserObserverControls=True,cases=retain.OBSERVER_CASES,realNetworkAllocated=False)
            (root/"browser-observer-controls.json").write_text(json.dumps(controls))
            realm=dict(schema=1,state="owned-realm-destroyed-after-settled-evaluation",observerId="c"*32,
                       installationDispatched=True,installationEvaluationSettled=True,retainedEvaluationSettled=True,
                       ownedPageClosed=True,joinedReaderReceipt=None,runId="123",runAttempt="1")
            first=root/"browser-observer-realm-control.json"
            second=root/"browser-observer-installation-control.json"
            first.write_text(json.dumps(realm)); second.write_text(json.dumps(dict(realm,observerId="d"*32)))
            self.assertTrue(retain.browser_controls_receipts(root))
            for key,value in dict(runId="124",runAttempt="2",retainedEvaluationSettled=False,
                                  installationEvaluationSettled=False,ownedPageClosed=False,joinedReaderReceipt=self.observation(),observerId="d"*32).items():
                first.write_text(json.dumps(dict(realm,**{key:value})))
                self.assertFalse(retain.browser_controls_receipts(root),key)
            first.write_text(json.dumps(realm))
            (root/"browser-observer-controls.json").write_text(json.dumps(dict(controls,cases=retain.OBSERVER_CASES[:-1])))
            self.assertFalse(retain.browser_controls_receipts(root))

    def journey_row(self):
        return dict(schema=1, owner="a" * 32, runId="123", runAttempt="1",
                    supplierId=7, persistedSupplierId=7, originalAddressId=11,
                    persistedAddressId=11, profileAddressId=11, initialPostcode="10110",
                    selectedPostcode="11120", persistedPostcode="11120",
                    resolutionOutcome="exact", candidateCount=1,
                    datasetVersion="thailand-geography-json:fixture", directResolveStatus=200, resolveStatus=200,
                    updateStatus=204, addressReadStatus=200, profileReadStatus=200,
                    csrfDeniedStatus=400, lookupDeniedStatus=403, manualSaveStatus=204,
                    directAndBffResolutionMatched=True, noAutoApply=True, candidateSelectionDidNotApply=True,
                    editedPreviewDidNotApply=True, explicitApplyMatched=True, reviewedDetailPersisted=True,
                    address2AndBuildingPreserved=True, countryPreserved=True,
                    taxPreserved=True, deniedResolvePreservedOriginal=True, reloadMatched=True,
                    singleSupplierAndAddress=True, browserBodyObservation=self.observation())

    def test_resolve_receipt_requires_deliberate_confirmation_identity_denials_and_reload(self):
        with tempfile.TemporaryDirectory() as directory, mock.patch.dict(os.environ, GITHUB_RUN_ID="123", GITHUB_RUN_ATTEMPT="1"):
            root = pathlib.Path(directory)
            path = root / "resolve-journey.json"
            row = self.journey_row()
            (root / "resources.jsonl").write_text(json.dumps(dict(owner=row["owner"])) + "\n")
            (root / "browser-observer.jsonl").write_text(json.dumps(dict(owner=row["owner"], state="reader-joined-fetch-restored", joinedReaderReceipt=row["browserBodyObservation"])))
            path.write_text(json.dumps(row))
            self.assertTrue(retain.resolve_journey(root))
            for key in row:
                with self.subTest(missing=key):
                    changed = dict(row)
                    changed.pop(key)
                    path.write_text(json.dumps(changed))
                    self.assertFalse(retain.resolve_journey(root))
            mutations = {"owner": "b" * 32, "runId": "124", "runAttempt": "2",
                         "persistedSupplierId": 8, "persistedAddressId": 12, "profileAddressId": 12,
                         "supplierId": True, "originalAddressId": 0, "selectedPostcode": "10110",
                         "persistedPostcode": "10110", "datasetVersion": "fabricated",
                         "directResolveStatus": 500, "resolveStatus": 500, "resolutionOutcome": "ambiguous", "candidateCount": 2, "updateStatus": 201, "addressReadStatus": 404,
                         "profileReadStatus": 403, "csrfDeniedStatus": 204,
                         "lookupDeniedStatus": 204, "manualSaveStatus": 403}
            mutations.update({key: False for key, value in row.items() if value is True})
            for key, value in mutations.items():
                with self.subTest(key=key, value=value):
                    path.write_text(json.dumps(dict(row, **{key: value})))
                    self.assertFalse(retain.resolve_journey(root))
            for malformed in ("null", "[]", "{", ""):
                path.write_text(malformed)
                self.assertFalse(retain.resolve_journey(root))

    def test_resolve_native_result_rejects_nonzero_terminal_and_pending_counters(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "edit.trx"
            path.write_text(self.controls_xml(names=[retain.EXPECTED]))
            self.assertTrue(retain.actual_result(path)["complete"])
            for counter in retain.ZERO_COUNTERS:
                path.write_text(self.controls_xml(names=[retain.EXPECTED], overrides={counter: 1}))
                self.assertFalse(retain.actual_result(path)["complete"])

    def controls_xml(self, names=None, outcome="Passed", summary="Completed", overrides=None):
        names = retain.EXPECTED_CONTROLS if names is None else names
        counters = {"total": len(names), "executed": len(names), "passed": len(names),
                    **dict.fromkeys(retain.ZERO_COUNTERS, 0)}
        counters.update(overrides or {})
        attributes = " ".join(f'{key}="{value}"' for key, value in counters.items())
        results = "".join(f'<UnitTestResult testName="{name}" outcome="{outcome}" />' for name in names)
        return f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>{results}</Results><ResultSummary outcome="{summary}"><Counters {attributes} /></ResultSummary></TestRun>'

    def test_all_exact_resource_controls_must_actually_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "controls.trx"
            path.write_text(self.controls_xml())
            result = retain.actual_controls(path)
            self.assertTrue(result["complete"])
            self.assertEqual([row["name"] for row in result["tests"]], list(retain.EXPECTED_CONTROLS))
            self.assertTrue(all(row["outcomes"] == ["Passed"] for row in result["tests"]))

    def test_control_zero_missing_wrong_duplicate_skipped_and_incomplete_results_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "controls.trx"
            wrong = list(retain.EXPECTED_CONTROLS)
            wrong[-1] = "foreign.test"
            duplicate = list(retain.EXPECTED_CONTROLS)
            duplicate[-1] = duplicate[0]
            cases = [dict(names=[]), dict(names=retain.EXPECTED_CONTROLS[:-1]), dict(names=wrong),
                     dict(names=duplicate), dict(outcome="NotExecuted"), dict(outcome="Failed"),
                     dict(summary="Aborted"), dict(overrides={"total": 0}),
                     dict(overrides={"executed": 0}), dict(overrides={"passed": 0})]
            cases.extend(dict(overrides={counter: 1}) for counter in retain.ZERO_COUNTERS)
            for arguments in cases:
                with self.subTest(arguments=arguments):
                    path.write_text(self.controls_xml(**arguments))
                    self.assertFalse(retain.actual_controls(path)["complete"])

    def test_missing_or_malformed_control_results_are_unavailable(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "controls.trx"
            self.assertFalse(retain.actual_controls(path)["complete"])
            path.write_text("<TestRun>")
            self.assertFalse(retain.actual_controls(path)["complete"])

    def result(self, outcome="Passed", count=1, name=None, summary="Completed", failed=0, skipped=0):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "result.trx"
            result = f'<UnitTestResult testName="{name or retain.EXPECTED}" outcome="{outcome}" />'
            counters = {"total": count, "executed": count, "passed": count, **dict.fromkeys(retain.ZERO_COUNTERS, 0)}
            counters.update(failed=failed, notExecuted=skipped)
            attributes = " ".join(f'{key}="{value}"' for key, value in counters.items())
            path.write_text(f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>{result * count}</Results><ResultSummary outcome="{summary}"><Counters {attributes} /></ResultSummary></TestRun>')
            return retain.actual_result(path)["complete"]

    def test_one_actual_named_pass(self):
        self.assertTrue(self.result())

    def test_failure_skip_duplicate_wrong_name_or_aborted_rejected(self):
        for arguments in [dict(outcome="Failed"), dict(outcome="NotExecuted"), dict(count=2), dict(count=0), dict(name="another.test"), dict(summary="Aborted"), dict(failed=1), dict(skipped=1)]:
            with self.subTest(arguments=arguments):
                self.assertFalse(self.result(**arguments))

    def test_candidate_binds_only_exact_checkout_or_second_merge_parent(self):
        a, b, c = "a" * 40, "b" * 40, "c" * 40
        self.assertTrue(retain.bound_candidate(a, a, []))
        self.assertTrue(retain.bound_candidate(a, b, [c, b]))
        self.assertFalse(retain.bound_candidate(a, b, []))
        self.assertFalse(retain.bound_candidate(a, b, [b, c]))
        self.assertFalse(retain.bound_candidate(a, "", []))

    def test_missing_file_rejected(self):
        self.assertFalse(retain.actual_result(pathlib.Path("nonexistent-owned-result.trx"))["complete"])

    def test_missing_checkout_writes_incomplete_receipt_despite_other_pass_conditions(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            missing = "Legacy.Maliev.CatalogService"
            head = "a" * 40
            result = root / "supplier-address-resolve-persistence.trx"
            result.write_text(self.controls_xml(names=[retain.EXPECTED]))
            actual_revision = retain.revision

            def available_revision(path):
                if path == root:
                    return head
                return actual_revision(path) if path.name == missing else retain.PINS[path.name]

            environment = {"PROOF_RESULTS": str(root), "PROOF_EVIDENCE": str(root / "evidence"),
                           "CANDIDATE_HEAD": head, "BUILD_OUTCOME": "success", "EXECUTION_OUTCOME": "success",
                           "RESOURCE_CONTROLS_OUTCOME": "success"}
            (root / "supplier-resource-controls.trx").write_text(self.controls_xml())
            with mock.patch.dict(os.environ, environment), mock.patch("retain.revision", side_effect=available_revision), mock.patch("retain.resource_release", return_value=True), mock.patch("retain.browser_observer_release", return_value=True), mock.patch("retain.resolve_journey", return_value=True):
                with self.assertRaises(SystemExit):
                    retain.main(root)
            evidence = json.loads((root / "evidence" / "proof.json").read_text())
            self.assertFalse(evidence["complete"])
            self.assertEqual(evidence["tests"][0]["outcomes"], ["Passed"])
            self.assertEqual(evidence["unavailableProducers"], [missing])
            self.assertIsNone(evidence["producers"][missing])
            self.assertEqual(evidence["executedSource"], head)
            self.assertTrue(evidence["resourceControls"]["complete"])
            self.assertTrue(evidence["cleanup"]["observedExactBackendAbsenceAndOwnerRelease"])
            self.assertEqual({name: value for name, value in evidence["producers"].items() if name != missing},
                             {name: value for name, value in retain.PINS.items() if name != missing})

    def test_revision_rejects_missing_checkout_and_parent_repository(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            subprocess.run(["git", "init", "-q", str(root)], check=True, capture_output=True)
            subprocess.run(["git", "-C", str(root), "-c", "user.name=Synthetic fixture",
                            "-c", "user.email=fixture@example.invalid", "commit", "--allow-empty", "-qm", "Fixture"],
                           check=True, capture_output=True)
            child = root / "empty-checkout"
            child.mkdir()
            self.assertIsNotNone(retain.revision(root))
            self.assertIsNone(retain.revision(child))
            self.assertIsNone(retain.revision(root / "missing-checkout"))

    def test_observed_backend_absence_and_scope_release_are_required(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            births, absent, scopes = self.release_rows()
            def write(rows, scopes):
                (root / "resources.jsonl").write_text("\n".join(map(json.dumps, rows)))
                (root / "scope.jsonl").write_text("\n".join(map(json.dumps, scopes)))
            write(births + absent, scopes)
            self.assertTrue(retain.resource_release(root))
            for bad_rows, bad_scopes in [(births + absent[:1], scopes),
                                         (births + [dict(row, daemon="foreign") for row in absent], scopes),
                                         (births + absent + absent[:1], scopes),
                                         (births + absent, scopes[:1])]:
                write(bad_rows, bad_scopes)
                self.assertFalse(retain.resource_release(root))

    def release_rows(self):
        owner, run = "c" * 32, "d" * 32
        births = [dict(state="created", Id=value * 64, Name=f"supplier-proof-{run}-{name}", run=run,
                       owner=owner, sequence=index, daemon="daemon", expires="expiry", persistentData=False)
                  for index, (value, name) in enumerate([("a", "postgres"), ("b", "redis")], 1)]
        commands = {
            "postgres": "exec timeout -k 5 900 docker-entrypoint.sh postgres",
            "redis": "exec timeout -k 5 900 docker-entrypoint.sh redis-server --save '' --appendonly no",
        }
        for row, role in zip(births, ("postgres", "redis")):
            row["ownershipSignature"] = json.dumps({"ID": row["Id"], "Name": "/" + row["Name"],
                "Init": True, "Entrypoint": ["/bin/sh", "-c"], "Cmd": [commands[role]]})
        absent = [dict(row, state="verified-absent", sequence=index) for index, row in enumerate(births, 4)]
        clients = [dict(Name=name, Phase=phase, Released=True, startupSettled=True)
                   for name, phase in {
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
                   }.items()]
        scope = dict(owner=owner, clients=clients,
                     backends=[dict(Name=name, Released=True, startupSettled=True) for name in ("postgres-and-redis", "backend-startup")],
                     backendBindings=[dict(Name="postgres-and-redis", Run=run, ids=sorted(row["Id"] for row in births))])
        quiescent = copy.deepcopy(dict(scope, state="clients-quiescent", sequence=3))
        for backend in quiescent["backends"]:
            backend["Released"] = False
        return births, absent, [quiescent, copy.deepcopy(dict(scope, state="released", sequence=6))]

    def test_foreign_scope_backend_run_or_captured_id_cannot_supply_cleanup(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            births, absent, scopes = self.release_rows()
            for bad_scopes in [[dict(row, owner="e" * 32) for row in scopes],
                               [dict(row, backendBindings=[dict(Name="postgres-and-redis", Run="e" * 32, ids=["a" * 64, "b" * 64])]) for row in scopes],
                               [dict(row, backendBindings=[dict(Name="postgres-and-redis", Run="d" * 32, ids=["a" * 64, "e" * 64])]) for row in scopes]]:
                (root / "resources.jsonl").write_text("\n".join(map(json.dumps, births + absent)))
                (root / "scope.jsonl").write_text("\n".join(map(json.dumps, bad_scopes)))
                self.assertFalse(retain.resource_release(root))

    def test_cleanup_requires_quiescence_then_absence_then_release_event_order(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            births, absent, scopes = self.release_rows()
            cases = [(births + absent, [dict(scopes[0], sequence=7), dict(scopes[1], sequence=8)]),
                     (births + absent, [scopes[0], dict(scopes[1], sequence=4)]),
                     (births + absent, scopes[::-1]),
                     (births + [dict(absent[0], sequence=3), absent[1]], scopes),
                     (births + absent, [dict(scopes[0], clients=[dict(Released=False, startupSettled=True)]), scopes[1]])]
            for rows, scope_rows in cases:
                (root / "resources.jsonl").write_text("\n".join(map(json.dumps, rows)))
                (root / "scope.jsonl").write_text("\n".join(map(json.dumps, scope_rows)))
                self.assertFalse(retain.resource_release(root))

    def test_successful_step_outcome_does_not_replace_actual_control_trx(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            head = "a" * 40
            (root / "supplier-address-resolve-persistence.trx").write_text(f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testName="{retain.EXPECTED}" outcome="Passed" /></Results><ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" notExecuted="0" /></ResultSummary></TestRun>')
            environment = {"PROOF_RESULTS": str(root), "PROOF_EVIDENCE": str(root / "evidence"),
                           "CANDIDATE_HEAD": head, "BUILD_OUTCOME": "success", "EXECUTION_OUTCOME": "success",
                           "RESOURCE_CONTROLS_OUTCOME": "success"}
            with mock.patch.dict(os.environ, environment), mock.patch("retain.revision", side_effect=lambda path: head if path == root else retain.PINS[path.name]), mock.patch("retain.merge_parents", return_value=[]), mock.patch("retain.resource_release", return_value=True), mock.patch("retain.browser_observer_release", return_value=True), mock.patch("retain.resolve_journey", return_value=True):
                with self.assertRaises(SystemExit):
                    retain.main(root)
            evidence = json.loads((root / "evidence" / "proof.json").read_text())
            self.assertFalse(evidence["complete"])
            self.assertFalse(evidence["resourceControls"]["complete"])
            self.assertTrue(evidence["cleanup"]["observedExactBackendAbsenceAndOwnerRelease"])

    def test_missing_resource_ledger_cannot_claim_cleanup(self):
        with tempfile.TemporaryDirectory() as directory:
            self.assertFalse(retain.resource_release(pathlib.Path(directory)))

    def write_release_rows(self, directory, births, absent, scopes):
        (directory / "resources.jsonl").write_text("\n".join(map(json.dumps, births + absent)))
        (directory / "scope.jsonl").write_text("\n".join(map(json.dumps, scopes)))

    def test_backend_signature_refuses_missing_init_old_pid1_watchdog_and_foreign_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            for index in (0, 1):
                for field, value in [("Init", None), ("Init", False), ("Init", 1),
                                     ("ID", "f" * 64), ("Name", "/foreign"),
                                     ("Entrypoint", ["sh", "-c"]),
                                     ("Cmd", ["(sleep 900; kill -TERM 1; sleep 5; kill -KILL 1) & exec docker-entrypoint.sh postgres"])]:
                    with self.subTest(index=index, field=field, value=value):
                        births, absent, scopes = self.release_rows()
                        signature = json.loads(births[index]["ownershipSignature"])
                        if value is None:
                            signature.pop(field)
                        else:
                            signature[field] = value
                        births[index]["ownershipSignature"] = json.dumps(signature)
                        self.write_release_rows(root, births, absent, scopes)
                        self.assertFalse(retain.resource_release(root))
            for malformed in ("null", "[]", "{", "", None):
                births, absent, scopes = self.release_rows()
                births[0]["ownershipSignature"] = malformed
                self.write_release_rows(root, births, absent, scopes)
                self.assertFalse(retain.resource_release(root))

    def test_exact_healthy_participant_inventory_is_unique_and_order_independent(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            births, absent, scopes = self.release_rows()
            self.assertEqual(21, len(scopes[0]["clients"]))
            self.assertEqual(21, len({item["Name"] for item in scopes[0]["clients"]}))
            self.assertEqual(2, len(scopes[0]["backends"]))
            for row in scopes:
                row["clients"].reverse()
                row["backends"].reverse()
            self.write_release_rows(root, births, absent, scopes)
            self.assertTrue(retain.resource_release(root))

    def test_each_client_omission_duplicate_foreign_phase_or_unsettled_state_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            births, absent, scopes = self.release_rows()
            for client in scopes[0]["clients"]:
                name = client["Name"]
                for state_index in (0, 1):
                    omitted = copy.deepcopy(scopes)
                    omitted[state_index]["clients"] = [item for item in omitted[state_index]["clients"] if item["Name"] != name]
                    with self.subTest(client=name, omitted_state=state_index):
                        self.write_release_rows(root, births, absent, omitted)
                        self.assertFalse(retain.resource_release(root))
                for mutation in ("duplicate", "foreign", "phase", "unsettled"):
                    changed = copy.deepcopy(scopes)
                    for row in changed:
                        item = next(item for item in row["clients"] if item["Name"] == name)
                        if mutation == "duplicate": row["clients"].append(copy.deepcopy(item))
                        if mutation == "foreign": item["Name"] = "foreign-client"
                        if mutation == "phase": item["Phase"] = 999
                        if mutation == "unsettled": item["startupSettled"] = False
                    with self.subTest(client=name, mutation=mutation):
                        self.write_release_rows(root, births, absent, changed)
                        self.assertFalse(retain.resource_release(root))

    def test_backend_inventory_and_fenced_final_inventory_changes_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            births, absent, scopes = self.release_rows()
            for mutation in ("missing_backend", "duplicate_backend", "foreign_backend", "boolean_phase",
                             "missing_phase", "unreleased_client", "foreign_only", "changed_final_only"):
                changed = copy.deepcopy(scopes)
                for row in changed:
                    if mutation == "missing_backend": row["backends"].pop()
                    if mutation == "duplicate_backend": row["backends"][1] = copy.deepcopy(row["backends"][0])
                    if mutation == "foreign_backend": row["backends"][1]["Name"] = "foreign-backend"
                    if mutation == "boolean_phase": row["clients"][0]["Phase"] = False
                    if mutation == "missing_phase": row["clients"][0].pop("Phase")
                    if mutation == "unreleased_client": row["clients"][0]["Released"] = False
                    if mutation == "foreign_only": row["clients"] = [dict(Name="foreign", Phase=0, Released=True, startupSettled=True)]
                if mutation == "changed_final_only":
                    changed[1]["clients"] = [dict(Name="foreign-final", Phase=0, Released=True, startupSettled=True)]
                with self.subTest(mutation=mutation):
                    self.write_release_rows(root, births, absent, changed)
                    self.assertFalse(retain.resource_release(root))

if __name__ == "__main__":
    unittest.main()
