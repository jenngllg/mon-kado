"""Launch explicitly selected local operations tests in private mount/network namespaces."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile

SUITES = {
    'security': ('Deployment.SecurityTests', 'test_caddy.py', 'MONKADO_CADDY_TESTS'),
    'frontend-http': ('Deployment.FrontendTests', 'test_frontend_http.py', 'MONKADO_FRONTEND_NATIVE_TESTS'),
    'frontend-release': ('Deployment.FrontendTests', 'test_frontend_campaign.py', 'MK936_NATIVE_CAMPAIGN'),
    'installer': ('Deployment.MonitoringTests', 'test_install.py', 'MONKADO_MONITOR_INSTALL_TESTS'),
    'entrypoint': ('../deployments/production', 'test_deploy_container.py', 'MONKADO_DEPLOYMENT_SANDBOX'),
    'database-security': ('Deployment.SecurityTests', 'test_secret_replacement.py', 'MONKADO_DATABASE_SECURITY_TESTS'),
    'go-smoke': ('Native.OperationsTests', 'test_go_smoke.py', 'MONKADO_NATIVE_GO_SMOKE'),
}


def main():
    if len(sys.argv) != 2 or sys.argv[1] not in SUITES or sys.platform != 'linux' or os.geteuid() != 0:
        raise SystemExit('NATIVE_LOCAL_ROOT_AND_KNOWN_SUITE_REQUIRED')
    root = Path(__file__).resolve().parents[2]
    suite, pattern, flag = SUITES[sys.argv[1]]
    environment = {'PATH':os.environ['PATH'], 'LANG':'C.UTF-8', 'PYTHONDONTWRITEBYTECODE':'1', flag:'1',
                   'PYTHONPATH':str(root/'tests/Native.OperationsTests')+':'+str(root/'tests'/suite)+':'+str(root/'src/Operations.Frontend')}
    if sys.argv[1] == 'go-smoke':
        binaries = Path(os.environ['MONKADO_NATIVE_BINARIES']).resolve(strict=True)
        environment['MONKADO_NATIVE_BINARIES'] = str(binaries)
    with tempfile.TemporaryDirectory(prefix='monkado-native-operations-') as temporary:
        environment['MONKADO_NATIVE_FIXTURE'] = temporary
        coverage = Path(temporary)/'coverage'
        coverage.mkdir(mode=0o711)
        environment['MONKADO_NATIVE_COVERAGE'] = str(coverage)
        # No named namespace, host port, trust-store install or external route exists.
        result = subprocess.run(['unshare','--net','--mount','--propagation','private',
                                 'python3','-m','unittest','discover','-s',str(root/'tests'/suite),'-p',pattern],
                                env=environment, timeout=300, check=False)
    return result.returncode


if __name__ == '__main__':
    raise SystemExit(main())
