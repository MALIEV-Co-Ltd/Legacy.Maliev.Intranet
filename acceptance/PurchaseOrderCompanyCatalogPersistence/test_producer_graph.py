"""Causal metadata controls only: never starts SDK, Docker or a service."""
import copy
import contextlib
import io
import json
import pathlib
import os
import tempfile
import unittest
from unittest import mock
import uuid
import xml.etree.ElementTree as ET

import graph_evidence as evidence
import graph_receipt as receipt
import graph_supervisor as supervisor
import daemon_exe_probe as probe
import producer_graph


@contextlib.contextmanager
def projected_links(fail=False):
    """Explicit actor-free link projection: no privileged Windows symlink or OS proof."""
    aliases = {}
    identities = {}
    original_symlink = pathlib.Path.is_symlink
    original_resolve = pathlib.Path.resolve
    original_unlink = pathlib.Path.unlink
    original_lstat = pathlib.Path.lstat
    def create(path, target, target_is_directory=False):
        if fail:
            raise OSError('synthetic unavailable symlink')
        aliases[path] = pathlib.Path(target)
        identities[path] = os.stat_result((0o120777, len(identities) + 100001, 1, 1, 0, 0, 0, 0, 0, 0))
    def resolve(path, strict=False):
        for alias in sorted(aliases, key=lambda value: len(value.parts), reverse=True):
            if path.is_relative_to(alias):
                return original_resolve(aliases[alias] / path.relative_to(alias), strict=strict)
        return original_resolve(path, strict=strict)
    def unlink(path, missing_ok=False):
        if path in aliases:
            del aliases[path]
            identities.pop(path, None)
        else:
            original_unlink(path, missing_ok=missing_ok)
    def lstat(path):
        if path in aliases:
            if path not in identities:
                identities[path] = os.stat_result((0o120777, len(identities) + 200001, 1, 1, 0, 0, 0, 0, 0, 0))
            return identities[path]
        return original_lstat(path)
    with mock.patch.object(pathlib.Path, 'symlink_to', create), \
         mock.patch.object(pathlib.Path, 'is_symlink', lambda path: path in aliases or original_symlink(path)), \
         mock.patch.object(pathlib.Path, 'resolve', resolve), \
         mock.patch.object(pathlib.Path, 'lstat', lstat), \
         mock.patch.object(pathlib.Path, 'unlink', unlink):
        yield aliases



def synthetic_probe(daemon):
    chain = [{'pid': 201, 'birth': '701', 'ppid': 202, 'session': 203, 'exe': '/usr/bin/python3.12'},
             {'pid': 202, 'birth': '702', 'ppid': 203, 'session': 203, 'exe': '/usr/bin/timeout'},
             {'pid': 203, 'birth': '703', 'ppid': 204, 'session': 203, 'exe': '/usr/bin/sudo'},
             {'pid': 204, 'birth': '704', 'ppid': 1, 'session': 204, 'exe': '/synthetic/supervisor'}]
    for index, member in enumerate(chain):
        member['children'] = None if index == len(chain) - 1 else ([] if index == 0 else [chain[index - 1]['pid']])
    payload = {'schema': 1, 'daemon': {key: daemon[key] for key in ('pid', 'birth', 'exe')},
               'chain': chain, 'startedNs': 100, 'completedNs': 200, 'expirySeconds': 5}
    raw = json.dumps(payload, separators=(',', ':')) + '\n'
    return {'sourceSha256': evidence.digest(pathlib.Path(__file__).with_name('daemon_exe_probe.py')),
            'interpreterExe': '/usr/bin/python3.12',
            'owner': {key: chain[-1][key] for key in ('pid', 'birth', 'session', 'exe')},
            'launcher': {key: chain[-2][key] for key in ('pid', 'birth', 'session')},
            'payload': payload, 'raw': raw, 'rawSha256': evidence.hashlib.sha256(raw.encode()).hexdigest(),
            'exitCode': 0, 'joined': True, 'firstFailure': None,
            'probeFailure': None, 'stderrSha256': evidence.hashlib.sha256(b'').hexdigest(),
            'outputSha256': evidence.hashlib.sha256(raw.encode()).hexdigest(),
            'absence': [{'pid': member['pid'], 'birth': member['birth'], 'absent': True} for member in chain[:-1]],
            'daemonBefore': copy.deepcopy(daemon), 'daemonAfter': copy.deepcopy(daemon)}


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
            'firstFailure': None, 'daemonProbes': [synthetic_probe(daemon), synthetic_probe(daemon)],
            'observerDiagnostic': {'firstRejection': None, 'firstMismatch': None},
            'fences': [{'startNano': 1, 'endNano': 30, 'eventCount': 2, 'streamSha256': 'e' * 64,
                        'replaySha256': 'e' * 64, 'historyCount': 2, 'historyOldestNano': 10,
                        'anchor': None, 'anchorPresent': True, 'initialPrefixComplete': True,
                        'nextAnchor': {'Type': 'container', 'Action': 'destroy', 'timeNano': 20, 'id': 'c' * 64}, 'success': True}]}


class ProducerGraphTests(unittest.TestCase):
    def setUp(self):
        patch = mock.patch.object(evidence, 'interpreter_exe', return_value='/usr/bin/python3.12')
        patch.start()
        self.addCleanup(patch.stop)

    @staticmethod
    def projected_event(nano=10, action='start', key='c' * 64):
        return {'Type': 'container', 'Action': action, 'timeNano': nano, 'id': key}

    @staticmethod
    def resource_phase(image='postgres:18-alpine', session='12345678-1234-1234-1234-123456789abc', reaper=None):
        phase = supervisor.Phase.__new__(supervisor.Phase)
        phase.policy = json.loads(pathlib.Path(supervisor.__file__).with_name('graph-actor-policy.json').read_text())
        phase.baseline = closure()['baseline']
        phase.resources, phase.errors, phase.foreign = {}, [], []
        phase.first_rejection = None
        phase.docker = mock.Mock()
        phase.docker.get.return_value = {'Id': 'c' * 64, 'Created': 'synthetic-original-generation',
            'Image': 'sha256:' + 'd' * 64, 'Mounts': [], 'Config': {'Image': image, 'Labels': {
                'org.testcontainers.session-id': session,
                'org.testcontainers.resource-reaper-session': session if reaper is None else reaper,
                'org.testcontainers': 'true', 'org.testcontainers.lang': 'dotnet'}}}
        return phase

    def test_policy_remains_exact_and_source_expected_redis_still_rejects(self):
        for image, session, reaper in (('redis:8-alpine', '12345678-1234-1234-1234-123456789abc', None),
                ('arbitrary-private-image', '12345678-1234-1234-1234-123456789abc', None),
                ('postgres:18-alpine', '', None), ('postgres:18-alpine', '12345678-1234-1234-1234-123456789abc', 'foreign')):
            with self.subTest(image=image, session=session, reaper=reaper):
                phase = self.resource_phase(image, session, reaper)
                phase._resource({'Type': 'container', 'Action': 'create', 'timeNano': 10, 'Actor': {'ID': 'c' * 64}})
                self.assertEqual(['foreign-container'], phase.foreign)
                self.assertTrue(evidence.observer_diagnostic({'firstRejection': phase.first_rejection, 'firstMismatch': None}))
                self.assertNotIn('arbitrary-private-image', json.dumps(phase.first_rejection))
        self.assertEqual(['postgres:18-alpine', 'testcontainers/ryuk:0.14.0'], phase.policy['images'])

    def test_existing_session_ownership_blocker_is_observed_without_gate_expansion(self):
        # Characterize the existing weakness, not a successful ownership proof or correction.
        foreign = 'ffffffff-ffff-ffff-ffff-ffffffffffff'
        phase = self.resource_phase(session=foreign)
        phase._resource({'Type': 'container', 'Action': 'create', 'timeNano': 10, 'Actor': {'ID': 'c' * 64}})
        self.assertEqual([], phase.foreign)
        row = closure()
        resource = row['resources'][0]
        resource['session'] = foreign
        for label in ('org.testcontainers.session-id', 'org.testcontainers.resource-reaper-session'):
            resource['inspect']['labels'][label] = foreign
        self.assertTrue(evidence.cleanup(row))  # No producer birth -> expected SDK session join exists.

    def test_first_foreign_rejection_is_sticky_bounded_and_excludes_arbitrary_payload(self):
        phase = self.resource_phase()
        secret = 'https://private.invalid/token=synthetic-secret'
        event = {'Type': secret, 'Action': secret, 'timeNano': True,
                 'Actor': {'ID': secret, 'Attributes': {'private-label': secret}}}
        phase._resource(event)
        retained = copy.deepcopy(phase.first_rejection)
        phase._resource({'Type': 'container', 'Action': 'destroy', 'timeNano': 20, 'Actor': {'ID': 'a' * 64}})
        self.assertEqual(retained, phase.first_rejection)
        self.assertEqual(['unexpected-daemon-actor', 'terminal-without-original-create'], phase.foreign)
        self.assertNotIn(secret, json.dumps(retained))
        self.assertEqual({'kind': 'other', 'action': 'other', 'id': None, 'timeNano': None,
                          'sha256': retained['coordinate']['sha256']}, retained['coordinate'])
        self.assertLess(len(json.dumps(retained)), 350)
        self.assertTrue(evidence.observer_diagnostic({'firstRejection': retained, 'firstMismatch': None}))

    def test_actual_fence_retains_order_discrepancy_without_changing_failure_or_daemon_short_circuit(self):
        observed = [self.projected_event(10), self.projected_event(20, 'die')]
        replay = list(reversed(observed))
        phase = self.resource_phase()
        phase.started = phase.fenced_until = 1
        phase.anchor = None
        phase.events, phase.fences, phase.errors = observed, [], []
        phase.first_mismatch = None
        phase.docker.daemon.return_value = phase.baseline['daemon']
        connection = mock.Mock()
        connection.getresponse.return_value.status = 200
        connection.getresponse.return_value.read.return_value = b'\n'.join(json.dumps({
            **{key: value for key, value in event.items() if key != 'id'}, 'Actor': {'ID': event['id']}}).encode()
            for event in replay)
        with mock.patch.object(supervisor, 'DockerConnection', return_value=connection), \
             mock.patch.object(supervisor.time, 'monotonic', side_effect=[10, 16]), \
             mock.patch.object(supervisor.subprocess, 'Popen') as actor:
            phase._fence(30)
            actor.assert_not_called()
        phase.docker.daemon.assert_not_called()
        self.assertFalse(phase.fences[0]['success'])
        self.assertEqual(['EventReplayGapOverflowOrDaemonChange'], phase.errors)
        self.assertTrue(phase.first_mismatch['orderOnly'])
        self.assertEqual(0, phase.first_mismatch['firstMismatchIndex'])
        for key in ('startNano', 'endNano', 'streamSha256', 'replaySha256'):
            self.assertEqual(phase.fences[0][key], phase.first_mismatch[key])
        self.assertTrue(evidence.observer_diagnostic({'firstRejection': None, 'firstMismatch': phase.first_mismatch}))
        connection.close.assert_called_once()

    def test_rejection_diagnostic_exception_preserves_original_return_failure_and_first_attempt(self):
        phase = self.resource_phase(image='redis:8-alpine')
        event = {'Type': 'container', 'Action': 'create', 'timeNano': 10, 'Actor': {'ID': 'c' * 64}}
        with mock.patch.object(supervisor, 'safe_coordinate', side_effect=RuntimeError('synthetic-private-text')) as capture:
            phase._resource(event)
            phase._resource({'Type': 'container', 'Action': 'destroy', 'timeNano': 20, 'Actor': {'ID': 'a' * 64}})
            capture.assert_called_once()
        self.assertEqual(['foreign-container', 'terminal-without-original-create'], phase.foreign)
        self.assertEqual([], phase.errors)
        self.assertEqual('redis:8-alpine', phase.resources['c' * 64]['inspect']['configImage'])
        self.assertIsNone(phase.first_rejection)
        row = closure()
        row['foreignEvents'] = phase.foreign
        self.assertFalse(evidence.cleanup(row))
        self.assertNotIn('synthetic-private-text', json.dumps(row))

    def test_fence_diagnostic_exception_preserves_original_append_failure_close_and_first_attempt(self):
        phase = self.resource_phase()
        phase.started = phase.fenced_until = 1
        phase.anchor = None
        phase.events = [self.projected_event(10), self.projected_event(20),
                        self.projected_event(40), self.projected_event(50)]
        phase.fences, phase.errors, phase.first_mismatch = [], [], None
        history = [phase.events[i] for i in (1, 0, 3, 2)]
        connection = mock.Mock()
        connection.getresponse.return_value.status = 200
        connection.getresponse.return_value.read.return_value = b'\n'.join(json.dumps({
            **{key: value for key, value in event.items() if key != 'id'}, 'Actor': {'ID': event['id']}}).encode()
            for event in history)
        with mock.patch.object(supervisor, 'DockerConnection', return_value=connection), \
             mock.patch.object(supervisor.time, 'monotonic', side_effect=[10, 16, 20, 26]), \
             mock.patch.object(supervisor, 'mismatch_diagnostic', side_effect=RuntimeError('synthetic-private-text')) as capture:
            phase._fence(30)
            phase._fence(60)
            capture.assert_called_once()
        self.assertEqual(2, len(phase.fences))
        self.assertTrue(all(row['success'] is False for row in phase.fences))
        self.assertEqual(['EventReplayGapOverflowOrDaemonChange'] * 2, phase.errors)
        self.assertIsNone(phase.first_mismatch)
        self.assertEqual(2, connection.close.call_count)
        phase.docker.daemon.assert_not_called()
        row = closure()
        row['fences'], row['readerErrors'] = phase.fences, phase.errors
        self.assertFalse(evidence.cleanup(row))
        self.assertNotIn('synthetic-private-text', json.dumps(row))

    def test_mismatch_diagnostic_distinguishes_missing_extra_reordered_and_caps_content(self):
        a, b = self.projected_event(10), self.projected_event(20, 'die')
        self.assertIsNone(supervisor.mismatch_diagnostic([a], [a], 1, 30))
        for observed, replay, order_only in (([a, b], [b, a], True), ([a], [a, b], False),
                                           ([a, b], [a], False), ([a], [b], False)):
            with self.subTest(observed=observed, replay=replay):
                value = supervisor.mismatch_diagnostic(observed, replay, 1, 30)
                self.assertEqual(order_only, value['orderOnly'])
                self.assertTrue(evidence.observer_diagnostic({'firstRejection': None, 'firstMismatch': value}))
        value = supervisor.mismatch_diagnostic([a] * 201, [a] * 200 + [b], 1, 30)
        self.assertTrue(value['truncated'])
        self.assertIsNone(value['orderOnly'])
        self.assertEqual(200, len(value['streamSequenceSha256']))
        self.assertEqual(200, len(value['replaySequenceSha256']))
        self.assertLess(len(json.dumps(value)), 28500)
        self.assertTrue(evidence.observer_diagnostic({'firstRejection': None, 'firstMismatch': value}))

    def test_diagnostic_tampering_or_forged_success_never_qualifies_cleanup(self):
        mismatch = supervisor.mismatch_diagnostic([self.projected_event()], [self.projected_event(20)], 1, 30)
        value = {'firstRejection': None, 'firstMismatch': mismatch}
        for mutation in ('count-bool', 'index-bool', 'extra-text', 'raw-id', 'hash', 'over-cap', 'order', 'truncated'):
            changed = copy.deepcopy(value)
            row = changed['firstMismatch']
            if mutation == 'count-bool': row['streamCount'] = True
            elif mutation == 'index-bool': row['firstMismatchIndex'] = True
            elif mutation == 'extra-text': row['stream']['private'] = 'synthetic-secret'
            elif mutation == 'raw-id': row['stream']['id'] = 'private-url'
            elif mutation == 'hash': row['stream']['sha256'] = 'a' * 64
            elif mutation == 'over-cap': row['streamSequenceSha256'] *= 201
            elif mutation == 'order': row['orderOnly'] = True
            else: row['truncated'] = True
            with self.subTest(mutation=mutation):
                self.assertFalse(evidence.observer_diagnostic(changed))
        row = closure()
        row['observerDiagnostic'] = value
        self.assertFalse(evidence.cleanup(row))  # All old success flags remain set; diagnostic cannot grant success.
        row['observerDiagnostic'] = {'firstRejection': None, 'firstMismatch': None}
        row['readerErrors'] = ['EventReplayGapOverflowOrDaemonChange']
        self.assertFalse(evidence.cleanup(row))
        row = closure()
        del row['observerDiagnostic']
        self.assertFalse(evidence.cleanup(row))

    def test_openapi_metadata_command_keeps_compilation_in_owned_sdk_cohort(self):
        # Observe the actual argv at the supervisor boundary without starting any SDK process.
        stopped = RuntimeError('synthetic command boundary')
        phase = mock.Mock()
        phase.run.side_effect = stopped
        with tempfile.TemporaryDirectory() as temporary:
            private = pathlib.Path(temporary)
            directory = private / 'document-source'
            env = {'MSBUILDDISABLENODEREUSE': '1'}
            with self.assertRaises(RuntimeError) as raised:
                producer_graph.document_policy(phase, directory, private, env)
            self.assertIs(stopped, raised.exception)
            phase.run.assert_called_once()
            argv, actual_directory, actual_env, label = phase.run.call_args.args
            self.assertEqual('openapi-inputs', label)
            self.assertEqual(directory, actual_directory)
            self.assertIs(env, actual_env)
            self.assertEqual(1, argv.count('-p:UseSharedCompilation=false'))
            self.assertFalse(any(value.startswith('-p:UseSharedCompilation=')
                                 and value != '-p:UseSharedCompilation=false' for value in argv))
            self.assertIn('-target:ResolveReferences,GenerateAdditionalXmlFilesForOpenApi', argv)
            self.assertIn('-getItem:AdditionalFiles,ReferencePath', argv)

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
             mock.patch.object(supervisor, 'daemon_probe', side_effect=error), \
             mock.patch.object(supervisor.os, 'stat', return_value=mock.Mock(st_ino=80)):
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
            legacy, target, pins, identity = self.layout_fixture(root)
            pins['Legacy.Maliev.DocumentService'] = json.loads((producer_graph.HERE / 'candidate-pins.json').read_text())['pins']['Legacy.Maliev.DocumentService']
            def bound_git(directory, *arguments):
                return '1' * 40 if directory == root else identity(directory, *arguments)
            # Upstream content checks are isolated here; this control exercises real admission-path IO.
            with mock.patch.dict('os.environ', environment), mock.patch.object(receipt, 'complete', return_value=True), \
                 projected_links(), mock.patch.object(producer_graph, 'git', side_effect=bound_git), \
                 mock.patch.object(producer_graph, 'verify_sources', return_value=[]), \
                 mock.patch.object(producer_graph, 'assets', return_value=[]), mock.patch.object(receipt, 'trx', return_value={}), \
                 mock.patch.object(receipt, 'coverage', return_value={}), mock.patch.object(receipt, 'audit', return_value={}), \
                 mock.patch.object(receipt, 'digest', return_value='a' * 64):
                self.assertFalse(receipt.join(directory, root, admit=True))
                self.assertFalse((directory / 'producer-graph-admission.json').exists())
                producer_graph.prepare_document_layout(root, pins, root / 'unused-private/Document')
                self.assertTrue(receipt.join(directory, root, admit=True))
                self.assertTrue(receipt.join(directory, root))
                with mock.patch.object(producer_graph, 'pristine_document', side_effect=ValueError('Document checkout is not pristine')):
                    self.assertFalse(receipt.join(directory, root))
                legacy.unlink()
                self.assertFalse(receipt.join(directory, root))
                legacy.symlink_to(target, target_is_directory=True)
                self.assertFalse(receipt.join(directory, root, admit=True))
                with mock.patch.object(producer_graph, 'assets', side_effect=ValueError('missing restored shared graph')):
                    self.assertFalse(receipt.join(directory, root))
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

    @staticmethod
    def native_failure_fixture():
        first = {'testName': 'duplicate(value: "source-owned")', 'className': 'OwnedFixture', 'method': 'First'}
        second = {**first, 'method': 'Second'}
        inventory = [first, second, first]
        root = ET.Element('TestRun', xmlns=evidence.NS['t'], id=str(uuid.uuid4()))
        definitions, results, entries = (ET.SubElement(root, name) for name in ('TestDefinitions', 'Results', 'TestEntries'))
        for index, item in enumerate(inventory):
            test, execution = str(uuid.uuid4()), str(uuid.uuid4())
            definition = ET.SubElement(definitions, 'UnitTest', id=test, name=item['testName'])
            ET.SubElement(definition, 'Execution', id=execution)
            ET.SubElement(definition, 'TestMethod', className=item['className'], name=item['method'], codeBase='/PRIVATE/path')
            row = ET.SubElement(results, 'UnitTestResult', testId=test, executionId=execution,
                                testName=item['testName'], outcome='Failed' if index == 0 else 'Passed')
            ET.SubElement(row, 'Output').text = 'PRIVATE token/session/path/assertion'
            ET.SubElement(row, 'ErrorInfo').text = 'PRIVATE exception details'
            ET.SubElement(entries, 'TestEntry', testId=test, executionId=execution)
        summary = ET.SubElement(root, 'ResultSummary', outcome='Failed')
        counters = {'total': '3', 'executed': '3', 'passed': '2', **dict.fromkeys(evidence.ZERO, '0')}
        counters['failed'] = '1'
        ET.SubElement(summary, 'Counters', **counters)
        return root, inventory

    def test_failure_trx_retains_source_cases_multiplicity_and_original_joins_without_private_output(self):
        root, inventory = self.native_failure_fixture()
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / 'own.trx'
            ET.ElementTree(root).write(path)
            value = producer_graph.safe_native_failure(path, inventory)
            self.assertEqual('retained', value['status'])
            self.assertFalse(value['accepted'])
            self.assertEqual(evidence.digest(path), value['rawSha256'])
            self.assertEqual(root.attrib['id'], value['runId'])
            self.assertEqual(1, value['counters']['failed'])
            self.assertEqual(['Failed', 'Passed', 'Passed'], [row['outcome'] for row in value['rows']])
            self.assertEqual(inventory, [{key: row[key] for key in ('testName', 'className', 'method')} for row in value['rows']])
            self.assertEqual(3, len({row['executionId'] for row in value['rows']}))
            self.assertNotIn('PRIVATE', json.dumps(value))
            with self.assertRaises(ValueError):
                evidence.trx(path, inventory)
            root.find('Results')[0].set('outcome', 'Passed')
            root.find('ResultSummary').set('outcome', 'Completed')
            root.find('ResultSummary/Counters').set('passed', '3')
            root.find('ResultSummary/Counters').set('failed', '0')
            ET.ElementTree(root).write(path)
            self.assertEqual(3, evidence.trx(path, inventory)['count'])
            self.assertFalse(producer_graph.safe_native_failure(path, inventory)['accepted'])

    def test_failure_trx_foreign_duplicate_missing_or_inconsistent_metadata_stays_unavailable(self):
        root, inventory = self.native_failure_fixture()
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / 'own.trx'
            mutations = {
                'missing_definition': lambda r: r.find('TestDefinitions').remove(r.find('TestDefinitions')[0]),
                'missing_result': lambda r: r.find('Results').remove(r.find('Results')[0]),
                'missing_entry': lambda r: r.find('TestEntries').remove(r.find('TestEntries')[0]),
                'duplicate_definition': lambda r: r.find('TestDefinitions')[1].set('id', r.find('TestDefinitions')[0].get('id')),
                'duplicate_execution': lambda r: r.find('Results')[1].set('executionId', r.find('Results')[0].get('executionId')),
                'foreign_entry': lambda r: r.find('TestEntries')[0].set('executionId', str(uuid.uuid4())),
                'foreign_method': lambda r: r.find('TestDefinitions')[0].find('TestMethod').set('name', 'PRIVATE method'),
                'foreign_name': lambda r: r.find('Results')[0].set('testName', 'PRIVATE identity'),
                'foreign_outcome': lambda r: r.find('Results')[0].set('outcome', 'PRIVATE outcome'),
                'invalid_uuid': lambda r: r.set('id', 'PRIVATE identifier'),
                'counter_mismatch': lambda r: r.find('ResultSummary/Counters').set('failed', '0'),
                'counter_pending': lambda r: r.find('ResultSummary/Counters').set('pending', '1'),
                'counter_negative': lambda r: r.find('ResultSummary/Counters').set('failed', '-1'),
                'summary_foreign': lambda r: r.find('ResultSummary').set('outcome', 'PRIVATE summary'),
                'multiplicity': lambda r: r.find('TestDefinitions')[1].find('TestMethod').set('name', 'First'),
            }
            for name, mutate in mutations.items():
                changed = copy.deepcopy(root)
                mutate(changed)
                ET.ElementTree(changed).write(path)
                with self.subTest(mutation=name):
                    value = producer_graph.safe_native_failure(path, inventory)
                    self.assertEqual('unavailable', value['status'])
                    self.assertFalse(value['accepted'])
                    self.assertIn(value['reason'], {'lineage-invalid', 'inventory-mismatch', 'outcome-invalid', 'counters-invalid'})
                    self.assertNotIn('rows', value)
                    self.assertNotIn('PRIVATE', json.dumps(value))

    def test_failure_trx_missing_malformed_entity_oversize_or_unreadable_inputs_have_typed_reasons(self):
        _, inventory = self.native_failure_fixture()
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / 'own.trx'
            self.assertEqual('trx-missing', producer_graph.safe_native_failure(path, inventory)['reason'])
            for raw, reason in ((b'PRIVATE malformed', 'xml-malformed'),
                                (b'<!DOCTYPE TestRun [<!ENTITY secret "PRIVATE">]><TestRun>&secret;</TestRun>', 'xml-forbidden-declaration'),
                                ('<!DOCTYPE TestRun [<!ENTITY secret "PRIVATE">]><TestRun/>'.encode('utf-16'), 'xml-malformed'),
                                ('<!DOCTYPE TestRun [<!ENTITY secret "PRIVATE">]><TestRun/>'.encode('utf-16-le'), 'xml-encoding-invalid'),
                                ('<!DOCTYPE TestRun [<!ENTITY secret "PRIVATE">]><TestRun/>'.encode('utf-16-be'), 'xml-encoding-invalid'),
                                (b'x' * 8_388_609, 'trx-size-limit')):
                path.write_bytes(raw)
                value = producer_graph.safe_native_failure(path, inventory)
                self.assertEqual(reason, value['reason'])
                self.assertFalse(value['accepted'])
                self.assertNotIn('PRIVATE', json.dumps(value))
            with mock.patch.object(pathlib.Path, 'open', side_effect=PermissionError(13, 'PRIVATE path')):
                self.assertEqual('trx-unreadable', producer_graph.safe_native_failure(path, inventory)['reason'])
            for invalid in ([], inventory * 683, [{'testName': 'PRIVATE'}]):
                value = producer_graph.safe_native_failure(path, invalid)
                self.assertEqual('inventory-invalid', value['reason'])
                self.assertNotIn('PRIVATE', json.dumps(value))

    def test_qualify_preserves_failed_native_command_or_reader_and_secondary_cleanup(self):
        for fail_command, capture_error in ((True, False), (False, False), (True, True)):
            with self.subTest(fail_command=fail_command, capture_error=capture_error), tempfile.TemporaryDirectory() as temporary:
                root = pathlib.Path(temporary)
                directory = root / '.dependencies/Legacy.Maliev.DocumentService/Legacy.Maliev.DocumentService.Api'
                directory.mkdir(parents=True)
                (directory / 'Legacy.Maliev.DocumentService.Api.csproj').write_text('synthetic')
                private = root / 'private/Document'
                full = private / 'receipt-evidence/full'
                full.mkdir(parents=True)
                document, inventory = self.native_failure_fixture()
                ET.ElementTree(document).write(full / 'full-suite.trx')
                audited = root / 'audit.json'
                audited.write_text(json.dumps({'version': 1, 'parameters': '--vulnerable --include-transitive',
                                              'sources': ['https://api.nuget.org/v3/index.json'],
                                              'projects': [{'path': '/synthetic/Legacy.Maliev.DocumentService.Api.csproj'}]}))
                original = supervisor.CommandFailure('PRIVATE original command')
                secondary = PermissionError(13, 'PRIVATE secondary cleanup')
                phase = mock.Mock()
                events = []
                def command(arguments, directory, env, label):
                    if label == 'full-native' and fail_command:
                        raise original
                    return audited
                phase.run.side_effect = command
                def settle():
                    events.append('settle')
                    raise secondary
                phase.settle.side_effect = settle
                native_reader = producer_graph.safe_native_failure
                def observe(*arguments):
                    events.append('capture')
                    if capture_error:
                        raise RuntimeError('PRIVATE capture')
                    return native_reader(*arguments)
                with mock.patch.object(producer_graph, 'Phase', return_value=phase), \
                     mock.patch.object(producer_graph, 'producer_directory', return_value=directory.parent), \
                     mock.patch.object(producer_graph, 'binaries', return_value=[]), \
                     mock.patch.object(producer_graph, 'assets', return_value=[]), \
                     mock.patch.object(producer_graph, 'document_policy', return_value=private / 'receipt-evidence'), \
                     mock.patch.object(producer_graph, 'safe_native_failure', side_effect=observe) as capture:
                    with self.assertRaises(supervisor.CommandFailure if fail_command else ValueError) as raised:
                        producer_graph.qualify(root, 'Document', {'Legacy.Maliev.DocumentService': 'a' * 40},
                                               {'Document': {'inventory': inventory}}, private)
                if fail_command:
                    self.assertIs(original, raised.exception)
                self.assertIs(secondary, raised.exception.__cause__)
                diagnostic = raised.exception.producer_validation_failure
                self.assertEqual('full-native-command' if fail_command else 'native-readback', diagnostic['gate'])
                self.assertFalse(diagnostic['nativeMetadata']['accepted'])
                self.assertEqual('unavailable' if capture_error else 'retained', diagnostic['nativeMetadata']['status'])
                self.assertNotIn('PRIVATE', json.dumps(diagnostic))
                capture.assert_called_once_with(full / 'full-suite.trx', inventory)
                phase.settle.assert_called_once()
                self.assertEqual(['capture', 'settle'], events)
                self.assertEqual('full-native', phase.run.call_args.args[-1])

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
        value = {'version': 1, 'parameters': '--vulnerable --include-transitive', 'sources': ['https://api.nuget.org/v3/index.json'], 'projects': [{'path': '/synthetic/Owned.csproj', 'frameworks': [{'framework': 'net10.0'}]}]}
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
        # Actual successful main projection must still pass the unchanged strict reader.
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            env = {'PROOF_EVIDENCE': str(root / 'output'), 'RUNNER_TEMP': str(root),
                   'CANDIDATE_HEAD': owner['candidateHead'], 'GITHUB_RUN_ID': owner['runId'],
                   'GITHUB_RUN_ATTEMPT': owner['runAttempt'], 'RUNNER_ENVIRONMENT': 'github-hosted'}
            with mock.patch.dict(producer_graph.os.environ, env), mock.patch.object(producer_graph.sys, 'platform', 'linux'), \
                 mock.patch.object(producer_graph, 'git', return_value=owner['executedSource']), \
                 mock.patch.object(producer_graph, 'verify_sources', return_value=graph['sourceTrees']), \
                 mock.patch.object(producer_graph, 'prepare_document_layout'), \
                 mock.patch.object(producer_graph, 'qualify', side_effect=graph['producers']), \
                 mock.patch.object(producer_graph, 'shared_binaries', return_value=graph['sharedBinaries']):
                producer_graph.main()
            successful = json.loads((root / 'output/producer-graph.json').read_text())
            self.assertEqual(set(graph), set(successful))
            self.assertNotIn('validationFailure', successful)
            self.assertTrue(receipt.complete(successful, owner, inventories, pins))
            rejected = {**successful, 'validationFailure': None}
            self.assertFalse(receipt.complete(rejected, owner, inventories, pins))
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



    def test_probe_helper_uses_literal_daemon_and_actual_bounded_ancestry(self):
        daemon = closure()['daemon']
        payload = synthetic_probe(daemon)['payload']
        table = {member['pid']: {key: value for key, value in member.items() if key != 'children'} for member in payload['chain']}
        table[50] = {'pid': 50, 'birth': '70', 'exe': '/synthetic/dockerd', 'ppid': 1, 'session': 50}
        launcher = synthetic_probe(daemon)['launcher']
        with mock.patch('builtins.open', side_effect=lambda path, **kw: io.StringIO('50' if path == '/var/run/docker.pid' else {'/proc/201/task/201/children': '', '/proc/202/task/202/children': '201', '/proc/203/task/203/children': '202'}[path])), \
             mock.patch.object(probe.os, 'getpid', return_value=201), \
             mock.patch.object(probe, 'process', side_effect=lambda pid, **kwargs: copy.deepcopy(table[pid])) as reads:
            result = probe.observe(204, '704', 50, '70', launcher)
        self.assertEqual(payload['chain'], result['chain'])
        self.assertEqual(payload['daemon'], result['daemon'])
        self.assertEqual([50, 201, 202, 203, 204, 201, 202, 203, 50], [call.args[0] for call in reads.call_args_list])
        for value in ('0', '-1', '01', '../50', '2147483648', '５０', 'True'):
            with self.assertRaises(ValueError):
                probe.positive(value)

    def test_probe_helper_rejects_changed_daemon_and_unbound_launcher(self):
        daemon = closure()['daemon']
        row = synthetic_probe(daemon)
        table = {member['pid']: {key: value for key, value in member.items() if key != 'children'} for member in row['payload']['chain']}
        table[50] = {'pid': 50, 'birth': '70', 'exe': '/synthetic/dockerd', 'ppid': 1, 'session': 50}
        for target, birth, launcher in ((51, '70', row['launcher']), (50, 'foreign', row['launcher']),
                                         (50, '70', {**row['launcher'], 'birth': '999'})):
            with mock.patch('builtins.open', side_effect=lambda path, **kw: io.StringIO('50' if path == '/var/run/docker.pid' else {'/proc/201/task/201/children': '', '/proc/202/task/202/children': '201', '/proc/203/task/203/children': '202'}[path])), \
                 mock.patch.object(probe.os, 'getpid', return_value=201), \
                 mock.patch.object(probe, 'process', side_effect=lambda pid, **kwargs: table[pid]):
                with self.assertRaises(ValueError):
                    probe.observe(204, '704', target, birth, launcher)


    def test_probe_helper_unknown_sibling_or_reparenting_is_fatal(self):
        row = synthetic_probe(closure()['daemon'])
        table = {member['pid']: {key: value for key, value in member.items() if key != 'children'} for member in row['payload']['chain']}
        table[50] = {'pid': 50, 'birth': '70', 'exe': '/synthetic/dockerd', 'ppid': 1, 'session': 50}
        for children in ('201 900', '01', '2147483648'):
            def read(path, **kwargs):
                return io.StringIO('50' if path == '/var/run/docker.pid' else (children if '/202/' in path else ''))
            with mock.patch('builtins.open', side_effect=read), \
                 mock.patch.object(probe.os, 'getpid', return_value=201), \
                 mock.patch.object(probe, 'process', side_effect=lambda pid, **kwargs: copy.deepcopy(table[pid])):
                with self.assertRaises(ValueError):
                    probe.observe(204, '704', 50, '70', row['launcher'])

    def test_probe_helper_changed_own_generation_or_parent_is_fatal(self):
        row = synthetic_probe(closure()['daemon'])
        table = {member['pid']: {key: value for key, value in member.items() if key != 'children'} for member in row['payload']['chain']}
        table[50] = {'pid': 50, 'birth': '70', 'exe': '/synthetic/dockerd', 'ppid': 1, 'session': 50}
        for changed in ({'birth': '999'}, {'ppid': 900}, {'exe': '/foreign/python'}):
            counts = {}
            def identity(pid, **kwargs):
                counts[pid] = counts.get(pid, 0) + 1
                result = copy.deepcopy(table[pid])
                if pid == 201 and counts[pid] > 1:
                    result.update(changed)
                return result
            with mock.patch('builtins.open', side_effect=lambda path, **kw: io.StringIO('50' if path == '/var/run/docker.pid' else '')), \
                 mock.patch.object(probe.os, 'getpid', return_value=201), \
                 mock.patch.object(probe, 'process', side_effect=identity):
                with self.assertRaisesRegex(ValueError, 'cohort'):
                    probe.observe(204, '704', 50, '70', row['launcher'])

    def test_probe_helper_second_stat_bound_and_denied_exe_fail(self):
        text = '50 (synthetic) ' + ' '.join(['S', '1', '50', '50'] + ['0'] * 15 + ['70'])
        with mock.patch('builtins.open', side_effect=[io.StringIO(text), io.StringIO('x' * 8193)]), \
             mock.patch.object(probe.os, 'readlink', return_value='/synthetic/dockerd'):
            with self.assertRaises(ValueError):
                probe.process(50)
        error = PermissionError(13, 'synthetic denied')
        with mock.patch('builtins.open', return_value=io.StringIO(text)), \
             mock.patch.object(probe.os, 'readlink', side_effect=error):
            with self.assertRaises(PermissionError) as observed:
                probe.process(50)
        self.assertIs(error, observed.exception)
        with self.assertRaises(TimeoutError):
            probe.expire(14, None)

    def test_probe_wrong_source_never_launches(self):
        with mock.patch.object(supervisor.pathlib.Path, 'read_bytes', return_value=b'foreign utility'), \
             mock.patch.object(supervisor.subprocess, 'Popen') as launch:
            with self.assertRaises(ValueError):
                supervisor.daemon_probe(50, '70', [])
        launch.assert_not_called()

    def test_probe_fixed_source_argv_handshake_and_original_reap(self):
        original = synthetic_probe(closure()['daemon'])
        process = mock.Mock(pid=203, returncode=0)
        process.communicate.return_value = (original['raw'].encode(), b'')
        rows = []
        with mock.patch.object(supervisor, 'probe_stat', side_effect=[{key: original['owner'][key] for key in ('pid', 'birth', 'session')}, original['launcher']]), \
             mock.patch.object(supervisor.os, 'getpid', return_value=204), \
             mock.patch.object(supervisor.os, 'readlink', return_value='/synthetic/supervisor'), \
             mock.patch.object(supervisor.os.path, 'realpath', return_value='/usr/bin/python3.12'), \
             mock.patch.object(supervisor.subprocess, 'Popen', return_value=process) as launch, \
             mock.patch.object(supervisor, 'probe_absence', return_value=original['absence']):
            self.assertEqual('/synthetic/dockerd', supervisor.daemon_probe(50, '70', rows))
        argv = launch.call_args.args[0]
        self.assertEqual(['/usr/bin/sudo', '-n', '--', '/usr/bin/timeout', '--signal=TERM', '--kill-after=1s', '5s', '/usr/bin/python3', '-I', '-S', '-B', '-c'], argv[:12])
        self.assertEqual(pathlib.Path(supervisor.__file__).with_name('daemon_exe_probe.py').read_bytes(), argv[12].encode('utf-8'))
        self.assertEqual(['204', '704', '50', '70'], argv[13:])
        self.assertTrue(launch.call_args.kwargs['start_new_session'])
        process.communicate.assert_called_once_with((json.dumps(original['launcher']) + '\n').encode('ascii'), timeout=8)
        self.assertTrue(rows[0]['joined'])
        self.assertEqual(original['absence'], rows[0]['absence'])
        self.assertIsNone(rows[0]['firstFailure'])

    def test_probe_timeout_retains_unknown_and_never_general_kills(self):
        original = synthetic_probe(closure()['daemon'])
        process = mock.Mock(pid=203)
        process.communicate.side_effect = supervisor.subprocess.TimeoutExpired('fixed', 8)
        process.wait.side_effect = supervisor.subprocess.TimeoutExpired('fixed', 1)
        rows = []
        with mock.patch.object(supervisor, 'probe_stat', side_effect=[{key: original['owner'][key] for key in ('pid', 'birth', 'session')}, original['launcher']]), \
             mock.patch.object(supervisor.os, 'getpid', return_value=204), \
             mock.patch.object(supervisor.os, 'readlink', return_value='/synthetic/supervisor'), \
             mock.patch.object(supervisor.os.path, 'realpath', return_value='/usr/bin/python3.12'), \
             mock.patch.object(supervisor.subprocess, 'Popen', return_value=process):
            with self.assertRaises(supervisor.subprocess.TimeoutExpired):
                supervisor.daemon_probe(50, '70', rows)
        self.assertFalse(rows[0]['joined'])
        self.assertEqual('TimeoutExpired', rows[0]['firstFailure']['category'])
        process.kill.assert_not_called()
        process.terminate.assert_not_called()
        self.assertFalse(evidence.probe_receipt(rows[0]))

    def test_probe_terminal_absence_rejects_denied_read_reuse_and_unknown_session(self):
        chain = synthetic_probe(closure()['daemon'])['payload']['chain'][:-1]
        for error in (PermissionError(13, 'synthetic'), ValueError('unknown')):
            with mock.patch.object(supervisor, 'probe_stat', side_effect=error):
                with self.assertRaises(type(error)):
                    supervisor.probe_absence(chain)
        with mock.patch.object(supervisor, 'probe_stat', return_value={'pid': 201, 'birth': '999', 'session': 203}):
            with self.assertRaisesRegex(ValueError, 'reused'):
                supervisor.probe_absence(chain)
        with mock.patch.object(supervisor, 'probe_stat', side_effect=[FileNotFoundError()] * 3 + [{'pid': 900, 'birth': '999', 'session': 203}]), \
             mock.patch.object(supervisor.pathlib.Path, 'iterdir', return_value=[pathlib.Path('/proc/900')]):
            with self.assertRaisesRegex(ValueError, 'unknown'):
                supervisor.probe_absence(chain)
        with mock.patch.object(supervisor, 'probe_stat', side_effect=FileNotFoundError()), \
             mock.patch.object(supervisor.pathlib.Path, 'iterdir', return_value=[]):
            self.assertEqual(synthetic_probe(closure()['daemon'])['absence'], supervisor.probe_absence(chain))

    def test_probe_receipt_every_member_source_raw_identity_and_closure_fail_closed(self):
        original = synthetic_probe(closure()['daemon'])
        self.assertTrue(evidence.probe_receipt(original))
        for key in original:
            changed = copy.deepcopy(original)
            del changed[key]
            self.assertFalse(evidence.probe_receipt(changed), key)
        changes = [lambda r: r.update(sourceSha256='0' * 64), lambda r: r.update(joined=False),
                   lambda r: r.update(exitCode=1), lambda r: r.update(firstFailure={'category': 'PermissionError'}),
                   lambda r: r.update(rawSha256='0' * 64), lambda r: r.update(absence=[]),
                   lambda r: r['owner'].update(pid=True), lambda r: r['launcher'].update(session=True),
                   lambda r: r['daemonBefore'].update(pid=True), lambda r: r['daemonAfter'].update(socketInode=True),
                   lambda r: r['payload'].update(expirySeconds=6), lambda r: r['payload'].update(completedNs=5_000_000_101),
                   lambda r: r['payload']['chain'][0].update(birth='foreign'),
                   lambda r: r['payload']['chain'][1].update(exe='/foreign/executable'),
                   lambda r: r['payload']['chain'][1].update(ppid=999),
                   lambda r: r['payload']['chain'][0].update(session=204),
                   lambda r: r['payload']['chain'][0].update(children=[900]),
                   lambda r: r['payload']['chain'][1].update(children=[201, 900]),
                   lambda r: r['payload']['chain'][1].update(children=[True])]
        for change in changes:
            changed = copy.deepcopy(original)
            change(changed)
            # Rebind raw so each structural rejection is independently causal.
            changed['raw'] = json.dumps(changed['payload'], separators=(',', ':')) + '\n'
            if changed['rawSha256'] != '0' * 64:
                changed['rawSha256'] = evidence.hashlib.sha256(changed['raw'].encode()).hexdigest()
            changed['outputSha256'] = evidence.hashlib.sha256(changed['raw'].encode()).hexdigest()
            self.assertFalse(evidence.probe_receipt(changed))
        closed = closure()
        for row in closed['daemonProbes']:
            row['absence'] = []
        self.assertFalse(evidence.cleanup(closed))


    def test_probe_each_snapshot_inode_rejects_bool_even_when_equal_to_one(self):
        original = synthetic_probe({**closure()['daemon'], 'socketInode': 1})
        self.assertTrue(evidence.probe_receipt(original))
        for key in ('daemonBefore', 'daemonAfter'):
            changed = copy.deepcopy(original)
            changed[key]['socketInode'] = True
            self.assertFalse(evidence.probe_receipt(changed))

    def test_failed_unsettled_or_partial_probe_prevents_any_second_launch(self):
        original = synthetic_probe(closure()['daemon'])
        for change in (lambda r: r.update(firstFailure={'category': 'TimeoutExpired'}),
                       lambda r: r.update(joined=False), lambda r: r.update(absence=[]),
                       lambda r: r.update(daemonAfter=None), lambda r: r.update(rawSha256='0' * 64)):
            row = copy.deepcopy(original)
            change(row)
            with mock.patch.object(supervisor.subprocess, 'Popen') as launch:
                with self.assertRaisesRegex(ValueError, 'prior daemon probe'):
                    supervisor.daemon_probe(50, '70', [row])
            launch.assert_not_called()
        # A real actor-free first call leaves retained unknown settlement.
        process = mock.Mock(pid=203)
        process.communicate.side_effect = supervisor.subprocess.TimeoutExpired('fixed', 8)
        process.wait.side_effect = supervisor.subprocess.TimeoutExpired('fixed', 1)
        rows = []
        with mock.patch.object(supervisor, 'probe_stat', side_effect=[{key: original['owner'][key] for key in ('pid', 'birth', 'session')}, original['launcher']]), \
             mock.patch.object(supervisor.os, 'getpid', return_value=204), \
             mock.patch.object(supervisor.os, 'readlink', return_value='/synthetic/supervisor'), \
             mock.patch.object(supervisor.os.path, 'realpath', return_value='/usr/bin/python3.12'), \
             mock.patch.object(supervisor.subprocess, 'Popen', return_value=process) as launch:
            with self.assertRaises(supervisor.subprocess.TimeoutExpired):
                supervisor.daemon_probe(50, '70', rows)
            with self.assertRaisesRegex(ValueError, 'prior daemon probe'):
                supervisor.daemon_probe(50, '70', rows)
            self.assertEqual(1, launch.call_count)
        self.assertEqual('TimeoutExpired', rows[0]['firstFailure']['category'])
        self.assertFalse(rows[0]['joined'])

    def test_probe_failure_remains_sticky_before_sdk_dispatch(self):
        phase = supervisor.Phase.__new__(supervisor.Phase)
        phase.first_failure = {'gate': 'observer-startup', 'category': 'PermissionError'}
        phase.errors, phase.foreign, phase.limit = [], [], False
        with mock.patch.object(supervisor.subprocess, 'Popen') as launch:
            with self.assertRaises(supervisor.CommandFailure):
                phase.run(['dotnet', 'build'], '.', {}, 'must-not-dispatch')
        launch.assert_not_called()
        self.assertEqual('PermissionError', phase.first_failure['category'])


    def helper_failure_output(self, observe):
        output = io.StringIO()
        stdin = mock.Mock(buffer=io.BytesIO(b'{"pid":203,"birth":"703","session":203}\n'))
        with mock.patch.object(probe.sys, 'argv', ['fixed', '204', '704', '50', '70']), \
             mock.patch.object(probe.sys, 'stdin', stdin), mock.patch.object(probe.sys, 'stdout', output), \
             mock.patch.object(probe.signal, 'SIGALRM', 14, create=True), \
             mock.patch.object(probe.signal, 'signal'), mock.patch.object(probe.signal, 'alarm', create=True), \
             mock.patch.object(probe, 'observe', side_effect=observe):
            self.assertEqual(1, probe.main())
        payload = json.loads(output.getvalue())
        self.assertFalse(payload['complete'])
        self.assertTrue(evidence.utility_failure(payload))
        return output.getvalue(), payload

    def test_helper_original_denied_read_retains_safe_cause(self):
        stat = '50 (synthetic) ' + ' '.join(['S', '1', '50', '50'] + ['0'] * 15 + ['70'])
        def observe(*arguments):
            with mock.patch('builtins.open', return_value=io.StringIO(stat)), \
                 mock.patch.object(probe.os, 'readlink', side_effect=PermissionError(13, 'PRIVATE exception')):
                return probe.process(50, operation='daemon')
        raw, payload = self.helper_failure_output(observe)
        self.assertEqual('PermissionError', payload['category'])
        self.assertEqual(13, payload['errno'])
        self.assertEqual({'operation': 'daemon-exe-readlink', 'path': '/proc/50/exe'}, payload['failedRead'])
        self.assertNotIn('PRIVATE', raw)

    def test_helper_actual_unknown_cohort_retains_allowlisted_cause(self):
        original = synthetic_probe(closure()['daemon'])
        table = {row['pid']: {key: value for key, value in row.items() if key != 'children'} for row in original['payload']['chain']}
        table[50] = {'pid': 50, 'birth': '70', 'exe': '/synthetic/dockerd', 'ppid': 1, 'session': 50}
        original_observe = probe.observe
        def observe(*arguments):
            def read(path, **kwargs):
                return io.StringIO('50' if path == '/var/run/docker.pid' else ('201 900' if '/202/' in path else ''))
            with mock.patch('builtins.open', side_effect=read), mock.patch.object(probe.os, 'getpid', return_value=201), \
                 mock.patch.object(probe, 'process', side_effect=lambda pid, **kwargs: copy.deepcopy(table[pid])):
                return original_observe(*arguments)
        raw, payload = self.helper_failure_output(observe)
        self.assertEqual('cohort-unknown-or-changed', payload['reason'])
        self.assertEqual('ValueError', payload['category'])
        self.assertIsNone(payload['failedRead'])

    def mocked_probe_output(self, payload, exit_code, stderr=b''):
        original = synthetic_probe(closure()['daemon'])
        process = mock.Mock(pid=203, returncode=exit_code)
        process.communicate.return_value = ((json.dumps(payload, separators=(',', ':')) + '\n').encode(), stderr)
        rows = []
        with mock.patch.object(supervisor, 'probe_stat', side_effect=[{key: original['owner'][key] for key in ('pid', 'birth', 'session')}, original['launcher']]), \
             mock.patch.object(supervisor.os, 'getpid', return_value=204), \
             mock.patch.object(supervisor.os, 'readlink', return_value='/synthetic/supervisor'), \
             mock.patch.object(supervisor.subprocess, 'Popen', return_value=process) as launch:
            with self.assertRaises(ValueError):
                supervisor.daemon_probe(50, '70', rows)
            with self.assertRaisesRegex(ValueError, 'prior daemon probe'):
                supervisor.daemon_probe(50, '70', rows)
            self.assertEqual(1, launch.call_count)
        self.assertFalse(evidence.probe_receipt(rows[0]))
        self.assertIsNone(rows[0]['payload'])
        self.assertEqual([], rows[0]['absence'])
        return rows[0]

    def test_supervisor_nonzero_retains_only_safe_failure_not_private_stderr(self):
        denial = PermissionError(13, 'PRIVATE')
        denial.probe_read_site = {'operation': 'daemon-exe-readlink', 'path': '/proc/50/exe'}
        payload = probe.failure(denial)
        row = self.mocked_probe_output(payload, 1, b'PRIVATE stderr')
        self.assertEqual(payload, row['probeFailure'])
        self.assertEqual('PermissionError', row['firstFailure']['utilityCategory'])
        self.assertEqual(13, row['firstFailure']['errno'])
        self.assertEqual(payload['failedRead'], row['firstFailure']['failedRead'])
        self.assertNotIn('PRIVATE', json.dumps(row))

    def test_fabricated_diagnostic_and_nonzero_fake_success_fail_closed(self):
        safe = probe.failure(PermissionError(13, 'PRIVATE'))
        changed = []
        for key, value in (('reason', 'PRIVATE'), ('category', 'PRIVATE'), ('complete', True), ('errno', True),
                           ('failedRead', {'operation': 'daemon-exe-readlink', 'path': '/etc/shadow'})):
            item = copy.deepcopy(safe)
            item[key] = value
            changed.append(item)
            self.assertFalse(evidence.utility_failure(item))
        for payload, code in [(synthetic_probe(closure()['daemon'])['payload'], 1), (safe, 0)] + [(item, 1) for item in changed]:
            row = self.mocked_probe_output(payload, code)
            self.assertIsNone(row['probeFailure'])
            self.assertEqual('', row['raw'])

    def test_package_audit_gate_preserves_original_reason_before_cleanup_failure(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            directory = root / '.dependencies/Legacy.Maliev.DocumentService/Legacy.Maliev.DocumentService.Api'
            directory.mkdir(parents=True)
            (directory / 'Legacy.Maliev.DocumentService.Api.csproj').write_text('synthetic')
            audited = root / 'audit.json'
            audited.write_text(json.dumps({'version': 1, 'parameters': '--vulnerable --include-transitive', 'sources': ['https://api.nuget.org/v3/index.json'], 'projects': [{'path': '/PRIVATE/Legacy.Maliev.DocumentService.Api.csproj', 'frameworks': []}]}))
            phase = mock.Mock()
            phase.run.return_value = audited
            phase.settle.side_effect = PermissionError(13, 'PRIVATE secondary cleanup')
            with mock.patch.object(producer_graph, 'Phase', return_value=phase), \
                 mock.patch.object(producer_graph, 'producer_directory', return_value=directory.parent), \
                 mock.patch.object(producer_graph, 'binaries', return_value=[]), \
                 mock.patch.object(producer_graph, 'assets', return_value=[]), \
                 mock.patch.object(producer_graph, 'document_policy') as document_policy:
                with self.assertRaisesRegex(ValueError, '^audit project incomplete$') as raised:
                    producer_graph.qualify(root, 'Document', {'Legacy.Maliev.DocumentService': 'a' * 40}, {}, root / 'private/Document')
            diagnostic = raised.exception.producer_validation_failure
            self.assertEqual('package-audit-readback', diagnostic['gate'])
            self.assertEqual('audit-project-incomplete', diagnostic['reason'])
            self.assertEqual('ValueError', diagnostic['category'])
            self.assertEqual([], diagnostic['auditMetadata']['actualProjects'][0]['frameworks'])
            self.assertNotIn('PRIVATE', json.dumps(diagnostic))
            self.assertIsInstance(raised.exception.__cause__, PermissionError)
            document_policy.assert_not_called()
            phase.settle.assert_called_once()
            self.assertEqual('package-audit', phase.run.call_args.args[-1])
            self.assertEqual(['dotnet', 'list', 'Legacy.Maliev.DocumentService.slnx', 'package', '--vulnerable',
                              '--include-transitive', '--no-restore', '--format', 'json', '--output-version', '1'],
                             phase.run.call_args.args[0])

    def test_safe_audit_metadata_has_only_known_basenames_and_numeric_counts(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / 'audit.json'
            expected = ['Legacy.Maliev.DocumentService.Api.csproj']
            path.write_text(json.dumps({'projects': [
                {'path': 'C:\\PRIVATE\\Legacy.Maliev.DocumentService.Api.csproj', 'frameworks': [
                    {'framework': 'PRIVATE framework', 'topLevelPackages': [{'id': 'PRIVATE', 'url': 'PRIVATE'}],
                     'transitivePackages': [{}, {}], 'problems': ['PRIVATE problem']}]},
                {'path': '/PRIVATE/private-project.csproj', 'frameworks': []}], 'problems': ['PRIVATE problem']}))
            result = producer_graph.safe_audit_metadata(path, expected)
            self.assertEqual(expected, result['expectedProjects'])
            self.assertEqual('unrecognized-project', result['actualProjects'][1]['project'])
            self.assertEqual({'frameworkPresent': True, 'problemCount': 1, 'topLevelPackageCount': 1,
                              'transitivePackageCount': 2}, result['actualProjects'][0]['frameworks'][0])
            self.assertNotIn('PRIVATE', json.dumps(result))
            path.write_text('PRIVATE malformed JSON')
            self.assertIsNone(producer_graph.safe_audit_metadata(path, expected))

    def test_unknown_validator_text_and_fabricated_gate_never_enter_diagnostic(self):
        for error in (ValueError('PRIVATE path/secret'), PermissionError(13, 'PRIVATE'), RuntimeError('PRIVATE')):
            result = producer_graph.validation_failure(error, 'PRIVATE gate')
            self.assertEqual('unknown-validator', result['gate'])
            self.assertEqual('unknown-validator-reason', result['reason'])
            self.assertNotIn('PRIVATE', json.dumps(result))
        self.assertEqual('audit-framework-incomplete', producer_graph.validation_failure(
            ValueError('audit framework incomplete'), 'package-audit-readback')['reason'])
        self.assertEqual('audit-project-missing', producer_graph.validation_failure(
            ValueError('missing audited project'), 'package-audit-readback')['reason'])

    def test_main_retains_original_validator_diagnostic_in_nonaccepted_report(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            error = ValueError('audit framework incomplete')
            error.producer_validation_failure = producer_graph.validation_failure(error, 'package-audit-readback')
            env = {'PROOF_EVIDENCE': str(root / 'output'), 'RUNNER_TEMP': str(root), 'CANDIDATE_HEAD': 'a' * 40,
                   'GITHUB_RUN_ID': '123', 'GITHUB_RUN_ATTEMPT': '1', 'RUNNER_ENVIRONMENT': 'github-hosted'}
            with mock.patch.dict(producer_graph.os.environ, env), mock.patch.object(producer_graph.sys, 'platform', 'linux'), \
                 mock.patch.object(producer_graph, 'git', return_value='a' * 40), \
                 mock.patch.object(producer_graph, 'verify_sources', return_value=[]), \
                 mock.patch.object(producer_graph, 'prepare_document_layout'), \
                 mock.patch.object(producer_graph, 'qualify', side_effect=error) as qualify:
                with self.assertRaises(SystemExit):
                    producer_graph.main()
            result = json.loads((root / 'output/producer-graph.json').read_text())
            self.assertEqual(error.producer_validation_failure, result['validationFailure'])
            self.assertFalse(result['graphComplete'])
            self.assertFalse(result['acceptedProducerGraph'])
            self.assertEqual('Document', result['failedProducer'])
            self.assertEqual([], result['producers'])
            qualify.assert_called_once()

    def test_filtered_zero_findings_requires_exact_protocol_and_unique_projects(self):
        value = {'version': 1, 'parameters': '--vulnerable --include-transitive',
                 'sources': ['https://api.nuget.org/v3/index.json'],
                 'projects': [{'path': '/synthetic/Owned.csproj'}]}
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / 'audit.json'
            path.write_text(json.dumps(value))
            self.assertEqual(['Owned.csproj'], evidence.audit(path, ['Owned.csproj'])['projectNames'])
            changes = [lambda r: r.pop('version'), lambda r: r.update(version=True), lambda r: r.update(version=2),
                       lambda r: r.update(parameters='--include-transitive'), lambda r: r.update(sources=[]),
                       lambda r: r.update(sources=[None]), lambda r: r.update(problems=['failed source']),
                       lambda r: r.update(problems=None), lambda r: r.update(projects=[]),
                       lambda r: r['projects'].append(copy.deepcopy(r['projects'][0])),
                       lambda r: r['projects'].append({'path': '/synthetic/Foreign.csproj'}),
                       lambda r: r['projects'][0].update(path=None), lambda r: r['projects'][0].update(problems=['failed']),
                       lambda r: r['projects'][0].update(frameworks=[]), lambda r: r['projects'][0].update(frameworks=None),
                       lambda r: r['projects'][0].update(frameworks=[{'framework': 'net10.0', 'problems': ['failed']}]),
                       lambda r: r['projects'][0].update(frameworks=[{'framework': 'net10.0', 'transitivePackages': [{'vulnerabilities': [{'severity': 'high'}]}]}]),
                       lambda r: r['projects'][0].update(frameworks=[{'framework': 'net10.0', 'topLevelPackages': [{}]}]),
                       lambda r: r['projects'][0].update(frameworks=[{'framework': 'net10.0', 'transitivePackages': None}]),
                       lambda r: r['projects'][0].update(frameworks=[None])]
            for change in changes:
                changed = copy.deepcopy(value)
                change(changed)
                path.write_text(json.dumps(changed))
                with self.subTest(value=changed), self.assertRaises(ValueError):
                    evidence.audit(path, ['Owned.csproj'])

    def test_restored_graph_requires_every_owned_project_framework_and_identity(self):
        with tempfile.TemporaryDirectory() as temporary:
            workspace = pathlib.Path(temporary)
            directory = workspace / 'Legacy.Maliev.DocumentService'
            fixture = {}
            for module in ('Api', 'Domain'):
                name = 'Legacy.Maliev.DocumentService.' + module
                project = directory / name / (name + '.csproj')
                project.parent.mkdir(parents=True)
                project.write_text('synthetic')
                libraries = {} if module == 'Domain' else {shared + '/1.0.0': {
                    'type': 'project', 'path': str(workspace / shared / 'src' / shared / (shared + '.csproj'))}
                    for shared in ('Legacy.Maliev.ServiceDefaults', 'Legacy.Maliev.CompatibilityContracts')}
                value = {'version': 3, 'project': {'restore': {'projectPath': str(project)}, 'frameworks': {'net10.0': {}}},
                         'targets': {'net10.0': {key: {'type': row['type']} for key, row in libraries.items()}},
                         'libraries': libraries}
                path = project.parent / 'obj/project.assets.json'
                path.parent.mkdir()
                fixture[path] = value
            def write_all():
                for path, value in fixture.items():
                    path.write_text(json.dumps(value))
            write_all()
            self.assertEqual(2, len(producer_graph.assets(directory, workspace)))
            domain = next(path for path in fixture if '.Domain/' in path.as_posix())
            original = fixture[domain]
            changes = [lambda r: r['project'].update(frameworks={}), lambda r: r.update(targets={}),
                       lambda r: r.update(targets={'net9.0': {}}), lambda r: r['project']['restore'].update(projectPath='/foreign/Domain.csproj'),
                       lambda r: r.update(libraries=None), lambda r: r.update(targets={'net10.0': {'missing/1': {'type': 'package'}}}),
                       lambda r: r.update(logs=[{'level': 'Warning'}])]
            for change in changes:
                changed = copy.deepcopy(original)
                change(changed)
                domain.write_text(json.dumps(changed))
                with self.subTest(value=changed), self.assertRaises(ValueError):
                    producer_graph.assets(directory, workspace)
            write_all()
            domain.unlink()
            with self.assertRaises(ValueError):
                producer_graph.assets(directory, workspace)

    @staticmethod
    def layout_fixture(root, sources=True):
        root = root.resolve()
        legacy = root / '.dependencies/Legacy.Maliev.DocumentService'
        target = root / '.producer-sources/Legacy.Maliev.DocumentService'
        (legacy / '.git').mkdir(parents=True)
        if sources:
            (legacy / 'Production').mkdir()
            for index in range(40):
                (legacy / 'Production' / ('Source' + str(index) + '.cs')).write_text('synthetic source')
            (legacy / 'Production/Rendering.csproj').write_text('synthetic QuestPDF reference')
        pins = {'Legacy.Maliev.DocumentService': 'a' * 40}
        def identity(directory, *arguments):
            if arguments == ('rev-parse', '--show-toplevel'):
                return str(directory)
            if arguments == ('rev-parse', 'HEAD'):
                return pins['Legacy.Maliev.DocumentService']
            if arguments == ('rev-parse', 'HEAD^{tree}'):
                return 'b' * 40
            if arguments == ('status', '--porcelain', '--untracked-files=all'):
                return ''
            if arguments == ('ls-files', '--others', '--ignored', '--exclude-standard'):
                return ''
            raise AssertionError(arguments)
        return legacy, target, pins, identity

    def test_document_layout_restores_census_and_exact_source_without_os_link_claim(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary).resolve()
            legacy, target, pins, identity = self.layout_fixture(root)
            before = {path.relative_to(legacy): path.read_bytes() for path in legacy.rglob('*') if path.is_file()}
            def census(directory):
                return [path for path in directory.rglob('*') if path.suffix in ('.cs', '.csproj')
                        and '.dependencies' not in [part.casefold() for part in path.parts]]
            self.assertEqual(41, len(before))
            self.assertEqual(0, len(census(legacy)))
            with projected_links() as aliases, mock.patch.object(producer_graph, 'git', side_effect=identity):
                self.assertEqual(target, producer_graph.prepare_document_layout(root, pins, root / 'private/Document'))
                self.assertEqual({legacy: target}, aliases)
                self.assertEqual(target, producer_graph.producer_directory(root, 'Document', pins))
                self.assertEqual(41, len(census(target)))
                self.assertTrue(any(b'QuestPDF' in path.read_bytes() for path in census(target)))
                self.assertEqual(before, {path.relative_to(target): path.read_bytes() for path in target.rglob('*') if path.is_file()})
                for other in ('File', 'Employee'):
                    self.assertEqual(root / '.dependencies' / ('Legacy.Maliev.' + other + 'Service'),
                                     producer_graph.producer_directory(root, other, pins))
                (target / 'Production/obj').mkdir()
                # Generated output is expected after actors and cannot masquerade as source dirt.
                self.assertEqual(target, producer_graph.producer_directory(root, 'Document', pins))

    def test_document_layout_rejects_unsafe_geometry_before_move_or_actor(self):
        for mutation in ('ancestor', 'target', 'dangling-target', 'legacy-alias', 'parent-alias',
                         'git-indirection', 'git-alias', 'source-alias', 'actor-bin', 'actor-assets',
                         'prior-private', 'dirty', 'ignored', 'head', 'tree', 'toplevel', 'reader-no-alias'):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                root = pathlib.Path(temporary).resolve()
                if mutation == 'ancestor':
                    root = root / '.DEPENDENCIES/workspace'
                    root.mkdir(parents=True)
                legacy, target, pins, identity = self.layout_fixture(root)
                private = root / 'private/Document'
                with projected_links() as aliases:
                    if mutation == 'target':
                        target.mkdir(parents=True)
                    elif mutation == 'dangling-target':
                        aliases[target] = root / 'absent'
                    elif mutation == 'legacy-alias':
                        aliases[legacy] = root / 'foreign'
                    elif mutation == 'parent-alias':
                        aliases[target.parent] = root / 'foreign'
                    elif mutation == 'git-indirection':
                        (legacy / '.git').rmdir()
                        (legacy / '.git').write_text('gitdir: foreign')
                    elif mutation == 'git-alias':
                        aliases[legacy / '.git'] = root / 'foreign'
                    elif mutation == 'source-alias':
                        aliases[legacy / 'Production/Source0.cs'] = root / 'foreign'
                    elif mutation == 'actor-bin':
                        (legacy / 'Production/bin').mkdir()
                    elif mutation == 'actor-assets':
                        (legacy / 'project.assets.json').write_text('{}')
                    elif mutation == 'prior-private':
                        private.mkdir(parents=True)
                    def changed_identity(directory, *arguments):
                        if mutation == 'dirty' and arguments[0] == 'status':
                            return ' M Production/Source0.cs'
                        if mutation == 'ignored' and arguments[0] == 'ls-files':
                            return 'Production/ignored.cs'
                        if mutation == 'head' and arguments == ('rev-parse', 'HEAD'):
                            return 'c' * 40
                        if mutation == 'tree' and arguments == ('rev-parse', 'HEAD^{tree}'):
                            return 'malformed'
                        if mutation == 'toplevel' and arguments == ('rev-parse', '--show-toplevel'):
                            return str(root / 'foreign')
                        return identity(directory, *arguments)
                    with mock.patch.object(producer_graph, 'git', side_effect=changed_identity), \
                         mock.patch.object(pathlib.Path, 'rename') as rename, \
                         mock.patch.object(producer_graph, 'Phase') as actor:
                        with self.assertRaises(ValueError):
                            if mutation == 'reader-no-alias':
                                producer_graph.producer_directory(root, 'Document', pins)
                            else:
                                producer_graph.prepare_document_layout(root, pins, private)
                        rename.assert_not_called()
                        actor.assert_not_called()

    def test_document_layout_alias_failure_rolls_back_exact_known_leaf(self):
        for post_move_mismatch in (False, True):
            with self.subTest(post_move_mismatch=post_move_mismatch), tempfile.TemporaryDirectory() as temporary:
                root = pathlib.Path(temporary).resolve()
                legacy, target, pins, identity = self.layout_fixture(root)
                original = (legacy / 'Production/Rendering.csproj').read_bytes()
                def changed_identity(directory, *arguments):
                    if post_move_mismatch and directory == target and arguments == ('rev-parse', 'HEAD^{tree}'):
                        return 'c' * 40
                    return identity(directory, *arguments)
                with projected_links(fail=not post_move_mismatch) as aliases, \
                     mock.patch.object(producer_graph, 'git', side_effect=changed_identity):
                    with self.assertRaisesRegex(ValueError, '^Document checkout alias acquisition failed$'):
                        producer_graph.prepare_document_layout(root, pins, root / 'private/Document')
                    self.assertFalse(aliases)
                    self.assertTrue(legacy.is_dir())
                    self.assertFalse(target.exists())
                    self.assertEqual(original, (legacy / 'Production/Rendering.csproj').read_bytes())

    def test_document_qualify_uses_canonical_cwd_and_preserves_original_failure_and_argv(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary).resolve()
            legacy, target, pins, identity = self.layout_fixture(root)
            private = root / 'private/Document'
            original = RuntimeError('synthetic first command boundary')
            phase = mock.Mock()
            phase.run.side_effect = original
            with projected_links(), mock.patch.object(producer_graph, 'git', side_effect=identity):
                producer_graph.prepare_document_layout(root, pins, private)
                with mock.patch.object(producer_graph, 'Phase', return_value=phase):
                    with self.assertRaises(RuntimeError) as observed:
                        producer_graph.qualify(root, 'Document', pins, {}, private)
                self.assertIs(original, observed.exception)
                arguments, cwd, env, label = phase.run.call_args.args
                self.assertEqual(target, cwd)
                self.assertEqual(['go', 'install', 'github.com/zricethezav/gitleaks/v8@6eaad039603a4de39fddd1cf5f727391efe9974e'], arguments)
                self.assertEqual('gitleaks-install', label)
                self.assertEqual(str(root / '.dependencies'), env['MalievWorkspaceRoot'])
                phase.settle.assert_called_once()
                with mock.patch.object(producer_graph, 'Phase') as actor:
                    # A lost alias is rejected before even the SDK/resource supervisor is constructed.
                    legacy.unlink()
                    with self.assertRaisesRegex(ValueError, '^Document checkout layout invalid$'):
                        producer_graph.qualify(root, 'Document', pins, {}, private)
                    actor.assert_not_called()

    def test_document_layout_failed_rollback_is_closed_and_does_not_mask_foreign_state(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary).resolve()
            legacy, target, pins, identity = self.layout_fixture(root)
            rename = pathlib.Path.rename
            def fail_rollback(path, destination):
                if path == target:
                    raise PermissionError(13, 'PRIVATE rollback path')
                return rename(path, destination)
            with projected_links(fail=True), mock.patch.object(producer_graph, 'git', side_effect=identity), \
                 mock.patch.object(pathlib.Path, 'rename', fail_rollback), \
                 mock.patch.object(producer_graph, 'Phase') as actor:
                with self.assertRaisesRegex(ValueError, '^Document checkout rollback incomplete$') as failed:
                    producer_graph.prepare_document_layout(root, pins, root / 'private/Document')
                diagnostic = producer_graph.validation_failure(failed.exception, 'document-layout-preparation')
                self.assertEqual('document-layout-rollback-incomplete', diagnostic['reason'])
                self.assertNotIn('PRIVATE', json.dumps(diagnostic))
                self.assertTrue(target.is_dir())
                self.assertFalse(legacy.exists())
                actor.assert_not_called()
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary).resolve()
            legacy, target, pins, identity = self.layout_fixture(root)
            with projected_links() as aliases, mock.patch.object(producer_graph, 'git', side_effect=identity):
                def foreign_alias(path, destination, target_is_directory=False):
                    aliases[path] = root / 'foreign'
                    raise OSError('synthetic foreign acquisition')
                with mock.patch.object(pathlib.Path, 'symlink_to', foreign_alias):
                    with self.assertRaisesRegex(ValueError, '^Document checkout rollback incomplete$'):
                        producer_graph.prepare_document_layout(root, pins, root / 'private/Document')
                self.assertEqual(root / 'foreign', aliases[legacy])
                self.assertTrue(target.is_dir())

    def test_document_layout_assets_join_physical_owner_and_original_shared_roots(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary).resolve()
            legacy, target, pins, identity = self.layout_fixture(root, sources=False)
            shared = ('Legacy.Maliev.ServiceDefaults', 'Legacy.Maliev.CompatibilityContracts')
            for name in shared:
                directory = root / '.dependencies' / name / 'src' / name
                directory.mkdir(parents=True)
                (directory / (name + '.csproj')).write_text('synthetic shared project')
            for module in ('Api', 'Application', 'Domain', 'Rendering', 'Tests'):
                name = 'Legacy.Maliev.DocumentService.' + module
                directory = legacy / name
                directory.mkdir()
                (directory / (name + '.csproj')).write_text('synthetic source project')
            with projected_links(), mock.patch.object(producer_graph, 'git', side_effect=identity):
                producer_graph.prepare_document_layout(root, pins, root / 'private/Document')
                owner = producer_graph.producer_directory(root, 'Document', pins)
                for directory in owner.iterdir():
                    if directory.name == '.git':
                        continue
                    assets = directory / 'obj/project.assets.json'
                    assets.parent.mkdir()
                    packages = {name + '/1.0.0': {'type': 'project',
                        'path': '../../../.dependencies/' + name + '/src/' + name + '/' + name + '.csproj'} for name in shared}
                    assets.write_text(json.dumps({'project': {'restore': {'projectPath': str(directory / (directory.name + '.csproj'))}, 'frameworks': {'net10.0': {}}},
                        'targets': {'net10.0': {key: {'type': 'project'} for key in packages}}, 'libraries': packages}))
                restored = producer_graph.assets(owner, root / '.dependencies')
                self.assertEqual(5, len(restored))
                self.assertEqual(restored, producer_graph.assets(producer_graph.producer_directory(root, 'Document', pins), root / '.dependencies'))
                self.assertEqual(owner, producer_graph.producer_directory(root, 'Document', pins))
                sample = owner / 'Legacy.Maliev.DocumentService.Api/obj/project.assets.json'
                actual = json.loads(sample.read_text())
                for mutation in ('foreign-owner', 'foreign-shared', 'packaged-shared'):
                    changed = copy.deepcopy(actual)
                    if mutation == 'foreign-owner':
                        changed['project']['restore']['projectPath'] = str(root / 'foreign/Api.csproj')
                    elif mutation == 'foreign-shared':
                        changed['libraries'][shared[0] + '/1.0.0']['path'] = '../../../foreign/Service.csproj'
                    else:
                        changed['libraries'][shared[0] + '/1.0.0']['type'] = 'package'
                        changed['targets']['net10.0'][shared[0] + '/1.0.0']['type'] = 'package'
                    sample.write_text(json.dumps(changed))
                    with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                        producer_graph.assets(owner, root / '.dependencies')
                sample.write_text(json.dumps(actual))

    def test_document_layout_rollback_preserves_foreign_exact_target_alias_and_replaced_directory(self):
        for mutation in ('exact-target-alias', 'replaced-directory', 'rollback-parent-alias'):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                root = pathlib.Path(temporary).resolve()
                legacy, target, pins, identity = self.layout_fixture(root)
                with projected_links() as aliases, mock.patch.object(producer_graph, 'git', side_effect=identity):
                    def failed_acquisition(path, destination, target_is_directory=False):
                        if mutation == 'exact-target-alias':
                            aliases[path] = target
                        elif mutation == 'replaced-directory':
                            target.rename(root / 'original-directory')
                            target.mkdir()
                            (target / 'foreign-marker').write_text('synthetic foreign node')
                        else:
                            aliases[legacy.parent] = root / 'foreign-parent'
                        raise OSError('synthetic failed acquisition')
                    with mock.patch.object(pathlib.Path, 'symlink_to', failed_acquisition):
                        with self.assertRaisesRegex(ValueError, '^Document checkout rollback incomplete$'):
                            producer_graph.prepare_document_layout(root, pins, root / 'private/Document')
                    self.assertTrue(target.is_dir())
                    if mutation == 'exact-target-alias':
                        self.assertEqual(target, aliases[legacy])
                    elif mutation == 'replaced-directory':
                        self.assertTrue((root / 'original-directory/Production/Rendering.csproj').exists())
                        self.assertEqual('synthetic foreign node', (target / 'foreign-marker').read_text())
                        self.assertFalse(legacy.exists())
                    else:
                        self.assertEqual(root / 'foreign-parent', aliases[legacy.parent])
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary).resolve()
            legacy, target, pins, identity = self.layout_fixture(root)
            node = producer_graph.layout_node
            legacy_reads = []
            def replaced_alias(path):
                value = node(path)
                if path == legacy:
                    legacy_reads.append(value)
                    if len(legacy_reads) > 2:
                        return value[0], value[1] + 1, value[2]
                return value
            def changed_tree(directory, *arguments):
                return 'c' * 40 if directory == target and arguments == ('rev-parse', 'HEAD^{tree}') else identity(directory, *arguments)
            with projected_links() as aliases, mock.patch.object(producer_graph, 'git', side_effect=changed_tree), \
                 mock.patch.object(producer_graph, 'layout_node', side_effect=replaced_alias):
                with self.assertRaisesRegex(ValueError, '^Document checkout rollback incomplete$'):
                    producer_graph.prepare_document_layout(root, pins, root / 'private/Document')
                self.assertEqual(target, aliases[legacy])
                self.assertTrue(target.is_dir())

if __name__ == '__main__':
    unittest.main()
