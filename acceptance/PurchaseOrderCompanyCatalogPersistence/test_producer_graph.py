"""Causal metadata controls only: never starts SDK, Docker or a service."""
import copy
import json
import pathlib
import tempfile
import unittest
from unittest import mock
import uuid
import xml.etree.ElementTree as ET

import graph_evidence as evidence
import graph_receipt as receipt
import graph_supervisor as supervisor


def closure():
    daemon = {'id': 'synthetic-original-daemon', 'pid': 50, 'birth': '70', 'exe': '/synthetic/dockerd', 'socketInode': 80}
    before = {'daemon': daemon, 'success': True, 'containers': [], 'volumes': [], 'networks': []}
    process = {'pid': 123, 'birth': '456', 'exe': '/synthetic/dotnet', 'session': 123,
               'exitCode': 0, 'joined': True, 'remaining': [], 'name': 'full-native',
               'logSha256': 'a' * 64, 'argumentsSha256': 'b' * 64,
               'firstFailure': None, 'cleanupErrors': [], 'settlement': [], 'settlementStarted': 1.0, 'settlementDeadline': 31.0}
    original = {'kind': 'containers', 'id': 'c' * 64, 'createdNano': 10, 'terminalNano': 20,
                'session': '12345678-1234-1234-1234-123456789abc', 'daemon': before['daemon'],
                'inspect': {'kind': 'containers', 'id': 'c' * 64, 'generation': '2026-10-08T00:00:00Z',
                            'image': 'sha256:' + 'd' * 64, 'configImage': 'postgres:18-alpine',
                            'labels': {'org.testcontainers.session-id': '12345678-1234-1234-1234-123456789abc',
                                       'org.testcontainers.resource-reaper-session': '12345678-1234-1234-1234-123456789abc',
                                       'org.testcontainers': 'true', 'org.testcontainers.lang': 'dotnet'}, 'volumes': []}}
    return {'daemon': before['daemon'], 'baseline': before, 'final': copy.deepcopy(before),
            'admitted': True, 'readerJoined': True, 'readerErrors': [], 'processes': [process],
            'resources': [original], 'foreignEvents': [], 'limitExceeded': False, 'fencerJoined': True,
            'firstFailure': None,
            'fences': [{'startNano': 1, 'endNano': 30, 'eventCount': 2, 'streamSha256': 'e' * 64,
                        'replaySha256': 'e' * 64, 'historyCount': 2, 'historyOldestNano': 10,
                        'anchor': None, 'anchorPresent': True, 'initialPrefixComplete': True,
                        'nextAnchor': {'Type': 'container', 'Action': 'destroy', 'timeNano': 20, 'id': 'c' * 64}, 'success': True}]}


class ProducerGraphTests(unittest.TestCase):
    def test_foreign_session_does_not_read_executable(self):
        def stat(entry):
            pid = int(entry.parent.name)
            return f'{pid} (synthetic) ' + ' '.join(['S', '1', str(pid), str(pid)] + ['0'] * 15 + ['456'])
        def executable(entry):
            if entry.parent.name == '789':
                raise PermissionError(13, 'synthetic foreign denial', str(entry))
            return '/synthetic/dotnet'
        with mock.patch.object(supervisor.pathlib.Path, 'iterdir', return_value=iter([pathlib.Path('/proc/123'), pathlib.Path('/proc/789')])), \
             mock.patch.object(supervisor.pathlib.Path, 'read_text', stat), \
             mock.patch.object(supervisor.os, 'readlink', side_effect=executable) as reads:
            self.assertEqual([{'pid': 123, 'birth': '456', 'session': 123, 'exe': '/synthetic/dotnet'}], supervisor.group_members(123))
        self.assertEqual([mock.call(pathlib.Path('/proc/123/exe'))], reads.call_args_list)

    def test_unknown_membership_and_owned_executable_denials_remain_hard_failures(self):
        text = '123 (synthetic) ' + ' '.join(['S', '1', '123', '123'] + ['0'] * 15 + ['456'])
        for operation in ('process-stat-read', 'process-exe-readlink'):
            error = PermissionError(13, 'private message', '/private/unretained')
            with mock.patch.object(supervisor.pathlib.Path, 'read_text', return_value=text,
                                   side_effect=error if operation == 'process-stat-read' else None), \
                 mock.patch.object(supervisor.os, 'readlink', side_effect=error):
                with self.assertRaises(PermissionError) as observed:
                    supervisor.process_identity(123, expected_session=123)
            self.assertIs(error, observed.exception)
            self.assertEqual({'operation': operation, 'errno': 13,
                              'path': '/proc/123/' + ('stat' if operation.endswith('stat-read') else 'exe')},
                             supervisor.read_failure('synthetic', error)['failedRead'])

    def test_daemon_actual_read_site_preserves_original_exception_and_safe_coordinates(self):
        error = PermissionError(13, 'private message', '/private/unretained')
        def read(entry):
            return '50' if entry.as_posix() == '/var/run/docker.pid' else '50 (synthetic) ' + ' '.join(['S'] * 19 + ['70'])
        docker = supervisor.Docker()
        with mock.patch.object(supervisor.pathlib.Path, 'read_text', read), \
             mock.patch.object(docker, 'get', return_value={'ID': 'synthetic-daemon'}), \
             mock.patch.object(supervisor.os, 'readlink', side_effect=error):
            with self.assertRaises(PermissionError) as observed:
                docker.daemon()
        self.assertIs(error, observed.exception)
        self.assertEqual({'gate': 'observer-startup', 'category': 'PermissionError',
                          'failedRead': {'operation': 'daemon-exe-readlink', 'errno': 13, 'path': '/proc/50/exe'}},
                         supervisor.read_failure('observer-startup', error))

    def test_read_coordinates_reject_unknown_private_or_fabricated_metadata(self):
        for operation, path in (('foreign', '/proc/50/exe'), ('daemon-exe-readlink', '/private/path'),
                                ('process-stat-read', '/proc/../stat'), ('process-stat-read', '/proc/0/stat')):
            callback = mock.Mock()
            with self.assertRaises(ValueError):
                supervisor.read_site(operation, path, callback)
            callback.assert_not_called()
        error = PermissionError(13, 'synthetic')
        for detail in ({'operation': 'foreign', 'errno': 13, 'path': '/proc/50/exe'},
                       {'operation': 'daemon-exe-readlink', 'errno': 13, 'path': '/private/path'},
                       {'operation': 'daemon-exe-readlink', 'errno': 1, 'path': '/proc/50/exe'},
                       {'operation': 'daemon-exe-readlink', 'errno': 13, 'path': '/proc/50/exe', 'body': 'private'}):
            error.graph_failed_read = detail
            self.assertEqual({'gate': 'synthetic', 'category': 'PermissionError'}, supervisor.read_failure('synthetic', error))

    def test_failed_read_startup_preserves_first_cause_after_secondary_census_failure(self):
        error = PermissionError(13, 'synthetic original denial', '/private/unretained')
        with self.assertRaises(PermissionError):
            supervisor.read_site('daemon-exe-readlink', '/proc/50/exe', mock.Mock(side_effect=error))
        docker = mock.Mock()
        docker.census.side_effect = [error, OSError(5, 'synthetic secondary failure')]
        with tempfile.TemporaryDirectory() as temporary, \
             mock.patch.object(supervisor, 'Docker', return_value=docker), \
             mock.patch.object(supervisor, 'DockerConnection') as connection, \
             mock.patch.object(supervisor.threading, 'Thread') as thread:
            with self.assertRaises(supervisor.AdmissionFailure) as observed:
                supervisor.Phase({}, temporary)
            result = json.loads((pathlib.Path(temporary) / 'cleanup.json').read_text())
        self.assertIs(error, observed.exception.__cause__)
        self.assertEqual(supervisor.read_failure('observer-startup', error), result['firstFailure'])
        self.assertEqual([], result['processes'])
        self.assertFalse(result['admitted'])
        self.assertFalse(evidence.cleanup(result))
        thread.assert_not_called()
        connection.return_value.stop.assert_called_once()

    def test_admission_writes_only_owned_evidence_after_all_three_producer_readbacks(self):
        import producer_graph
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            directory = root / 'owned-evidence'
            directory.mkdir()
            private = root / 'po-producer-graph-private'
            rows = []
            for producer in ('Document', 'File', 'Employee'):
                original = private / producer
                original.mkdir(parents=True)
                closed = {'processes': []}
                (original / 'cleanup.json').write_text(json.dumps(closed))
                producer_directory = root / '.dependencies' / ('Legacy.Maliev.' + producer + 'Service')
                producer_directory.mkdir(parents=True)
                policy = None
                if producer == 'Document':
                    policy = dict.fromkeys(('policySha256', 'compiledProofSha256', 'controlsSha256'), 'a' * 64)
                    retained = original / 'receipt-evidence'
                    retained.mkdir()
                    (retained / 'contract-applicability-acceptance.json').write_text(json.dumps(policy))
                rows.append({'producer': producer, 'native': {}, 'focus': {}, 'assets': [], 'coverage': {},
                             'audit': {}, 'applicability': policy, 'cleanup': closed, 'binaries': []})
            (directory / 'producer-graph.json').write_text(json.dumps({'producers': rows, 'sourceTrees': [], 'sharedBinaries': []}))
            environment = {'CANDIDATE_HEAD': '1' * 40, 'GITHUB_RUN_ID': '123', 'GITHUB_RUN_ATTEMPT': '1', 'RUNNER_TEMP': temporary}
            # Upstream content checks are isolated here; this control exercises real admission-path IO.
            with mock.patch.dict('os.environ', environment), mock.patch.object(receipt, 'complete', return_value=True), \
                 mock.patch.object(producer_graph, 'git', return_value='1' * 40), mock.patch.object(producer_graph, 'verify_sources', return_value=[]), \
                 mock.patch.object(producer_graph, 'assets', return_value=[]), mock.patch.object(receipt, 'trx', return_value={}), \
                 mock.patch.object(receipt, 'coverage', return_value={}), mock.patch.object(receipt, 'audit', return_value={}), \
                 mock.patch.object(receipt, 'digest', return_value='a' * 64):
                self.assertTrue(receipt.join(directory, root, admit=True))
                self.assertTrue(receipt.join(directory, root))
                self.assertFalse(receipt.join(directory, root, admit=True))
            self.assertTrue((directory / 'producer-graph-admission.json').is_file())
            self.assertEqual([], list((root / '.dependencies').rglob('producer-graph-admission.json')))

    def test_leader_exit_with_surviving_child_settles_original_generation_and_preserves_failure(self):
        original = {'pid': 123, 'birth': '456', 'session': 123, 'exe': '/synthetic/dotnet'}
        child = {'pid': 789, 'birth': '900', 'session': 123, 'exe': '/synthetic/testhost'}
        members = [child]
        process = mock.Mock(pid=123)
        process.wait.return_value = 0
        process.poll.return_value = 0
        def send(member, session, sig):
            self.assertEqual(child, member)
            self.assertEqual(123, session)
            members.clear()
        with tempfile.TemporaryDirectory() as temporary:
            phase = supervisor.Phase.__new__(supervisor.Phase)
            phase.private = pathlib.Path(temporary)
            phase.errors, phase.foreign, phase.processes, phase.commands = [], [], [], []
            phase.limit, phase.first_failure = False, None
            with mock.patch.object(supervisor.subprocess, 'Popen', return_value=process), \
                 mock.patch.object(supervisor, 'process_identity', return_value=original), \
                 mock.patch.object(supervisor, 'group_members', side_effect=lambda session: list(members)), \
                 mock.patch.object(supervisor.time, 'monotonic', return_value=31.123), \
                 mock.patch.object(supervisor.signal, 'SIGKILL', 9, create=True), \
                 mock.patch.object(supervisor, 'signal_original', side_effect=send) as signal_original:
                with self.assertRaises(supervisor.CommandFailure):
                    phase.run(['synthetic-sdk'], temporary, {}, 'full-native')
            row = phase.processes[0]
            self.assertEqual(0, row['exitCode'])
            self.assertTrue(row['joined'])
            self.assertEqual([], row['remaining'])
            self.assertIn('descendants remain', row['firstFailure']['reason'])
            self.assertEqual(row['firstFailure'], phase.first_failure)
            self.assertEqual(row['settlementStarted'] + 30, row['settlementDeadline'])
            signal_original.assert_called_once()
            self.assertTrue(row['settlement'][0]['sent'])

    def test_pid_generation_change_refuses_signal_and_original_deadline_is_not_restarted(self):
        member = {'pid': 123, 'birth': '456', 'session': 123, 'exe': '/synthetic/dotnet'}
        with mock.patch.object(supervisor, 'process_identity', return_value={**member, 'birth': 'foreign'}), \
             mock.patch.object(supervisor.os, 'pidfd_open', create=True) as open_descriptor:
            with self.assertRaises(ValueError):
                supervisor.signal_original(member, 123, supervisor.signal.SIGTERM)
            open_descriptor.assert_not_called()
        with mock.patch.object(supervisor, 'process_identity', side_effect=[member, {**member, 'birth': 'foreign'}]), \
             mock.patch.object(supervisor.os, 'pidfd_open', return_value=42, create=True), \
             mock.patch.object(supervisor.os, 'close') as close_descriptor, \
             mock.patch.object(supervisor.signal, 'pidfd_send_signal', create=True) as send_descriptor:
            with self.assertRaises(ValueError):
                supervisor.signal_original(member, 123, supervisor.signal.SIGTERM)
            send_descriptor.assert_not_called()
            close_descriptor.assert_called_once_with(42)
        row = {**member, 'settlementStarted': 1, 'settlementDeadline': 31, 'settlement': [],
               'cleanupErrors': [], 'firstFailure': {'gate': 'original', 'category': 'TimeoutExpired'}}
        process = mock.Mock()
        process.poll.return_value = 0
        with mock.patch.object(supervisor, 'group_members', return_value=[member]), \
             mock.patch.object(supervisor.time, 'monotonic', return_value=100), \
             mock.patch.object(supervisor.signal, 'SIGKILL', 9, create=True), \
             mock.patch.object(supervisor, 'signal_original') as send:
            supervisor.settle_process(row, process)
            send.assert_not_called()
        self.assertEqual(31, row['settlementDeadline'])
        self.assertEqual('TimeoutExpired', row['firstFailure']['category'])
        self.assertEqual([member], row['remaining'])
        self.assertIn('OriginalProcessSessionUnsettled', row['cleanupErrors'])

    def test_replacement_original_leader_is_rejected_before_pidfd_or_signal(self):
        original = {'pid': 123, 'birth': '456', 'session': 123, 'exe': '/synthetic/dotnet'}
        replacement = {**original, 'birth': 'foreign'}
        child = {'pid': 789, 'birth': '900', 'session': 123, 'exe': '/synthetic/testhost'}
        row = {**original, 'settlementStarted': None, 'settlementDeadline': None, 'settlement': [],
               'cleanupErrors': [], 'firstFailure': {'gate': 'original', 'category': 'TimeoutExpired'}}
        process = mock.Mock()
        process.poll.return_value = 0
        with mock.patch.object(supervisor, 'group_members', return_value=[child, replacement]), \
             mock.patch.object(supervisor.time, 'monotonic', return_value=31.123), \
             mock.patch.object(supervisor.signal, 'SIGKILL', 9, create=True), \
             mock.patch.object(supervisor.os, 'pidfd_open', create=True) as open_descriptor, \
             mock.patch.object(supervisor.signal, 'pidfd_send_signal', create=True) as send_descriptor:
            supervisor.settle_process(row, process)
            open_descriptor.assert_not_called()
            send_descriptor.assert_not_called()
        self.assertIn('OriginalLeaderGenerationChanged', row['cleanupErrors'])
        self.assertEqual([], row['settlement'])
        self.assertEqual([child, replacement], row['remaining'])
        self.assertEqual('TimeoutExpired', row['firstFailure']['category'])
        self.assertEqual(row['settlementStarted'] + 30, row['settlementDeadline'])

    def test_closed_original_session_is_never_reenrolled(self):
        row = closure()['processes'][0]
        process = mock.Mock()
        process.poll.return_value = 0
        phase = supervisor.Phase.__new__(supervisor.Phase)
        phase.commands = [(row, process)]
        phase.first_failure = None
        phase.baseline = closure()['baseline']
        phase.resources, phase.foreign = {}, []
        with mock.patch.object(supervisor, 'group_members') as enumerate_members, \
             mock.patch.object(supervisor, 'signal_original') as send, \
             mock.patch.object(phase, '_final_census', return_value=phase.baseline), \
             mock.patch.object(phase, '_stop_readers'), \
             mock.patch.object(phase, '_write_receipt', return_value=closure()):
            supervisor.settle_process(row, process)
            phase.settle()
            enumerate_members.assert_not_called()
            send.assert_not_called()
        self.assertIsNone(row['firstFailure'])
        self.assertEqual([], row['cleanupErrors'])

    def test_observer_startup_failure_retains_original_close_join_census_and_nonpass_receipt(self):
        for alive in (False, True):
            reader = mock.Mock()
            reader.is_alive.return_value = alive
            event = mock.Mock()
            event.wait.return_value = False
            event.is_set.return_value = False
            connection = mock.Mock()
            docker = mock.Mock()
            docker.census.return_value = closure()['baseline']
            with tempfile.TemporaryDirectory() as temporary, \
                 mock.patch.object(supervisor, 'Docker', return_value=docker), \
                 mock.patch.object(supervisor, 'DockerConnection', return_value=connection), \
                 mock.patch.object(supervisor.threading, 'Thread', return_value=reader), \
                 mock.patch.object(supervisor.threading, 'Event', return_value=event):
                with self.assertRaises(supervisor.AdmissionFailure):
                    supervisor.Phase({}, temporary)
                observed = json.loads((pathlib.Path(temporary) / 'cleanup.json').read_text())
            connection.stop.assert_called_once()
            reader.join.assert_called_once_with(35)
            self.assertEqual(2, docker.census.call_count)
            self.assertEqual([], observed['processes'])
            self.assertFalse(observed['admitted'])
            self.assertEqual(not alive, observed['readerJoined'])
            self.assertEqual('observer-startup', observed['firstFailure']['gate'])
            self.assertFalse(evidence.cleanup(observed))

    def test_native_multiset_preserves_duplicate_display_names_and_distinct_method_identity(self):
        inventory = [{'testName': 'duplicate', 'className': 'Fixture', 'method': 'Original'}] * 2
        root = ET.Element('TestRun', xmlns=evidence.NS['t'], id=str(uuid.uuid4()))
        definitions, results = ET.SubElement(root, 'TestDefinitions'), ET.SubElement(root, 'Results')
        entries = ET.SubElement(root, 'TestEntries')
        for item in inventory:
            test, execution = str(uuid.uuid4()), str(uuid.uuid4())
            definition = ET.SubElement(definitions, 'UnitTest', id=test, name=item['testName'])
            ET.SubElement(definition, 'Execution', id=execution)
            ET.SubElement(definition, 'TestMethod', className=item['className'], name=item['method'])
            ET.SubElement(results, 'UnitTestResult', testId=test, executionId=execution, testName=item['testName'], outcome='Passed')
            ET.SubElement(entries, 'TestEntry', testId=test, executionId=execution)
        summary = ET.SubElement(root, 'ResultSummary', outcome='Completed')
        ET.SubElement(summary, 'Counters', total='2', executed='2', passed='2', **dict.fromkeys(evidence.ZERO, '0'))
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / 'own.trx'
            ET.ElementTree(root).write(path)
            self.assertEqual(2, evidence.trx(path, inventory)['count'])
            for mutation in ('drop', 'foreign_method', 'reuse_execution', 'nonpass', 'pending', 'missing_entries', 'foreign_entry', 'duplicate_entry'):
                changed = copy.deepcopy(root)
                if mutation == 'drop':
                    changed.find('Results').remove(changed.find('Results')[0])
                elif mutation == 'foreign_method':
                    changed.find('TestDefinitions')[0].find('TestMethod').set('name', 'Foreign')
                elif mutation == 'reuse_execution':
                    changed.find('Results')[1].set('executionId', changed.find('Results')[0].get('executionId'))
                elif mutation == 'nonpass':
                    changed.find('Results')[0].set('outcome', 'NotExecuted')
                elif mutation == 'pending':
                    changed.find('ResultSummary/Counters').set('pending', '1')
                elif mutation == 'missing_entries':
                    changed.remove(changed.find('TestEntries'))
                elif mutation == 'foreign_entry':
                    changed.find('TestEntries')[0].set('executionId', str(uuid.uuid4()))
                else:
                    changed.find('TestEntries').append(copy.deepcopy(changed.find('TestEntries')[0]))
                ET.ElementTree(changed).write(path)
                with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                    evidence.trx(path, inventory)

    def test_raw_coverage_retains_generated_lines_and_rejects_empty_or_below_floor(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / 'coverage.cobertura.xml'
            def xml(hits, name='Owned.Api'):
                lines = ''.join(f'<line number="{index + 1}" hits="{hit}"/>' for index, hit in enumerate(hits))
                path.write_text(f'<coverage><packages><package name="{name}"><classes><class filename="generated.g.cs"><lines>{lines}</lines></class></classes></package></packages></coverage>')
            xml([1, 1, 1, 1, 0])
            self.assertEqual(5, evidence.coverage([path], ['Owned.Api'])['assemblies'][0]['valid'])
            for hits in ([], [1, 1, 1, 0, 0]):
                xml(hits)
                with self.assertRaises(ValueError):
                    evidence.coverage([path], ['Owned.Api'])

    def test_contract_only_application_cannot_become_numerical_pass_or_hide_executable_lines(self):
        value = {'rawSha256': 'a' * 64, 'exclusions': [], 'assemblies': [
            {'assembly': 'Legacy.Maliev.DocumentService.' + name, 'valid': 0 if name == 'Application' else 10,
             'covered': 0 if name == 'Application' else 8, 'percent': None if name == 'Application' else 80,
             'applicability': 'contract-only' if name == 'Application' else 'executable'}
            for name in ('Api', 'Application', 'Domain', 'Rendering')]}
        self.assertTrue(receipt.observed_coverage(value, 'Document'))
        for mutation in ('numerical', 'executable', 'excluded', 'missing'):
            changed = copy.deepcopy(value)
            if mutation == 'numerical':
                changed['assemblies'][1]['percent'] = 100
            elif mutation == 'executable':
                changed['assemblies'][1]['valid'] = 1
            elif mutation == 'excluded':
                changed['exclusions'] = ['generated.g.cs']
            else:
                changed['assemblies'].pop()
            with self.subTest(mutation=mutation):
                self.assertFalse(receipt.observed_coverage(changed, 'Document'))

    def test_audit_rejects_missing_framework_project_problem_and_vulnerability(self):
        value = {'projects': [{'path': '/synthetic/Owned.csproj', 'frameworks': [{'framework': 'net10.0'}]}]}
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / 'audit.json'
            path.write_text(json.dumps(value))
            self.assertEqual(['Owned.csproj'], evidence.audit(path, ['Owned.csproj'])['projectNames'])
            for mutation in ('framework', 'project', 'problem', 'vulnerability'):
                changed = copy.deepcopy(value)
                if mutation == 'framework':
                    changed['projects'][0]['frameworks'] = []
                elif mutation == 'project':
                    changed['projects'][0]['path'] = 'Foreign.csproj'
                elif mutation == 'problem':
                    changed['problems'] = ['unavailable']
                else:
                    changed['projects'][0]['frameworks'][0]['transitivePackages'] = [{'vulnerabilities': [{'severity': 'high'}]}]
                path.write_text(json.dumps(changed))
                with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                    evidence.audit(path, ['Owned.csproj'])

    def test_original_resource_requires_admission_identity_terminal_fresh_absence_and_reader_process_join(self):
        original = closure()
        self.assertTrue(evidence.cleanup(original))
        for mutation in ('unadmitted', 'missing_inspect', 'missing_terminal', 'remaining_id', 'daemon',
                         'reader', 'reader_error', 'descendant', 'process_birth', 'foreign', 'overflow', 'baseline_adoption',
                         'fence_gap', 'fence_digest', 'fence_overflow', 'fence_unsettled', 'anchor_lost', 'truncated_initial_prefix'):
            changed = copy.deepcopy(original)
            if mutation == 'unadmitted':
                changed['admitted'] = False
            elif mutation == 'missing_inspect':
                changed['resources'][0]['inspect'] = {}
            elif mutation == 'missing_terminal':
                changed['resources'][0]['terminalNano'] = 0
            elif mutation == 'remaining_id':
                changed['final']['containers'].append('c' * 64)
            elif mutation == 'daemon':
                changed['final']['daemon'] = 'foreign'
            elif mutation == 'reader':
                changed['readerJoined'] = False
            elif mutation == 'reader_error':
                changed['readerErrors'].append('gap')
            elif mutation == 'descendant':
                changed['processes'][0]['remaining'].append({'pid': 456, 'birth': '999'})
            elif mutation == 'process_birth':
                changed['processes'][0]['birth'] = ''
            elif mutation == 'foreign':
                changed['foreignEvents'].append('foreign-create')
            elif mutation == 'overflow':
                changed['limitExceeded'] = True
            elif mutation == 'baseline_adoption':
                changed['baseline']['containers'].append('c' * 64)
                changed['final']['containers'].append('c' * 64)
            elif mutation == 'fence_gap':
                changed['fences'].append({**changed['fences'][0], 'startNano': 31, 'endNano': 40})
            elif mutation == 'fence_digest':
                changed['fences'][0]['replaySha256'] = 'f' * 64
            elif mutation == 'fence_overflow':
                changed['fences'][0]['eventCount'] = 200
            elif mutation == 'anchor_lost':
                changed['fences'][0]['anchorPresent'] = False
            elif mutation == 'truncated_initial_prefix':
                changed['fences'][0]['historyCount'] = 256
                changed['fences'][0]['historyOldestNano'] = 2
            else:
                changed['fencerJoined'] = False
            with self.subTest(mutation=mutation):
                self.assertFalse(evidence.cleanup(changed))

    def test_candidate_flag_or_stale_graph_cannot_open_business_admission(self):
        self.assertFalse(receipt.complete({'graphComplete': True, 'acceptedProducerGraph': True}, {}, {}, {}))
        self.assertFalse(receipt.join('/synthetic/missing', '/synthetic/missing'))

    def test_whole_receipt_requires_fresh_owner_every_native_lane_policy_security_and_compiled_graph(self):
        inventories = json.loads((receipt.HERE / 'graph-native-inventory.json').read_text())
        pins = json.loads((receipt.HERE / 'candidate-pins.json').read_text())['pins']
        owner = {'candidateHead': '1' * 40, 'executedSource': '1' * 40, 'runId': '123', 'runAttempt': '1'}
        extra = {'Legacy.Maliev.Intranet': '3f5f7542c93cb085757130971c4fc7cf61043f01',
                 'Legacy.Maliev.Workflows': '0159e67a033712a7120d52173819e8bde214cf6e',
                 'Legacy.Maliev.Workflows.Security': 'e3a6093324a24968876782153286f52db8b29fd8'}
        graph = {'schema': 1, 'graphComplete': True, 'acceptedProducerGraph': False, **owner,
                 'pins': pins, 'sourceTrees': [{'repository': name, 'head': head, 'tree': '2' * 40} for name, head in {**pins, **extra}.items()],
                 'producers': [], 'sharedBinaries': [{'name': name + '.' + extension, 'bytes': 1, 'sha256': 'a' * 64}
                     for name in ('Legacy.Maliev.ServiceDefaults', 'Legacy.Maliev.CompatibilityContracts') for extension in ('dll', 'pdb')],
                 'failureCategory': None, 'failedProducer': None, 'failureCleanup': None, 'scope': 'synthetic metadata control'}
        def fresh_native(expected):
            return {'rawSha256': 'a' * 64, 'runId': str(uuid.uuid4()), 'count': expected['count'],
                    'rows': [{**item, 'testId': str(uuid.uuid4()), 'executionId': str(uuid.uuid4())} for item in expected['inventory']]}
        for producer in ('Document', 'File', 'Employee'):
            modules = ('Api', 'Application', 'Domain', 'Rendering' if producer == 'Document' else 'Data')
            projects = ['Legacy.Maliev.' + producer + 'Service.' + part for part in (*modules, 'Tests')]
            commands = ['gitleaks', 'jwt-scan', 'tree-scan', 'restore', 'build', 'format', 'package-audit']
            policy = None
            if producer == 'Document':
                commands = ['gitleaks-install', *commands, 'openapi-inputs', 'pristine-contract', 'mutation-CONCRETE', 'mutation-DEFAULT_METHOD', 'mutation-STATIC_METHOD', 'mutation-EXTRA_TYPE', 'mutation-RESOURCE', 'contract-controls', 'document-focus', 'full-native', 'test_receipt_evidence.py', 'test_document_application_proof.py', 'read-receipt-evidence.py', 'read-document-contract-applicability.py', 'original-coverage-policy']
                policy = {'schemaVersion': 'document-contract-applicability-acceptance/v1', 'policyActive': True,
                          'head': pins['Legacy.Maliev.DocumentService'], 'runId': owner['runId'], 'runAttempt': owner['runAttempt'],
                          'policySha256': 'b' * 64, 'compiledProofSha256': 'c' * 64, 'controlsSha256': 'd' * 64,
                          'applicationStatus': 'N/A contract-only', 'applicationNumericalPercent': None,
                          'applicationNumericalPassed': False, 'executableFloorsPassed': True, 'actualHttpPassed': 43,
                          'fourAssemblyNumericalAcceptance': False, 'applicabilityAcceptance': True, 'exclusions': [], 'deployed': False}
            else:
                commands += ['full-native', 'original-coverage-policy']
                if producer == 'Employee':
                    commands += ['original-scaffold-controls']
            closed = closure()
            closed['processes'] = [{**closed['processes'][0], 'name': name, 'pid': index + 100, 'session': index + 100} for index, name in enumerate(commands)]
            graph['producers'].append({'producer': producer, 'head': pins['Legacy.Maliev.' + producer + 'Service'],
                'native': fresh_native(inventories[producer]), 'focus': fresh_native(inventories['Document-focus']) if producer == 'Document' else None,
                'coverage': {'rawSha256': 'a' * 64, 'exclusions': [], 'assemblies': [
                    {'assembly': 'Legacy.Maliev.' + producer + 'Service.' + name,
                     'valid': 0 if producer == 'Document' and name == 'Application' else 10,
                     'covered': 0 if producer == 'Document' and name == 'Application' else 8,
                     'percent': None if producer == 'Document' and name == 'Application' else 80,
                     'applicability': 'contract-only' if producer == 'Document' and name == 'Application' else 'executable'} for name in modules]},
                'audit': {'rawSha256': 'a' * 64, 'projectNames': [name + '.csproj' for name in projects]},
                'assets': [{'path': name + '/obj/project.assets.json', 'sha256': 'a' * 64} for name in projects],
                'binaries': [{'name': 'Legacy.Maliev.' + producer + 'Service.' + name + '.' + extension, 'sha256': 'a' * 64, 'bytes': 1} for name in modules for extension in ('dll', 'pdb')],
                'applicability': policy, 'cleanup': closed})
        self.assertTrue(receipt.complete(graph, owner, inventories, pins))
        for mutation in ('stale_run', 'static_acceptance', 'missing_producer', 'lost_duplicate', 'shared_pin', 'missing_security',
                         'missing_asset', 'foreign_binary', 'numerical_application', 'scope_extra', 'stale_focus', 'missing_pdb'):
            changed = copy.deepcopy(graph)
            if mutation == 'stale_run':
                changed['runId'] = '122'
            elif mutation == 'static_acceptance':
                changed['acceptedProducerGraph'] = True
            elif mutation == 'missing_producer':
                changed['producers'].pop()
            elif mutation == 'lost_duplicate':
                changed['producers'][1]['native']['rows'].pop()
            elif mutation == 'shared_pin':
                changed['pins'] = {**changed['pins'], 'Legacy.Maliev.ServiceDefaults': '3' * 40}
            elif mutation == 'missing_security':
                changed['producers'][0]['cleanup']['processes'].pop(1)
            elif mutation == 'missing_asset':
                changed['producers'][0]['assets'].pop()
            elif mutation == 'foreign_binary':
                changed['producers'][1]['binaries'][0]['name'] = 'foreign.dll'
            elif mutation == 'numerical_application':
                changed['producers'][0]['applicability']['fourAssemblyNumericalAcceptance'] = True
            elif mutation == 'scope_extra':
                changed['inventedGate'] = True
            elif mutation == 'stale_focus':
                changed['producers'][0]['focus']['runId'] = changed['producers'][0]['native']['runId']
            else:
                changed['sharedBinaries'].pop()
            with self.subTest(mutation=mutation):
                self.assertFalse(receipt.complete(changed, owner, inventories, pins))


if __name__ == '__main__':
    unittest.main()
