"""Owned Ubuntu process/Docker observer. Never deletes or adopts daemon objects."""
import http.client
import hashlib
import json
import os
import pathlib
import signal
import socket
import subprocess
import threading
import time
import urllib.parse

from graph_evidence import cleanup, probe_receipt, utility_failure

PROBE_SHA256 = 'ac5ffb6b4bb2e2647f58797359e992ca5f72a46d5430cfb96a90d06afab34919'


def read_site(operation, path, callback):
    """Retain only source-owned read coordinates on the original OSError."""
    if not allowed_read_site(operation, path):
        raise ValueError('unrecognized owned read site')
    try:
        return callback()
    except OSError as error:
        error.graph_failed_read = {'operation': operation, 'errno': error.errno, 'path': path}
        raise


def allowed_read_site(operation, path):
    fixed = {'daemon-pid-read': '/var/run/docker.pid',
             'daemon-socket-stat': '/var/run/docker.sock', 'docker-socket-connect': '/var/run/docker.sock'}
    if operation in fixed:
        return path == fixed[operation]
    suffixes = {'daemon-stat-read': 'stat', 'daemon-exe-readlink': 'exe',
                'process-stat-read': 'stat', 'process-exe-readlink': 'exe'}
    if operation not in suffixes or not isinstance(path, str):
        return False
    pieces = path.split('/')
    return (len(pieces) == 4 and pieces[:2] == ['', 'proc'] and pieces[3] == suffixes[operation]
            and pieces[2].isascii() and pieces[2].isdecimal() and len(pieces[2]) <= 10
            and 0 < int(pieces[2]) <= 2147483647 and str(int(pieces[2])) == pieces[2])


def read_failure(gate, error):
    failure = {'gate': gate, 'category': type(error).__name__}
    detail = getattr(error, 'graph_failed_read', None)
    if (isinstance(error, OSError) and isinstance(detail, dict)
            and set(detail) == {'operation', 'errno', 'path'}
            and isinstance(detail['operation'], str) and isinstance(detail['path'], str)
            and allowed_read_site(detail['operation'], detail['path'])
            and detail['errno'] == error.errno
            and (detail['errno'] is None or (type(detail['errno']) is int and 0 < detail['errno'] <= 4095))):
        failure['failedRead'] = dict(detail)
    return failure



def probe_stat(pid):
    """Unprivileged stat only; this never substitutes executable identity."""
    if type(pid) is not int or not 0 < pid <= 2147483647:
        raise ValueError('invalid probe process PID')
    text = pathlib.Path(f'/proc/{pid}/stat').read_text(encoding='ascii')
    if len(text) > 8192:
        raise ValueError('probe stat bound')
    fields = text.rsplit(')', 1)[1].split()
    return {'pid': pid, 'birth': fields[19], 'session': int(fields[3])}


def probe_absence(chain):
    """Require actual absence, not denied read, zombie, PID reuse or exit status."""
    sessions = {row['session'] for row in chain}
    originals = {row['pid']: row for row in chain}
    absent = []
    for pid, row in originals.items():
        try:
            current = probe_stat(pid)
        except FileNotFoundError:
            absent.append({'pid': pid, 'birth': row['birth'], 'absent': True})
            continue
        if current['birth'] != row['birth']:
            raise ValueError('original probe PID reused')
        raise ValueError('original probe process remains')
    # Enumerate stat only, preserving the existing SDK executable/session gates.
    for entry in pathlib.Path('/proc').iterdir():
        if not entry.name.isascii() or not entry.name.isdecimal():
            continue
        try:
            current = probe_stat(int(entry.name))
        except FileNotFoundError:
            continue
        if current['session'] in sessions:
            raise ValueError('unknown probe session member remains')
    return absent


def daemon_probe(pid, birth, receipts):
    """Run only the exact source-owned read utility; no shell, grant or SDK action."""
    if not isinstance(receipts, list) or len(receipts) >= 4096 or any(not probe_receipt(row) for row in receipts):
        raise ValueError('prior daemon probe failed or remains unqualified')
    source = pathlib.Path(__file__).with_name('daemon_exe_probe.py').read_bytes()
    source_sha = hashlib.sha256(source).hexdigest()
    if source_sha != PROBE_SHA256 or not 0 < len(source) <= 16384 or source.decode('utf-8').encode('utf-8') != source or b'\r' in source:
        raise ValueError('fixed probe source changed')
    owner = probe_stat(os.getpid())
    owner['exe'] = os.readlink(f'/proc/{owner["pid"]}/exe')
    interpreter = os.path.realpath('/usr/bin/python3')
    arguments = ['/usr/bin/sudo', '-n', '--', '/usr/bin/timeout', '--signal=TERM',
                 '--kill-after=1s', '5s', '/usr/bin/python3', '-I', '-S', '-B', '-c', source.decode('utf-8'),
                 str(owner['pid']), owner['birth'], str(pid), birth]
    row = {'sourceSha256': source_sha, 'interpreterExe': interpreter, 'owner': owner,
           'launcher': None, 'payload': None, 'raw': '', 'rawSha256': '',
           'exitCode': None, 'joined': False, 'absence': [], 'firstFailure': None,
           'daemonBefore': None, 'daemonAfter': None,
           'probeFailure': None, 'stderrSha256': '', 'outputSha256': ''}
    receipts.append(row)  # Retain partial failure before any privileged launch.
    process = None
    safe_payload = False
    try:
        process = subprocess.Popen(arguments, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                   stderr=subprocess.PIPE, start_new_session=True,
                                   env={'PATH': '/usr/bin:/bin', 'LANG': 'C.UTF-8'})
        launcher = probe_stat(process.pid)
        if launcher['session'] != launcher['pid']:
            raise ValueError('probe launcher session not isolated')
        row['launcher'] = launcher
        out, error = process.communicate((json.dumps(launcher) + '\n').encode('ascii'), timeout=8)
        row['exitCode'] = process.returncode
        row['joined'] = True  # communicate waits/reaps the retained direct child.
        row['stderrSha256'] = hashlib.sha256(error).hexdigest()
        row['outputSha256'] = hashlib.sha256(out).hexdigest()
        if not 0 < len(out) <= 32768:
            raise ValueError('readonly probe failed or output bound')
        raw = out.decode('utf-8')
        payload = json.loads(raw)
        if utility_failure(payload):
            if raw != json.dumps(payload, separators=(',', ':')) + '\n':
                raise ValueError('utility failure encoding invalid')
            if process.returncode == 0:
                raise ValueError('utility failure cannot be successful exit')
            safe_payload = True
            row['probeFailure'] = payload
            row['raw'], row['rawSha256'] = raw, row['outputSha256']
            row['firstFailure'] = {'category': 'ProbeUtilityFailure', 'reason': payload['reason'],
                                   'utilityCategory': payload['category'], 'errno': payload['errno'],
                                   'failedRead': payload['failedRead']}
            raise ValueError('readonly utility failed; safe original cause retained')
        if process.returncode != 0 or error:
            raise ValueError('readonly probe nonzero or unexpected stderr')
        row['raw'], row['rawSha256'], row['payload'] = raw, row['outputSha256'], payload
        if payload['daemon']['pid'] != pid or payload['daemon']['birth'] != birth:
            raise ValueError('probe target generation changed')
        if not probe_receipt(row, terminal=False):
            raise ValueError('probe source or original ancestry invalid')
        safe_payload = True
        row['absence'] = probe_absence(payload['chain'][:-1])
        return payload['daemon']['exe']
    except Exception as failure:
        if not safe_payload:
            row['raw'], row['rawSha256'], row['payload'] = '', '', None
        if row['firstFailure'] is None:
            row['firstFailure'] = {'category': type(failure).__name__}
        if process is not None and not row['joined']:
            try:
                process.wait(timeout=1)
                row['exitCode'], row['joined'] = process.returncode, True
            except subprocess.TimeoutExpired:
                pass  # No general privileged kill or unknown-child absence claim.
        raise


class DockerConnection(http.client.HTTPConnection):
    def __init__(self, timeout=30):
        super().__init__('localhost', timeout=timeout)
        self.active_socket = None

    def connect(self):
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.active_socket = self.sock
        self.sock.settimeout(self.timeout)
        read_site('docker-socket-connect', '/var/run/docker.sock',
                  lambda: self.sock.connect('/var/run/docker.sock'))

    def stop(self):
        # Retain the exact socket even when HTTPConnection hands its file to HTTPResponse.
        try:
            if self.active_socket is not None and self.active_socket.fileno() >= 0:
                try:
                    self.active_socket.shutdown(socket.SHUT_RDWR)
                finally:
                    self.active_socket.close()
        finally:
            self.close()


class Docker:
    def __init__(self, probes=None):
        self.probes = probes if probes is not None else []
        self.probe_lock = threading.Lock()
        self.connections = {}
        self.lock = threading.Lock()

    def cancel_reads(self, thread_id):
        with self.lock:
            connections = list(self.connections.get(thread_id, []))
        for connection in connections:
            connection.stop()

    def daemon(self):
        # Serialize utility cohorts; no probe may outlive this identity observation.
        with self.probe_lock:
            pid = int(read_site('daemon-pid-read', '/var/run/docker.pid',
                                lambda: pathlib.Path('/var/run/docker.pid').read_text()).strip())
            if type(pid) is not int or not 0 < pid <= 2147483647:
                raise ValueError('invalid daemon PID')
            def snapshot():
                current = int(read_site('daemon-pid-read', '/var/run/docker.pid',
                                       lambda: pathlib.Path('/var/run/docker.pid').read_text()).strip())
                fields = read_site('daemon-stat-read', f'/proc/{pid}/stat',
                                   lambda: pathlib.Path(f'/proc/{pid}/stat').read_text()).rsplit(')', 1)[1].split()
                inode = read_site('daemon-socket-stat', '/var/run/docker.sock',
                                  lambda: os.stat('/var/run/docker.sock')).st_ino
                return (current, fields[19], self.get('/info')['ID'], inode)
            before = snapshot()
            if before[0] != pid:
                raise ValueError('daemon PID changed before observation')
            executable = read_site('daemon-exe-readlink', f'/proc/{pid}/exe',
                                   lambda: daemon_probe(pid, before[1], self.probes))
            after = snapshot()
            row = self.probes[-1]
            row['daemonBefore'] = {'id': before[2], 'pid': pid, 'birth': before[1],
                                   'exe': executable, 'socketInode': before[3]}
            row['daemonAfter'] = {'id': after[2], 'pid': after[0], 'birth': after[1],
                                  'exe': executable, 'socketInode': after[3]}
            if after != before or not probe_receipt(row):
                row['firstFailure'] = {'category': 'DaemonProbeIdentityOrClosureInvalid'}
                raise ValueError('daemon generation or probe closure changed across privileged read')
            return row['daemonBefore']

    def get(self, route):
        connection = DockerConnection()
        caller = threading.get_ident()
        with self.lock:
            self.connections.setdefault(caller, []).append(connection)
        try:
            connection.request('GET', route)
            response = connection.getresponse()
            if response.status != 200:
                raise ValueError('Docker read unsuccessful')
            return json.loads(response.read())
        finally:
            connection.close()
            with self.lock:
                self.connections[caller].remove(connection)

    def census(self):
        daemon = self.daemon()
        result = {'daemon': daemon, 'success': True,
                'containers': sorted(item['Id'] for item in self.get('/containers/json?all=1')),
                'volumes': sorted(item['Name'] for item in self.get('/volumes').get('Volumes') or []),
                'networks': sorted(item['Id'] for item in self.get('/networks'))}
        if self.daemon() != daemon:
            raise ValueError('daemon generation changed during census')
        return result


def process_identity(pid, expected_session=None):
    if type(pid) is not int or not 0 < pid <= 2147483647:
        raise ValueError('invalid process PID')
    entry = pathlib.Path('/proc') / str(pid)
    try:
        fields = read_site('process-stat-read', f'/proc/{pid}/stat',
                           lambda: (entry / 'stat').read_text()).rsplit(')', 1)[1].split()
    except FileNotFoundError:
        return None
    if expected_session is not None and int(fields[3]) != expected_session:
        return None  # A known foreign session needs no executable read or enrollment.
    try:
        executable = read_site('process-exe-readlink', f'/proc/{pid}/exe', lambda: os.readlink(entry / 'exe'))
    except FileNotFoundError:
        executable = None  # A still-present zombie is not absence.
    return {'pid': pid, 'birth': fields[19], 'session': int(fields[3]), 'exe': executable}


def group_members(session):
    members = []
    for entry in pathlib.Path('/proc').iterdir():
        if not entry.name.isdecimal():
            continue
        try:
            member = process_identity(int(entry.name), expected_session=session)
            if member is not None and member['session'] == session:
                members.append(member)
        except FileNotFoundError:
            continue
    return sorted(members, key=lambda item: item['pid'])


def signal_original(member, session, sig):
    """Signal only an admitted original PID generation through its own pidfd."""
    if member['session'] != session or process_identity(member['pid']) != member:
        raise ValueError('owned process generation changed before signal')
    descriptor = os.pidfd_open(member['pid'])
    try:
        if process_identity(member['pid']) != member:
            raise ValueError('owned process generation changed during pidfd admission')
        signal.pidfd_send_signal(descriptor, sig)
    finally:
        os.close(descriptor)


def settle_process(row, process):
    """Preserve the first failure; settle original leader and surviving session children."""
    # A closed original session is sealed; a reused session number is not ours.
    if row.get('joined') is True and row.get('remaining') == []:
        return
    actions = row['settlement']
    if row['settlementStarted'] is None:
        row['settlementStarted'] = time.monotonic()
        row['settlementDeadline'] = row['settlementStarted'] + 30
    for sig, deadline in ((signal.SIGTERM, row['settlementStarted'] + 15), (signal.SIGKILL, row['settlementDeadline'])):
        members = group_members(row['session'])
        original = {key: row[key] for key in ('pid', 'birth', 'session', 'exe')}
        leader = next((member for member in members if member['pid'] == row['pid']), None)
        if leader is not None and leader != original:
            row['cleanupErrors'].append('OriginalLeaderGenerationChanged')
            break  # Reject the entire replacement session before any pidfd or signal.
        if not members and process.poll() is not None:
            break
        if time.monotonic() >= deadline:
            continue  # Phase revisits share the original budget; never restart it.
        if not row['birth']:
            row['cleanupErrors'].append('MissingOriginalProcessAdmission')
            break  # Never adopt a session whose original leader generation was not admitted.
        for member in members:
            try:
                signal_original(member, row['session'], sig)
                actions.append({'pid': member['pid'], 'birth': member['birth'], 'session': member['session'],
                                'exe': member['exe'], 'signal': int(sig), 'sent': True})
            except (ProcessLookupError, FileNotFoundError):
                actions.append({'pid': member['pid'], 'birth': member['birth'], 'session': member['session'],
                                'exe': member['exe'], 'signal': int(sig), 'sent': False})
            except Exception as error:
                row['cleanupErrors'].append(type(error).__name__)
        while time.monotonic() < deadline:
            process.poll()  # Reap the original leader without waiting for another generation.
            if not group_members(row['session']) and process.poll() is not None:
                break
            threading.Event().wait(0.05)
    row['exitCode'] = process.poll()
    row['joined'] = row['exitCode'] is not None
    row['remaining'] = group_members(row['session'])
    if row['remaining'] or not row['joined']:
        row['cleanupErrors'].append('OriginalProcessSessionUnsettled')


class CommandFailure(RuntimeError):
    pass


class AdmissionFailure(RuntimeError):
    pass


class Phase:
    """The caller must settle every phase even when a native command fails."""
    def __init__(self, actor_policy, private):
        self.policy = actor_policy
        self.private = pathlib.Path(private)
        self.private.mkdir(parents=True, exist_ok=True)
        self.probes = []
        self.docker = Docker(self.probes)
        self.baseline = {'daemon': None, 'success': False, 'containers': [], 'volumes': [], 'networks': []}
        self.events, self.resources, self.errors, self.foreign, self.processes = [], {}, [], [], []
        self.limit = False
        self.started = time.time_ns()
        self.ready = threading.Event()
        self.fence_stop = threading.Event()
        self.fences = []
        self.fenced_until = self.started
        self.anchor = None
        self.stopping = False
        self.first_failure = None
        self.commands = []
        self.connection = DockerConnection(timeout=30)
        self.reader = None
        self.fencer = None
        self.fence_connection = None
        try:
            self.baseline = self.docker.census()
            self.reader = threading.Thread(target=self._observe, name='owned-graph-docker-events', daemon=False)
            self.reader.start()
            if not self.ready.wait(30) or self.errors:
                raise AdmissionFailure('Docker reader not admitted')
            self.fencer = threading.Thread(target=self._periodic_fences, name='owned-graph-event-fences', daemon=False)
            self.fencer.start()
        except Exception as error:
            self.first_failure = read_failure('observer-startup', error)
            self.errors.append('ObserverAdmissionFailed')
            self._stop_readers(final_fence=False)
            final = self._final_census()
            self._write_receipt(final)
            raise AdmissionFailure('Observer startup failed; owned reader settlement retained') from error

    def _route(self, until=None):
        parameters = {'since': str(self.started // 1_000_000_000)}
        if until is not None:
            parameters['until'] = str(until / 1_000_000_000)
        return '/events?' + urllib.parse.urlencode(parameters)

    def _observe(self):
        try:
            self.connection.request('GET', self._route())
            response = self.connection.getresponse()
            if response.status != 200:
                raise ValueError('Docker stream not admitted')
            self.connection.active_socket.settimeout(86400)
            self.ready.set()  # Actual HTTP response headers; precedes every SDK dispatch.
            while not self.stopping:
                line = response.readline(1_048_577)
                if not line:
                    if not self.stopping:
                        raise ValueError('Docker stream ended unexpectedly')
                    break
                if len(line) > 1_048_576 or len(self.events) >= 16384:
                    self.limit = True
                    raise ValueError('Docker event limit')
                event = json.loads(line)
                if event['timeNano'] <= self.started:
                    continue
                projected = {key: event.get(key) for key in ('Type', 'Action', 'timeNano')}
                projected['id'] = event['Actor']['ID']
                self.events.append(projected)
                self._resource(event)
        except Exception as error:
            if not self.stopping:
                self.errors.append(type(error).__name__)
        finally:
            self.connection.close()

    def _fence(self, end):
        start = self.fenced_until
        connection = DockerConnection(timeout=30)
        self.fence_connection = connection
        success = False
        replay = []
        observed = []
        history = []
        anchor = self.anchor
        prefix = False
        anchor_present = False
        try:
            # UNFILTERED entire daemon history: filtering occurs after Docker's 256-event ring.
            # Preserve an actual prior boundary event, rather than assuming a timer avoids bursts.
            query = {'until': str((end // 1_000_000_000) + 1)}
            connection.request('GET', '/events?' + urllib.parse.urlencode(query))
            response = connection.getresponse()
            if response.status != 200:
                raise ValueError('event replay fence failed')
            raw = response.read(4_194_305)
            if len(raw) > 4_194_304:
                raise ValueError('event replay byte limit')
            for line in raw.splitlines():
                event = json.loads(line)
                projected = {'Type': event.get('Type'), 'Action': event.get('Action'),
                             'timeNano': event.get('timeNano'), 'id': event['Actor']['ID']}
                history.append(projected)
                if start < event['timeNano'] <= end:
                    replay.append(projected)
            prefix = len(history) < 256 or (bool(history) and history[0]['timeNano'] <= self.started)
            anchor_present = anchor in history if anchor is not None else prefix
            deadline = time.monotonic() + 5
            while True:
                observed = [event for event in self.events if start < event['timeNano'] <= end]
                if observed == replay or time.monotonic() >= deadline:
                    break
                threading.Event().wait(0.05)
            success = (anchor_present and len(replay) < 200 and observed == replay and
                       self.docker.daemon() == self.baseline['daemon'])
            if not success:
                self.errors.append('EventReplayGapOverflowOrDaemonChange')
        except Exception as error:
            self.errors.append(type(error).__name__)
        finally:
            connection.close()
            self.fence_connection = None
            self.fences.append({'startNano': start, 'endNano': end, 'eventCount': len(observed),
                                'streamSha256': hashlib.sha256(json.dumps(observed, sort_keys=True).encode()).hexdigest(),
                                'replaySha256': hashlib.sha256(json.dumps(replay, sort_keys=True).encode()).hexdigest(),
                                'historyCount': len(history), 'historyOldestNano': history[0]['timeNano'] if history else None,
                                'anchor': anchor, 'anchorPresent': anchor_present, 'initialPrefixComplete': prefix,
                                'nextAnchor': observed[-1] if observed else anchor, 'success': success})
            if observed:
                self.anchor = observed[-1]
            self.fenced_until = end

    def _periodic_fences(self):
        while not self.fence_stop.wait(4):
            self._fence(time.time_ns())

    def _resource(self, event):
        kind = {'container': 'containers', 'volume': 'volumes', 'network': 'networks'}.get(event['Type'])
        action, key = event['Action'], event['Actor']['ID']
        if kind is None:
            if event['Type'] == 'image' and action == 'pull':
                try:
                    image = self.docker.get('/images/' + urllib.parse.quote(key, safe='') + '/json')
                    if set(image.get('RepoTags') or []) & set(self.policy['images']):
                        return  # Source-expected image cache boundary, not an owned resource cleanup claim.
                except Exception:
                    self.errors.append('UnknownImageCacheIdentity')
            self.foreign.append('unexpected-daemon-actor')
            return
        if action == 'create':
            if key in self.resources or key in self.baseline[kind]:
                self.foreign.append('duplicate-or-baseline-create')
                return
            row = {'kind': kind, 'id': key, 'createdNano': event['timeNano'], 'terminalNano': 0,
                   'inspect': {}, 'session': '', 'daemon': self.baseline['daemon']}
            self.resources[key] = row
            try:
                route = {'containers': '/containers/', 'volumes': '/volumes/', 'networks': '/networks/'}[kind]
                value = self.docker.get(route + urllib.parse.quote(key, safe='') + ('/json' if kind == 'containers' else ''))
                if kind == 'containers':
                    labels = value['Config'].get('Labels') or {}
                    session = labels.get(self.policy['sessionLabel'], '')
                    image = value['Config']['Image']
                    reaper = '00000000-0000-0000-0000-000000000000' if image == self.policy['ryukImage'] else session
                    if not session or image not in self.policy['images'] or labels.get(self.policy['reaperLabel']) != reaper or any(labels.get(name) != expected for name, expected in self.policy['requiredLabels'].items()):
                        self.foreign.append('foreign-container')
                    row['session'] = session
                    row['inspect'] = {'id': value['Id'], 'kind': kind, 'generation': value['Created'],
                                      'image': value['Image'], 'configImage': image,
                                      'labels': {name: labels[name] for name in [self.policy['sessionLabel'], self.policy['reaperLabel'], *self.policy['requiredLabels']] if name in labels},
                                      'volumes': sorted(mount['Name'] for mount in value['Mounts'] if mount['Type'] == 'volume')}
                else:
                    row['inspect'] = {'id': value.get('Id', value.get('Name')), 'kind': kind,
                                      'generation': value.get('Created', value.get('CreatedAt'))}
                    # Anonymous volumes require actual owned-container mount attribution at settlement.
            except Exception:
                self.errors.append('MissingOriginalInspectIdentity')
        elif action in ('destroy', 'remove'):
            if key in self.resources:
                self.resources[key]['terminalNano'] = event['timeNano']
            elif key in self.baseline.get(kind, []):
                self.foreign.append('baseline-object-destroyed')
            else:
                self.foreign.append('terminal-without-original-create')
        elif key not in self.resources:
            attributes = event['Actor'].get('Attributes') or {}
            container = attributes.get('container')
            if not (kind == 'networks' and key in self.baseline['networks'] and action in ('connect', 'disconnect') and container in self.resources and self.resources[container]['kind'] == 'containers'):
                self.foreign.append('event-without-original-create')

    def run(self, arguments, cwd, environment, name, timeout=1800):
        if self.errors or self.foreign or self.limit or self.first_failure is not None:
            if self.first_failure is None:
                self.first_failure = {'gate': name, 'category': 'ObserverFailureBeforeDispatch',
                                      'reason': self.errors[0] if self.errors else 'UnexpectedActorOrEventLimit'}
            raise CommandFailure('observer failed before SDK dispatch')
        log = self.private / (name + '.log')
        row = None
        primary = None
        with log.open('wb') as output:
            try:
                process = subprocess.Popen(arguments, cwd=cwd, env=environment, stdin=subprocess.DEVNULL,
                                           stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
            except Exception as error:
                self.first_failure = {'gate': name, 'category': type(error).__name__}
                raise
            row = {'pid': process.pid, 'birth': '', 'exe': '', 'session': process.pid,
                   'exitCode': None, 'joined': False, 'remaining': [], 'firstFailure': None,
                   'cleanupErrors': [], 'settlement': [], 'settlementStarted': None, 'settlementDeadline': None,
                   'name': name, 'logSha256': '',
                   'argumentsSha256': hashlib.sha256(json.dumps(arguments).encode()).hexdigest()}
            self.processes.append(row)
            self.commands.append((row, process))
            try:
                original = process_identity(process.pid)
                if original is None or original['session'] != process.pid:
                    raise CommandFailure('original process admission unavailable')
                row.update(original)
                if not row['exe']:
                    raise CommandFailure('original executable identity unavailable')
                row['exitCode'] = process.wait(timeout=timeout)
                if row['exitCode'] != 0:
                    raise CommandFailure('original command exit nonzero')
                if group_members(row['session']):
                    raise CommandFailure('original descendants remain after leader exit')
            except Exception as error:
                primary = error
                row['firstFailure'] = read_failure(name, error)
                row['firstFailure']['reason'] = str(error) if isinstance(error, CommandFailure) else type(error).__name__
                if self.first_failure is None:
                    self.first_failure = row['firstFailure']
            finally:
                try:
                    settle_process(row, process)
                except Exception as error:
                    row['cleanupErrors'].append(type(error).__name__)
        row['logSha256'] = hashlib.sha256(log.read_bytes()).hexdigest()
        # The log descriptor has now closed; settlement must never erase native failure.
        if primary is not None:
            raise CommandFailure('original command failed: ' + name) from primary
        if row['remaining'] or row['cleanupErrors'] or not row['joined']:
            if self.first_failure is None:
                self.first_failure = {'gate': name, 'category': 'CommandSettlementFailed'}
            raise CommandFailure('owned command settlement incomplete: ' + name)
        return log

    def _stop_readers(self, final_fence):
        self.fence_stop.set()
        if self.fencer is not None:
            try:
                self.fencer.join(38)
                if self.fencer.is_alive():
                    # Cancel only this retained fence thread's original sockets within its40s budget.
                    if self.fence_connection is not None:
                        self.fence_connection.stop()
                    self.docker.cancel_reads(self.fencer.ident)
                    self.fencer.join(2)
                if self.fencer.is_alive():
                    self.errors.append('FenceReaderUnsettled')
                elif final_fence:
                    self._fence(time.time_ns())
            except Exception as error:
                self.errors.append(type(error).__name__)
        self.stopping = True
        try:
            self.connection.stop()
            if self.reader is not None:
                self.docker.cancel_reads(self.reader.ident)
        except Exception as error:
            self.errors.append(type(error).__name__)
        if self.reader is not None:
            try:
                self.reader.join(35)
                if self.reader.is_alive():
                    self.errors.append('ObserverReaderUnsettled')
            except Exception as error:
                self.errors.append(type(error).__name__)

    def _final_census(self):
        try:
            return self.docker.census()
        except Exception as error:
            self.errors.append(type(error).__name__)
            return {'daemon': None, 'success': False, 'containers': [], 'volumes': [], 'networks': []}

    def _write_receipt(self, final):
        receipt = {'daemon': self.baseline['daemon'], 'baseline': self.baseline, 'final': final,
                   'admitted': self.ready.is_set(),
                   'readerJoined': self.reader is None or not self.reader.is_alive(),
                   'readerErrors': self.errors, 'processes': self.processes,
                   'resources': list(self.resources.values()), 'foreignEvents': self.foreign,
                   'limitExceeded': self.limit, 'fences': self.fences,
                   'fencerJoined': self.fencer is None or not self.fencer.is_alive(),
                   'firstFailure': self.first_failure, 'daemonProbes': self.probes}
        (self.private / 'cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
        return receipt

    def settle(self):
        # Revisit every retained original command, including leader-exited failures.
        for row, process in self.commands:
            try:
                if row.get('joined') is True and row.get('remaining') == []:
                    continue  # Never re-enroll a completed original session.
                if process.poll() is None or group_members(row['session']):
                    if row['firstFailure'] is None:
                        row['firstFailure'] = {'gate': row['name'], 'category': 'ProcessRemainingAtPhaseSettlement'}
                    if self.first_failure is None:
                        self.first_failure = row['firstFailure']
                    settle_process(row, process)
            except Exception as error:
                row['cleanupErrors'].append(type(error).__name__)
        # Fresh daemon census is the absence oracle; never inspect NotFound.
        deadline = time.monotonic() + 30
        final = self._final_census()
        while final['success'] and final != self.baseline and time.monotonic() < deadline:
            threading.Event().wait(0.2)
            final = self._final_census()
        self._stop_readers(final_fence=True)
        final = self._final_census()
        for row in self.resources.values():
            if row['kind'] == 'volumes':
                owners = [item for item in self.resources.values() if item['kind'] == 'containers' and
                          row['id'] in item['inspect'].get('volumes', [])]
                sessions = {item['session'] for item in owners}
                if len(sessions) == 1:
                    row['session'] = next(iter(sessions))
                else:
                    self.foreign.append('unattributed-volume')
            elif row['kind'] == 'networks':
                self.foreign.append('unexpected-created-network')
        receipt = self._write_receipt(final)
        if not cleanup(receipt):
            raise CommandFailure('graph process/resource closure incomplete; first failure retained')
        return receipt
