"""Verify the packaged contract on an isolated, network-disabled API with synthetic settings."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import uuid

PYTHON_IMAGE = 'python:3.13-slim@sha256:8d9d0b8bcf6506481eae4907c18f5e3e7902e629f5f6d684f9e7c32e85e3ddf0'


def run(arguments, timeout=90):
    result = subprocess.run(arguments, capture_output=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError('STATIC_OPENAPI_IMAGE_CHECK_FAILED')
    return result.stdout


def main(image):
    name = 'monkado-openapi-check-' + uuid.uuid4().hex
    settings = {
        'ASPNETCORE_ENVIRONMENT': 'Production',
        'AllowedHosts': 'localhost',
        'WebSecurity__AllowedOrigins__0': 'https://openapi-check.invalid',
        'WishlistSharing__FrontendOrigin': 'https://openapi-check.invalid',
        'GoogleAuthentication__Enabled': 'false',
        'GoogleAuthentication__FrontendOrigin': 'https://openapi-check.invalid',
        'ReverseProxy__KnownNetworks__0': '127.0.0.0/8',
        'ConnectionStrings__PostgreSql': 'Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=test-only;Timeout=1;Pooling=false',
        'Jwt__SigningKey': 'AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=',
        'DataProtection__KeysPath': '/tmp/keys',
        'GiftImages__StoragePath': '/tmp/images',
        'PersonalDataExports__StoragePath': '/tmp/exports',
        'GeneralRateLimit__PermitLimit': '1000',
        'DOTNET_GCHeapHardLimit': '0x10000000'
    }
    arguments = ['docker', 'run', '-d', '--name', name, '--network', 'none', '--read-only',
                 '--tmpfs', '/tmp:size=64m,mode=1777', '--cap-drop', 'ALL', '--security-opt',
                 'no-new-privileges:true', '--memory', '384m']
    for key, value in settings.items():
        arguments += ['-e', key + '=' + value]
    created = False
    try:
        run(arguments + [image])
        created = True
        configured = run(['docker', 'inspect', '--format',
            '{{range .Config.Env}}{{if eq . "OpenApi__DocumentPath=/app/openapi/v1.json"}}true{{end}}{{end}}', name])
        assert configured.strip() == b'true', 'STATIC_DOCUMENT_MODE_NOT_CONFIGURED'
        with tempfile.TemporaryDirectory(prefix='monkado-openapi-artifact-') as directory:
            artifact = Path(directory) / 'v1.json'
            run(['docker', 'cp', name + ':/app/openapi/v1.json', str(artifact)])
            document = json.loads(artifact.read_bytes())
            assert document['openapi'] == '3.1.1'
            assert document['info']['version'] == 'v1'
            assert not document.get('servers'), 'BUILD_ORIGIN_LEAKED_IN_DOCUMENT'
            assert '/api/v1/auth/google/completions' in document['paths']
            assert '/api/v1/wishlists/{wishlistId}' in document['paths']
            expected_hash = hashlib.sha256(artifact.read_bytes()).hexdigest()
            probe = '''
import hashlib, json, time, urllib.request, urllib.error
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
deadline = time.monotonic() + 45
while True:
    try:
        response = opener.open('http://localhost:8080/openapi/v1.json', timeout=5)
        break
    except OSError:
        if time.monotonic() >= deadline:
            raise RuntimeError('STATIC_API_START_TIMEOUT') from None
        time.sleep(.2)
with response:
    assert response.status == 200
    assert response.headers['Content-Type'].startswith('application/json')
    assert response.headers['X-Content-Type-Options'] == 'nosniff'
    assert response.headers['X-Frame-Options'] == 'DENY'
    assert response.headers['X-Correlation-ID']
    body = response.read(2 * 1024 ** 2 + 1)
for method, route, status in [('HEAD', '/openapi/v1.json', 200), ('GET', '/openapi/unknown.json', 404),
                               ('POST', '/openapi/v1.json', 405), ('GET', '/liveness', 200),
                               ('GET', '/api/v1/wishlists', 401)]:
    request = urllib.request.Request('http://localhost:8080' + route, method=method)
    try:
        reply = opener.open(request, timeout=5)
    except urllib.error.HTTPError as error:
        reply = error
    with reply:
        assert reply.status == status, (route, reply.status)
        if method == 'HEAD':
            assert not reply.read()
print(json.dumps({'documentHash': hashlib.sha256(body).hexdigest(), 'staticContractVerified': True}))
'''
            result = json.loads(run(['docker', 'run', '--rm', '--network', 'container:' + name,
                                     PYTHON_IMAGE, 'python', '-c', probe]))
            assert result['documentHash'] == expected_hash, 'SERVED_DOCUMENT_DIFFERS_FROM_BUILD_ARTIFACT'
            print(json.dumps({'staticContractVerified': True, 'artifactMatchesResponse': True,
                              'securityHeadersVerified': True, 'productionModified': False}), flush=True)
    finally:
        if created:
            run(['docker', 'rm', '-f', name])


if __name__ == '__main__':
    main(sys.argv[1])
