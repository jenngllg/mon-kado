"""Private native fixtures; require a separate loopback-only Linux network namespace."""
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def isolated():
    if os.geteuid() != 0 or os.readlink('/proc/self/ns/net') == os.readlink('/proc/1/ns/net'):
        raise RuntimeError('PRIVATE_NETWORK_NAMESPACE_REQUIRED')
    subprocess.run(['ip', 'link', 'set', 'lo', 'up'], check=True, capture_output=True, timeout=5)
    routes = subprocess.run(['ip', 'route', 'show'], check=True, capture_output=True, timeout=5)
    if routes.stdout.strip():
        raise RuntimeError('EXTERNAL_ROUTE_REFUSED')


def stop(process):
    if process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)


def ready(process, marker, certificates=()):
    def read():
        initial = False
        pending = set(certificates)
        for line in process.stdout:
            if marker in line:
                initial = True
            # Issuance finishes before CertMagic installs the certificate in its
            # live cache. HTTPS is ready only after every requested name is cached.
            if 'added certificate to cache' in line:
                pending.difference_update(json.loads(line).get('subjects', []))
            if initial and not pending:
                return
        raise RuntimeError('FIXTURE_ENDED_BEFORE_READINESS')
    with ThreadPoolExecutor(max_workers=1) as executor:
        try:
            executor.submit(read).result(timeout=30)
        except BaseException:
            stop(process)
            raise


class NativeCaddy:
    """Run real Caddy and an optional synthetic upstream without host network access."""
    def __init__(self, root, configuration, environment=None, server=None):
        isolated()
        self.root = Path(root)
        self.environment = dict(environment or {})
        self.configuration = configuration
        self.server = server
        self.processes = []

    def __enter__(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='monkado-native-tls-')
        self.directory = Path(self.temporary.name)
        self.directory.chmod(0o700)
        try:
            if self.server:
                self.upstream = subprocess.Popen(['python3', '-u', '-c', self.server], stdout=subprocess.PIPE,
                                                 stderr=subprocess.DEVNULL, text=True)
                self.processes.append(self.upstream)
                ready(self.upstream, 'fixture-ready')
            config = self.directory / 'Caddyfile'
            config.write_text(self.configuration.replace('{\n', '{\n log tls-cache {\n include tls.cache\n level DEBUG\n }\n admin off\n local_certs\n skip_install_trust\n', 1))
            config.chmod(0o600)
            environment = {'PATH':os.environ['PATH'], 'HOME':str(self.directory), 'LANG':'C.UTF-8',
                           'XDG_DATA_HOME':str(self.directory), 'XDG_CONFIG_HOME':str(self.directory), **self.environment}
            self.edge = subprocess.Popen([shutil.which('caddy'), 'run', '--config', str(config), '--adapter', 'caddyfile'],
                                         env=environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
            self.processes.append(self.edge)
            certificates = [self.environment[key] for key in ('API_HOST','FRONTEND_HOST','FRONTEND_APEX_HOST')
                            if key in self.environment]
            ready(self.edge, 'serving initial configuration', certificates)
            self.certificate = self.directory / 'caddy/pki/authorities/local/root.crt'
            if not self.certificate.is_file():
                raise RuntimeError('FIXTURE_CA_UNAVAILABLE')
            return self
        except BaseException:
            self.__exit__(None, None, None)
            raise

    def __exit__(self, *_):
        for process in reversed(self.processes):
            stop(process)
            process.stdout.close()
        self.temporary.cleanup()


class MountSandbox:
    """Map installation targets to fresh private directories in a child mount namespace."""
    def __init__(self):
        isolated()
        self.temporary = tempfile.TemporaryDirectory(prefix='monkado-native-install-')
        self.root = Path(self.temporary.name)
        for name in ('etc/systemd/system','etc/monkado','opt','varlib','sbin','commands'):
            (self.root/name).mkdir(parents=True)
        (self.root/'etc/os-release').write_text('ID=ubuntu\nVERSION_ID="24.04"\n')
        (self.root/'etc/passwd').write_text('root:x:0:0:root:/root:/bin/bash\nmonkado:x:1654:1654:fixture:/nonexistent:/usr/sbin/nologin\n')
        (self.root/'etc/group').write_text('root:x:0:\nmonkado:x:1654:\n')
        self.wrapper = self.root/'sandbox.sh'
        self.wrapper.write_text('''#!/usr/bin/env bash
set -Eeuo pipefail
mount --make-rprivate /
mount --bind "$1/etc" /etc
mount --bind "$1/varlib" /var/lib
mount --bind "$1/opt" /opt
mount --bind "$1/sbin" /usr/local/sbin
shift
exec "$@"
''')

    def execute(self, arguments, environment=None):
        return subprocess.run(['unshare','--mount','bash',str(self.wrapper),str(self.root),*arguments],
                              env=environment, capture_output=True, timeout=30, check=False)

    def close(self):
        self.temporary.cleanup()
