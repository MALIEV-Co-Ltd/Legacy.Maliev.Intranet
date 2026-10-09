"""Serial own-run producer qualification. Hosted Ubuntu only; raw output stays private."""
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys

from graph_evidence import audit, coverage, digest, trx
from graph_supervisor import Phase

HERE = pathlib.Path(__file__).resolve().parent
PRODUCERS = ('Document', 'File', 'Employee')
SECURITY_HEAD = 'e3a6093324a24968876782153286f52db8b29fd8'
TEST_INTRANET = '3f5f7542c93cb085757130971c4fc7cf61043f01'
TEST_WORKFLOWS = '0159e67a033712a7120d52173819e8bde214cf6e'


# These are source-owned validator literals, never caller exception text.
VALIDATION_REASONS = {
    'audit incomplete': 'audit-incomplete', 'audit project incomplete': 'audit-project-incomplete',
    'audit framework incomplete': 'audit-framework-incomplete',
    'vulnerable or incomplete package': 'audit-package-incomplete-or-vulnerable',
    'missing audited project': 'audit-project-missing',
    'source checkout mismatch': 'source-checkout-mismatch', 'source blob mismatch': 'source-blob-mismatch',
    'source canonical hash mismatch': 'source-hash-mismatch',
    'working source differs from canonical blob': 'working-source-mismatch',
    'original CRLF File consumer checkout projection mismatch': 'consumer-projection-mismatch',
    'security source mismatch': 'security-source-mismatch',
    'restore warnings/errors': 'restore-warnings-or-errors',
    'packaged shared dependency': 'packaged-shared-dependency',
    'foreign shared project path': 'foreign-shared-project',
    'missing restored shared graph': 'restored-shared-graph-missing',
    'missing production PE/PDB': 'production-binary-missing', 'missing shared PE/PDB': 'shared-binary-missing',
    'OpenAPI generated source ambiguous': 'openapi-source-ambiguous',
    'duplicate TRX definition': 'native-definition-duplicate',
    'foreign or duplicate TRX TestEntry': 'native-entry-foreign-or-duplicate',
    'missing TestEntry lineage': 'native-entry-lineage-missing',
    'nonpass or duplicate execution': 'native-nonpass-or-duplicate',
    'broken definition/execution lineage': 'native-lineage-broken', 'foreign result name': 'native-result-foreign',
    'native inventory mismatch': 'native-inventory-mismatch', 'native counters mismatch': 'native-counters-mismatch',
    'missing or distinct raw coverage': 'coverage-missing-or-distinct',
    'missing coverage filename': 'coverage-filename-missing', 'invalid raw line': 'coverage-line-invalid',
    'raw owned coverage fails': 'coverage-floor-failed',
    'production binaries not restored after instrumentation': 'production-binaries-not-restored',
    'not exact-head owned hosted Ubuntu': 'hosted-source-invalid',
    'candidate flag must remain unaccepted': 'candidate-flag-invalid'}
VALIDATION_GATES = {'hosted-source', 'candidate-manifest', 'source-verification', 'native-inventory',
                    'producer-commands', 'production-binary-readback', 'restore-assets-readback',
                    'package-audit-command', 'package-audit-readback', 'document-policy',
                    'document-focus-command', 'full-native-command', 'native-readback',
                    'document-focus-readback', 'document-evidence-policy', 'coverage-policy',
                    'employee-scaffold-policy', 'coverage-readback', 'production-binary-restoration',
                    'producer-cleanup', 'shared-binary-readback'}


def safe_audit_metadata(path, expected):
    """Counts and source-known basenames only; unavailable data grants no acceptance."""
    try:
        if path.stat().st_size > 8_388_608:
            return None
        value = json.loads(path.read_text())
        projects = value['projects']
        if not isinstance(projects, list) or len(projects) > 128:
            return None
        names = sorted(set(name for name in expected if isinstance(name, str) and re.fullmatch(r'Legacy\.Maliev\.[A-Za-z0-9.]+\.csproj', name)))
        rows = []
        for project in projects:
            basename = pathlib.PurePosixPath(str(project.get('path', '')).replace('\\', '/')).name
            frameworks = project.get('frameworks', [])
            if not isinstance(frameworks, list) or len(frameworks) > 128:
                return None
            counts = []
            for framework in frameworks:
                top, transitive = framework.get('topLevelPackages', []), framework.get('transitivePackages', [])
                if not isinstance(top, list) or not isinstance(transitive, list):
                    return None
                counts.append({'frameworkPresent': bool(framework.get('framework')),
                               'problemCount': len(framework.get('problems') or []),
                               'topLevelPackageCount': len(top), 'transitivePackageCount': len(transitive)})
            rows.append({'project': basename if basename in names else 'unrecognized-project',
                         'problemCount': len(project.get('problems') or []), 'frameworks': counts})
        return {'expectedProjects': names, 'actualProjects': rows, 'problemCount': len(value.get('problems') or [])}
    except Exception:
        return None


def validation_failure(error, gate, audit_metadata=None):
    category = type(error).__name__
    return {'gate': gate if gate in VALIDATION_GATES else 'unknown-validator',
            'category': category if category in {'ValueError', 'KeyError', 'TypeError', 'JSONDecodeError',
                                               'FileNotFoundError', 'PermissionError', 'OSError', 'CommandFailure'} else 'ProducerValidationError',
            'reason': VALIDATION_REASONS.get(str(error), 'unknown-validator-reason') if type(error) is ValueError else 'unknown-validator-reason',
            'auditMetadata': audit_metadata}


def safe_native_failure(path, inventory):
    """Failure-only original TRX projection; source-known identities, never assertion/output text."""
    from collections import Counter
    import hashlib
    import uuid
    import xml.etree.ElementTree as ET
    from graph_evidence import NS, ZERO

    reason = 'inventory-invalid'
    raw_hash = None
    try:
        if not isinstance(inventory, list) or not 0 < len(inventory) <= 2048:
            raise ValueError()
        known = {}
        for item in inventory:
            if set(item) != {'testName', 'className', 'method'} or any(
                    not isinstance(item[key], str) or not 0 < len(item[key]) <= 4096 for key in item):
                raise ValueError()
            known[(item['testName'], item['className'], item['method'])] = item
        expected = Counter((item['testName'], item['className'], item['method']) for item in inventory)
        reason = 'trx-unreadable'
        with pathlib.Path(path).open('rb') as stream:
            raw = stream.read(8_388_609)
        reason = 'trx-size-limit'
        if len(raw) > 8_388_608:
            raise ValueError()
        raw_hash = hashlib.sha256(raw).hexdigest()
        reason = 'xml-malformed'
        text = raw.decode('utf-8-sig')
        reason = 'xml-encoding-invalid'
        if '\x00' in text:
            raise ValueError()
        reason = 'xml-forbidden-declaration'
        if '<!DOCTYPE' in text.upper() or '<!ENTITY' in text.upper():
            raise ValueError()
        reason = 'xml-malformed'
        root = ET.fromstring(text)
        reason = 'lineage-invalid'
        if root.tag != '{' + NS['t'] + '}TestRun':
            raise ValueError()
        run_id = str(uuid.UUID(root.attrib['id']))
        definitions, executions = {}, {}
        definition_rows = root.findall('./t:TestDefinitions/t:UnitTest', NS)
        rows = root.findall('./t:Results/t:UnitTestResult', NS)
        entries = root.findall('./t:TestEntries/t:TestEntry', NS)
        if any(len(group) != len(inventory) for group in (definition_rows, rows, entries)):
            raise ValueError()
        for definition in definition_rows:
            test_id = str(uuid.UUID(definition.attrib['id']))
            methods = definition.findall('./t:TestMethod', NS)
            execution_rows = definition.findall('./t:Execution', NS)
            if test_id in definitions or len(methods) != 1 or len(execution_rows) != 1:
                raise ValueError()
            identity = (definition.attrib['name'], methods[0].attrib['className'], methods[0].attrib['name'])
            reason = 'inventory-mismatch'
            if identity not in known:
                raise ValueError()
            reason = 'lineage-invalid'
            execution_id = str(uuid.UUID(execution_rows[0].attrib['id']))
            if execution_id in executions.values():
                raise ValueError()
            definitions[test_id], executions[test_id] = identity, execution_id
        entry_map = {}
        for entry in entries:
            test_id, execution_id = str(uuid.UUID(entry.attrib['testId'])), str(uuid.UUID(entry.attrib['executionId']))
            if test_id in entry_map or executions.get(test_id) != execution_id:
                raise ValueError()
            entry_map[test_id] = execution_id
        if entry_map != executions:
            raise ValueError()
        outcomes = {'Passed': 'passed', 'Failed': 'failed', 'NotExecuted': 'notExecuted', 'Aborted': 'aborted',
                    'Timeout': 'timeout', 'Error': 'error', 'Inconclusive': 'inconclusive', 'NotRunnable': 'notRunnable'}
        actual, result_ids, captured = [], set(), []
        for row in rows:
            test_id, execution_id = str(uuid.UUID(row.attrib['testId'])), str(uuid.UUID(row.attrib['executionId']))
            if execution_id in result_ids or executions.get(test_id) != execution_id or entry_map.get(test_id) != execution_id:
                raise ValueError()
            identity = definitions[test_id]
            if row.attrib['testName'] != identity[0]:
                raise ValueError()
            result_ids.add(execution_id)
            outcome = row.attrib['outcome']
            reason = 'outcome-invalid'
            if outcome not in outcomes:
                raise ValueError()
            reason = 'lineage-invalid'
            actual.append(identity)
            captured.append({**known[identity], 'testId': test_id, 'executionId': execution_id, 'outcome': outcome})
        reason = 'inventory-mismatch'
        if Counter(actual) != expected:
            raise ValueError()
        reason = 'counters-invalid'
        summaries = root.findall('./t:ResultSummary', NS)
        counters = root.findall('./t:ResultSummary/t:Counters', NS)
        if len(summaries) != 1 or summaries[0].get('outcome') not in {'Completed', 'Failed', 'Aborted', 'Error', 'Timeout'} or len(counters) != 1:
            raise ValueError()
        observed = Counter(outcomes[item['outcome']] for item in captured)
        required = dict.fromkeys(('total', 'executed', 'passed', *ZERO), 0)
        required.update(observed)
        required.update(total=len(rows), executed=len(rows) - observed['notExecuted'] - observed['notRunnable'])
        if counters[0].attrib != {key: str(value) for key, value in required.items()}:
            raise ValueError()
        return {'status': 'retained', 'accepted': False, 'rawSha256': raw_hash, 'runId': run_id,
                'summaryOutcome': summaries[0].get('outcome'), 'counters': required, 'rows': captured}
    except FileNotFoundError:
        reason = 'trx-missing'
    except Exception:
        pass
    return {'status': 'unavailable', 'accepted': False, 'reason': reason, 'rawSha256': raw_hash}


def git(root, *args):
    return subprocess.check_output(['git', '-C', str(root), *args]).decode().strip()


def verify_sources(root, pins):
    locations = {name: root / '.dependencies' / name for name in pins}
    locations.update({'Legacy.Maliev.Intranet': root / '.dependencies/Legacy.Maliev.Intranet',
                      'Legacy.Maliev.Workflows': root / '.dependencies/Legacy.Maliev.FileService/.dependencies/Legacy.Maliev.Workflows'})
    required = {**pins, 'Legacy.Maliev.Intranet': TEST_INTRANET, 'Legacy.Maliev.Workflows': TEST_WORKFLOWS}
    result = []
    for name, head in required.items():
        directory = locations[name]
        if git(directory, 'rev-parse', 'HEAD') != head or git(directory, 'diff', '--name-only', 'HEAD'):
            raise ValueError('source checkout mismatch')
        result.append({'repository': name, 'head': head, 'tree': git(directory, 'rev-parse', 'HEAD^{tree}')})
    bindings = json.loads((HERE / 'candidate-pins.json').read_text())['sourceBindings']
    bindings += json.loads((HERE / 'graph-source-bindings.json').read_text())
    for item in bindings:
        directory = locations[item['repository']]
        if git(directory, 'rev-parse', 'HEAD:' + item['path']) != item['gitBlob']:
            raise ValueError('source blob mismatch')
        committed = subprocess.check_output(['git', '-C', str(directory), 'show', 'HEAD:' + item['path']])
        import hashlib
        if hashlib.sha256(committed).hexdigest() != item['canonicalSha256']:
            raise ValueError('source canonical hash mismatch')
        # Git's working-tree conversion is explicit, not an unexplained normalization.
        actual_blob = git(directory, 'hash-object', '--path=' + item['path'], str(directory / item['path']))
        if actual_blob != item['gitBlob']:
            raise ValueError('working source differs from canonical blob')
        if item['repository'] == 'Legacy.Maliev.Intranet' and item['path'].endswith('LegacyFileClient.cs') and digest(directory / item['path']) != 'a4a31a998cc168709d0914fa19b4836f5da219c73a3d6f99c7a2e40d15429758':
            raise ValueError('original CRLF File consumer checkout projection mismatch')
    security = root / '.graph-security'
    if git(security, 'rev-parse', 'HEAD') != SECURITY_HEAD or git(security, 'diff', '--name-only', 'HEAD'):
        raise ValueError('security source mismatch')
    result.append({'repository': 'Legacy.Maliev.Workflows.Security', 'head': SECURITY_HEAD, 'tree': git(security, 'rev-parse', 'HEAD^{tree}')})
    return result


def assets(directory, workspace):
    inventory, found = [], set()
    projects = {path.resolve() for path in directory.glob('*/*.csproj')}
    restored_projects = set()
    for path in sorted(directory.glob('*/obj/project.assets.json')):
        value = json.loads(path.read_text())
        owner = path.parent.parent / (path.parent.parent.name + '.csproj')
        frameworks = value.get('project', {}).get('frameworks')
        targets = value.get('targets')
        libraries = value.get('libraries')
        restored_owner = value.get('project', {}).get('restore', {}).get('projectPath')
        if owner.resolve() not in projects or not isinstance(restored_owner, str) or pathlib.Path(restored_owner).resolve() != owner.resolve() or owner.resolve() in restored_projects or not isinstance(frameworks, dict) or not frameworks or any(not isinstance(name, str) or not name or not isinstance(row, dict) for name, row in frameworks.items()) or not isinstance(targets, dict) or not targets or not isinstance(libraries, dict):
            raise ValueError('missing restored shared graph')
        if {name.split('/')[0] for name in targets} != set(frameworks) or any(not isinstance(row, dict) or any(key not in libraries or not isinstance(library, dict) or library.get('type') not in ('package', 'project') or not isinstance(libraries[key], dict) or library.get('type') != libraries[key].get('type') for key, library in row.items()) for row in targets.values()):
            raise ValueError('missing restored shared graph')
        restored_projects.add(owner.resolve())
        if value.get('logs') and any(item.get('level') in ('Error', 'Warning') for item in value['logs']):
            raise ValueError('restore warnings/errors')
        for key, library in value.get('libraries', {}).items():
            if key.startswith(('Legacy.Maliev.ServiceDefaults/', 'Legacy.Maliev.CompatibilityContracts/')):
                if library['type'] != 'project':
                    raise ValueError('packaged shared dependency')
                name = key.split('/')[0]
                resolved = (path.parent.parent / library['path']).resolve()
                expected = workspace / name / 'src' / name / (name + '.csproj')
                if resolved != expected.resolve():
                    raise ValueError('foreign shared project path')
                found.add(name)
        inventory.append({'path': path.relative_to(directory).as_posix(), 'sha256': digest(path)})
    if not projects or restored_projects != projects or not inventory or found != {'Legacy.Maliev.ServiceDefaults', 'Legacy.Maliev.CompatibilityContracts'}:
        raise ValueError('missing restored shared graph')
    return inventory


def binaries(directory, producer):
    modules = ('Api', 'Application', 'Domain', 'Rendering' if producer == 'Document' else 'Data')
    result = []
    for module in modules:
        name = 'Legacy.Maliev.' + producer + 'Service.' + module
        for extension in ('dll', 'pdb'):
            path = directory / name / 'bin/Release/net10.0' / (name + '.' + extension)
            if not path.is_file() or path.stat().st_size == 0:
                raise ValueError('missing production PE/PDB')
            result.append({'name': path.name, 'sha256': digest(path), 'bytes': path.stat().st_size})
    return result


def shared_binaries(root):
    result = []
    for name in ('Legacy.Maliev.ServiceDefaults', 'Legacy.Maliev.CompatibilityContracts'):
        for extension in ('dll', 'pdb'):
            path = root / '.dependencies' / name / 'src' / name / 'bin/Release/net10.0' / (name + '.' + extension)
            if not path.is_file() or path.stat().st_size == 0:
                raise ValueError('missing shared PE/PDB')
            result.append({'name': path.name, 'sha256': digest(path), 'bytes': path.stat().st_size})
    return result


def document_policy(phase, directory, private, env):
    evidence = private / 'receipt-evidence'
    evidence.mkdir()
    api = 'Legacy.Maliev.DocumentService.Api'
    inputs = phase.run(['dotnet', 'msbuild', api + '/' + api + '.csproj',
                        '-target:ResolveReferences,GenerateAdditionalXmlFilesForOpenApi',
                        '-p:Configuration=Release', '-p:UseLocalMalievDependencies=true', '-p:GITHUB_ACTIONS=false',
                        '-p:UseSharedCompilation=false',
                        '-getItem:AdditionalFiles,ReferencePath'], directory, env, 'openapi-inputs')
    target = evidence / 'openapi-compile'
    target.mkdir()
    shutil.copyfile(inputs, target / 'resolved-inputs.json')
    for name in (api, 'Legacy.Maliev.DocumentService.Domain'):
        shutil.copyfile(directory / name / 'bin/Release/net10.0' / (name + '.xml'), target / (name + '.xml'))
    generated = list((directory / api / 'obj/Release/net10.0').rglob('OpenApiXmlCommentSupport.generated.cs'))
    if len(generated) != 1:
        raise ValueError('OpenAPI generated source ambiguous')
    shutil.copyfile(generated[0], target / 'OpenApiXmlCommentSupport.generated.cs.source')
    phase.run([sys.executable, '-B', 'scripts/capture-document-contract-proof.py', str(evidence / 'application-provenance')], directory, env, 'pristine-contract')
    fixtures = private / 'contract-fixtures'
    for mutation in ('CONCRETE', 'DEFAULT_METHOD', 'STATIC_METHOD', 'EXTRA_TYPE', 'RESOURCE'):
        output = fixtures / mutation
        phase.run(['dotnet', 'build', 'scripts/fixtures/contract-surface/ContractSurface.Tests.csproj', '-c', 'Release', '--warnaserror',
                   '-p:SurfaceMutation=' + mutation, '-p:SourceRevisionId=' + env['DOCUMENT_SOURCE_SHA'],
                   '-p:DomainAssemblyPath=' + str(directory / 'Legacy.Maliev.DocumentService.Domain/bin/Release/net10.0/Legacy.Maliev.DocumentService.Domain.dll'),
                   '-p:BaseIntermediateOutputPath=' + str(output / 'obj') + '/', '-p:OutputPath=' + str(output) + '/',
                   '-p:AppendTargetFrameworkToOutputPath=false', '-p:UseSharedCompilation=false'], directory, env, 'mutation-' + mutation)
        retained = evidence / 'contract-fixtures' / mutation
        retained.mkdir(parents=True)
        for extension in ('dll', 'pdb'):
            shutil.copyfile(output / ('Legacy.Maliev.DocumentService.Application.' + extension), retained / ('Legacy.Maliev.DocumentService.Application.' + extension))
    phase.run([sys.executable, '-B', 'scripts/test-document-contract-controls.py', str(evidence / 'application-provenance'), str(fixtures)], directory, env, 'contract-controls')
    return evidence


def qualify(root, producer, pins, inventory, private):
    name = 'Legacy.Maliev.' + producer + 'Service'
    directory = root / '.dependencies' / name
    env = {**os.environ, 'GITHUB_ACTIONS': 'false', 'UseLocalMalievDependencies': 'true',
           'MalievWorkspaceRoot': str(root / '.dependencies'), 'DocumentProvenanceCapture': 'false',
           'DOCUMENT_SOURCE_SHA': pins['Legacy.Maliev.DocumentService'],
           'DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER': '1', 'MSBUILDDISABLENODEREUSE': '1'}
    tools = private.parent / 'tools'
    tools.mkdir(parents=True, exist_ok=True)
    env['GOBIN'] = str(tools)
    policy = json.loads((HERE / 'graph-actor-policy.json').read_text())
    phase = Phase(policy, private)
    result, failure, cleanup_failure = None, None, None
    gate, audit_metadata = 'producer-commands', None
    try:
        solution = name + '.slnx'
        if producer == 'Document':
            phase.run(['go', 'install', 'github.com/zricethezav/gitleaks/v8@6eaad039603a4de39fddd1cf5f727391efe9974e'], directory, env, 'gitleaks-install')
        for command, label in (([str(tools / 'gitleaks'), 'git', '--redact', '--exit-code', '1'], 'gitleaks'),
                               (['pwsh', '-NoProfile', '-File', str(root / '.graph-security/scripts/Invoke-JwtSigningResourceScan.ps1'), '-RepositoryPath', str(directory)], 'jwt-scan'),
                               (['pwsh', '-NoProfile', '-File', str(root / '.graph-security/scripts/Invoke-CurrentTreeCredentialScan.ps1'), '-RepositoryPath', str(directory)], 'tree-scan'),
                               (['dotnet', 'restore', solution, '-p:UseLocalMalievDependencies=true', '-p:GITHUB_ACTIONS=false'], 'restore'),
                               (['dotnet', 'build', solution, '-c', 'Release', '--no-restore', '--warnaserror', '-p:UseLocalMalievDependencies=true', '-p:GITHUB_ACTIONS=false', '-p:EmitCompilerGeneratedFiles=true', '-p:UseSharedCompilation=false'], 'build'),
                               (['dotnet', 'format', solution, '--verify-no-changes', '--no-restore'], 'format')):
            phase.run(command, directory, env, label)
        gate = 'production-binary-readback'
        pristine = binaries(directory, producer)
        gate = 'restore-assets-readback'
        restored = assets(directory, root / '.dependencies')
        gate = 'package-audit-command'
        audit_path = phase.run(['dotnet', 'list', solution, 'package', '--vulnerable', '--include-transitive', '--no-restore', '--format', 'json', '--output-version', '1'], directory, env, 'package-audit')
        projects = [path.name for path in directory.glob('*/*.csproj')]
        gate = 'package-audit-readback'
        audit_metadata = safe_audit_metadata(audit_path, projects)
        audited = audit(audit_path, projects)
        audit_metadata = None
        gate = 'document-policy'
        evidence = document_policy(phase, directory, private, env) if producer == 'Document' else private / 'results'
        full = evidence / 'full' if producer == 'Document' else evidence
        if producer == 'Document':
            gate = 'document-focus-command'
            phase.run(['dotnet', 'test', name + '.Tests/' + name + '.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore', '-p:UseLocalMalievDependencies=true',
                       '--filter', 'FullyQualifiedName~ThaiBahtAmountWordsTests|FullyQualifiedName~ReceiptThaiAmountContentTests|FullyQualifiedName~DocumentRuntimeHttpTests|FullyQualifiedName~ReceiptAmountBrandPatternTests',
                       '--logger', 'trx;LogFileName=receipt-focus.trx', '--results-directory', str(evidence / 'focus')], directory, env, 'document-focus')
        gate = 'full-native-command'
        phase.run(['dotnet', 'test', solution, '-c', 'Release', '--no-build', '--no-restore', '-p:UseLocalMalievDependencies=true',
                   '--logger', 'trx;LogFileName=full-suite.trx', '--results-directory', str(full), '--collect', 'XPlat Code Coverage'], directory, env, 'full-native')
        gate = 'native-readback'
        native = trx(full / 'full-suite.trx', inventory[producer]['inventory'])
        applicability = None
        focus = None
        if producer == 'Document':
            gate = 'document-focus-readback'
            focus = trx(evidence / 'focus/receipt-focus.trx', inventory['Document-focus']['inventory'])
            gate = 'document-evidence-policy'
            for pattern in ('test_receipt_evidence.py', 'test_document_application_proof.py'):
                phase.run([sys.executable, '-B', '-m', 'unittest', 'discover', '-s', 'scripts', '-p', pattern], directory, env, pattern)
            for script in ('read-receipt-evidence.py', 'read-document-contract-applicability.py'):
                phase.run([sys.executable, '-B', 'scripts/' + script, str(evidence)], directory, env, script)
            gate = 'coverage-policy'
            phase.run([sys.executable, '-B', 'scripts/verify-runner-coverage.py', str(full), str(evidence)], directory, env, 'original-coverage-policy')
            applicability = json.loads((evidence / 'contract-applicability-acceptance.json').read_text())
        else:
            gate = 'coverage-policy'
            phase.run([sys.executable, '-B', 'scripts/verify-runner-coverage.py', str(full)], directory, env, 'original-coverage-policy')
        if producer == 'Employee':
            gate = 'employee-scaffold-policy'
            phase.run(['pwsh', '-NoProfile', '-File', 'tooling/Test-EmployeeScaffoldContract.ps1', '-EvidencePath', str(private / 'employee-scaffold-orchestration.json')], directory, env, 'original-scaffold-controls')
        assemblies = ['Legacy.Maliev.' + producer + 'Service.' + module for module in ('Api', 'Application', 'Domain', 'Rendering' if producer == 'Document' else 'Data')]
        gate = 'coverage-readback'
        covered = coverage(full.rglob('coverage.cobertura.xml'), assemblies, producer == 'Document')
        gate = 'production-binary-restoration'
        if binaries(directory, producer) != pristine:
            raise ValueError('production binaries not restored after instrumentation')
        result = {'producer': producer, 'head': pins[name], 'native': native, 'focus': focus,
                  'coverage': covered, 'audit': audited, 'assets': restored, 'binaries': pristine,
                  'applicability': applicability}
    except Exception as error:
        error.producer_validation_failure = validation_failure(error, gate, audit_metadata)
        if gate in {'full-native-command', 'native-readback'}:
            try:
                error.producer_validation_failure['nativeMetadata'] = safe_native_failure(
                    full / 'full-suite.trx', inventory[producer]['inventory'])
            except Exception:
                error.producer_validation_failure['nativeMetadata'] = {
                    'status': 'unavailable', 'accepted': False, 'reason': 'capture-unavailable', 'rawSha256': None}
        failure = error
    finally:
        try:
            closure = phase.settle()
        except Exception as error:
            cleanup_failure = error
    if failure is not None:
        raise failure from cleanup_failure
    if cleanup_failure is not None:
        cleanup_failure.producer_validation_failure = validation_failure(cleanup_failure, 'producer-cleanup')
        raise cleanup_failure
    result['cleanup'] = closure
    return result


def main():
    root = HERE.parents[1]
    output = pathlib.Path(os.environ['PROOF_EVIDENCE'])
    output.mkdir(parents=True, exist_ok=True)
    private = pathlib.Path(os.environ['RUNNER_TEMP']) / 'po-producer-graph-private'
    report = {'schema': 1, 'graphComplete': False, 'acceptedProducerGraph': False,
              'candidateHead': os.environ['CANDIDATE_HEAD'], 'executedSource': git(root, 'rev-parse', 'HEAD'),
              'runId': os.environ['GITHUB_RUN_ID'], 'runAttempt': os.environ['GITHUB_RUN_ATTEMPT'],
              'pins': {}, 'sourceTrees': [], 'producers': [], 'sharedBinaries': [], 'failureCategory': None,
              'failedProducer': None, 'failureCleanup': None,
              'scope': 'fresh native fixture completion; observed original resource absence and SDK process closure; no SDK-object Dispose certification'}
    gate = 'hosted-source'
    try:
        if sys.platform != 'linux' or os.environ.get('RUNNER_ENVIRONMENT') != 'github-hosted' or not re.fullmatch('[0-9a-f]{40}', report['candidateHead']) or report['candidateHead'] != report['executedSource']:
            raise ValueError('not exact-head owned hosted Ubuntu')
        gate = 'candidate-manifest'
        manifest = json.loads((HERE / 'candidate-pins.json').read_text())
        if manifest['acceptedProducerGraph'] is not False:
            raise ValueError('candidate flag must remain unaccepted')
        report['pins'] = manifest['pins']
        gate = 'source-verification'
        report['sourceTrees'] = verify_sources(root, report['pins'])
        gate = 'native-inventory'
        inventory = json.loads((HERE / 'graph-native-inventory.json').read_text())
        for producer in PRODUCERS:
            report['producers'].append(qualify(root, producer, report['pins'], inventory, private / producer))
        gate = 'shared-binary-readback'
        report['sharedBinaries'] = shared_binaries(root)
        report['graphComplete'] = True
    except Exception as error:
        report['validationFailure'] = getattr(error, 'producer_validation_failure', None) or validation_failure(error, gate)
        report['failureCategory'] = report['validationFailure']['category']
        if 'producer' in locals():
            report['failedProducer'] = producer
            cleanup_path = private / producer / 'cleanup.json'
            if cleanup_path.is_file():
                report['failureCleanup'] = json.loads(cleanup_path.read_text())
    finally:
        (output / 'producer-graph.json').write_text(json.dumps(report, indent=2) + '\n')
    if not report['graphComplete']:
        raise SystemExit('Fresh producer graph incomplete; sanitized actual receipt retained')


if __name__ == '__main__':
    main()
