"""Qualify real native rollout, rollback and migration failure without a public target."""
from datetime import datetime, timezone
import copy
import hashlib
import json
import os
from pathlib import Path
import pwd
import secrets
import shutil
import ssl
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT/'src/Operations.Deployment'), str(ROOT/'src/Operations.Native'),
               str(ROOT/'src/Operations.Performance'), str(ROOT/'deployments/production')]
from deploy_engine import Engine
from deploy_policy import DeploymentError, initial_state
from deploy_process import command as bounded_command
from deploy_runtime import Runtime
from deploy_storage import Store, atomic_write
import native_artifacts
import native_services
from native_runtime import NativeBench, PG, command, private_write
from release_catalog import fingerprint
from release_manifest import configuration_hash
from smoke_functional import FunctionalSmoke
from smoke_http import Client


class Fixture(NativeBench):
    """Reuse the owned four-service fixture; this campaign never starts load generators."""
    def prepare_images(self):
        pass


def write_environment(path, values):
    atomic_write(path, ''.join(key+'='+value+'\n' for key, value in values.items()).encode())


def configure(bench):
    """Bind the production database contract to this fresh isolated PostgreSQL only."""
    bench.owned()
    command(['systemctl','stop',*[bench.unit(name) for name in ('api','worker','caddy')]])
    original = bench.identifier.replace('-','_')
    password = secrets.token_hex(32)
    bench.namespace(str(PG/'psql'),'-X','-qAt','-h','127.0.0.1','-U','postgres','-d','postgres',
                    '-v','ON_ERROR_STOP=1',data=f"CREATE ROLE mon_kado LOGIN PASSWORD '{password}'; ALTER DATABASE {original} RENAME TO mon_kado; ALTER DATABASE mon_kado OWNER TO mon_kado;")
    bench.namespace(str(PG/'psql'),'-X','-qAt','-h','127.0.0.1','-U','postgres','-d','mon_kado',
                    '-v','ON_ERROR_STOP=1',data="GRANT ALL ON SCHEMA public TO mon_kado; GRANT ALL ON ALL TABLES IN SCHEMA public TO mon_kado; GRANT ALL ON ALL SEQUENCES IN SCHEMA public TO mon_kado; INSERT INTO user_roles(user_id,role_id) SELECT u.id,r.id FROM users u CROSS JOIN roles r WHERE r.normalized_name='MEMBER';")
    installation, settings, state = [bench.directory/name for name in ('installation','settings','deployment')]
    for path in (installation, settings, state):
        path.mkdir(mode=0o755 if path == installation else 0o700)
    values = dict(line.split('=',1) for line in (bench.directory/'api.env').read_text().splitlines())
    values.update(DATABASE_URL='postgres://mon_kado:'+password+'@127.0.0.1:5432/mon_kado?sslmode=disable',
                  ALLOWED_HOSTS='api.monkado.fr', ALLOWED_ORIGINS='https://www.monkado.fr',
                  FRONTEND_ORIGIN='https://www.monkado.fr', PUBLIC_API_ORIGIN='https://api.monkado.fr',
                  OBSERVABILITY_DIRECTORY=str(bench.directory/'application/telemetry'))
    write_environment(settings/'production.env', values)
    write_environment(bench.directory/'api.env', values)
    write_environment(bench.directory/'worker.env', dict(values,GOMEMLIMIT='192MiB'))
    fixture = json.loads((bench.directory/'fixtures-0.json').read_text())
    account = {key:fixture['accounts'][0][key] for key in ('memberId','email')}
    account['password'] = fixture['password']
    atomic_write(settings/'deployment-smoke.json',json.dumps(account).encode())
    atomic_write(settings/'monitoring.json',b'{"frontendEnabled":false}')
    (bench.directory/'Caddyfile').write_text((bench.directory/'Caddyfile').read_text().replace('mk816.test','api.monkado.fr'))
    hosts = '127.0.0.1 localhost api.monkado.fr\n'
    (bench.directory/'hosts').write_text(hosts)
    (Path('/etc/netns')/bench.identifier/'hosts').write_text(hosts)
    # The failing SQL exists solely in a private copy. Production migrations are immutable.
    source = bench.directory/'candidate-source'
    source.mkdir(mode=0o700)
    for name in ('go.mod','go.sum'):
        shutil.copyfile(ROOT/name,source/name)
    for name in ('cmd','internal'):
        shutil.copytree(ROOT/name,source/name,ignore=shutil.ignore_patterns('*_test.go'))
    (source/'internal/persistence/migrations/013_campaign_failure.sql').write_text('SELECT 1 / 0;\n')
    revision = json.loads((bench.directory/'metadata.json').read_text())['revision']
    command(['go','-C',str(source),'build','-buildvcs=false','-trimpath',
             '-ldflags=-s -w -X github.com/JennGllg/mon-kado/internal/buildinfo.Revision='+revision,
             '-o',str(bench.directory/'failing-migrate'),'./cmd/migrate'],timeout=600,
            environment=dict(os.environ,CGO_ENABLED='0',GOPROXY='off'))


def campaign(identifier):
    """Execute the unmodified deployment engine/runtime against owned native effects."""
    bench = Fixture(ROOT,identifier)
    bench.directory = Path('/tmp')/identifier
    bench.owned()
    assert os.readlink('/proc/self/ns/net') != os.readlink('/proc/1/ns/net')
    assert not command(['ip','route','show'])
    native_services.UNITS = {name:bench.unit(name) for name in ('api','worker','postgres','caddy')}
    installation, settings, state = [bench.directory/name for name in ('installation','settings','deployment')]
    metadata = json.loads((bench.directory/'metadata.json').read_text())
    revision = metadata['revision']
    sources, releases = {}, {}
    for index, letter in enumerate('abcd',1):
        binaries = {name:bench.directory/name for name in ('api','worker','migrate')}
        if letter == 'd':
            binaries['migrate'] = bench.directory/'failing-migrate'
        catalog = json.loads(bounded_command([str(binaries['migrate']),'--catalog'],maximum=32768))
        release = {'schemaVersion':3,'publicationId':str(index)+'-1','revision':revision,
                   'configurationHash':configuration_hash(ROOT),'migrationCatalog':catalog,
                   'migrationHash':fingerprint(catalog),'rollbackAllowed':True,'binaries':{}}
        for name, path in binaries.items():
            url = f'https://github.com/jenngllg/mon-kado/releases/download/backend-{revision}-{index}-1/monkado-{name}-linux-amd64'
            sources[url] = path
            release['binaries'][name] = {'url':url,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'size':path.stat().st_size}
        releases[letter] = release

    def transport(url,destination,maximum):
        # Only external artifact distribution is substituted; digest/catalog checks remain real.
        source = sources[url]
        assert source.stat().st_size <= maximum
        shutil.copyfile(source,destination)
        destination.chmod(0o600)

    def adapter(arguments, **options):
        arguments = list(arguments)
        if arguments[:2] == ['systemctl','is-active']:
            assert arguments == ['systemctl','is-active','--quiet','monkado.slice']
            arguments[-1] = bench.parent
        elif arguments[:2] == ['python3','-c']:
            arguments[2] = arguments[2].replace('/opt/monkado-backup',str(ROOT/'src/Operations.Backups')).replace('/opt/monkado/deployments/production',str(ROOT/'deployments/production'))
        elif arguments[:4] == ['runuser','-u','postgres','--']:
            arguments[4] = str(PG/'psql')
            arguments[5:5] = ['-h','127.0.0.1','-U','postgres']
        elif arguments[:2] == ['systemctl','reset-failed']:
            # Stopped transient units unload; there are then no counters left to reset.
            for name in arguments[2:]:
                loaded = command(['systemctl','show',name,'--property=LoadState','--value'])
                if loaded != 'not-found':
                    bounded_command(['systemctl','reset-failed',name])
            return b''
        elif arguments[:2] == ['systemctl','start']:
            assert arguments[2:] == [bench.unit(name) for name in ('api','worker','caddy')]
            active = (installation/'current').resolve(strict=True)
            values = dict(line.split('=',1) for line in (settings/'production.env').read_text().splitlines())
            if active.name.endswith('-3-1'):
                # A real bind collision with this fixture's PostgreSQL kills candidate C.
                values['HTTP_ADDRESS'] = '127.0.0.1:5432'
            write_environment(bench.directory/'api.env',values)
            bench.start('api',[str(active/'api')],environment=bench.directory/'api.env')
            bench.start('worker',[str(active/'worker')],environment=bench.directory/'worker.env')
            bench.start('caddy',[shutil.which('caddy'),'run','--config',str(bench.directory/'Caddyfile'),'--adapter','caddyfile'],environment=bench.directory/'caddy.env')
            return b''
        elif arguments[0] == 'systemd-run':
            arguments = [item.replace('User=monkado','User=nobody').replace('Group=monkado','Group=nogroup')
                         .replace('Slice=monkado.slice','Slice='+bench.parent)
                         .replace('--unit=mon-kado-migrate-','--unit='+identifier+'-migrate-') for item in arguments]
            arguments[1:1] = ['--property=NetworkNamespacePath=/run/netns/'+identifier]
        return bounded_command(arguments,**options)

    context = ssl.create_default_context(cafile=str(bench.directory/'ca.crt'))
    clients = lambda: Client(context=context)
    runtime = Runtime(runner=adapter,client_factory=clients,root=installation,state=state,settings=settings,
                      observability=bench.directory/'application/telemetry',transport=transport,
                      service_uid=pwd.getpwnam('nobody').pw_uid)
    report = {'schemaVersion':1,'sourceRevision':revision,'productionAccess':False,'externalProviders':False,
              'frontendQualifiedSeparately':True,'campaignSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
              'configurationHash':configuration_hash(ROOT),'binaries':metadata['binaries'],'stages':[]}
    def stage(name):
        report['stages'].append(name)
        print(json.dumps({'stage':name,'passed':True}),flush=True)
    def sql(statement):
        return command([str(PG/'psql'),'-X','-qAt','-h','127.0.0.1','-U','postgres','-d','mon_kado',
                        '-v','ON_ERROR_STOP=1'],data=statement)
    clock = lambda: datetime.now(timezone.utc).isoformat()
    runtime.prepare(releases['a'])
    runtime.start(releases['a'])
    runtime.observe_baseline()
    runtime.smoke(releases['a'])
    store = Store(state)
    store.finish(initial_state() | {'phase':'succeeded','current':releases['a']})
    stage('baseline-production-https-authenticated-crud-etag-cleanup')
    history = runtime.history()
    before = sql('SELECT count(*) FROM users; SELECT count(*) FROM wishlists; SELECT count(*) FROM wishes;')
    sentinel = bench.directory/'application/images/preserved.bin'
    sentinel.parent.mkdir(mode=0o700,exist_ok=True)
    sentinel.write_bytes(b'Native deployment preservation fixture')
    image_hash = hashlib.sha256(sentinel.read_bytes()).hexdigest()
    key_hash = hashlib.sha256((settings/'production.env').read_bytes()).hexdigest()
    result = Engine(store,runtime,clock).deploy(releases['b'])
    assert result['phase'] == 'succeeded' and result['current'] == releases['b']
    assert runtime.history() == history
    stage('a-to-b-real-process-replacement-without-migration')
    for letter, code in (('c','PUBLICATION_ROLLED_BACK'),('c','PUBLICATION_REJECTED'),('d','RECOVERY_REQUIRED')):
        try:
            Engine(store,runtime,clock).deploy(releases[letter])
            raise AssertionError('Faulted candidate unexpectedly succeeded')
        except DeploymentError as error:
            assert error.code == code, error.code
        if letter == 'c':
            assert store.load()['current'] == releases['b']
            assert not (state/'in-progress.env').exists()
            runtime.verify_active(releases['b'])
        else:
            assert store.load()['phase'] == 'recoveryRequired' and (state/'in-progress.env').exists()
        assert runtime.history() == history
        assert sql('SELECT count(*) FROM users; SELECT count(*) FROM wishlists; SELECT count(*) FROM wishes;') == before
        assert hashlib.sha256(sentinel.read_bytes()).hexdigest() == image_hash
        assert hashlib.sha256((settings/'production.env').read_bytes()).hexdigest() == key_hash
        stage({'PUBLICATION_ROLLED_BACK':'broken-c-to-b-verified-rollback-preserves-data-files-keys',
               'PUBLICATION_REJECTED':'rejected-publication-not-retried',
               'RECOVERY_REQUIRED':'real-sql-failure-retains-recovery-intent-without-database-rollback'}[code])
    report['passed'] = True
    private_write(bench.directory/'deployment-report.json',json.dumps(report,indent=2))
    print(json.dumps(report),flush=True)


def main():
    if os.environ.get('MONKADO_NATIVE_DEPLOYMENT_CAMPAIGN') != '1':
        raise SystemExit('EXPLICIT_NATIVE_DEPLOYMENT_CAMPAIGN_REQUIRED')
    if len(sys.argv) == 3 and sys.argv[1] == 'child':
        campaign(sys.argv[2])
        return
    assert len(sys.argv) == 1
    bench = Fixture(ROOT,'mk816-'+secrets.token_hex(6))
    print(json.dumps({'runId':bench.identifier,'qualificationScope':'local-native-deployment'}),flush=True)
    try:
        bench.create()
        configure(bench)
        result = subprocess.run(['ip','netns','exec',bench.identifier,'python3',str(Path(__file__)),
                                 'child',bench.identifier],timeout=900,capture_output=True,
                                env={'PATH':os.environ['PATH'],'LANG':'C.UTF-8','MONKADO_NATIVE_DEPLOYMENT_CAMPAIGN':'1'})
        private_write(bench.directory/'campaign.stdout',result.stdout.decode())
        private_write(bench.directory/'campaign.stderr',result.stderr.decode())
        if result.returncode:
            raise DeploymentError('NATIVE_DEPLOYMENT_CAMPAIGN_FAILED')
        print(json.dumps(json.loads((bench.directory/'deployment-report.json').read_text())),flush=True)
    finally:
        if bench.directory == Path('/tmp')/bench.identifier and bench.directory.exists():
            bench.cleanup()


if __name__ == '__main__':
    main()
