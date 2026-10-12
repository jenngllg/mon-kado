"""Run production deployment probes against real Go, PostgreSQL and Caddy locally."""
import base64
import hashlib
import json
import os
from pathlib import Path
import secrets
import ssl
import struct
import sys
import tempfile
import unittest
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT/'tests/Native.ProcessTests'),str(ROOT/'src/Operations.Deployment')]
import test_journeys as journeys
from native_fixture import NativeCaddy, isolated
from smoke_functional import FunctionalSmoke
from smoke_http import Client
import smoke_technical


@unittest.skipUnless(os.environ.get('MONKADO_NATIVE_GO_SMOKE') == '1','Explicit native Go/proxy exercise required')
class GoSmokeTests(unittest.TestCase):
    def test_real_go_with_production_cookies_passes_technical_and_functional_deployment_smoke(self):
        # Arrange: the existing process fixture owns a fresh native Unix-socket cluster.
        isolated()
        fixture = journeys.NativeProcessTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        fixture.start_database()
        user, email, password = str(uuid.uuid4()), 'smoke@native.invalid', secrets.token_urlsafe(32)+'Aa1!'
        salt = secrets.token_bytes(16)
        hashed = base64.b64encode(b'\x01'+struct.pack('>III',2,100000,16)+salt+
                                 hashlib.pbkdf2_hmac('sha512',password.encode(),salt,100000,32)).decode()
        query = """INSERT INTO users(id,user_name,normalized_user_name,email,normalized_email,email_confirmed,password_hash,
                   security_stamp,concurrency_stamp,phone_number_confirmed,two_factor_enabled,lockout_enabled,
                   access_failed_count,display_name,created_at)
                   VALUES ('%s','%s','%s','%s','%s',true,'%s','%s','%s',false,false,true,0,'Native smoke',now());
                   INSERT INTO user_roles(user_id,role_id) SELECT '%s',id FROM roles WHERE normalized_name='MEMBER';""" % (
                       user,email,email.upper(),email,email.upper(),hashed,user,user,user)
        result = journeys.subprocess.run([*fixture.identity,str(journeys.PG/'psql'),'-X','-h',str(fixture.root/'socket'),
                                          '-p','55439','-d','mon_kado_process_test','-v','ON_ERROR_STOP=1'],
                                         input=query,text=True,capture_output=True,timeout=10,check=False)
        self.assertEqual(0,result.returncode,'NATIVE_SMOKE_SEED_FAILED')
        fixture.environment.update(APP_ENV='production',EMAIL_PROVIDER='Disabled',HTTP_ADDRESS='127.0.0.1:7000',
                                   PUBLIC_API_ORIGIN='https://api.monkado.fr',ALLOWED_HOSTS='api.monkado.fr',
                                   FRONTEND_ORIGIN='https://www.monkado.fr',ALLOWED_ORIGINS='https://www.monkado.fr',
                                   TRUSTED_PROXIES='127.0.0.1/32')
        api,reader,events = fixture.start_process('api')
        fixture.event(events,lambda item:item.get('eventId') == 901)
        configuration = (ROOT/'deployments/caddy/Caddyfile').read_text().replace(
            'import /opt/monkado/deployments/caddy/frontend/*.caddy','')
        with NativeCaddy(ROOT,configuration,{'API_HOST':'api.monkado.fr'}) as native:
            context = ssl.create_default_context(cafile=str(native.certificate))
            client = Client(context=context)
            account = {'memberId':user,'email':email,'password':password}
            journal = fixture.root/'smoke-journal.json'
            # Act: original production probes, real HTTP, TLS, CSRF, sessions and database mutations.
            smoke_technical.redirect()
            smoke_technical.check(client)
            FunctionalSmoke(client,account,journal).run()
            # Assert: cleanup and session revocation completed; no provider was enabled.
            self.assertFalse(journal.exists())
            self.assertIsNone(client.token)
            self.assertEqual({},client.cookies)
            clean = Client(context=context)
            FunctionalSmoke(clean,account,journal).login()
            lists,_ = clean.json('GET','/api/v1/wishlists')
            self.assertEqual([],lists)
            clean.json('DELETE','/api/v1/auth/sessions/current',expected=204)
            clean.clear()
        fixture.stop_process(api,reader)
