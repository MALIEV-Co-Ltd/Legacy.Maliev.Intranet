"""Retain only exact native component outcomes and source identity, never assertion bodies."""
import json
import os
import pathlib
import re
import subprocess
import xml.etree.ElementTree as ET

PREFIX = "Legacy.Maliev.Intranet.Tests.QuotationRequestCompanyLookupComponentTests."
EXPECTED = tuple(PREFIX + name for name in (
    "EnglishSelectionUsesExistingSaveAndPreservesUnrelatedFields",
    "ThaiSelectionUsesExistingSaveAndPreservesUnrelatedFields",
    "MissingNamesAndTaxKeepManualValuesAndDoNotSubmit",
    "UnavailableLookupKeepsManualEditorAndAccessibleFailure",
    "DeniedLookupKeepsManualEditorAndAccessibleFailure",
    "InFlightSaveIgnoresSuggestionAndRetainsOriginalRequest",
    "MissingLocalizedNameUsesAvailableNameAndKeepsManualTax",
))
ZERO = ("error", "failed", "timeout", "aborted", "inconclusive", "passedButRunAborted",
        "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending")
OUTCOMES = {"Passed", "Failed", "NotExecuted", "Aborted", "Timeout", "Error", "Inconclusive", "NotRunnable"}


def read_result(path):
    result = {"tests": [{"name": name, "outcomes": []} for name in EXPECTED], "complete": False}
    try:
        doc = ET.parse(path)
        ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
        rows = doc.findall(".//t:UnitTestResult", ns)
        for test in result["tests"]:
            test["outcomes"] = [row.get("outcome") if row.get("outcome") in OUTCOMES else "Unavailable"
                                for row in rows if row.get("testName") == test["name"]]
        counters = doc.findall(".//t:ResultSummary/t:Counters", ns)
        summary = doc.findall(".//t:ResultSummary", ns)
        expected = {"total": str(len(EXPECTED)), "executed": str(len(EXPECTED)), "passed": str(len(EXPECTED)),
                    **dict.fromkeys(ZERO, "0")}
        result["complete"] = (len(rows) == len(EXPECTED) and all(test["outcomes"] == ["Passed"] for test in result["tests"])
                              and len(counters) == 1 and counters[0].attrib == expected
                              and len(summary) == 1 and summary[0].get("outcome") == "Completed")
    except (OSError, ET.ParseError):
        pass
    return result


def main():
    actual = subprocess.run(["git", "rev-parse", "HEAD"], check=True, text=True, capture_output=True).stdout.strip()
    expected = os.environ.get("EXPECTED_SOURCE_REVISION", "")
    result = read_result(pathlib.Path(os.environ["FOCUS_RESULTS"]) / "quotation-company.trx")
    complete = (result["complete"] and re.fullmatch(r"[0-9a-f]{40}", expected) is not None and actual == expected
                and all(os.environ.get(key) == "success" for key in ("BUILD_OUTCOME", "EXECUTION_OUTCOME", "FORMAT_OUTCOME")))
    evidence = {"schema": 1, "complete": complete, "executedSource": actual, "expectedSource": expected,
                "runId": os.environ.get("GITHUB_RUN_ID"), "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
                "nativeComponentResults": result, "domainPersistenceCertified": False, "liveProviderCertified": False}
    destination = pathlib.Path(os.environ["FOCUS_EVIDENCE"])
    destination.mkdir(parents=True, exist_ok=True)
    (destination / "proof.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    if not complete:
        raise SystemExit("Quotation company component proof incomplete; bounded actual outcomes retained")


if __name__ == "__main__":
    main()
