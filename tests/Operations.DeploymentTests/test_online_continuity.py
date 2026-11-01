"""Exercise real HTTP traffic through native Caddy during a rolling API switch.

Run only in a private Linux network namespace. Application processes are small
HTTP fakes; the proxy, sockets, reload and concurrent requests are real.
"""

import http.server
import os
from pathlib import Path
import subprocess
import ssl
import tempfile
import threading
import unittest
from unittest.mock import patch
import urllib.request

from deploy_runtime import OnlineRuntime, Runtime
from deploy_policy import DeploymentError
from test_engine import release


@unittest.skipUnless(os.environ.get("MONKADO_ONLINE_HTTP_TESTS") == "1", "requires a private Linux network namespace")
class OnlineContinuityTests(unittest.TestCase):
    def test_real_proxy_keeps_serving_during_candidate_switch_and_failed_candidate(self):
        subprocess.run(["ip", "link", "set", "lo", "up"], check=True)
        caddy = os.environ["MONKADO_TEST_CADDY"]
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            config = root / "deployments/caddy/Caddyfile"
            config.parent.mkdir(parents=True)
            certificate, key = root / "certificate.pem", root / "key.pem"
            subprocess.run(["openssl", "req", "-x509", "-newkey", "ec", "-pkeyopt", "ec_paramgen_curve:P-256", "-nodes",
                            "-keyout", str(key), "-out", str(certificate), "-subj", "/CN=localhost", "-addext", "subjectAltName=DNS:localhost", "-days", "1"],
                           check=True, capture_output=True, timeout=10)
            routing = root / "routing"
            routing.mkdir(mode=0o755)
            (routing / "blue").write_text("blue")
            proxy_options = "lb_policy first\n lb_try_duration 5s\n lb_try_interval 50ms\n fail_duration 1s\n transport http {\n keepalive off\n }\n"
            config.write_text("{\n admin 127.0.0.1:7081\n auto_https off\n}\nhttps://localhost:7080 {\n tls " + str(certificate) + " " + str(key) +
                              "\n @blue file {\n root " + str(routing) + "\n try_files blue\n }\n handle @blue {\n reverse_proxy 127.0.0.1:7000 127.0.0.1:7001 {\n" + proxy_options +
                              "}\n }\n handle {\n reverse_proxy 127.0.0.1:7001 127.0.0.1:7000 {\n" + proxy_options + "}\n }\n}\n")
            tls_context = ssl.create_default_context(cafile=str(certificate))
            servers = {}
            self.addCleanup(lambda: [server.shutdown() or server.server_close() for server in list(servers.values())])

            def start_api(port, identity):
                class Handler(http.server.BaseHTTPRequestHandler):
                    def do_GET(self):
                        body = b"Healthy" if self.path == "/readiness" else identity.encode()
                        self.send_response(200); self.end_headers(); self.wfile.write(body)
                    def log_message(self, *_):
                        pass
                server = http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler)
                servers[port] = server
                threading.Thread(target=lambda: server.serve_forever(poll_interval=0.01), daemon=True).start()

            start_api(7000, "old")
            proxy = subprocess.Popen([caddy, "run", "--config", str(config), "--adapter", "caddyfile"], stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
                                     env=os.environ | {"XDG_CONFIG_HOME": str(root / "config"), "XDG_DATA_HOME": str(root / "data")})
            def stop_proxy():
                if proxy.poll() is None:
                    proxy.terminate(); proxy.wait(timeout=5)
                proxy.stderr.close()
            self.addCleanup(stop_proxy)
            ready = threading.Event()
            proxy_logs = []
            def read_proxy():
                for line in iter(proxy.stderr.readline, b""):
                    proxy_logs.append(line.decode(errors="replace"))
                    if b"serving initial configuration" in line:
                        ready.set()
            threading.Thread(target=read_proxy, daemon=True).start()
            self.assertTrue(ready.wait(10), "native Caddy did not become ready")
            condition, stopped = threading.Condition(), threading.Event()
            observed, failures = [], []
            def traffic():
                while not stopped.is_set():
                    try:
                        with urllib.request.urlopen("https://localhost:7080/", context=tls_context, timeout=3) as response:
                            value = response.read().decode()
                            if response.status != 200:
                                raise ValueError("HTTP_ERROR")
                        with condition:
                            observed.append(value); condition.notify_all()
                    except Exception as error:
                        with condition:
                            failures.append(type(error).__name__); condition.notify_all()
            monitor = threading.Thread(target=traffic)
            monitor.start()
            self.addCleanup(lambda: (stopped.set(), monitor.join(timeout=5)))
            def wait_for(predicate):
                with condition:
                    self.assertTrue(condition.wait_for(lambda: predicate() or failures, timeout=10))
                    self.assertEqual([], failures, "".join(proxy_logs[-8:]))
            wait_for(lambda: len(observed) >= 100)

            def runner(args, **_kwargs):
                if args[0] == "curl":
                    result = subprocess.run(args, capture_output=True, check=True, timeout=4)
                    return result.stdout
                if args == ["systemctl", "stop", "mon-kado-api.service"]:
                    wait_for(lambda: observed.count("new") >= 20)
                    server = servers.pop(7000); server.shutdown(); server.server_close()
                    return b""
                raise AssertionError("Unexpected process boundary")

            runtime = OnlineRuntime(runner=runner, root=root, state=root, routing=routing)
            runtime.prepare = lambda _: root
            runtime.container = lambda service: {"running": True, "image": ("a" if service == "api" else "b") * 64}
            runtime.stop_candidate = lambda: None
            runtime.candidate = lambda _: (_ for _ in ()).throw(DeploymentError("ONLINE_API_NOT_READY"))
            with patch("deploy_runtime.native_artifacts.verify_directory"), self.assertRaises(DeploymentError):
                runtime.start(release("b"))
            previous_count = len(observed)
            wait_for(lambda: len(observed) >= previous_count + 100)
            self.assertEqual({"old"}, set(observed))
            runtime.candidate = lambda _: start_api(7001, "new")
            with patch("deploy_runtime.native_artifacts.verify_directory"), patch.object(Runtime, "start", side_effect=lambda *_: start_api(7000, "new")):
                runtime.start(release("b"))
            # Repeat real serving switches; any proxy reload is an unexpected effect.
            for _ in range(10):
                runtime.route(7001)
                count = len(observed)
                wait_for(lambda: len(observed) >= count + 20)
                runtime.route(7000)
                count = len(observed)
                wait_for(lambda: len(observed) >= count + 20)
            previous_count = len(observed)
            wait_for(lambda: len(observed) >= previous_count + 100)
            stopped.set(); monitor.join(timeout=5)
            self.assertEqual([], failures)
            self.assertEqual({"old", "new"}, set(observed))
            print(f"Native rolling HTTP continuity: {len(observed)} successful responses, zero failures.")


if __name__ == "__main__":
    unittest.main()
