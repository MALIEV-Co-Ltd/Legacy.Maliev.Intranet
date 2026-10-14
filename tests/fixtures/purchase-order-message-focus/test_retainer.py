import copy
import importlib.util
import json
import pathlib
import unittest
import tempfile
import uuid
ROOT=pathlib.Path(__file__).resolve().parents[3]
SPEC=importlib.util.spec_from_file_location("focus",ROOT/"scripts/verify-purchase-order-message-focus.py")
RET=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(RET)
def fixture():
    root=RET.ET.Element("TestRun")
    definitions=RET.ET.SubElement(root,"TestDefinitions")
    entries=RET.ET.SubElement(root,"TestEntries")
    results=RET.ET.SubElement(root,"Results")
    for index in range(2):
        test,execution=str(uuid.uuid4()),str(uuid.uuid4())
        name="controlled-private-case-"+str(index)
        definition=RET.ET.SubElement(definitions,"UnitTest",id=test,name=name)
        RET.ET.SubElement(definition,"Execution",id=execution)
        RET.ET.SubElement(definition,"TestMethod",className="Legacy.Maliev.Intranet.Tests.PurchaseOrderFailureMessageContractTests",name="UnconfirmedFailureGuidanceRequiresReconciliationWithoutClaimingRollback")
        RET.ET.SubElement(entries,"TestEntry",testId=test,executionId=execution)
        RET.ET.SubElement(results,"UnitTestResult",testId=test,executionId=execution,testName=name,outcome="Passed")
    summary=RET.ET.SubElement(root,"ResultSummary",outcome="Completed")
    counters={name:"0" for name in ("failed","error","timeout","aborted","inconclusive","passedButRunAborted","notRunnable","notExecuted","disconnected","warning","completed","inProgress","pending")}
    counters.update(total="2",executed="2",passed="2")
    RET.ET.SubElement(summary,"Counters",**counters)
    return root
class FocusTests(unittest.TestCase):
    def test_actual_two_identity_joins_are_hashed_and_uncertified(self):
        root=fixture();raw=RET.ET.tostring(root);proof=RET.retain_focus(raw,ROOT,"1"*40)
        self.assertEqual(proof["counters"]["passed"],2)
        self.assertEqual(proof["rawTrxSha256"],RET.digest(raw))
        self.assertEqual(proof["definitions"][0]["testId"],sorted(n.get("id") for n in RET.one_child(root,"TestDefinitions"))[0])
        self.assertNotIn("controlled-private-case",json.dumps(proof))
        self.assertFalse(proof["exactCaseIdentityPreservationVerified"])
        self.assertFalse(proof["compiledMembershipCertified"])
        self.assertFalse(proof["generatedInclusiveCertified"])
    def test_adverse_incomplete_unknown_summary_refused(self):
        for failure in ("summary","counter","warning","private-extra","error","runinfo"):
            root=fixture();summary=RET.one_child(root,"ResultSummary");counters=RET.one_child(summary,"Counters")
            if failure=="summary":summary.attrib.pop("outcome")
            if failure=="counter":counters.attrib.pop("warning")
            if failure=="warning":counters.set("warning","1")
            if failure=="private-extra":counters.set("private_person_example.invalid","0")
            if failure=="error":RET.ET.SubElement(summary,"ErrorInfo")
            if failure=="runinfo":RET.ET.SubElement(summary,"RunInfo")
            with self.subTest(failure=failure),self.assertRaises(RET.InvalidEvidence):RET.retain_focus(RET.ET.tostring(root),ROOT,"1"*40)
    def test_crosswired_missing_or_renamed_identity_refused(self):
        for failure in ("execution","entry","definition","name","method"):
            root=fixture();result=list(RET.one_child(root,"Results"))[0]
            if failure=="execution":result.set("executionId",str(uuid.uuid4()))
            if failure=="entry":RET.one_child(root,"TestEntries").remove(list(RET.one_child(root,"TestEntries"))[0])
            if failure=="definition":RET.one_child(root,"TestDefinitions").remove(list(RET.one_child(root,"TestDefinitions"))[0])
            if failure=="name":result.set("testName","private-renamed")
            if failure=="method":RET.one_child(list(RET.one_child(root,"TestDefinitions"))[0],"TestMethod").set("name","OtherCase")
            with self.subTest(failure=failure),self.assertRaises(RET.InvalidEvidence):RET.retain_focus(RET.ET.tostring(root),ROOT,"1"*40)
    def test_duplicate_case_names_and_extra_rows_refused(self):
        root=fixture();definition=list(RET.one_child(root,"TestDefinitions"))[1];definition.set("name","controlled-private-case-0");list(RET.one_child(root,"Results"))[1].set("testName","controlled-private-case-0")
        with self.assertRaises(RET.InvalidEvidence):RET.retain_focus(RET.ET.tostring(root),ROOT,"1"*40)
        root=fixture();results=RET.one_child(root,"Results");results.append(copy.deepcopy(list(results)[0]))
        with self.assertRaises(RET.InvalidEvidence):RET.retain_focus(RET.ET.tostring(root),ROOT,"1"*40)
    def test_failed_case_and_diagnostic_output_refused(self):
        for failure in ("failed","output"):
            root=fixture();result=list(RET.one_child(root,"Results"))[0]
            if failure=="failed":result.set("outcome","Failed")
            else:RET.ET.SubElement(RET.ET.SubElement(result,"Output"),"StdOut").text="private-diagnostic"
            with self.subTest(failure=failure),self.assertRaises(RET.InvalidEvidence):RET.retain_focus(RET.ET.tostring(root),ROOT,"1"*40)
    def test_changed_bound_resource_is_refused(self):
        with tempfile.TemporaryDirectory(prefix="purchase-order-focus-") as directory:
            workspace=pathlib.Path(directory)
            self.assertEqual(workspace.resolve().parent,pathlib.Path(tempfile.gettempdir()).resolve())
            for relative in RET.FOCUSED_BASELINE["sourceFiles"]:
                target=workspace/relative;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes((ROOT/relative).read_bytes())
            first=next(iter(RET.FOCUSED_BASELINE["sourceFiles"]))
            target=workspace/first;target.write_bytes(target.read_bytes()+b"changed")
            with self.assertRaises(RET.InvalidEvidence):RET.retain_focus(RET.ET.tostring(fixture()),workspace,"1"*40)
    def test_pinned_adapter_summary_is_allowed_but_other_output_is_refused(self):
        status="[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v3.1.4+50e68bbb8b (64-bit .NET 10.0.12)\n[xUnit.net 00:00:00.19]   Discovering: Legacy.Maliev.Intranet.Tests\n[xUnit.net 00:00:00.81]   Discovered:  Legacy.Maliev.Intranet.Tests\n[xUnit.net 00:00:00.84]   Starting:    Legacy.Maliev.Intranet.Tests\n[xUnit.net 00:00:01.23]   Finished:    Legacy.Maliev.Intranet.Tests\n"
        for ending in ("\n","\r\n"):
            root=fixture();summary=RET.one_child(root,"ResultSummary")
            RET.ET.SubElement(RET.ET.SubElement(summary,"Output"),"StdOut").text=status.replace("\n",ending)
            self.assertEqual(RET.retain_focus(RET.ET.tostring(root),ROOT,"1"*40)["counters"]["passed"],2)
        for failure in ("private-text","extra-line","other-assembly","other-version","test-output","stderr"):
            root=fixture();summary=RET.one_child(root,"ResultSummary")
            text=status
            if failure=="private-text":text="private-diagnostic"
            if failure=="extra-line":text+="private-diagnostic\n"
            if failure=="other-assembly":text=text.replace("Intranet.Tests","Intranet.Other")
            if failure=="other-version":text=text.replace("v3.1.4","v3.1.5")
            parent=list(RET.one_child(root,"Results"))[0] if failure=="test-output" else summary
            tag="StdErr" if failure=="stderr" else "StdOut"
            RET.ET.SubElement(RET.ET.SubElement(parent,"Output"),tag).text=text
            with self.subTest(failure=failure),self.assertRaises(RET.InvalidEvidence):RET.retain_focus(RET.ET.tostring(root),ROOT,"1"*40)
    def test_output_shape_rejects_hidden_nested_sibling_tail_and_attributes(self):
        status="[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v3.1.4+50e68bbb8b (64-bit .NET 10.0.12)\n[xUnit.net 00:00:00.19]   Discovering: Legacy.Maliev.Intranet.Tests\n[xUnit.net 00:00:00.81]   Discovered:  Legacy.Maliev.Intranet.Tests\n[xUnit.net 00:00:00.84]   Starting:    Legacy.Maliev.Intranet.Tests\n[xUnit.net 00:00:01.23]   Finished:    Legacy.Maliev.Intranet.Tests\n"
        for failure in ("nested","sibling","tail","stdout-attribute","output-attribute","output-text","output-tail","empty-row-nested","duplicate"):
            root=fixture();summary=RET.one_child(root,"ResultSummary");output=RET.ET.SubElement(summary,"Output");stdout=RET.ET.SubElement(output,"StdOut");stdout.text=status
            if failure=="nested":RET.ET.SubElement(stdout,"Private").text="private-diagnostic"
            if failure=="sibling":RET.ET.SubElement(output,"Private").text="private-diagnostic"
            if failure=="tail":stdout.tail="private-diagnostic"
            if failure=="stdout-attribute":stdout.set("Private","private-diagnostic")
            if failure=="output-attribute":output.set("Private","private-diagnostic")
            if failure=="output-text":output.text="private-diagnostic"
            if failure=="output-tail":output.tail="private-diagnostic"
            if failure=="empty-row-nested":RET.ET.SubElement(RET.ET.SubElement(RET.ET.SubElement(list(RET.one_child(root,"Results"))[0],"Output"),"StdOut"),"Private").text="private-diagnostic"
            if failure=="duplicate":RET.ET.SubElement(output,"StdOut").text=status
            with self.subTest(failure=failure),self.assertRaises(RET.InvalidEvidence):RET.retain_focus(RET.ET.tostring(root),ROOT,"1"*40)
    def test_nested_failed_summary_and_extra_result_are_refused(self):
        root=fixture()
        wrapper=RET.ET.SubElement(root,"ForeignEvidence")
        summary=RET.ET.SubElement(wrapper,"ResultSummary",outcome="Failed")
        counters=copy.deepcopy(RET.one_child(RET.one_child(root,"ResultSummary"),"Counters"))
        counters.set("total","1");counters.set("executed","1");counters.set("passed","0");counters.set("failed","1")
        summary.append(counters)
        extra=copy.deepcopy(list(RET.one_child(root,"Results"))[0])
        extra.set("outcome","Failed")
        RET.ET.SubElement(wrapper,"Results").append(extra)
        with self.assertRaises(RET.InvalidEvidence):
            RET.retain_focus(RET.ET.tostring(root),ROOT,"1"*40)
    def test_foreign_nested_identity_sections_and_rows_are_refused(self):
        for tag in ("TestRun","ResultSummary","Counters","TestDefinitions","TestEntries","Results",
                    "UnitTest","TestEntry","UnitTestResult","Execution","TestMethod"):
            root=fixture()
            original=next(node for node in root.iter() if RET.local_tag(node)==tag)
            extra=copy.deepcopy(original)
            RET.ET.SubElement(root,"ForeignEvidence").append(extra)
            with self.subTest(tag=tag),self.assertRaises(RET.InvalidEvidence):
                RET.retain_focus(RET.ET.tostring(root),ROOT,"1"*40)
    def test_dtd_refused(self):
        with self.assertRaises(RET.InvalidEvidence):RET.retain_focus(b'<!DOCTYPE TestRun>'+RET.ET.tostring(fixture()),ROOT,"1"*40)
if __name__=="__main__":unittest.main()
