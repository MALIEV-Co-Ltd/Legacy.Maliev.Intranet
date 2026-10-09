"""Independent readers for fresh producer evidence; no SDK or Docker allocation."""
from collections import Counter
import hashlib
import json
import os
import pathlib
import re
import uuid
import xml.etree.ElementTree as ET

NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
ZERO = ('error', 'failed', 'timeout', 'aborted', 'inconclusive', 'passedButRunAborted',
        'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed', 'inProgress', 'pending')


def digest(path):
    return hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()


def identity(value):
    return isinstance(value, str) and re.fullmatch('[0-9a-f]{64}', value) is not None


def trx(path, inventory):
    """Require row multiplicity, own fresh IDs and complete original TRX joins."""
    root = ET.parse(path).getroot()
    uuid.UUID(root.attrib['id'])
    definitions = {}
    executions = {}
    for definition in root.findall('./t:TestDefinitions/t:UnitTest', NS):
        test_id = str(uuid.UUID(definition.attrib['id']))
        if test_id in definitions:
            raise ValueError('duplicate TRX definition')
        method = definition.find('./t:TestMethod', NS)
        execution = definition.find('./t:Execution', NS)
        definitions[test_id] = (definition.attrib['name'], method.attrib['className'], method.attrib['name'])
        executions[test_id] = str(uuid.UUID(execution.attrib['id']))
    rows = root.findall('./t:Results/t:UnitTestResult', NS)
    entries = root.findall('./t:TestEntries/t:TestEntry', NS)
    entry_map = {}
    for entry in entries:
        test_id = str(uuid.UUID(entry.attrib['testId']))
        execution_id = str(uuid.UUID(entry.attrib['executionId']))
        if test_id in entry_map or executions.get(test_id) != execution_id:
            raise ValueError('foreign or duplicate TRX TestEntry')
        entry_map[test_id] = execution_id
    if len(entries) != len(rows) or entry_map != executions:
        raise ValueError('missing TestEntry lineage')
    actual, result_ids = [], set()
    for row in rows:
        test_id = str(uuid.UUID(row.attrib['testId']))
        execution_id = str(uuid.UUID(row.attrib['executionId']))
        if execution_id in result_ids or row.attrib['outcome'] != 'Passed':
            raise ValueError('nonpass or duplicate execution')
        result_ids.add(execution_id)
        if executions.get(test_id) != execution_id or entry_map.get(test_id) != execution_id or test_id not in definitions:
            raise ValueError('broken definition/execution lineage')
        item = definitions[test_id]
        if row.attrib['testName'] != item[0]:
            raise ValueError('foreign result name')
        actual.append(item)
    expected = Counter((item['testName'], item['className'], item['method']) for item in inventory)
    if Counter(actual) != expected or len(definitions) != len(rows):
        raise ValueError('native inventory mismatch')
    summaries = root.findall('./t:ResultSummary', NS)
    counters = root.findall('./t:ResultSummary/t:Counters', NS)
    required = {'total': str(len(rows)), 'executed': str(len(rows)), 'passed': str(len(rows)),
                **dict.fromkeys(ZERO, '0')}
    if len(summaries) != 1 or summaries[0].get('outcome') != 'Completed' or len(counters) != 1 or counters[0].attrib != required:
        raise ValueError('native counters mismatch')
    return {'rawSha256': digest(path), 'runId': root.attrib['id'], 'count': len(rows),
            'rows': [{'testName': item[0], 'className': item[1], 'method': item[2],
                      'executionId': row.attrib['executionId'], 'testId': row.attrib['testId']}
                     for item, row in zip(actual, rows)]}


def coverage(paths, assemblies, contract_application=False):
    paths = list(paths)
    hashes = {digest(path) for path in paths}
    if len(hashes) != 1:
        raise ValueError('missing or distinct raw coverage')
    lines = {name: {} for name in assemblies}
    for package in ET.parse(paths[0]).findall('./packages/package'):
        if package.get('name') not in lines:
            continue
        table = lines[package.attrib['name']]
        for cls in package.findall('./classes/class'):
            filename = cls.attrib['filename']
            if not filename:
                raise ValueError('missing coverage filename')
            for line in cls.findall('./lines/line'):
                key = (filename, int(line.attrib['number']))
                hits = int(line.attrib['hits'])
                if key[1] <= 0 or hits < 0:
                    raise ValueError('invalid raw line')
                table[key] = table.get(key, False) or hits > 0
    result = []
    for name, table in sorted(lines.items()):
        valid, covered = len(table), sum(table.values())
        na = contract_application and name.endswith('.Application')
        if (na and valid != 0) or (not na and (valid == 0 or covered * 100 < valid * 80)):
            raise ValueError('raw owned coverage fails')
        result.append({'assembly': name, 'valid': valid, 'covered': covered,
                       'percent': covered * 100 / valid if valid else None,
                       'applicability': 'contract-only' if na else 'executable'})
    return {'rawSha256': next(iter(hashes)), 'exclusions': [], 'assemblies': result}


def audit(path, expected_projects):
    """Read the version-one vulnerability-filtered protocol, not a package inventory."""
    document = json.loads(pathlib.Path(path).read_text())
    if not isinstance(document, dict) or set(document) - {'version', 'parameters', 'sources', 'projects', 'problems'} or type(document.get('version')) is not int or document['version'] != 1 or document.get('parameters') != '--vulnerable --include-transitive':
        raise ValueError('audit incomplete')
    sources, projects = document.get('sources'), document.get('projects')
    if not isinstance(sources, list) or not sources or any(not isinstance(source, str) or not source.strip() for source in sources) or not isinstance(projects, list) or not projects or ('problems' in document and (not isinstance(document['problems'], list) or document['problems'])):
        raise ValueError('audit incomplete')
    expected = list(expected_projects)
    if not expected or any(not isinstance(name, str) or pathlib.Path(name).name != name or not name.endswith('.csproj') for name in expected) or len(set(expected)) != len(expected):
        raise ValueError('missing audited project')
    names = set()
    for project in projects:
        if not isinstance(project, dict) or set(project) - {'path', 'frameworks', 'problems'} or not isinstance(project.get('path'), str) or not project['path'] or ('problems' in project and (not isinstance(project['problems'], list) or project['problems'])):
            raise ValueError('audit project incomplete')
        name = pathlib.PurePosixPath(project['path'].replace('\\', '/')).name
        if name in names or name not in expected:
            raise ValueError('missing audited project')
        names.add(name)
        # NuGet omits this key when the vulnerability filter returns no frameworks.
        if 'frameworks' not in project:
            continue
        frameworks = project['frameworks']
        if not isinstance(frameworks, list) or not frameworks:
            raise ValueError('audit project incomplete')
        seen = set()
        for framework in frameworks:
            if not isinstance(framework, dict) or set(framework) - {'framework', 'topLevelPackages', 'transitivePackages', 'problems'} or not isinstance(framework.get('framework'), str) or not framework['framework'] or framework['framework'] in seen or ('problems' in framework and (not isinstance(framework['problems'], list) or framework['problems'])):
                raise ValueError('audit framework incomplete')
            seen.add(framework['framework'])
            for key in ('topLevelPackages', 'transitivePackages'):
                if key in framework and (not isinstance(framework[key], list) or framework[key]):
                    # Every reported package is a filtered finding or malformed row.
                    raise ValueError('vulnerable or incomplete package')
    if set(expected) != names:
        raise ValueError('missing audited project')
    return {'rawSha256': digest(path), 'projectNames': sorted(names)}



PROBE_SHA256 = 'ac5ffb6b4bb2e2647f58797359e992ca5f72a46d5430cfb96a90d06afab34919'



PROBE_FAILURE_REASONS = {'ancestry-bound', 'children-bound', 'cohort-unknown-or-changed', 'cyclic-ancestry', 'daemon-generation-changed', 'daemon-identity-changed-after', 'daemon-pid-changed', 'daemon-pid-changed-after', 'generation-changed', 'handshake-bound', 'invalid-arguments', 'invalid-birth', 'invalid-launcher', 'invalid-pid', 'launcher-unbound', 'os-read-failed', 'probe-expired', 'session-not-isolated', 'stat-bound', 'supervisor-generation-changed', 'unclassified-probe-failure'}
PROBE_FAILURE_CATEGORIES = {'ValueError', 'PermissionError', 'FileNotFoundError', 'ProcessLookupError',
                            'TimeoutError', 'OSError', 'UnicodeDecodeError', 'JSONDecodeError', 'BrokenPipeError', 'ProbeError'}


def utility_failure(value):
    """Permit only bounded safe diagnostic data, never private exception/stderr text."""
    try:
        if set(value) != {'schema', 'status', 'complete', 'reason', 'category', 'errno', 'failedRead'} or type(value['schema']) is not int or value['schema'] != 1 or value['status'] != 'failure' or value['complete'] is not False or value['reason'] not in PROBE_FAILURE_REASONS or value['category'] not in PROBE_FAILURE_CATEGORIES:
            return False
        errno = value['errno']
        os_categories = {'PermissionError', 'FileNotFoundError', 'ProcessLookupError', 'TimeoutError', 'OSError', 'BrokenPipeError'}
        if errno is not None and (type(errno) is not int or not 0 < errno <= 4095 or value['category'] not in os_categories):
            return False
        site = value['failedRead']
        if site is None:
            return True
        if set(site) != {'operation', 'path'} or value['category'] not in os_categories:
            return False
        operation, path = site['operation'], site['path']
        if operation == 'daemon-pid-read':
            return path == '/var/run/docker.pid'
        parts = path.split('/') if isinstance(path, str) else []
        suffix = {'daemon-stat-read': 'stat', 'daemon-exe-readlink': 'exe', 'probe-stat-read': 'stat', 'probe-exe-readlink': 'exe'}
        canonical = (len(parts) >= 3 and parts[:2] == ['', 'proc'] and parts[2].isascii() and parts[2].isdecimal() and len(parts[2]) <= 10 and 0 < int(parts[2]) <= 2147483647 and str(int(parts[2])) == parts[2])
        return bool(canonical and ((len(parts) == 4 and operation in suffix and parts[3] == suffix[operation]) or (operation == 'probe-children-read' and len(parts) == 6 and parts[3] == 'task' and parts[4] == parts[2] and parts[5] == 'children')))
    except (KeyError, TypeError, ValueError):
        return False


def interpreter_exe():
    return os.path.realpath('/usr/bin/python3')


def probe_receipt(row, terminal=True):
    """Independent source, actual ancestor generation and terminal-absence joins."""
    try:
        if set(row) != {'sourceSha256', 'interpreterExe', 'owner', 'launcher', 'payload', 'raw',
                        'rawSha256', 'exitCode', 'joined', 'absence', 'firstFailure', 'daemonBefore', 'daemonAfter',
                        'probeFailure', 'stderrSha256', 'outputSha256'}:
            return False
        if row['sourceSha256'] != PROBE_SHA256 or row['sourceSha256'] != digest(pathlib.Path(__file__).with_name('daemon_exe_probe.py')) or row['probeFailure'] is not None or row['firstFailure'] is not None or row['stderrSha256'] != hashlib.sha256(b'').hexdigest() or type(row['exitCode']) is not int or row['exitCode'] != 0 or row['joined'] is not True:
            return False
        raw = row['raw']
        payload = row['payload']
        if not isinstance(raw, str) or not 0 < len(raw.encode('utf-8')) <= 32768 or raw != json.dumps(payload, separators=(',', ':')) + '\n' or hashlib.sha256(raw.encode('utf-8')).hexdigest() != row['rawSha256'] or row['outputSha256'] != row['rawSha256']:
            return False
        if set(payload) != {'schema', 'daemon', 'chain', 'startedNs', 'completedNs', 'expirySeconds'} or type(payload['schema']) is not int or payload['schema'] != 1 or type(payload['expirySeconds']) is not int or payload['expirySeconds'] != 5 or type(payload['startedNs']) is not int or type(payload['completedNs']) is not int or not 0 < payload['startedNs'] < payload['completedNs'] <= payload['startedNs'] + 5_000_000_000:
            return False
        daemon = payload['daemon']
        if set(daemon) != {'pid', 'birth', 'exe'} or type(daemon['pid']) is not int or not 0 < daemon['pid'] <= 2147483647 or not isinstance(daemon['birth'], str) or not daemon['birth'].isascii() or not daemon['birth'].isdecimal() or not isinstance(daemon['exe'], str) or not daemon['exe'].startswith('/') or len(daemon['exe']) > 4096:
            return False
        owner, launcher, chain = row['owner'], row['launcher'], payload['chain']
        if set(owner) != {'pid', 'birth', 'session', 'exe'} or set(launcher) != {'pid', 'birth', 'session'} or type(launcher['session']) is not int or launcher['session'] != launcher['pid'] or not isinstance(chain, list) or not 4 <= len(chain) <= 8:
            return False
        for value in (owner, launcher):
            if any(type(value[key]) is not int or not 0 < value[key] <= 2147483647 for key in ('pid', 'session')) or not isinstance(value['birth'], str) or not value['birth'].isascii() or not value['birth'].isdecimal():
                return False
        if not isinstance(owner['exe'], str) or not owner['exe'].startswith('/') or len(owner['exe']) > 4096:
            return False
        for member in chain:
            if set(member) != {'pid', 'birth', 'ppid', 'session', 'exe', 'children'} or any(type(member[key]) is not int or not 0 < member[key] <= 2147483647 for key in ('pid', 'ppid', 'session')) or not isinstance(member['birth'], str) or not member['birth'].isascii() or not member['birth'].isdecimal() or not isinstance(member['exe'], str) or not member['exe'].startswith('/') or len(member['exe']) > 4096:
                return False
        if len({member['pid'] for member in chain}) != len(chain) or any(member['ppid'] != following['pid'] for member, following in zip(chain, chain[1:])) or {key: chain[-1][key] for key in owner} != owner or not any({key: member[key] for key in launcher} == launcher for member in chain[:-1]):
            return False
        if not isinstance(row['interpreterExe'], str) or row['interpreterExe'] != interpreter_exe() or chain[0]['exe'] != row['interpreterExe'] or any(member['session'] == owner['session'] for member in chain[:-1]):
            return False
        if chain[-1]['children'] is not None:
            return False
        for index, member in enumerate(chain[:-1]):
            expected_children = [] if index == 0 else [chain[index - 1]['pid']]
            if member['children'] != expected_children or not isinstance(member['children'], list) or any(type(pid) is not int or not 0 < pid <= 2147483647 for pid in member['children']):
                return False
        wrappers = chain[1:-1]
        if any(member['exe'] not in ('/usr/bin/sudo', '/usr/bin/timeout') for member in wrappers) or not {'/usr/bin/sudo', '/usr/bin/timeout'} <= {member['exe'] for member in wrappers}:
            return False
        if not terminal:
            return True
        original = row['daemonBefore']
        if set(original) != {'id', 'pid', 'birth', 'exe', 'socketInode'} or original != row['daemonAfter'] or not isinstance(original['id'], str) or not original['id'] or type(original['socketInode']) is not int or original['socketInode'] <= 0 or {key: original[key] for key in daemon} != daemon:
            return False
        for value in (original, row['daemonAfter']):
            if type(value['socketInode']) is not int or value['socketInode'] <= 0 or type(value['pid']) is not int or not 0 < value['pid'] <= 2147483647 or not isinstance(value['birth'], str) or not value['birth'].isascii() or not value['birth'].isdecimal() or not isinstance(value['exe'], str) or not value['exe'].startswith('/') or len(value['exe']) > 4096:
                return False
        expected = [{'pid': member['pid'], 'birth': member['birth'], 'absent': True} for member in chain[:-1]]
        if row['absence'] != expected or any(type(member['pid']) is not int or member['absent'] is not True for member in row['absence']):
            return False
        return True
    except (KeyError, TypeError, ValueError, OSError):
        return False


def cleanup(receipt):
    """Validate observed process/resource closure, never infer SDK object disposal."""
    try:
        if set(receipt) != {'daemon', 'baseline', 'final', 'admitted', 'readerJoined', 'readerErrors',
                            'processes', 'resources', 'foreignEvents', 'limitExceeded', 'fences', 'fencerJoined', 'firstFailure', 'daemonProbes'}:
            return False
        if not receipt['daemon'] or receipt['admitted'] is not True or receipt['readerJoined'] is not True or receipt['fencerJoined'] is not True or receipt['firstFailure'] is not None or receipt['readerErrors'] or receipt['foreignEvents'] or receipt['limitExceeded']:
            return False
        daemon = receipt['daemon']
        if set(daemon) != {'id', 'pid', 'birth', 'exe', 'socketInode'} or not daemon['id'] or type(daemon['pid']) is not int or daemon['pid'] <= 0 or not daemon['birth'] or not daemon['exe'] or type(daemon['socketInode']) is not int or daemon['socketInode'] <= 0:
            return False
        probes = receipt['daemonProbes']
        if not isinstance(probes, list) or len(probes) < 2 or any(not probe_receipt(row) or row['daemonBefore'] != daemon for row in probes):
            return False
        fences = receipt['fences']
        if not fences:
            return False
        for index, fence in enumerate(fences):
            if set(fence) != {'startNano', 'endNano', 'eventCount', 'streamSha256', 'replaySha256', 'success', 'historyCount', 'historyOldestNano', 'anchor', 'anchorPresent', 'initialPrefixComplete', 'nextAnchor'} or fence['success'] is not True or type(fence['startNano']) is not int or type(fence['endNano']) is not int or fence['startNano'] <= 0 or fence['endNano'] <= fence['startNano'] or type(fence['eventCount']) is not int or not 0 <= fence['eventCount'] < 200 or not identity(fence['streamSha256']) or fence['streamSha256'] != fence['replaySha256'] or (index > 0 and fence['startNano'] != fences[index - 1]['endNano']):
                return False
            if type(fence['historyCount']) is not int or fence['historyCount'] < fence['eventCount'] or fence['anchorPresent'] is not True or type(fence['initialPrefixComplete']) is not bool:
                return False
            if fence['anchor'] is None:
                if fence['initialPrefixComplete'] is not True or (fence['historyCount'] >= 256 and (fence['historyOldestNano'] is None or fence['historyOldestNano'] > fences[0]['startNano'])):
                    return False
            elif set(fence['anchor']) != {'Type', 'Action', 'timeNano', 'id'} or fence['anchor']['timeNano'] > fence['startNano']:
                return False
            if index > 0 and fence['anchor'] != fences[index - 1]['nextAnchor']:
                return False
            if fence['eventCount'] == 0:
                if fence['nextAnchor'] != fence['anchor']:
                    return False
            elif set(fence['nextAnchor']) != {'Type', 'Action', 'timeNano', 'id'} or not fence['startNano'] < fence['nextAnchor']['timeNano'] <= fence['endNano']:
                return False
        before, after = receipt['baseline'], receipt['final']
        census_keys = {'daemon', 'success', 'containers', 'volumes', 'networks'}
        if set(before) != census_keys or set(after) != census_keys:
            return False
        if before['daemon'] != receipt['daemon'] or after['daemon'] != receipt['daemon'] or before['success'] is not True or after['success'] is not True:
            return False
        for kind in ('containers', 'volumes', 'networks'):
            if len(set(before[kind])) != len(before[kind]) or len(set(after[kind])) != len(after[kind]) or set(before[kind]) != set(after[kind]):
                return False
        if not receipt['processes']:
            return False
        for process in receipt['processes']:
            if set(process) != {'pid', 'birth', 'exe', 'session', 'exitCode', 'joined', 'remaining', 'name', 'logSha256', 'argumentsSha256', 'firstFailure', 'cleanupErrors', 'settlement', 'settlementStarted', 'settlementDeadline'} or type(process['pid']) is not int or process['pid'] <= 0 or not process['birth'] or not process['exe'] or type(process['session']) is not int or process['session'] != process['pid'] or type(process['exitCode']) is not int or process['exitCode'] != 0 or process['joined'] is not True or process['firstFailure'] is not None or process['cleanupErrors'] or process['settlement'] or process['remaining'] or not process['name'] or not identity(process['logSha256']) or not identity(process['argumentsSha256']):
                return False
            if type(process['settlementStarted']) not in (int, float) or process['settlementStarted'] <= 0 or process['settlementDeadline'] != process['settlementStarted'] + 30:
                return False
        ids = set()
        for resource in receipt['resources']:
            if set(resource) != {'kind', 'id', 'createdNano', 'terminalNano', 'inspect', 'session', 'daemon'}:
                return False
            kind, key = resource['kind'], resource['id']
            if kind not in ('containers', 'volumes', 'networks') or key in ids or key in before[kind] or key in after[kind] or resource['daemon'] != receipt['daemon'] or not resource['session'] or type(resource['createdNano']) is not int or type(resource['terminalNano']) is not int or resource['terminalNano'] <= resource['createdNano'] or resource['createdNano'] <= 0:
                return False
            if not fences[0]['startNano'] < resource['createdNano'] < resource['terminalNano'] <= fences[-1]['endNano']:
                return False
            ids.add(key)
            original = resource['inspect']
            if original['id'] != key or original['kind'] != kind or not original['generation']:
                return False
            if kind == 'containers':
                policy = json.loads(pathlib.Path(__file__).with_name('graph-actor-policy.json').read_text())
                if set(original) != {'id', 'kind', 'generation', 'image', 'configImage', 'labels', 'volumes'} or not identity(key) or not identity(original['image'].removeprefix('sha256:')) or original['configImage'] not in policy['images'] or original['labels'].get(policy['sessionLabel']) != resource['session'] or any(original['labels'].get(name) != expected for name, expected in policy['requiredLabels'].items()):
                    return False
                reaper = '00000000-0000-0000-0000-000000000000' if original['configImage'] == policy['ryukImage'] else resource['session']
                if original['labels'].get(policy['reaperLabel']) != reaper:
                    return False
                uuid.UUID(resource['session'])
            elif kind == 'volumes':
                if set(original) != {'id', 'kind', 'generation'}:
                    return False
                owners = [item for item in receipt['resources'] if item['kind'] == 'containers' and key in item['inspect']['volumes']]
                if not owners or {item['session'] for item in owners} != {resource['session']}:
                    return False
            else:
                return False  # Frozen original producer fixtures create no custom network.
        return True
    except (KeyError, TypeError, ValueError, AttributeError):
        return False
