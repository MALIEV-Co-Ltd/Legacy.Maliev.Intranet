"""Fixed read-only Linux daemon observation; stdout is the sole output channel."""
import json
import os
import signal
import sys
import time



FAILURE_REASONS = {
    'invalid canonical PID': 'invalid-pid', 'process stat bound': 'stat-bound',
    'process generation changed': 'generation-changed', 'invalid original launcher': 'invalid-launcher',
    'daemon PID changed': 'daemon-pid-changed', 'daemon generation changed': 'daemon-generation-changed',
    'cyclic helper ancestry': 'cyclic-ancestry', 'supervisor generation changed': 'supervisor-generation-changed',
    'helper ancestry bound': 'ancestry-bound', 'original launcher not in helper ancestry': 'launcher-unbound',
    'helper session not isolated': 'session-not-isolated', 'probe child-list bound': 'children-bound',
    'unknown or changed probe cohort': 'cohort-unknown-or-changed',
    'daemon PID changed after observation': 'daemon-pid-changed-after',
    'daemon identity changed after observation': 'daemon-identity-changed-after',
    'readonly probe expiry': 'probe-expired', 'fixed probe arguments required': 'invalid-arguments',
    'invalid process birth': 'invalid-birth', 'launcher handshake bound': 'handshake-bound'}
CATEGORIES = {'ValueError', 'PermissionError', 'FileNotFoundError', 'ProcessLookupError',
              'TimeoutError', 'OSError', 'UnicodeDecodeError', 'JSONDecodeError', 'BrokenPipeError'}


def allowed_site(operation, path):
    if operation == 'daemon-pid-read':
        return path == '/var/run/docker.pid'
    parts = path.split('/') if isinstance(path, str) else []
    suffix = {'daemon-stat-read': 'stat', 'daemon-exe-readlink': 'exe',
              'probe-stat-read': 'stat', 'probe-exe-readlink': 'exe'}
    try:
        if len(parts) == 4 and parts[:2] == ['', 'proc'] and operation in suffix and parts[3] == suffix[operation]:
            positive(parts[2])
            return True
        if operation == 'probe-children-read' and len(parts) == 6 and parts[:2] == ['', 'proc'] and parts[3] == 'task' and parts[2] == parts[4] and parts[5] == 'children':
            positive(parts[2])
            return True
    except ValueError:
        pass
    return False


def read_site(operation, path, callback):
    if not allowed_site(operation, path):
        raise ValueError('invalid canonical PID')
    try:
        return callback()
    except OSError as error:
        error.probe_read_site = {'operation': operation, 'path': path}
        raise


def text(path, maximum):
    with open(path, encoding='ascii') as stream:
        return stream.read(maximum)


def failure(error):
    category = type(error).__name__
    reason = FAILURE_REASONS.get(str(error), 'os-read-failed' if isinstance(error, OSError) else 'unclassified-probe-failure')
    errno = error.errno if isinstance(error, OSError) and type(error.errno) is int and 0 < error.errno <= 4095 else None
    site = getattr(error, 'probe_read_site', None)
    if not isinstance(site, dict) or set(site) != {'operation', 'path'} or not allowed_site(site['operation'], site['path']):
        site = None
    return {'schema': 1, 'status': 'failure', 'complete': False, 'reason': reason,
            'category': category if category in CATEGORIES else ('OSError' if isinstance(error, OSError) else 'ProbeError'), 'errno': errno, 'failedRead': site}


def positive(value):
    if not isinstance(value, str) or not value.isascii() or not value.isdecimal() or str(int(value)) != value or not 0 < int(value) <= 2147483647:
        raise ValueError('invalid canonical PID')
    return int(value)


def process(pid, operation='probe'):
    positive(str(pid))
    path = '/proc/' + str(pid)
    before = read_site(operation + '-stat-read', path + '/stat', lambda: text(path + '/stat', 8193))
    if len(before) > 8192:
        raise ValueError('process stat bound')
    fields = before.rsplit(')', 1)[1].split()
    row = {'pid': pid, 'birth': fields[19], 'ppid': int(fields[1]),
           'session': int(fields[3]),
           'exe': read_site(operation + '-exe-readlink', path + '/exe', lambda: os.readlink(path + '/exe'))}
    after = read_site(operation + '-stat-read', path + '/stat', lambda: text(path + '/stat', 8193))
    if len(after) > 8192:
        raise ValueError('process stat bound')
    other = after.rsplit(')', 1)[1].split()
    if row['birth'] != other[19] or row['ppid'] != int(other[1]) or row['session'] != int(other[3]) or not row['birth'].isdecimal() or not row['exe'].startswith('/') or len(row['exe']) > 4096:
        raise ValueError('process generation changed')
    return row


def observe(owner_pid, owner_birth, daemon_pid, daemon_birth, launcher):
    if set(launcher) != {'pid', 'birth', 'session'} or type(launcher['pid']) is not int or launcher['session'] != launcher['pid'] or not isinstance(launcher['birth'], str) or not launcher['birth'].isdecimal():
        raise ValueError('invalid original launcher')
    positive(str(launcher['pid']))
    original = positive(read_site('daemon-pid-read', '/var/run/docker.pid', lambda: text('/var/run/docker.pid', 32)).strip())
    if original != daemon_pid:
        raise ValueError('daemon PID changed')
    daemon = process(original, operation='daemon')
    if daemon['birth'] != daemon_birth:
        raise ValueError('daemon generation changed')
    chain, seen = [], set()
    current = os.getpid()
    for _ in range(8):
        if current in seen:
            raise ValueError('cyclic helper ancestry')
        seen.add(current)
        row = process(current)
        chain.append(row)
        if current == owner_pid:
            if row['birth'] != owner_birth:
                raise ValueError('supervisor generation changed')
            break
        current = row['ppid']
    else:
        raise ValueError('helper ancestry bound')
    if len(chain) < 3 or not any({key: row[key] for key in launcher} == launcher for row in chain[:-1]):
        raise ValueError('original launcher not in helper ancestry')
    if any(row['session'] == chain[-1]['session'] for row in chain[:-1]):
        raise ValueError('helper session not isolated')
    for index, row in enumerate(chain):
        if index == len(chain) - 1:
            row['children'] = None  # The unprivileged supervisor is not a probe cohort.
            continue
        expected = [] if index == 0 else [chain[index - 1]['pid']]
        child_path = '/proc/' + str(row['pid']) + '/task/' + str(row['pid']) + '/children'
        raw_children = read_site('probe-children-read', child_path, lambda: text(child_path, 257))
        if len(raw_children) > 256:
            raise ValueError('probe child-list bound')
        children = [positive(value) for value in raw_children.split()]
        if children != expected or process(row['pid']) != row:
            raise ValueError('unknown or changed probe cohort')
        row['children'] = children
    if positive(read_site('daemon-pid-read', '/var/run/docker.pid', lambda: text('/var/run/docker.pid', 32)).strip()) != original:
        raise ValueError('daemon PID changed after observation')
    if process(original, operation='daemon') != daemon:
        raise ValueError('daemon identity changed after observation')
    return {'schema': 1, 'daemon': {'pid': daemon['pid'], 'birth': daemon['birth'], 'exe': daemon['exe']},
            'chain': chain}


def expire(signum, frame):
    raise TimeoutError('readonly probe expiry')


def main():
    signal.signal(signal.SIGALRM, expire)
    signal.alarm(5)
    started = time.monotonic_ns()
    try:
        if len(sys.argv) != 5:
            raise ValueError('fixed probe arguments required')
        owner_pid, daemon_pid = positive(sys.argv[1]), positive(sys.argv[3])
        owner_birth, daemon_birth = sys.argv[2], sys.argv[4]
        if not owner_birth.isascii() or not owner_birth.isdecimal() or not daemon_birth.isascii() or not daemon_birth.isdecimal():
            raise ValueError('invalid process birth')
        raw = sys.stdin.buffer.readline(257)
        if len(raw) > 256 or not raw.endswith(b'\n'):
            raise ValueError('launcher handshake bound')
        result = observe(owner_pid, owner_birth, daemon_pid, daemon_birth, json.loads(raw))
        result.update({'startedNs': started, 'completedNs': time.monotonic_ns(), 'expirySeconds': 5})
        sys.stdout.write(json.dumps(result, separators=(',', ':')) + '\n')
        sys.stdout.flush()
        return 0
    except Exception as error:
        # Allowlisted diagnostic only: never private exception text or partial success.
        try:
            sys.stdout.write(json.dumps(failure(error), separators=(',', ':')) + '\n')
            sys.stdout.flush()
        except Exception:
            pass  # Parent still retains nonzero status and hashed, never raw, unknown output.
        return 1


if __name__ == '__main__':
    sys.exit(main())
