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
    for path in sorted(directory.glob('*/obj/project.assets.json')):
        value = json.loads(path.read_text())
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
    if not inventory or found != {'Legacy.Maliev.ServiceDefaults', 'Legacy.Maliev.CompatibilityContracts'}:
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
        pristine = binaries(directory, producer)
        restored = assets(directory, root / '.dependencies')
        audit_path = phase.run(['dotnet', 'list', solution, 'package', '--vulnerable', '--include-transitive', '--no-restore', '--format', 'json'], directory, env, 'package-audit')
        projects = [path.name for path in directory.glob('*/*.csproj')]
        audited = audit(audit_path, projects)
        evidence = document_policy(phase, directory, private, env) if producer == 'Document' else private / 'results'
        full = evidence / 'full' if producer == 'Document' else evidence
        if producer == 'Document':
            phase.run(['dotnet', 'test', name + '.Tests/' + name + '.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore', '-p:UseLocalMalievDependencies=true',
                       '--filter', 'FullyQualifiedName~ThaiBahtAmountWordsTests|FullyQualifiedName~ReceiptThaiAmountContentTests|FullyQualifiedName~DocumentRuntimeHttpTests|FullyQualifiedName~ReceiptAmountBrandPatternTests',
                       '--logger', 'trx;LogFileName=receipt-focus.trx', '--results-directory', str(evidence / 'focus')], directory, env, 'document-focus')
        phase.run(['dotnet', 'test', solution, '-c', 'Release', '--no-build', '--no-restore', '-p:UseLocalMalievDependencies=true',
                   '--logger', 'trx;LogFileName=full-suite.trx', '--results-directory', str(full), '--collect', 'XPlat Code Coverage'], directory, env, 'full-native')
        native = trx(full / 'full-suite.trx', inventory[producer]['inventory'])
        applicability = None
        focus = None
        if producer == 'Document':
            focus = trx(evidence / 'focus/receipt-focus.trx', inventory['Document-focus']['inventory'])
            for pattern in ('test_receipt_evidence.py', 'test_document_application_proof.py'):
                phase.run([sys.executable, '-B', '-m', 'unittest', 'discover', '-s', 'scripts', '-p', pattern], directory, env, pattern)
            for script in ('read-receipt-evidence.py', 'read-document-contract-applicability.py'):
                phase.run([sys.executable, '-B', 'scripts/' + script, str(evidence)], directory, env, script)
            phase.run([sys.executable, '-B', 'scripts/verify-runner-coverage.py', str(full), str(evidence)], directory, env, 'original-coverage-policy')
            applicability = json.loads((evidence / 'contract-applicability-acceptance.json').read_text())
        else:
            phase.run([sys.executable, '-B', 'scripts/verify-runner-coverage.py', str(full)], directory, env, 'original-coverage-policy')
        if producer == 'Employee':
            phase.run(['pwsh', '-NoProfile', '-File', 'tooling/Test-EmployeeScaffoldContract.ps1', '-EvidencePath', str(private / 'employee-scaffold-orchestration.json')], directory, env, 'original-scaffold-controls')
        assemblies = ['Legacy.Maliev.' + producer + 'Service.' + module for module in ('Api', 'Application', 'Domain', 'Rendering' if producer == 'Document' else 'Data')]
        covered = coverage(full.rglob('coverage.cobertura.xml'), assemblies, producer == 'Document')
        if binaries(directory, producer) != pristine:
            raise ValueError('production binaries not restored after instrumentation')
        result = {'producer': producer, 'head': pins[name], 'native': native, 'focus': focus,
                  'coverage': covered, 'audit': audited, 'assets': restored, 'binaries': pristine,
                  'applicability': applicability}
    except Exception as error:
        failure = error
    finally:
        try:
            closure = phase.settle()
        except Exception as error:
            cleanup_failure = error
    if failure is not None:
        raise failure from cleanup_failure
    if cleanup_failure is not None:
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
    try:
        if sys.platform != 'linux' or os.environ.get('RUNNER_ENVIRONMENT') != 'github-hosted' or not re.fullmatch('[0-9a-f]{40}', report['candidateHead']) or report['candidateHead'] != report['executedSource']:
            raise ValueError('not exact-head owned hosted Ubuntu')
        manifest = json.loads((HERE / 'candidate-pins.json').read_text())
        if manifest['acceptedProducerGraph'] is not False:
            raise ValueError('candidate flag must remain unaccepted')
        report['pins'] = manifest['pins']
        report['sourceTrees'] = verify_sources(root, report['pins'])
        inventory = json.loads((HERE / 'graph-native-inventory.json').read_text())
        for producer in PRODUCERS:
            report['producers'].append(qualify(root, producer, report['pins'], inventory, private / producer))
        report['sharedBinaries'] = shared_binaries(root)
        report['graphComplete'] = True
    except Exception as error:
        report['failureCategory'] = type(error).__name__
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
