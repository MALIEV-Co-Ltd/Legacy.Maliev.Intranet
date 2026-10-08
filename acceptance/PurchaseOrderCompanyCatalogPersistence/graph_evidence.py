"""Independent readers for fresh producer evidence; no SDK or Docker allocation."""
from collections import Counter
import hashlib
import json
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
    document = json.loads(pathlib.Path(path).read_text())
    projects = document['projects']
    if document.get('problems') or not projects:
        raise ValueError('audit incomplete')
    names = set()
    for project in projects:
        if project.get('problems') or not project.get('frameworks'):
            raise ValueError('audit project incomplete')
        names.add(pathlib.Path(project['path']).name)
        for framework in project['frameworks']:
            if not framework.get('framework') or framework.get('problems'):
                raise ValueError('audit framework incomplete')
            for key in ('topLevelPackages', 'transitivePackages'):
                for package in framework.get(key, []):
                    if package.get('vulnerabilities') or package.get('problems'):
                        raise ValueError('vulnerable or incomplete package')
    if not set(expected_projects).issubset(names):
        raise ValueError('missing audited project')
    return {'rawSha256': digest(path), 'projectNames': sorted(names)}


def cleanup(receipt):
    """Validate observed process/resource closure, never infer SDK object disposal."""
    try:
        if set(receipt) != {'daemon', 'baseline', 'final', 'admitted', 'readerJoined', 'readerErrors',
                            'processes', 'resources', 'foreignEvents', 'limitExceeded', 'fences', 'fencerJoined', 'firstFailure'}:
            return False
        if not receipt['daemon'] or receipt['admitted'] is not True or receipt['readerJoined'] is not True or receipt['fencerJoined'] is not True or receipt['firstFailure'] is not None or receipt['readerErrors'] or receipt['foreignEvents'] or receipt['limitExceeded']:
            return False
        daemon = receipt['daemon']
        if set(daemon) != {'id', 'pid', 'birth', 'exe', 'socketInode'} or not daemon['id'] or type(daemon['pid']) is not int or daemon['pid'] <= 0 or not daemon['birth'] or not daemon['exe'] or type(daemon['socketInode']) is not int or daemon['socketInode'] <= 0:
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
