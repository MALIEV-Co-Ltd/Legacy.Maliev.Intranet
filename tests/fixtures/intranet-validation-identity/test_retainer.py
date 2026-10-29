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


def compiler_fixture(root):
    for project in ("Legacy.Maliev.Intranet.Bff", "Legacy.Maliev.Intranet.Server",
                    "Legacy.Maliev.Intranet.Contracts", "Legacy.Maliev.Intranet.Tests"):
        evidence = root / project / "obj/Release/net10.0/intranet-validation"
        evidence.mkdir(parents=True)
        source = root / project / "source.cs"
        source.write_bytes(b"controlled compiler input")
        outputs = []
        for suffix in ("dll", "pdb"):
            output = root / project / (project + "." + suffix)
            output.write_bytes(b"controlled compiler output")
            outputs.append(str(output) + "|" + RET.digest(output.read_bytes()))
        (evidence / "compiler-inputs.sha256").write_text(str(source) + "|" + RET.digest(source.read_bytes()))
        (evidence / "compiler-outputs.sha256").write_text("\n".join(outputs))
        (evidence / "emitted-sources.sha256").write_text("")
        (evidence / "symbol-policy.txt").write_text("portable|true|true\n")


class IdentityRetentionTests(unittest.TestCase):
    def test_compiler_path_diagnostics_classify_refusals_without_paths_or_content(self):
        with tempfile.TemporaryDirectory(prefix="intranet-path-rejection-") as directory:
            root = pathlib.Path(directory)
            compiler_fixture(root)
            evidence = root / "Legacy.Maliev.Intranet.Bff/obj/Release/net10.0/intranet-validation"
            manifest = evidence / "compiler-inputs.sha256"
            candidates = {
                "non-absolute": pathlib.Path("private-canary-relative/source.cs"),
                "leaf-symlink": root / "private-canary-link.cs",
                "resolved-outside-workspace": root.parent / "private-canary-outside.cs",
            }
            for reason, path in candidates.items():
                manifest.write_text(str(path) + "|" + "a" * 64)
                with self.subTest(reason=reason), mock.patch.object(
                        RET.pathlib.Path, "is_symlink", autospec=True,
                        side_effect=lambda candidate: reason == "leaf-symlink" and candidate == path):
                    with self.assertRaises(RET.InvalidEvidence):
                        RET.retain_compiler_custody(root)
                    diagnostic = RET.compiler_path_diagnostics(root)
                    row = self.assert_single_anomaly(diagnostic, reason)
                    self.assertEqual("Legacy.Maliev.Intranet.Bff", row["project"])
                    self.assertEqual("compiler-inputs", row["manifestKind"])
                    self.assertEqual(1, row["rowIndex"])
                    self.assertEqual(RET.digest(str(path).encode()), row["pathSha256"])
                    self.assertEqual(RET.digest(manifest.read_bytes()), row["manifestSha256"])
                    self.assertNotIn("private-canary", json.dumps(diagnostic))
                    self.assertNotIn("controlled compiler input", json.dumps(diagnostic))

    def assert_single_anomaly(self, diagnostic, reason):
        self.assertEqual({reason: 1}, diagnostic["reasonCounts"])
        self.assertEqual(0, diagnostic["unavailableManifests"])
        self.assertFalse(diagnostic["truncated"])
        self.assertEqual(1, len(diagnostic["anomalies"]))
        return diagnostic["anomalies"][0]

    def test_compiler_path_diagnostics_valid_input_cap_and_fixed_sdk_classification(self):
        with tempfile.TemporaryDirectory(prefix="intranet-path-bounds-") as directory:
            root = pathlib.Path(directory)
            compiler_fixture(root)
            self.assertEqual({"reasonCounts": {}, "anomalies": [], "unavailableManifests": 0,
                              "truncated": False}, RET.compiler_path_diagnostics(root))
            external = root.parent / "microsoft.net.test.sdk/17.14.1/build/net8.0/Microsoft.NET.Test.Sdk.Program.cs"
            manifest = root / "Legacy.Maliev.Intranet.Tests/obj/Release/net10.0/intranet-validation/compiler-inputs.sha256"
            manifest.write_text("\n".join([str(external) + "|" + "a" * 64] * 20))
            diagnostic = RET.compiler_path_diagnostics(root)
            self.assertEqual({"resolved-outside-workspace": 20}, diagnostic["reasonCounts"])
            self.assertEqual(16, len(diagnostic["anomalies"]))
            self.assertTrue(diagnostic["truncated"])
            for row in diagnostic["anomalies"]:
                self.assertEqual("test-sdk-entrypoint", row["inputKind"])
                self.assertEqual(set(row), {"project", "manifestKind", "rowIndex", "reason", "pathSha256",
                                            "manifestSha256", "isAbsolute", "leafIsSymlink",
                                            "resolvedInsideWorkspace", "inputKind"})
                self.assertTrue(row["isAbsolute"])
                self.assertFalse(row["leafIsSymlink"])
                self.assertFalse(row["resolvedInsideWorkspace"])
                for key in ("pathSha256", "manifestSha256"):
                    self.assertRegex(row[key], r"^[0-9a-f]{64}$")
            self.assertNotIn("Microsoft.NET.Test.Sdk.Program.cs", json.dumps(diagnostic))

    def test_compiler_path_diagnostics_bound_invalid_manifests_and_attach_only_to_rejection(self):
        raw, baseline, _, _ = fixture()
        with tempfile.TemporaryDirectory(prefix="intranet-path-export-") as directory:
            root = pathlib.Path(directory)
            compiler_fixture(root)
            manifest = root / "Legacy.Maliev.Intranet.Bff/obj/Release/net10.0/intranet-validation/compiler-inputs.sha256"
            for value in ("private-malformed-value", "x" * (1024 * 1024 + 1)):
                manifest.write_text(value)
                diagnostic = RET.compiler_path_diagnostics(root)
                self.assertEqual(1, diagnostic["unavailableManifests"])
                self.assertEqual([], diagnostic["anomalies"])
                self.assertNotIn("private-malformed-value", json.dumps(diagnostic))
            manifest.write_text("private-refused-path|" + "a" * 64)
            trx, frozen, output = root / "original.trx", root / "baseline.json", root / "proof/identity-sidecar.json"
            trx.write_bytes(raw)
            frozen.write_text(json.dumps(baseline))
            args = types.SimpleNamespace(trx=trx, baseline=frozen, workspace=root,
                                         source_revision="1" * 40, output=output)
            with mock.patch.object(RET.argparse.ArgumentParser, "parse_args", return_value=args):
                with self.assertRaises(RET.InvalidEvidence):
                    RET.main()
            record = json.loads(output.with_name("identity-rejection.json").read_bytes())
            self.assertFalse(record["accepted"])
            self.assertEqual("compiler-custody", record["stage"])
            self.assert_single_anomaly(record["compilerPathDiagnostics"], "non-absolute")
            self.assertFalse(output.exists())
            self.assertNotIn("private-refused-path", json.dumps(record))

    def test_capture_rebinds_real_test_sdk_compile_input_before_hashing(self):
        document = RET.ET.parse(ROOT / "Directory.Build.targets").getroot()
        target = document.find("./Target[@Name='OwnIntranetTestSdkProgramForCompilerCapture']")
        self.assertIsNotNone(target)
        self.assertEqual("CaptureIntranetCompilerInputs", target.get("BeforeTargets"))
        self.assertEqual("'$(MalievCaptureValidationEvidence)' == 'true' And "
                         "'$(MSBuildProjectName)' == 'Legacy.Maliev.Intranet.Tests' And "
                         "'$(GenerateProgramFile)' == 'true'", target.get("Condition"))
        owned = "$(IntermediateOutputPath)intranet-validation/test-sdk-program/Microsoft.NET.Test.Sdk.Program.cs"
        self.assertEqual(owned, target.findtext("./PropertyGroup/IntranetOwnedTestSdkProgram"))
        copy_task = target.find("Copy")
        self.assertEqual("$(GeneratedProgramFile)", copy_task.get("SourceFiles"))
        self.assertEqual("$(IntranetOwnedTestSdkProgram)", copy_task.get("DestinationFiles"))
        compile_items = target.findall("./ItemGroup/Compile")
        self.assertEqual([{"Remove": "$(GeneratedProgramFile)"},
                          {"Include": "$(IntranetOwnedTestSdkProgram)"}], [item.attrib for item in compile_items])
        tasks = list(target)
        self.assertLess(tasks.index(copy_task), tasks.index(target.find("ItemGroup")))
        self.assertEqual(2, len(target.findall("GetFileHash")))
        self.assertEqual(["$(GeneratedProgramFile)", "$(IntranetOwnedTestSdkProgram)"],
                         [task.get("Files") for task in target.findall("GetFileHash")])
        self.assertEqual("'@(IntranetTestSdkSourceHash->'%(FileHash)')' != "
                         "'@(IntranetTestSdkOwnedHash->'%(FileHash)')'", target.find("Error").get("Condition"))
        capture = document.find("./Target[@Name='CaptureIntranetCompilerInputs']")
        self.assertEqual("CoreCompile", capture.get("BeforeTargets"))
        self.assertEqual("@(Compile)", capture.find("GetFileHash").get("Files"))

    def test_entry_join_diagnostics_classify_missing_duplicate_and_mismatch(self):
        raw, baseline, test_id, execution_id = fixture()
        for reason in ("undeclared-test-id", "duplicate-test-id", "execution-mismatch", "missing-entry"):
            root = RET.ET.fromstring(raw)
            entries = RET.one_child(root, "TestEntries")
            if reason == "undeclared-test-id":
                entries[0].set("testId", str(uuid.uuid4()))
            elif reason == "duplicate-test-id":
                entries.append(copy.deepcopy(entries[0]))
            elif reason == "execution-mismatch":
                entries[0].set("executionId", str(uuid.uuid4()))
            else:
                entries.remove(entries[0])
            changed = RET.ET.tostring(root)
            with self.subTest(reason=reason):
                with self.assertRaises(RET.InvalidEvidence):
                    RET.retain(changed, baseline, "1" * 40)
                diagnostic = RET.entry_join_diagnostics(changed)
                self.assertEqual(1, diagnostic["reasonCounts"][reason])
                self.assertTrue(any(row["reason"] == reason for row in diagnostic["anomalies"]))
                self.assertNotIn("controlled-secret-display-value", json.dumps(diagnostic))
                self.assertNotIn("private-output-value", json.dumps(diagnostic))
                with tempfile.TemporaryDirectory(prefix="intranet-entry-rejection-") as directory:
                    temporary = pathlib.Path(directory)
                    trx = temporary / "original.trx"
                    trx.write_bytes(changed)
                    frozen = temporary / "baseline.json"
                    frozen.write_text(json.dumps(baseline))
                    output = temporary / "proof/identity-sidecar.json"
                    args = types.SimpleNamespace(trx=trx, baseline=frozen, workspace=temporary,
                                                 source_revision="1" * 40, output=output)
                    with mock.patch.object(RET.argparse.ArgumentParser, "parse_args", return_value=args):
                        with self.assertRaises(RET.InvalidEvidence):
                            RET.main()
                    rejection = json.loads(output.with_name("identity-rejection.json").read_bytes())
                    self.assertFalse(rejection["accepted"])
                    self.assertEqual("original-trx-identity", rejection["stage"])
                    self.assertEqual(RET.digest(changed), rejection["rawTrxSha256"])
                    self.assertEqual(diagnostic, rejection["entryJoinDiagnostics"])
                    self.assertFalse(output.exists())

    def test_entry_join_diagnostics_valid_input_and_hashes(self):
        raw, _, test_id, execution_id = fixture()
        self.assertEqual(RET.entry_join_diagnostics(raw), {
            "definitionCount": 1, "entryCount": 1, "reasonCounts": {}, "anomalies": [], "truncated": False,
        })
        changed = raw.replace(b'<TestEntry testId=', b'<TestEntry extra="private-entry-value" testId=')
        root = RET.ET.fromstring(changed)
        RET.one_child(root, "TestEntries")[0].set("executionId", str(uuid.uuid4()))
        diagnostic = RET.entry_join_diagnostics(RET.ET.tostring(root))
        row = diagnostic["anomalies"][0]
        self.assertEqual(test_id, row["testId"])
        self.assertEqual(execution_id, row["declaredExecutionId"])
        self.assertEqual(RET.method_hash("Tests.Control", "BoundedCase"), row["methodSha256"])
        self.assertEqual(RET.text_hash("controlled-secret-display-value"), row["canonicalNameSha256"])
        self.assertNotIn("private-entry-value", json.dumps(diagnostic))

    def test_entry_join_diagnostics_cap_and_invalid_identity_privacy(self):
        raw, _, _, _ = fixture()
        root = RET.ET.fromstring(raw)
        entries = RET.one_child(root, "TestEntries")
        for _ in range(20):
            entries.append(copy.deepcopy(entries[0]))
        diagnostic = RET.entry_join_diagnostics(RET.ET.tostring(root))
        self.assertEqual({"duplicate-test-id": 20}, diagnostic["reasonCounts"])
        self.assertEqual(16, len(diagnostic["anomalies"]))
        self.assertTrue(diagnostic["truncated"])
        entries[0].set("testId", "private-invalid-id")
        diagnostic = RET.entry_join_diagnostics(RET.ET.tostring(root))
        self.assertEqual(1, diagnostic["reasonCounts"]["invalid-entry-identity"])
        self.assertNotIn("private-invalid-id", json.dumps(diagnostic))
        for row in diagnostic["anomalies"]:
            self.assertLessEqual(set(row), {"reason", "testId", "executionId", "declaredExecutionId",
                                           "methodSha256", "canonicalNameSha256"})
            for key, value in row.items():
                if key.endswith("Id"):
                    self.assertEqual(str(uuid.UUID(value)), value)
                elif key.endswith("Sha256"):
                    self.assertRegex(value, r"^[0-9a-f]{64}$")

    def test_entry_join_diagnostics_refuse_unbounded_or_entity_input(self):
        for raw in (b"", b"not XML private-value", b'<!DOCTYPE TestRun><TestRun/>', b'x' * (64 * 1024 * 1024 + 1)):
            with self.subTest(size=len(raw)):
                self.assertEqual({"unavailable": "invalid-or-unbounded-input"}, RET.entry_join_diagnostics(raw))

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
                expected_keys = {"schemaVersion", "accepted", "stage", "validatorLines", "rawTrxSha256"}
                if stage == "original-trx-identity":
                    expected_keys.add("entryJoinDiagnostics")
                    self.assertEqual([], record["entryJoinDiagnostics"]["anomalies"])
                if stage == "compiler-custody":
                    expected_keys.add("compilerPathDiagnostics")
                    self.assertEqual([], record["compilerPathDiagnostics"]["anomalies"])
                self.assertEqual(set(record), expected_keys)
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
