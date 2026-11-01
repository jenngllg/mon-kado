"""Disposable native Go/PostgreSQL/Caddy qualification on an isolated Linux namespace."""
import base64
import csv
import ctypes
import hashlib
import json
import os
import pwd
import re
import secrets
import shutil
import subprocess
import sys
import time
from contextlib import contextmanager
from pathlib import Path

from cgroup import operate
from policy import MEMORY, PerformanceError, require, run_id
from seed import dataset, png

PG = Path('/usr/lib/postgresql/18/bin')
SERVICES = ('api', 'worker', 'postgres', 'caddy')


def command(arguments, *, data=None, timeout=180, include_stderr=False, environment=None):
    try:
        result = subprocess.run(arguments, input=data, capture_output=True,
                                text=not isinstance(data, bytes), timeout=timeout,
                                check=False, env=environment)
    except (OSError, subprocess.TimeoutExpired):
        raise PerformanceError('COMMAND_UNAVAILABLE') from None
    require(result.returncode == 0, 'COMMAND_FAILED')
    output = result.stdout + result.stderr if include_stderr else result.stdout
    return (output.decode('utf-8', errors='replace') if isinstance(output, bytes) else output).strip()


def private_write(path, content):
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    with path.open('x', encoding='utf-8') as stream:
        os.chmod(path, 0o600)
        stream.write(content)


def protect_directory(path):
    if sys.platform == 'win32':
        identity = next(csv.reader([command(['whoami', '/user', '/fo', 'csv', '/nh'])]))[-1]
        require(re.fullmatch(r'S-1-[0-9-]+', identity) is not None, 'OWNER_ID_UNAVAILABLE')
        command(['icacls', str(path), '/inheritance:r', '/grant:r', '*' + identity + ':(OI)(CI)F', '*S-1-5-18:(OI)(CI)F'])


@contextmanager
def keep_awake():
    windows = sys.platform == 'win32'
    if windows:
        require(bool(ctypes.windll.kernel32.SetThreadExecutionState(0x80000001)), 'WAKE_LOCK_UNAVAILABLE')
    try:
        yield
    finally:
        if windows:
            ctypes.windll.kernel32.SetThreadExecutionState(0x80000000)


class NativeBench:
    def __init__(self, source, identifier):
        self.source = Path(source).resolve()
        self.identifier = run_id(identifier)
        self.directory = self.source / 'TestResults' / 'performance' / self.identifier
        self.source_code = self.source / 'src' / 'Operations.Performance'
        self.code = self.directory / 'code'
        self.cg_created = False
        self.driver = 'systemd'
        self.stage = 'preflight'

    @property
    def parent(self):
        return self.identifier.replace('-', '') + '.slice'

    def unit(self, service):
        require(service in SERVICES, 'INVALID_SERVICE')
        return self.identifier + '-' + service + '.service'

    def git(self, *arguments):
        options = ['git', '--no-optional-locks', '-C', str(self.source)]
        reference = self.source / '.git'
        if reference.is_file() and 'microsoft' in os.uname().release.lower():
            # WSL shares Windows worktrees whose gitdir reference uses a drive path.
            value = reference.read_text().removeprefix('gitdir: ').strip().replace('\\', '/')
            if re.fullmatch(r'[A-Za-z]:/[^\r\n]+', value):
                directory = '/mnt/' + value[0].lower() + value[2:]
                options += ['--git-dir=' + directory, '-c', 'core.worktree=' + str(self.source)]
        return command([*options, *arguments])

    def local_only(self):
        require(sys.platform == 'linux' and os.geteuid() == 0, 'NATIVE_LINUX_ROOT_REQUIRED')
        require(Path('/proc/1/comm').read_text().strip() == 'systemd', 'SYSTEMD_REQUIRED')
        require({'cpu', 'memory'} <= set(Path('/sys/fs/cgroup/cgroup.controllers').read_text().split()), 'LINUX_CGROUP_V2_REQUIRED')
        for tool in ('go', 'k6', 'caddy', 'ip', 'systemd-run', 'runuser'):
            require(shutil.which(tool) is not None, 'NATIVE_TOOL_UNAVAILABLE')
        require((PG / 'initdb').is_file(), 'POSTGRESQL_18_REQUIRED')
        require(command(['k6', 'version']).startswith('k6 v2.3.0'), 'K6_VERSION_MISMATCH')
        require(command(['caddy', 'version']).startswith('v2.11.4'), 'CADDY_VERSION_MISMATCH')

    def namespace(self, *arguments, **options):
        return command(['ip', 'netns', 'exec', self.identifier, *arguments], **options)

    def sql(self, query):
        self.owned()
        return self.namespace(str(PG / 'psql'), '-X', '-qAt', '-v', 'ON_ERROR_STOP=1',
                              '-h', '127.0.0.1', '-U', 'postgres', '-d', self.identifier.replace('-', '_'), data=query)

    def owned(self):
        require(self.directory == Path('/tmp') / self.identifier and not self.directory.is_symlink(), 'OWNERSHIP_MISMATCH')
        marker = self.directory / 'ownership.json'
        require(marker.is_file() and not marker.is_symlink() and marker.stat().st_uid == 0, 'OWNERSHIP_MISMATCH')
        require(json.loads(marker.read_text()) == {'runId': self.identifier, 'schemaVersion': 1}, 'OWNERSHIP_MISMATCH')

    def group(self, action):
        require(action in ('status', 'remove'), 'INVALID_CGROUP_ACTION')
        return operate(action, self.identifier, root=Path('/sys/fs/cgroup'), driver='systemd')

    def start(self, service, arguments, *, user='nobody', environment=None, notify=False):
        options = ['--quiet', '--unit=' + self.unit(service), '--slice=' + self.parent,
                   '--property=Type=' + ('notify' if notify else 'exec'),
                   '--property=User=' + user, '--property=Group=' + ('postgres' if user == 'postgres' else 'nogroup'),
                   '--property=NetworkNamespacePath=/run/netns/' + self.identifier,
                   '--property=BindReadOnlyPaths=' + str(self.directory / 'hosts') + ':/etc/hosts',
                   '--property=NoNewPrivileges=yes', '--property=Restart=no', '--property=UMask=0077',
                   '--property=TimeoutStopSec=20s', '--property=KillMode=control-group',
                   '--property=ProtectSystem=strict', '--property=ProtectHome=yes',
                   '--property=ReadWritePaths=' + str(self.directory),
                   '--property=StandardOutput=null', '--property=StandardError=null']
        if environment is not None:
            options += ['--property=EnvironmentFile=' + str(environment)]
        if service == 'caddy':
            options += ['--property=AmbientCapabilities=CAP_NET_BIND_SERVICE']
        command(['systemd-run', *options, *arguments], timeout=60)

    def create(self):
        self.local_only()
        self.directory = Path('/tmp') / self.identifier
        self.code = self.directory / 'code'
        require(not self.directory.exists() and not Path('/run/systemd/system', self.parent).exists(), 'RUN_ALREADY_EXISTS')
        require(self.identifier not in command(['ip', 'netns', 'list']).split(), 'NAMESPACE_ALREADY_EXISTS')
        self.directory.mkdir(mode=0o711)
        self.directory.chmod(0o711)
        private_write(self.directory / 'ownership.json', json.dumps({'runId': self.identifier, 'schemaVersion': 1}))
        self.code.mkdir(mode=0o755)
        for path in self.source_code.iterdir():
            if path.suffix in ('.py', '.js'):
                shutil.copyfile(path, self.code / path.name)
                (self.code / path.name).chmod(0o644)
        private_write(self.directory / 'harness.json', json.dumps({path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in self.code.iterdir()}))
        postgres, app = pwd.getpwnam('postgres'), pwd.getpwnam('nobody')
        for name, owner in [('postgres', postgres), ('application', app), ('caddy', app)]:
            path = self.directory / name
            path.mkdir(mode=0o700)
            os.chown(path, owner.pw_uid, owner.pw_gid)
        private_write(self.directory / 'hosts', '127.0.0.1 localhost mk816.test\n')
        (self.directory / 'hosts').chmod(0o644)
        self.stage = 'build'
        environment = dict(os.environ, CGO_ENABLED='0')
        revision = self.git('rev-parse', 'HEAD')
        binaries = {}
        for service in ('api', 'worker', 'migrate'):
            path = self.directory / service
            command(['go', '-C', str(self.source), 'build', '-buildvcs=false', '-trimpath', '-ldflags=-s -w -X github.com/JennGllg/mon-kado/internal/buildinfo.Revision=' + revision, '-o', str(path), './cmd/' + service], timeout=600, environment=environment)
            path.chmod(0o755)
            binaries[service] = hashlib.sha256(path.read_bytes()).hexdigest()
        self.stage = 'namespace'
        hosts = Path('/etc/netns') / self.identifier
        require(not hosts.exists(), 'NAMESPACE_CONFIGURATION_EXISTS')
        hosts.mkdir(mode=0o755, parents=True)
        shutil.copyfile(self.directory / 'hosts', hosts / 'hosts')
        (hosts / 'hosts').chmod(0o644)
        command(['ip', 'netns', 'add', self.identifier])
        self.namespace('ip', 'link', 'set', 'lo', 'up')
        for shard in range(10):
            source, destination = '127.0.0.' + str(100+shard), '127.0.1.' + str(100+shard)
            self.namespace('ip','address','add',source+'/32','dev','lo')
            self.namespace('ip','address','add',destination+'/32','dev','lo')
            # k6 setup runs outside the VU address pool. Route-specific source addresses
            # keep preparation and measured traffic in the same per-shard quota scope.
            self.namespace('ip','route','replace','local',destination+'/32','dev','lo','src',source,'table','local')
        # The namespace has only loopback: no remote providers or host services can be reached.
        require(not self.namespace('ip', 'route', 'show'), 'REMOTE_ROUTE_REFUSED')
        self.stage = 'cgroup'
        private_write(Path('/run/systemd/system') / self.parent,
                      '[Unit]\nDescription=Disposable MonKado qualification\n[Slice]\nMemoryMax=' + str(MEMORY) + '\nMemorySwapMax=0\nCPUQuota=100%\n')
        command(['systemctl', 'daemon-reload'])
        command(['systemctl', 'start', self.parent])
        self.cg_created = True
        self.stage = 'postgres'
        data = self.directory / 'postgres' / 'data'
        command(['runuser', '-u', 'postgres', '--', str(PG / 'initdb'), '-D', str(data), '--auth=trust', '--no-locale', '--encoding=UTF8'], timeout=60)
        self.start('postgres', [str(PG / 'postgres'), '-D', str(data), '-c', 'listen_addresses=127.0.0.1', '-c', 'unix_socket_directories=', '-c', 'shared_buffers=64MB', '-c', 'max_connections=40', '-c', 'work_mem=2MB'], user='postgres', notify=True)
        database = self.identifier.replace('-', '_')
        self.namespace(str(PG / 'createdb'), '-h', '127.0.0.1', '-U', 'postgres', database)
        app_path = self.directory / 'application'
        values = {'APP_ENV':'production','DATABASE_URL':'postgres://postgres@127.0.0.1:5432/' + database + '?sslmode=disable',
                  'JWT_SIGNING_KEY':base64.b64encode(secrets.token_bytes(32)).decode(), 'DATA_ENCRYPTION_KEY':base64.b64encode(secrets.token_bytes(32)).decode(),
                  'HTTP_ADDRESS':'127.0.0.1:7000','ALLOWED_HOSTS':'mk816.test','ALLOWED_ORIGINS':'https://mk816.test',
                  'FRONTEND_ORIGIN':'https://mk816.test','PUBLIC_API_ORIGIN':'https://mk816.test','TRUSTED_PROXIES':'127.0.0.1/32',
                  'GOOGLE_ENABLED':'false','EMAIL_PROVIDER':'Disabled','WORKER_INTERVAL':'1s',
                  'IMAGE_PATH':str(app_path / 'images'),'EXPORT_PATH':str(app_path / 'exports'),
                  'OBSERVABILITY_ENABLED':'true','OBSERVABILITY_DIRECTORY':str(app_path / 'telemetry'),'APP_VERSION':revision,
                  'GOMEMLIMIT':'256MiB'}
        api_environment = self.directory / 'api.env'
        private_write(api_environment, '\n'.join(name + '=' + value for name, value in values.items()) + '\n')
        self.stage = 'migrations'
        self.namespace(str(self.directory / 'migrate'), environment={**os.environ, **values}, timeout=180)
        self.stage = 'seed'
        password = secrets.token_urlsafe(32) + '!Aa1'
        query, accounts = dataset(self.identifier, password, secrets.token_bytes(16))
        self.sql(query)
        self.stage = 'services'
        self.start('api', [str(self.directory / 'api')], environment=api_environment)
        worker_environment = self.directory / 'worker.env'
        private_write(worker_environment, '\n'.join(name + '=' + value for name, value in dict(values, GOMEMLIMIT='192MiB').items()) + '\n')
        self.start('worker', [str(self.directory / 'worker')], environment=worker_environment)
        caddy = (self.source / 'deployments/caddy/Caddyfile').read_text()
        caddy = caddy.replace('import /opt/monkado/deployments/caddy/frontend/*.caddy', '').replace('{$API_HOST} {', 'mk816.test {\n tls internal')
        caddy = caddy.replace('{\n', '{\n admin off\n skip_install_trust\n', 1)
        private_write(self.directory / 'Caddyfile', caddy)
        (self.directory / 'Caddyfile').chmod(0o644)
        caddy_environment = self.directory / 'caddy.env'
        private_write(caddy_environment, 'XDG_DATA_HOME=' + str(self.directory / 'caddy') + '\nXDG_CONFIG_HOME=' + str(self.directory / 'caddy') + '\n')
        self.start('caddy', [shutil.which('caddy'), 'run', '--config', str(self.directory / 'Caddyfile'), '--adapter', 'caddyfile'], environment=caddy_environment)
        self.namespace('python3', str(self.code / 'native_readiness.py'), str(self.directory), timeout=60)
        ca = self.directory / 'caddy/caddy/pki/authorities/local/root.crt'
        shutil.copyfile(ca, self.directory / 'ca.crt')
        (self.directory / 'ca.crt').chmod(0o600)
        for shard in range(10):
            private_write(self.directory / f'fixtures-{shard}.json', json.dumps({'accounts':accounts[shard*10:(shard+1)*10], 'password':password}))
            private_write(self.directory / f'k6-{shard}.json', json.dumps({'hosts':{'mk816.test':'127.0.1.' + str(100+shard)}}))
        (self.directory / 'image.png').write_bytes(png(128,128))
        (self.directory / 'large.png').write_bytes(png(8000,5000))
        private_write(self.directory / 'metadata.json', json.dumps({'schemaVersion':2,'runId':self.identifier,'revision':revision,
                      'dirty':bool(self.git('status','--porcelain')), 'host':os.uname().sysname + ' ' + os.uname().release,
                      'binaries':binaries,'caddy':command(['caddy','version']),'k6':command(['k6','version']),
                      'dataset':{'members':100,'lists':500,'wishes':10000,'images':200},'workerEnvironment':values['APP_ENV'],'externalProviders':False}))
        self.stage = 'prepare-images'
        self.prepare_images()
        return self.group('status')

    def prepare_images(self):
        for shard in range(10):
            self.namespace('env', 'MONKADO_PERFORMANCE_FIXTURE=' + str(self.directory),
                           'MONKADO_PERFORMANCE_SOURCE_IP=127.0.0.' + str(200+shard),
                           'python3', str(self.code / 'supplements.py'), 'prepare', str(shard), timeout=180)

    def inspect_health(self):
        self.owned()
        health = self.group('status')
        health.update(alive=True, restarts=0, images={})
        for service in SERVICES:
            fields = command(['systemctl','show',self.unit(service), '--property=ActiveState,NRestarts,Result,ControlGroup']).splitlines()
            state = dict(line.split('=',1) for line in fields if '=' in line)
            require(state.get('ControlGroup','').startswith('/' + self.parent + '/'), 'ISOLATION_MISMATCH')
            health['alive'] &= state.get('ActiveState') == 'active'
            health['restarts'] += int(state.get('NRestarts','0'))
            health['oom'] = max(health['oom'], int(state.get('Result') == 'oom-kill'))
        return health

    def verify_data(self):
        result = json.loads(self.sql("""SELECT json_build_object('members',(SELECT count(*) FROM users),
           'lists',(SELECT count(*) FROM wishlists),'wishes',(SELECT count(*) FROM wishes),
           'images',(SELECT count(*) FROM users WHERE profile_image_id IS NOT NULL)+(SELECT count(*) FROM wishes WHERE image_id IS NOT NULL),
           'overReserved',(SELECT count(*) FROM (SELECT w.id FROM wishes w JOIN gift_reservations r ON r.wish_id=w.id GROUP BY w.id,w.quantity HAVING sum(r.quantity)>w.quantity) violations));"""))
        require(result['members']==100 and result['lists']==500 and result['wishes']==10000 and result['images']>=200 and result['overReserved']==0, 'DATA_INVARIANT_FAILED')
        return result

    def generators(self, profile):
        processes = []
        for shard in range(10):
            output = self.directory / f'metrics-{shard}.jsonl'
            private_write(output, '')
            arguments = ['ip','netns','exec',self.identifier,'k6','run','--quiet','--no-usage-report',
                         '--config=' + str(self.directory / f'k6-{shard}.json'),'--local-ips=127.0.0.' + str(100+shard),
                         '--out=json=' + str(output),'-e','SHARD='+str(shard),'-e','PROFILE='+profile,
                         '-e','MONKADO_PERFORMANCE_FIXTURE='+str(self.directory),str(self.code / 'scenarios.js')]
            environment = {'PATH':os.environ['PATH'], 'HOME':str(self.directory), 'LANG':'C.UTF-8',
                           'SSL_CERT_FILE':str(self.directory / 'ca.crt'), 'NO_PROXY':'*'}
            # Closed environment prevents host debug/cloud settings from changing the campaign.
            diagnostics = self.directory / f'generator-{shard}.stderr'
            with diagnostics.open('x') as errors:
                diagnostics.chmod(0o600)
                processes.append(subprocess.Popen(arguments, env=environment, stdout=subprocess.DEVNULL, stderr=errors))
        return processes

    def start_supplement(self, profile):
        return subprocess.Popen(['ip','netns','exec',self.identifier,'env','MONKADO_PERFORMANCE_FIXTURE='+str(self.directory),
                                 'MONKADO_PERFORMANCE_SOURCE_IP=127.0.0.199','python3',str(self.code / 'supplements.py'),profile],
                                stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)

    def stop_processes(self, processes, supplement):
        for process in [*processes, *([] if supplement is None else [supplement])]:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=20)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=5)

    def failure_diagnostics(self):
        if not self.cg_created:
            return {}
        try:
            health = self.inspect_health()
            return {name:health[name] for name in ('alive','oom','restarts','qualified')}
        except (PerformanceError, OSError, ValueError, KeyError):
            return {'diagnosticsUnavailable':True}

    def cleanup(self):
        self.local_only()
        self.directory = Path('/tmp') / self.identifier
        self.owned()
        unit = Path('/run/systemd/system') / self.parent
        hosts = Path('/etc/netns') / self.identifier
        if unit.exists() or unit.is_symlink():
            require(not unit.is_symlink() and unit.stat().st_uid == 0 and
                    unit.read_text().startswith('[Unit]\nDescription=Disposable MonKado qualification\n[Slice]\n'), 'OWNERSHIP_MISMATCH')
        if hosts.exists() or hosts.is_symlink():
            require(not hosts.is_symlink() and hosts.stat().st_uid == 0 and
                    set(path.name for path in hosts.iterdir()) == {'hosts'}, 'OWNERSHIP_MISMATCH')
            source = hosts / 'hosts'
            require(not source.is_symlink() and source.stat().st_uid == 0 and
                    source.read_bytes() == (self.directory / 'hosts').read_bytes(), 'OWNERSHIP_MISMATCH')
        services = []
        for service in SERVICES:
            name = self.unit(service)
            fields = command(['systemctl','show',name,'--property=LoadState,ControlGroup,FragmentPath'])
            state = dict(line.split('=',1) for line in fields.splitlines() if '=' in line)
            if state.get('LoadState') == 'not-found':
                continue
            require(state.get('FragmentPath') == '/run/systemd/transient/' + name and
                    (not state.get('ControlGroup') or state['ControlGroup'].startswith('/' + self.parent + '/')), 'OWNERSHIP_MISMATCH')
            services.append(name)
        # Validate all targets before stopping anything; partial creation remains cleanable.
        for name in services:
            command(['systemctl','stop',name], timeout=60)
        if unit.exists():
            command(['systemctl','stop',self.parent])
            unit.unlink()
            command(['systemctl','daemon-reload'])
        if self.identifier in command(['ip','netns','list']).split():
            command(['ip','netns','delete',self.identifier])
        if hosts.exists():
            (hosts / 'hosts').unlink()
            hosts.rmdir()
        # Keep reports and stopped fixture data for diagnosis; never remove unrelated files.
