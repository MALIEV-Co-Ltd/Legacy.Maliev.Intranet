import copy
import importlib.util
import json
import pathlib
import tempfile
import types
import unittest
from unittest import mock
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location("identity_retainer", ROOT / "scripts/retain-intranet-test-identities.py")
RET = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RET)


def fixture():
    test_id, execution_id = str(uuid.uuid4()), str(uuid.uuid4())
    name = "controlled-secret-display-value"
    raw = f'''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
    <Results><UnitTestResult testId="{test_id}" executionId="{execution_id}" testName="{name}" outcome="Passed">
    <Output><StdOut>private-output-value</StdOut></Output></UnitTestResult></Results>
    <TestDefinitions><UnitTest id="{test_id}" name="{name}"><Execution id="{execution_id}"/>
    <TestMethod className="Tests.Control" name="BoundedCase"/></UnitTest></TestDefinitions>
    <TestEntries><TestEntry testId="{test_id}" executionId="{execution_id}"/></TestEntries>
    <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0"/></ResultSummary>
    </TestRun>'''.encode()
    baseline = {"schemaVersion": 1, "sourceFiles": {}, "focusedMethods": {RET.method_hash("Tests.Control", "BoundedCase"): 1}, "focusedCases": 1}
    return raw, baseline, test_id, execution_id


class IdentityRetentionTests(unittest.TestCase):
    def test_frozen_baseline_binds_only_accepted_current_sources_and_three_facts(self):
        baseline = json.loads((ROOT / "tests/fixtures/intranet-validation-identity/frozen-purchase-order-roster.json").read_bytes())
        self.assertEqual(baseline["sourceFiles"], {
            "Legacy.Maliev.Intranet.Server/PurchaseOrders/PurchaseOrderCreationService.cs": [
                "f8c89926e71c95383410bb6c078c7760960b94556f72da05ef899bf4665d02cc",
                "eaecc61d911e32b587107d8144c0ac6aec6c9153e2dd4fd160c690b5bb6bb9b9",
            ],
            "Legacy.Maliev.Intranet.Tests/PurchaseOrderCreationServiceTests.cs": [
                "916ccc7a299e8545dfca0ab251caea46897f6a4cb886bbdec0b3921c0a2b57ba",
                "9422aa843b110b78f23b6ab5742eb528434c51324ef01df645160188d167f63c",
            ],
        })
        methods = (
            "CreateAsync_CompletesOrderItemsQuestPdfUploadAndMetadataLink",
            "CreateAsync_FileLinkFailure_PreservesResourcesWithoutProvenInvocationOwnership",
            "CreateAsync_UnknownWriteOutcome_DoesNotAttemptDestructiveCompensation",
        )
        cls = "Legacy.Maliev.Intranet.Tests.PurchaseOrderCreationServiceTests"
        self.assertEqual({RET.method_hash(cls, method): 1 for method in methods}, baseline["focusedMethods"])
        self.assertEqual(3, baseline["focusedCases"])
        RET.verify_sources(ROOT, baseline)

    def test_frozen_baseline_refuses_each_superseded_source_hash(self):
        baseline = json.loads((ROOT / "tests/fixtures/intranet-validation-identity/frozen-purchase-order-roster.json").read_bytes())
        RET.verify_sources(ROOT, baseline)
        historical = {
            "Legacy.Maliev.Intranet.Server/PurchaseOrders/PurchaseOrderCreationService.cs": [
                "77704f410dda96df132f41177e4373258fa574748e8b60f9fc9802bffc8d2fe9",
                "87f74934b41894c3804236a0a375a26c222af96885034a0c197708213a2979f3",
            ],
            "Legacy.Maliev.Intranet.Tests/PurchaseOrderCreationServiceTests.cs": [
                "150acc8aeec0c7050c649663782a4e699c981e91c73cccca04ff18dfbd61792f",
                "e87e822b48a2387ea114152a9530e7ab08b170faafbdf3dbbb878022a2b1eeac",
            ],
        }
        for path, hashes in historical.items():
            for old_hash in hashes:
                stale = copy.deepcopy(baseline)
                stale["sourceFiles"][path] = [old_hash]
                with self.subTest(path=path, old_hash=old_hash), self.assertRaises(RET.InvalidEvidence):
                    RET.verify_sources(ROOT, stale)

    def test_failed_export_retains_only_stage_lines_and_input_hash(self):
        raw, baseline, _, _ = fixture()
        with tempfile.TemporaryDirectory(prefix="intranet-rejection-") as directory:
            root = pathlib.Path(directory)
            trx = root / "original.trx"
            frozen = root / "baseline.json"
            frozen.write_text(json.dumps(baseline))
            output = root / "proof/identity-sidecar.json"
            args = types.SimpleNamespace(trx=trx, baseline=frozen, workspace=root,
                                         source_revision="1" * 40, output=output)
            for value, stage in ((raw.replace(b'outcome="Completed"', b'outcome="Failed"'), "original-trx-identity"),
                                 (raw, "compiler-custody")):
                trx.write_bytes(value)
                with mock.patch.object(RET.argparse.ArgumentParser, "parse_args", return_value=args):
                    with self.assertRaises((RET.InvalidEvidence, OSError)):
                        RET.main()
                rejection = output.with_name("identity-rejection.json")
                record = json.loads(rejection.read_text())
                self.assertEqual(set(record), {"schemaVersion", "accepted", "stage", "validatorLines", "rawTrxSha256"})
                self.assertFalse(record["accepted"])
                self.assertEqual(record["stage"], stage)
                self.assertEqual(record["rawTrxSha256"], RET.digest(value))
                self.assertTrue(record["validatorLines"])
                self.assertTrue(all(type(line) is int and line > 0 for line in record["validatorLines"]))
                self.assertFalse(output.exists())
                self.assertNotIn("controlled-secret-display-value", rejection.read_text())
                self.assertNotIn("private-output-value", rejection.read_text())

    def test_preserves_actual_guids_and_hashes_names_without_output(self):
        raw, baseline, test_id, execution_id = fixture()
        evidence = RET.retain(raw, baseline, "1" * 40)
        serialized = json.dumps(evidence)
        self.assertNotIn("controlled-secret-display-value", serialized)
        self.assertNotIn("private-output-value", serialized)
        self.assertEqual(evidence["definitions"][0]["testId"], test_id)
        self.assertEqual(evidence["entries"][0]["executionId"], execution_id)
        self.assertEqual(evidence["results"][0]["executionId"], execution_id)
        self.assertEqual(evidence["rawTrxSha256"], RET.digest(raw))
        self.assertFalse(evidence["compiledMembershipCertified"])
        self.assertFalse(evidence["generatedInclusiveCertified"])

    def test_rejects_incomplete_or_adverse_summary_and_diagnostics(self):
        raw, baseline, _, _ = fixture()
        root = RET.ET.fromstring(raw)
        for failure in ("missing-outcome", "failed-summary", "missing-counter", "warning", "row-error", "summary-error", "run-info"):
            changed = copy.deepcopy(root)
            summary = RET.one_child(changed, "ResultSummary")
            counters = RET.one_child(summary, "Counters")
            if failure == "missing-outcome": summary.attrib.pop("outcome")
            if failure == "failed-summary": summary.set("outcome", "Failed")
            if failure == "missing-counter": counters.attrib.pop("warning")
            if failure == "warning": counters.set("warning", "1")
            if failure == "row-error": RET.ET.SubElement(list(RET.one_child(changed, "Results"))[0], "ErrorInfo")
            if failure == "summary-error": RET.ET.SubElement(summary, "ErrorInfo")
            if failure == "run-info": RET.ET.SubElement(RET.ET.SubElement(summary, "RunInfos"), "RunInfo")
            with self.subTest(failure=failure), self.assertRaises(RET.InvalidEvidence):
                RET.retain(RET.ET.tostring(changed), baseline, "1" * 40)

    def test_rejects_unknown_private_counter_name_without_export(self):
        raw, baseline, _, _ = fixture()
        root = RET.ET.fromstring(raw)
        private_name = "private_person_example.invalid"
        RET.one_child(RET.one_child(root, "ResultSummary"), "Counters").set(private_name, "0")
        with self.assertRaises(RET.InvalidEvidence) as caught:
            RET.retain(RET.ET.tostring(root), baseline, "1" * 40)
        self.assertNotIn(private_name, str(caught.exception))

    def test_case_identity_is_explicitly_unverified_even_for_consistent_rename(self):
        raw, baseline, _, _ = fixture()
        renamed = raw.replace(b"controlled-secret-display-value", b"consistently-renamed-case")
        evidence = RET.retain(renamed, baseline, "1" * 40)
        self.assertTrue(evidence["passingRunSummaryVerified"])
        self.assertFalse(evidence["exactCaseIdentityPreservationVerified"])
        self.assertNotEqual(RET.retain(raw, baseline, "1" * 40)["definitions"][0]["canonicalNameSha256"],
                            evidence["definitions"][0]["canonicalNameSha256"])
        root = RET.ET.fromstring(renamed)
        root[:] = list(reversed(list(root)))
        self.assertFalse(RET.retain(RET.ET.tostring(root), baseline, "1" * 40)["exactCaseIdentityPreservationVerified"])

    def test_rejects_missing_definition_and_entry_roster(self):
        raw, baseline, _, _ = fixture()
        root = RET.ET.fromstring(raw)
        for name in ("TestDefinitions", "TestEntries"):
            changed = copy.deepcopy(root)
            RET.one_child(changed, name).clear()
            with self.subTest(name=name), self.assertRaises(RET.InvalidEvidence):
                RET.retain(RET.ET.tostring(changed), baseline, "1" * 40)

    def test_rejects_crosswired_duplicate_or_renamed_result(self):
        raw, baseline, _, _ = fixture()
        root = RET.ET.fromstring(raw)
        for failure in ("execution", "test", "duplicate", "name", "failed"):
            changed = copy.deepcopy(root)
            results = RET.one_child(changed, "Results")
            result = list(results)[0]
            if failure == "execution": result.set("executionId", str(uuid.uuid4()))
            if failure == "test": result.set("testId", str(uuid.uuid4()))
            if failure == "duplicate": results.append(copy.deepcopy(result))
            if failure == "name": result.set("testName", "changed-private-name")
            if failure == "failed": result.set("outcome", "Failed")
            with self.subTest(failure=failure), self.assertRaises(RET.InvalidEvidence):
                RET.retain(RET.ET.tostring(changed), baseline, "1" * 40)

    def test_rejects_counter_and_frozen_method_drift(self):
        raw, baseline, _, _ = fixture()
        changed = copy.deepcopy(baseline)
        changed["focusedMethods"] = {"2" * 64: 1}
        with self.assertRaises(RET.InvalidEvidence): RET.retain(raw, changed, "1" * 40)
        with self.assertRaises(RET.InvalidEvidence): RET.retain(raw.replace(b'total="1"', b'total="2"'), baseline, "1" * 40)

    def test_rejects_dtd_and_entities(self):
        raw, baseline, _, _ = fixture()
        with self.assertRaises(RET.InvalidEvidence):
            RET.retain(b'<!DOCTYPE TestRun [<!ENTITY bad "value">]>' + raw, baseline, "1" * 40)

    def test_source_binding_rejects_changed_behavior(self):
        with tempfile.TemporaryDirectory(prefix="intranet-identity-") as directory:
            root = pathlib.Path(directory)
            self.assertEqual(root.resolve().parent, pathlib.Path(tempfile.gettempdir()).resolve())
            file = root / "case.cs"
            file.write_bytes(b"controlled source\n")
            baseline = {"sourceFiles": {"case.cs": [RET.digest(file.read_bytes())]}}
            RET.verify_sources(root, baseline)
            file.write_bytes(b"changed source\n")
            with self.assertRaises(RET.InvalidEvidence): RET.verify_sources(root, baseline)

    def test_compiler_custody_rejects_stale_binaries_and_keeps_certifications_false(self):
        with tempfile.TemporaryDirectory(prefix="intranet-compiler-") as directory:
            root = pathlib.Path(directory)
            self.assertEqual(root.resolve().parent, pathlib.Path(tempfile.gettempdir()).resolve())
            for project in ("Legacy.Maliev.Intranet.Bff", "Legacy.Maliev.Intranet.Server",
                            "Legacy.Maliev.Intranet.Contracts", "Legacy.Maliev.Intranet.Tests"):
                evidence = root / project / "obj/Release/net10.0/intranet-validation"
                evidence.mkdir(parents=True)
                source = root / project / "source.cs"
                source.write_bytes(b"controlled compiler input")
                binaries = []
                for suffix in ("dll", "pdb"):
                    file = root / project / (project + "." + suffix)
                    file.write_bytes(b"controlled output")
                    binaries.append(str(file) + "|" + RET.digest(file.read_bytes()))
                (evidence / "compiler-inputs.sha256").write_text(str(source) + "|" + RET.digest(source.read_bytes()))
                (evidence / "compiler-outputs.sha256").write_text("\n".join(binaries))
                (evidence / "emitted-sources.sha256").write_text("")
                (evidence / "symbol-policy.txt").write_text("portable|true|true\n")
            captured = RET.retain_compiler_custody(root)
            self.assertEqual(len(captured), 4)
            self.assertTrue(all(not item["compiledMembershipCertified"] for item in captured.values()))
            self.assertTrue(all(not item["generatedInclusiveCertified"] for item in captured.values()))
            contracts = "Legacy.Maliev.Intranet.Contracts"
            contract_evidence = root / contracts / "obj/Release/net10.0/intranet-validation"
            original_outputs = (contract_evidence / "compiler-outputs.sha256").read_text()
            dll_only = original_outputs.splitlines()[0]
            (contract_evidence / "compiler-outputs.sha256").write_text(dll_only)
            with self.assertRaises(RET.InvalidEvidence): RET.retain_compiler_custody(root)
            (contract_evidence / "symbol-policy.txt").write_text("None|false|\n")
            self.assertEqual(RET.retain_compiler_custody(root)[contracts]["symbolPolicy"]["debugType"], "none")
            for invalid in ("None|false|true", "portable|false|", "None|true|", "unknown|true|", "None|false|private"):
                (contract_evidence / "symbol-policy.txt").write_text(invalid)
                with self.assertRaises(RET.InvalidEvidence): RET.retain_compiler_custody(root)
            (contract_evidence / "symbol-policy.txt").write_text("None|false|\n")
            (contract_evidence / "compiler-outputs.sha256").write_text(original_outputs)
            with self.assertRaises(RET.InvalidEvidence): RET.retain_compiler_custody(root)
            (contract_evidence / "compiler-outputs.sha256").write_text(dll_only)
            server_policy = root / "Legacy.Maliev.Intranet.Server/obj/Release/net10.0/intranet-validation/symbol-policy.txt"
            server_policy.write_text("None|false|\n")
            with self.assertRaises(RET.InvalidEvidence): RET.retain_compiler_custody(root)
            server_policy.write_text("portable|true|true\n")
            (root / "Legacy.Maliev.Intranet.Server/Legacy.Maliev.Intranet.Server.dll").write_bytes(b"stale mismatch")
            with self.assertRaises(RET.InvalidEvidence): RET.retain_compiler_custody(root)


if __name__ == "__main__":
    unittest.main()
