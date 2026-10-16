"""Public native executables exercise real HTTP, worker delivery and graceful shutdown locally."""

import base64
import http.cookiejar
import io
import json
import os
from pathlib import Path
import pwd
import queue
import signal
import socket
import subprocess
import tempfile
import threading
import time
import unittest
import urllib.error
import urllib.parse
import urllib.request
import zipfile

PG = Path("/usr/lib/postgresql/18/bin")


class Browser:
    """Small HTTP client preserving only the application's public browser contracts."""

    def __init__(self, base, test):
        self.base, self.test = base, test
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()),
                                                 urllib.request.ProxyHandler({}))
        self.access = ""
        self.csrf = ""
        self.renew_csrf()

    def send(self, method, path, expected, body=None, headers=None):
        content = None if body is None else json.dumps(body).encode()
        values = {"X-CSRF-TOKEN": self.csrf}
        if content is not None:
            values["Content-Type"] = "application/json"
        if self.access:
            values["Authorization"] = "Bearer " + self.access
        values.update(headers or {})
        request = urllib.request.Request(self.base + path, data=content, headers=values, method=method)
        try:
            response = self.opener.open(request, timeout=10)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            data = response.read(2 * 1024 * 1024)
            self.test.assertEqual(expected, response.status, "Unexpected status for " + method + " " + path)
            self.test.assertTrue(response.headers.get("X-Correlation-ID"))
            self.test.assertEqual("no-store", response.headers.get("Cache-Control"))
            result = json.loads(data) if response.headers.get_content_type() == "application/json" else data
            return result, response.headers

    def renew_csrf(self):
        token, _ = self.send("GET", "/security/csrf-token", 200)
        self.csrf = token["token"]


class NativeProcessTests(unittest.TestCase):
    def setUp(self):
        self.assertEqual(0, os.geteuid(), "Native process tests require root for an isolated peer-authenticated PostgreSQL fixture")
        self.binaries = Path(os.environ["MONKADO_NATIVE_BINARIES"]).resolve()
        self.coverage = Path(os.environ["MONKADO_NATIVE_COVERAGE"]).resolve()
        self.temporary = tempfile.TemporaryDirectory(prefix="mk-process-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.root.chmod(0o711)
        self.account = pwd.getpwnam("postgres")
        self.identity = ["setpriv", "--reuid=" + str(self.account.pw_uid),
                         "--regid=" + str(self.account.pw_gid), "--init-groups", "--"]
        self.processes = []
        self.environment = {"PATH": "/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin", "LANG": "C.UTF-8", "TZ": "UTC",
                            "APP_ENV": "local", "JWT_SIGNING_KEY": base64.b64encode(bytes(range(32))).decode(),
                            "DATA_ENCRYPTION_KEY": base64.b64encode(bytes(range(32, 64))).decode(),
                            "EMAIL_PROVIDER": "Mailbox", "WORKER_INTERVAL": "1s", "GOOGLE_ENABLED": "false",
                            "FRONTEND_ORIGIN": "http://localhost:5173", "ALLOWED_ORIGINS": "http://localhost:5173",
                            "ALLOWED_HOSTS": "127.0.0.1", "OBSERVABILITY_ENABLED": "true", "OBSERVABILITY_INTERVAL": "5s"}
        for component in ("api", "worker", "migrate"):
            source = self.binaries / component
            self.assertTrue(source.is_file())
            destination = self.root / component
            destination.write_bytes(source.read_bytes())
            destination.chmod(0o755)
            directory = self.coverage / component
            directory.mkdir(mode=0o700, parents=True, exist_ok=True)
            os.chown(directory, self.account.pw_uid, self.account.pw_gid)
        self.coverage.chmod(0o711)
        for key, name in (("IMAGE_PATH", "images"), ("EXPORT_PATH", "exports"), ("MAILBOX_PATH", "mailbox"),
                         ("OBSERVABILITY_DIRECTORY", "telemetry")):
            directory = self.root / name
            directory.mkdir(mode=0o700)
            os.chown(directory, self.account.pw_uid, self.account.pw_gid)
            self.environment[key] = str(directory)

    def run_command(self, args, expected=0, environment=None):
        result = subprocess.run(args, env=environment or self.environment, stdout=subprocess.PIPE,
                                stderr=subprocess.DEVNULL, timeout=60, check=False)
        self.assertEqual(expected, result.returncode, "Native command failed")
        return result.stdout

    def binary_environment(self, component):
        return self.environment | {"GOCOVERDIR": str(self.coverage / component)}

    def start_database(self):
        cluster, socket_path = self.root / "pg", self.root / "socket"
        self.run_command(["install", "-d", "-m", "0700", "-o", "postgres", "-g", "postgres", str(cluster), str(socket_path)])
        self.run_command([*self.identity, str(PG / "initdb"), "--pgdata=" + str(cluster), "--auth-local=peer",
                          "--auth-host=reject", "--encoding=UTF8", "--locale=C.UTF-8", "--no-instructions"])
        control = [*self.identity, str(PG / "pg_ctl"), "--pgdata=" + str(cluster), "--wait", "--timeout=60"]
        options = "-c listen_addresses='' -c unix_socket_directories=" + str(socket_path) + " -c port=55439 -c shared_buffers=32MB -c max_connections=20"
        self.run_command([*control, "start", "--log=" + str(cluster / "startup.log"), "--options=" + options])
        self.addCleanup(lambda: self.run_command([*control, "stop", "--mode=fast"]))
        self.run_command([*self.identity, str(PG / "createdb"), "--host=" + str(socket_path), "--port=55439",
                          "--template=template0", "mon_kado_process_test"])
        self.environment["DATABASE_URL"] = "postgres://postgres@/mon_kado_process_test?host=" + str(socket_path) + "&port=55439&sslmode=disable"
        self.run_command([*self.identity, str(self.root / "migrate")], environment=self.binary_environment("migrate"))

    def start_process(self, component):
        process = subprocess.Popen([*self.identity, str(self.root / component)], env=self.binary_environment(component),
                                   cwd=self.root, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
        events = queue.Queue()

        def collect():
            for line in process.stdout:
                try:
                    events.put(json.loads(line))
                except ValueError:
                    events.put({"invalidLog": True})
            events.put({"processEnded": True})

        reader = threading.Thread(target=collect)
        reader.start()
        self.processes.append((process, reader))
        self.addCleanup(self.stop_process, process, reader)
        return process, reader, events

    def stop_process(self, process, reader):
        if process.poll() is None:
            process.send_signal(signal.SIGTERM)
        try:
            process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)
            self.fail("Native process did not shut down gracefully")
        reader.join(timeout=5)
        process.stdout.close()
        self.assertFalse(reader.is_alive())
        self.assertEqual(0, process.returncode, "Native process shutdown failed")

    def event(self, events, predicate):
        deadline = time.monotonic() + 20
        while True:
            try:
                record = events.get(timeout=max(0, deadline - time.monotonic()))
            except queue.Empty:
                self.fail("Expected native lifecycle event was not received")
            self.assertNotIn("processEnded", record, "Native process ended before its lifecycle event")
            self.assertNotIn("invalidLog", record)
            if predicate(record):
                return record

    def register(self, browser, events, email):
        password = "Native process journey password 🎁"
        browser.send("POST", "/api/v1/auth/registrations", 202,
                     {"email": email, "password": password, "displayName": "Native process member"})
        record = self.event(events, lambda item: item.get("eventId") == 6001)
        message = json.loads((self.root / "mailbox" / (record["properties"]["OutboxId"] + ".json")).read_bytes())
        self.assertEqual(email, message["to"])
        link = urllib.parse.urlsplit(message["actionUrl"])
        self.assertEqual("", link.query)
        proof = urllib.parse.parse_qs(link.fragment)
        browser.send("POST", "/api/v1/auth/email-confirmations", 204, {"userId": proof["userId"][0], "token": proof["token"][0]})
        access, _ = browser.send("POST", "/api/v1/auth/sessions", 200, {"email": email, "password": password})
        browser.access = access["accessToken"]
        browser.renew_csrf()

    def test_native_binaries_when_user_completes_core_journeys_persist_work_and_stop_cleanly(self):
        # Arrange: entirely fresh Unix-socket database, private files and local mailbox.
        self.start_database()
        with socket.socket() as reserved:
            reserved.bind(("127.0.0.1", 0))
            port = reserved.getsockname()[1]
        self.environment["HTTP_ADDRESS"] = "127.0.0.1:" + str(port)
        self.environment["PUBLIC_API_ORIGIN"] = "http://127.0.0.1:" + str(port)
        api, api_reader, api_events = self.start_process("api")
        self.event(api_events, lambda item: item.get("eventId") == 901)
        worker, worker_reader, worker_events = self.start_process("worker")
        owner = Browser(self.environment["PUBLIC_API_ORIGIN"], self)
        visitor = Browser(self.environment["PUBLIC_API_ORIGIN"], self)
        owner.send("GET", "/readiness", 200)
        visitor.send("GET", "/api/v1/wishlists", 401)
        self.register(owner, worker_events, "native-owner@example.test")
        self.register(visitor, worker_events, "native-visitor@example.test")
        # Act: create, favorite, share, follow, reserve, copy, archive and export over real HTTP.
        wishlist, metadata = owner.send("POST", "/api/v1/wishlists", 201, {"name": "Native gifts", "occasion": "birthday", "surpriseMode": False})
        base, list_version = metadata["Location"], metadata["ETag"]
        gift, metadata = owner.send("POST", base + "/wishes", 201, {"name": "Native gift", "price": "12.30", "quantity": 2, "isFavorite": True})
        wish_path = metadata["Location"]
        self.assertTrue(gift["isFavorite"])
        visitor.send("GET", wish_path, 404)
        share, metadata = owner.send("POST", base + "/share-link", 201)
        share_version = metadata["ETag"]
        headers = {"X-MonKado-Share-Token": urllib.parse.urlsplit(share["shareUrl"]).fragment}
        shared = "/api/v1/shared-wishlists/" + share["id"]
        visitor.send("GET", shared, 404)
        subscription, _ = visitor.send("POST", shared + "/subscriptions", 201, headers=headers)
        visitor.send("POST", shared + "/participants", 201, headers=headers)
        reservation, metadata = visitor.send("PUT", shared + "/wishes/" + gift["id"] + "/reservations/current", 201,
                                             {"quantity": 1}, headers)
        reservation_path = shared + "/wishes/" + gift["id"] + "/reservations/current"
        reservation_version = metadata["ETag"]
        visitor.send("PUT", reservation_path, 428, {"quantity": 1}, headers)
        copied, _ = owner.send("POST", wish_path + "/copies", 201, {"destinationWishlistId": wishlist["id"]})
        self.assertFalse(copied["isFavorite"])
        self.assertEqual(0, copied["reservedQuantity"])
        _, archived_headers = owner.send("PATCH", base, 200, {"isArchived": True}, {"If-Match": list_version})
        visitor.send("GET", "/api/v1/wishlist-subscriptions/" + subscription["id"], 404)
        export, _ = owner.send("POST", "/api/v1/members/current/data-exports", 202)
        # Synchronize on the local export-ready delivery event instead of polling the database.
        self.event(worker_events, lambda item: item.get("eventId") == 6001)
        status, _ = owner.send("GET", "/api/v1/members/current/data-exports/" + export["id"], 200)
        self.assertEqual("ready", status["status"])
        content, _ = owner.send("GET", "/api/v1/members/current/data-exports/" + export["id"] + "/archive", 200)
        with zipfile.ZipFile(io.BytesIO(content)) as archive:
            document = json.loads(archive.read("data.json"))
        # Assert: privacy, archive flags, favorite state, ownership and process shutdown.
        self.assertTrue(document["wishlists"][0]["isArchived"])
        self.assertEqual(2, len(document["wishes"]))
        self.assertEqual([], document["reservations"])
        self.assertNotIn(headers["X-MonKado-Share-Token"], json.dumps(document))
        visitor.send("GET", "/api/v1/members/current/data-exports/" + export["id"] + "/archive", 404)
        owner.send("DELETE", base + "/share-link", 409, headers={"If-Match": share_version})
        owner.send("PATCH", base, 200, {"isArchived": False}, {"If-Match": archived_headers["ETag"]})
        owner.send("DELETE", base + "/share-link", 204, headers={"If-Match": share_version})
        visitor.send("GET", reservation_path, 404, headers=headers)
        owner.send("DELETE", "/api/v1/auth/sessions/current", 204)
        owner.send("GET", "/api/v1/auth/sessions/current", 401)
        self.stop_process(worker, worker_reader)
        self.stop_process(api, api_reader)

    def test_native_commands_when_configuration_is_invalid_reject_before_listening_or_processing(self):
        # Arrange / Act / Assert
        for component in ("api", "worker"):
            environment = self.binary_environment(component) | {"APP_ENV": "Production"}
            self.run_command([*self.identity, str(self.root / component)], expected=1, environment=environment)
            metadata = json.loads(self.run_command([*self.identity, str(self.root / component), "--version"], environment=environment))
            self.assertEqual({"schemaVersion", "revision", "os", "architecture"}, set(metadata))
            self.assertEqual("linux", metadata["os"])
            self.assertEqual("amd64", metadata["architecture"])
        metadata = json.loads(self.run_command([*self.identity, str(self.root / "migrate"), "--catalog"], environment=self.binary_environment("migrate")))
        self.assertEqual(12, len(metadata["migrations"]))

    def test_native_commands_when_database_is_unavailable_fail_without_disclosing_connection_details(self):
        # Arrange: a Unix socket beneath this fresh private fixture cannot reach a host database.
        private = 'native-connection-secret-canary'
        for component in ('api','worker','migrate'):
            with self.subTest(component=component):
                environment = self.binary_environment(component) | {
                    'DATABASE_URL':'postgres://postgres:'+private+'@/absent?host='+str(self.root/'absent-socket')+'&sslmode=disable'}
                # Act
                result = subprocess.run([*self.identity,str(self.root/component)],env=environment,
                                        capture_output=True,timeout=15,check=False)
                # Assert
                self.assertEqual(1,result.returncode)
                self.assertNotIn(private.encode(),result.stdout+result.stderr)
                self.assertNotIn(b'postgres://',result.stdout+result.stderr)
                self.assertNotIn(str(self.root/'absent-socket').encode(),result.stdout+result.stderr)

    def test_native_api_when_listener_is_occupied_refuses_startup_and_does_not_replace_other_listener(self):
        # Arrange
        self.start_database()
        with socket.socket() as existing:
            existing.bind(('127.0.0.1',0))
            existing.listen(1)
            endpoint = existing.getsockname()
            environment = self.binary_environment('api') | {'HTTP_ADDRESS':'127.0.0.1:'+str(endpoint[1])}
            # Act
            result = subprocess.run([*self.identity,str(self.root/'api')],env=environment,
                                    capture_output=True,timeout=15,check=False)
            # Assert
            self.assertEqual(1,result.returncode)
            self.assertEqual(endpoint,existing.getsockname())
            self.assertNotIn(b'postgres://',result.stdout+result.stderr)


if __name__ == "__main__":
    unittest.main()
