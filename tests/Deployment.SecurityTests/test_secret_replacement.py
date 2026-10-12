"""Rehearse native PostgreSQL credential replacement with private synthetic values."""
import os
from pathlib import Path
import pwd
import secrets
import subprocess
import tempfile
import unittest

from native_fixture import isolated

PG = Path('/usr/lib/postgresql/18/bin')


@unittest.skipUnless(os.environ.get('MONKADO_DATABASE_SECURITY_TESTS') == '1','Explicit isolated native opt-in required')
class SecretReplacementTests(unittest.TestCase):
    def test_database_rotation_requires_matching_persisted_role_and_client_secret(self):
        # Arrange: no host access, secrets in arguments or shared diagnostic output.
        isolated()
        initial, replacement = secrets.token_hex(32), secrets.token_hex(32)
        owner = pwd.getpwnam('postgres')
        with tempfile.TemporaryDirectory(prefix='monkado-native-password-') as temporary:
            root = Path(temporary)
            root.chmod(0o711)
            cluster, socket = root/'data',root/'socket'
            cluster.mkdir(mode=0o700)
            os.chown(cluster,owner.pw_uid,owner.pw_gid)
            socket.mkdir(mode=0o700)
            os.chown(socket,owner.pw_uid,owner.pw_gid)
            self.assertEqual(0,self.owner('initdb','-D',str(cluster),'--auth-local=trust','--auth-host=scram-sha-256',
                                          '--no-locale','--encoding=UTF8'))
            options = '-c listen_addresses=127.0.0.1 -c shared_buffers=32MB -c max_connections=10 -c unix_socket_directories='+str(socket)
            try:
                self.assertEqual(0,self.owner('pg_ctl','-D',str(cluster),'-l',str(cluster/'server.log'),'-w','-t','20','-o',options,'start'))
                self.assertEqual(0,self.owner('psql','-X','-h',str(socket),'-d','postgres','-v','ON_ERROR_STOP=1',
                                              data="CREATE ROLE fixture LOGIN PASSWORD '"+initial+"';\n"))
                self.assertEqual(0,self.connect(initial))
                # Act: alter the persisted role through the private administrative Unix socket.
                self.assertEqual(0,self.owner('psql','-X','-h',str(socket),'-d','postgres','-v','ON_ERROR_STOP=1',
                                              data="ALTER ROLE fixture PASSWORD '"+replacement+"';\n"))
                # Assert: old credentials fail, including after an actual server restart.
                self.assertNotEqual(0,self.connect(initial))
                self.assertEqual(0,self.connect(replacement))
                self.assertEqual(0,self.owner('pg_ctl','-D',str(cluster),'-l',str(cluster/'server.log'),'-w','-t','20','restart'))
                self.assertEqual(0,self.connect(replacement))
                self.assertNotEqual(0,self.connect(initial))
            finally:
                self.owner('pg_ctl','-D',str(cluster),'-w','-t','20','stop')

    @staticmethod
    def owner(tool,*arguments,data=None):
        result = subprocess.run(['runuser','-u','postgres','--',str(PG/tool),*arguments],input=data,
                                text=True,capture_output=True,timeout=30,check=False)
        return result.returncode

    @staticmethod
    def connect(password):
        environment = {'PATH':os.environ['PATH'],'LANG':'C.UTF-8','PGPASSWORD':password}
        result = subprocess.run([str(PG/'psql'),'-X','-h','127.0.0.1','-U','fixture','-d','postgres',
                                 '-v','ON_ERROR_STOP=1','-c','SELECT 1'],env=environment,
                                capture_output=True,timeout=10,check=False)
        return result.returncode
