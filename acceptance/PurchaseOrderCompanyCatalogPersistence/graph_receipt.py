"""Fail-closed same-run graph admission and final-retainer conjunction."""
from collections import Counter
import json
import os
import pathlib
import uuid
import xml.etree.ElementTree as ET

from graph_evidence import audit, cleanup, coverage, digest, identity, trx

HERE = pathlib.Path(__file__).resolve().parent


def native(value, expected):
    try:
        if set(value) != {'rawSha256', 'runId', 'count', 'rows'} or not identity(value['rawSha256']):
            return False
        uuid.UUID(value['runId'])
        rows = value['rows']
        inventory = Counter((row['testName'], row['className'], row['method']) for row in rows)
        baseline = Counter((row['testName'], row['className'], row['method']) for row in expected['inventory'])
        return (inventory == baseline and value['count'] == len(rows) == expected['count'] and
                all(set(row) == {'testName', 'className', 'method', 'executionId', 'testId'} for row in rows) and
                len({str(uuid.UUID(row['executionId'])) for row in rows}) == len(rows) and
                len({str(uuid.UUID(row['testId'])) for row in rows}) == len(rows))
    except (KeyError, ValueError, TypeError):
        return False


def compiled(value, producer):
    expected = {'Legacy.Maliev.' + producer + 'Service.' + module + '.' + extension
                for module in ('Api', 'Application', 'Domain', 'Rendering' if producer == 'Document' else 'Data')
                for extension in ('dll', 'pdb')}
    return (len(value) == len(expected) and {item['name'] for item in value} == expected and
            all(set(item) == {'name', 'sha256', 'bytes'} and identity(item['sha256']) and
                type(item['bytes']) is int and item['bytes'] > 0 for item in value))


def observed_coverage(value, producer):
    if set(value) != {'rawSha256', 'exclusions', 'assemblies'} or not identity(value['rawSha256']) or value['exclusions'] != []:
        return False
    names = {'Legacy.Maliev.' + producer + 'Service.' + module for module in
             ('Api', 'Application', 'Domain', 'Rendering' if producer == 'Document' else 'Data')}
    if len(value['assemblies']) != 4 or {item['assembly'] for item in value['assemblies']} != names:
        return False
    for item in value['assemblies']:
        if set(item) != {'assembly', 'valid', 'covered', 'percent', 'applicability'} or type(item['valid']) is not int or type(item['covered']) is not int:
            return False
        valid, covered = item['valid'], item['covered']
        if producer == 'Document' and item['assembly'].endswith('.Application'):
            if valid != 0 or covered != 0 or item['percent'] is not None or item['applicability'] != 'contract-only':
                return False
        elif valid <= 0 or covered < 0 or covered > valid or covered * 100 < valid * 80 or item['percent'] != covered * 100 / valid or item['applicability'] != 'executable':
            return False
    return True


def complete(report, owner, inventories, pins):
    try:
        fields = {'schema', 'graphComplete', 'acceptedProducerGraph', 'candidateHead', 'executedSource', 'runId',
                  'runAttempt', 'pins', 'sourceTrees', 'producers', 'sharedBinaries', 'failureCategory', 'failedProducer', 'failureCleanup', 'scope'}
        if set(report) != fields or type(report['schema']) is not int or report['schema'] != 1 or report['graphComplete'] is not True or report['acceptedProducerGraph'] is not False or report['failureCategory'] is not None or report['failedProducer'] is not None or report['failureCleanup'] is not None:
            return False
        if any(report[key] != value for key, value in owner.items()) or report['pins'] != pins:
            return False
        if report['candidateHead'] != report['executedSource']:
            return False
        expected_counts = {'Document': 374, 'Document-focus': 160, 'File': 1021, 'Employee': 359}
        if set(inventories) != set(expected_counts) or any(inventories[name]['count'] != count or len(inventories[name]['inventory']) != count or inventories[name]['sourceHead'] != pins['Legacy.Maliev.' + name.split('-')[0] + 'Service'] for name, count in expected_counts.items()):
            return False
        extra = {'Legacy.Maliev.Intranet': '3f5f7542c93cb085757130971c4fc7cf61043f01',
                 'Legacy.Maliev.Workflows': '0159e67a033712a7120d52173819e8bde214cf6e',
                 'Legacy.Maliev.Workflows.Security': 'e3a6093324a24968876782153286f52db8b29fd8'}
        trees = report['sourceTrees']
        if len(trees) != len(pins) + 3 or {row['repository']: row['head'] for row in trees} != {**pins, **extra} or any(set(row) != {'repository', 'head', 'tree'} or len(row['tree']) != 40 for row in trees):
            return False
        if [row['producer'] for row in report['producers']] != ['Document', 'File', 'Employee']:
            return False
        seen_runs = set()
        for row in report['producers']:
            if set(row) != {'producer', 'head', 'native', 'focus', 'coverage', 'audit', 'assets', 'binaries', 'applicability', 'cleanup'}:
                return False
            producer = row['producer']
            if row['head'] != pins['Legacy.Maliev.' + producer + 'Service'] or not native(row['native'], inventories[producer]) or not observed_coverage(row['coverage'], producer) or not compiled(row['binaries'], producer) or not cleanup(row['cleanup']):
                return False
            commands = ['gitleaks', 'jwt-scan', 'tree-scan', 'restore', 'build', 'format', 'package-audit']
            if producer == 'Document':
                commands.insert(0, 'gitleaks-install')
                commands += ['openapi-inputs', 'pristine-contract'] + ['mutation-' + name for name in ('CONCRETE', 'DEFAULT_METHOD', 'STATIC_METHOD', 'EXTRA_TYPE', 'RESOURCE')]
                commands += ['contract-controls', 'document-focus', 'full-native', 'test_receipt_evidence.py', 'test_document_application_proof.py', 'read-receipt-evidence.py', 'read-document-contract-applicability.py', 'original-coverage-policy']
            else:
                commands += ['full-native', 'original-coverage-policy']
                if producer == 'Employee':
                    commands += ['original-scaffold-controls']
            if [item['name'] for item in row['cleanup']['processes']] != commands:
                return False
            if row['native']['runId'] in seen_runs:
                return False
            seen_runs.add(row['native']['runId'])
            if not row['assets'] or len({item['path'] for item in row['assets']}) != len(row['assets']) or any(set(item) != {'path', 'sha256'} or not identity(item['sha256']) or not item['path'].endswith('/obj/project.assets.json') for item in row['assets']):
                return False
            projects = {'Legacy.Maliev.' + producer + 'Service.' + part + '.csproj' for part in ('Api', 'Application', 'Domain', 'Rendering' if producer == 'Document' else 'Data', 'Tests')}
            if {item['path'] for item in row['assets']} != {name.removesuffix('.csproj') + '/obj/project.assets.json' for name in projects}:
                return False
            if set(row['audit']) != {'rawSha256', 'projectNames'} or not identity(row['audit']['rawSha256']) or not projects.issubset(set(row['audit']['projectNames'])):
                return False
            if producer == 'Document':
                if not native(row['focus'], inventories['Document-focus']) or row['focus']['runId'] in seen_runs:
                    return False
                seen_runs.add(row['focus']['runId'])
                policy = row['applicability']
                expected_policy_fields = {'schemaVersion', 'policyActive', 'head', 'runId', 'runAttempt', 'policySha256', 'compiledProofSha256', 'controlsSha256', 'applicationStatus', 'applicationNumericalPercent', 'applicationNumericalPassed', 'executableFloorsPassed', 'actualHttpPassed', 'fourAssemblyNumericalAcceptance', 'applicabilityAcceptance', 'exclusions', 'deployed'}
                if set(policy) != expected_policy_fields or policy['schemaVersion'] != 'document-contract-applicability-acceptance/v1':
                    return False
                if policy['head'] != row['head'] or policy['runId'] != owner['runId'] or policy['runAttempt'] != owner['runAttempt'] or policy['actualHttpPassed'] != 43 or policy['applicationNumericalPassed'] is not False or policy['fourAssemblyNumericalAcceptance'] is not False or policy['applicationNumericalPercent'] is not None or policy['applicationStatus'] != 'N/A contract-only' or policy['policyActive'] is not True or policy['applicabilityAcceptance'] is not True or policy['executableFloorsPassed'] is not True or policy['deployed'] is not False or policy['exclusions'] != [] or any(not identity(policy[name]) for name in ('policySha256', 'compiledProofSha256', 'controlsSha256')):
                    return False
            elif row['focus'] is not None or row['applicability'] is not None:
                return False
        shared = report['sharedBinaries']
        names = {name + '.' + ext for name in ('Legacy.Maliev.ServiceDefaults', 'Legacy.Maliev.CompatibilityContracts') for ext in ('dll', 'pdb')}
        return len(shared) == 4 and {item['name'] for item in shared} == names and all(set(item) == {'name', 'bytes', 'sha256'} and type(item['bytes']) is int and item['bytes'] > 0 and identity(item['sha256']) for item in shared)
    except (KeyError, ValueError, TypeError, AttributeError):
        return False


def join(directory, root, admit=False):
    try:
        evidence_directory = pathlib.Path(directory)
        graph_path = evidence_directory / 'producer-graph.json'
        graph = json.loads(graph_path.read_text())
        pins = json.loads((HERE / 'candidate-pins.json').read_text())['pins']
        owner = {'candidateHead': os.environ['CANDIDATE_HEAD'], 'executedSource': os.environ['CANDIDATE_HEAD'],
                 'runId': os.environ['GITHUB_RUN_ID'], 'runAttempt': os.environ['GITHUB_RUN_ATTEMPT']}
        if not complete(graph, owner, json.loads((HERE / 'graph-native-inventory.json').read_text()), pins):
            return False
        from producer_graph import assets, git, producer_directory, verify_sources
        root = pathlib.Path(root)
        if git(root, 'rev-parse', 'HEAD') != owner['candidateHead'] or verify_sources(root, pins) != graph['sourceTrees']:
            return False
        inventory = json.loads((HERE / 'graph-native-inventory.json').read_text())
        private = pathlib.Path(os.environ['RUNNER_TEMP']) / 'po-producer-graph-private'
        for row in graph['producers']:
            producer = row['producer']
            directory = producer_directory(root, producer, pins)
            original = private / producer
            full = original / ('receipt-evidence/full' if producer == 'Document' else 'results')
            if trx(full / 'full-suite.trx', inventory[producer]['inventory']) != row['native'] or assets(directory, root / '.dependencies') != row['assets']:
                return False
            assemblies = ['Legacy.Maliev.' + producer + 'Service.' + name for name in ('Api', 'Application', 'Domain', 'Rendering' if producer == 'Document' else 'Data')]
            if coverage(full.rglob('coverage.cobertura.xml'), assemblies, producer == 'Document') != row['coverage']:
                return False
            projects = [path.name for path in directory.glob('*/*.csproj')]
            if audit(original / 'package-audit.log', projects) != row['audit']:
                return False
            if producer == 'Document':
                if trx(original / 'receipt-evidence/focus/receipt-focus.trx', inventory['Document-focus']['inventory']) != row['focus']:
                    return False
                retained = original / 'receipt-evidence'
                policy = row['applicability']
                if json.loads((retained / 'contract-applicability-acceptance.json').read_text()) != policy or policy['policySha256'] != digest(directory / 'docs/document-contract-applicability-policy.json') or policy['compiledProofSha256'] != digest(retained / 'contract-applicability-proposal.json') or policy['controlsSha256'] != digest(retained / 'contract-negative-controls.json'):
                    return False
            for process in row['cleanup']['processes']:
                if digest(original / (process['name'] + '.log')) != process['logSha256']:
                    return False
            if json.loads((original / 'cleanup.json').read_text()) != row['cleanup']:
                return False
        consumed = pathlib.Path(root) / 'acceptance/PurchaseOrderCompanyCatalogPersistence/bin/Release/net10.0'
        binaries = [item for row in graph['producers'] for item in row['binaries']] + graph['sharedBinaries']
        actual = []
        for item in binaries:
            path = consumed / item['name']
            if not path.is_file() or digest(path) != item['sha256'] or path.stat().st_size != item['bytes']:
                return False
            actual.append(item)
        expected = {'schema': 1, **owner, 'graphReceiptSha256': digest(graph_path), 'consumed': actual}
        target = evidence_directory / 'producer-graph-admission.json'
        if admit:
            if target.exists():
                return False
            target.write_text(json.dumps(expected, indent=2) + '\n')
        return json.loads(target.read_text()) == expected
    except (OSError, ValueError, KeyError, TypeError, AttributeError, ET.ParseError):
        return False


if __name__ == '__main__':
    if not join(os.environ['PROOF_EVIDENCE'], HERE.parents[1], admit=True):
        raise SystemExit('Producer graph/actual consumed PE-PDB admission incomplete')
