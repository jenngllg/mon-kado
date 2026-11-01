"""Exercise actual frontend activation and verified TLS inside a private native namespace."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

from native_fixture import NativeCaddy


@unittest.skipUnless(os.environ.get('MK936_NATIVE_CAMPAIGN') == '1', 'Explicit isolated native opt-in required')
class FrontendCampaignTests(unittest.TestCase):
    def test_real_https_publication_rollback_and_interruption(self):
        # Arrange
        root = Path(__file__).resolve().parents[2]
        with tempfile.TemporaryDirectory(prefix='monkado-native-frontend-') as temporary:
            fixture = Path(temporary)
            configuration = '''{
}
import ''' + str(root/'deployments/frontend/frontend.caddy') + '''
api.monkado.fr {
    header Content-Type text/plain
    respond /readiness "Healthy"
}
'''
            environment = {'API_HOST':'api.monkado.fr','FRONTEND_HOST':'www.monkado.fr',
                           'FRONTEND_APEX_HOST':'monkado.fr','FRONTEND_API_ORIGIN':'https://api.monkado.fr',
                           'FRONTEND_ROOT':str(fixture/'state/releases/current')}
            with NativeCaddy(root, configuration, environment) as native:
                private = {'PATH':os.environ['PATH'], 'LANG':'C.UTF-8', 'PYTHONDONTWRITEBYTECODE':'1',
                           'MONKADO_NATIVE_FIXTURE':str(fixture),'CURL_CA_BUNDLE':str(native.certificate)}
                # Act: the production probe resolves its fixed public names to this namespace's loopback.
                result = subprocess.run(['python3',str(root/'tests/Deployment.FrontendTests/campaign_fixture.py')],
                                        env=private, capture_output=True, timeout=180, check=False)
                # Assert: only the fixed sanitized report is persisted; diagnostics stay private.
                self.assertEqual(0,result.returncode,'NATIVE_FRONTEND_CAMPAIGN_FAILED')
                report = json.loads(result.stdout)
                self.assertTrue(report['tlsVerified'])
                self.assertTrue(report['explicitRecoveryVerified'])
                self.assertTrue(report['backendUntouched'])
                output = root/'TestResults/mk936-native-frontend-campaign.json'
                output.parent.mkdir(exist_ok=True)
                output.write_text(json.dumps(report,sort_keys=True)+'\n')


if __name__ == '__main__':
    unittest.main()
