"""Explicitly enabled disposable Docker campaign. No public target or production credential input."""

from datetime import datetime, timezone
import copy
import http.client
import io
import json
import os
from pathlib import Path
import re
import secrets
import socket
import ssl
import subprocess
import sys
import tarfile
import time
from unittest.mock import patch

sys.path.insert(0, "/source/src/Operations.Deployment")
from deploy_engine import Engine
from deploy_policy import DeploymentError, initial_state
from deploy_runtime import Runtime
from deploy_storage import Store, atomic_write
from release_catalog import fingerprint, generate
from smoke_functional import FunctionalSmoke
from smoke_http import Client, LocalHttps

ROOT = Path("/source")
FIXTURE = Path("/fixture")


def run(arguments, timeout=120, maximum=4 * 1024 * 1024, input=None):
    """Keep Docker diagnostics private; public output contains stage names and fixed failure codes only."""
    result = subprocess.run(arguments, input=input, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout)
    if result.returncode or len(result.stdout) > maximum:
        raise DeploymentError("CAMPAIGN_COMMAND_FAILED")
    return result.stdout


def main():
    """Exercise actual Docker replacement, HTTPS, persistence, rollback and migration refusal."""
    project = os.environ["MK820_RUN_ID"]
    assert re.fullmatch(r"mk820-test-[0-9a-f]{12}", project)
    assert os.environ.get("MK820_DOCKER_CAMPAIGN") == "1"
    assert run(["docker", "inspect", "--format", '{{index .Config.Labels "mk820.campaign"}}', project + "-harness"]).decode().strip() == project
    volume = project + "-fixture"
    assert run(["docker", "volume", "inspect", "--format", '{{index .Labels "mk820.campaign"}}', volume]).decode().strip() == project
    os.umask(0o077)
    FIXTURE.chmod(0o755)
    settings, state = FIXTURE / "settings", FIXTURE / "state"
    settings.mkdir(mode=0o700)
    state.mkdir(mode=0o700)
    for service in ("api", "worker"):
        directory = FIXTURE / "observability" / service
        directory.mkdir(parents=True, mode=0o700)
        directory.parent.chmod(0o755)
        os.chown(directory, 1654, 1654)
    revision = run(["docker", "inspect", "--format", '{{index .Config.Labels "org.opencontainers.image.revision"}}',
                    "mon-kado-api:mk820"]).decode().strip()
    assert re.fullmatch("[0-9a-f]{40}", revision)
    # All secrets below are newly generated synthetic test material in a private disposable volume.
    environment = {"POSTGRES_DB": "mk820", "POSTGRES_USER": "mk820", "POSTGRES_PASSWORD": secrets.token_hex(32),
                   "JWT_SIGNING_KEY": secrets.token_hex(32), "API_HOST": "api.monkado.fr", "API_ALLOWED_HOST": "api.monkado.fr",
                   "FRONTEND_ORIGIN": "https://www.monkado.fr", "API_IMAGE": "mon-kado-api:mk820",
                   "WORKER_IMAGE": "mon-kado-worker:mk820", "RELEASE_REVISION": revision, "EDGE_NETWORK_CIDR": "172.29.182.0/24"}
    env_file = settings / "production.env"
    atomic_write(env_file, "".join(key + "=" + value + "\n" for key, value in environment.items()).encode())
    atomic_write(settings / "monitoring.json", b'{"frontendEnabled":true}')
    compose_prefix = ["docker", "compose", "-p", project, "--env-file", str(env_file), "-f", str(ROOT / "compose.yaml"),
                      "-f", str(ROOT / "deployments/production/compose.production.yaml")]
    config = json.loads(run(compose_prefix + ["config", "--format", "json"]))
    config["name"] = project
    config["networks"] = {"edge": {"internal": True, "ipam": {"config": [{"subnet": environment["EDGE_NETWORK_CIDR"]}]}},
                          "backend": {"internal": True}}
    for spec in config["volumes"].values():
        spec.pop("name", None)
    config["volumes"]["fixture"] = {"external": True, "name": volume}
    for name, spec in config["services"].items():
        spec.pop("cgroup_parent", None)
        spec.pop("build", None)
        spec.pop("ports", None)
        spec["restart"] = "no"
        spec["labels"] = {"mk820.campaign": project}
        spec["volumes"] = [mount for mount in spec.get("volumes", []) if mount["type"] == "volume"]
        if name in ("api", "worker", "caddy"):
            spec["volumes"].append({"type": "volume", "source": "fixture", "target": "/fixture"})
        if name in ("api", "worker"):
            spec["environment"]["Observability__Directory"] = "/fixture/observability/" + name
        if name == "worker":
            spec["environment"].update(DOTNET_ENVIRONMENT="Local", AuthenticationEmail__Provider="Disabled")
    config["services"]["caddy"]["command"] = ["caddy", "run", "--config", "/fixture/Caddyfile"]
    config["services"]["caddy"]["environment"].update(FRONTEND_ROOT="/fixture/frontend", FRONTEND_HOST="www.monkado.fr",
                                                       FRONTEND_APEX_HOST="monkado.fr", FRONTEND_API_ORIGIN="https://api.monkado.fr")
    caddy = (ROOT / "deployments/caddy/Caddyfile").read_text().replace("{\n", "{\n\tlocal_certs\n", 1)
    caddy = caddy.replace("import frontend/*.caddy", "import /fixture/frontend.caddy")
    (FIXTURE / "Caddyfile").write_text(caddy)
    (FIXTURE / "frontend.caddy").write_bytes((ROOT / "deployments/frontend/frontend.caddy").read_bytes())
    front = FIXTURE / "frontend"
    front.mkdir()
    (front / "index.html").write_text("<html><body>MK820 isolated frontend</body></html>")
    (front / "release.json").write_text(json.dumps({"revision": revision, "apiOrigin": "https://api.monkado.fr", "googleEnabled": False}))
    compose = FIXTURE / "compose.json"
    atomic_write(compose, json.dumps(config).encode())
    local_compose = ["docker", "compose", "-p", project, "-f", str(compose)]
    catalog = generate(ROOT / "src/Infrastructure.Persistence.PostgreSql/Migrations")
    mapping, releases, images = {}, {}, []
    for index, letter in enumerate(("a", "b", "c", "d"), start=1):
        migration_catalog = copy.deepcopy(catalog)
        if letter == "d":
            migration_catalog["migrations"].append({"id": "20990101000000_InjectedFailure", "sha256": "d" * 64})
        manifest = {"schemaVersion": 2, "publicationId": str(index) + "-1", "revision": revision, "configurationHash": "f" * 64,
                    "migrationCatalog": migration_catalog, "migrationHash": fingerprint(migration_catalog), "rollbackAllowed": True}
        for component in ("api", "worker"):
            reference = "ghcr.io/jenngllg/mon-kado-" + component + "@sha256:" + letter * 64
            tag = project + "-" + component + ":" + letter
            dockerfile = "FROM mon-kado-" + component + ":mk820\nLABEL mk820.variant=" + letter + "\nLABEL mk820.campaign=" + project + "\n"
            if component == "api":
                dockerfile += "COPY catalog.json /app/migration-catalog.json\n"
            if component == "api" and letter == "c":
                dockerfile += 'ENTRYPOINT ["/app/intentionally-missing-entrypoint"]\n'
            archive = io.BytesIO()
            with tarfile.open(fileobj=archive, mode="w") as bundle:
                for name, raw in (("Dockerfile", dockerfile.encode()), ("catalog.json", json.dumps(migration_catalog).encode())):
                    info = tarfile.TarInfo(name)
                    info.size = len(raw)
                    bundle.addfile(info, io.BytesIO(raw))
            run(["docker", "build", "--network", "none", "-t", tag, "-"], input=archive.getvalue(), timeout=180)
            images.append(tag)
            mapping[reference] = tag
            manifest[component + "Image"] = reference
        releases[letter] = manifest
    config["services"]["api"]["image"] = mapping[releases["a"]["apiImage"]]
    config["services"]["worker"]["image"] = mapping[releases["a"]["workerImage"]]
    atomic_write(compose, json.dumps(config).encode())
    report = {"schemaVersion": 1, "sourceRevision": revision, "productionAccess": False, "externalProviders": False, "stages": [],
              "baseImages": {name: run(["docker", "image", "inspect", "--format", "{{.Id}}", "mon-kado-" + name + ":mk820"]).decode().strip()
                             for name in ("api", "worker")}}

    def sql(statement):
        return run(["docker", "exec", "-i", project + "-postgres-1", "psql", "-X", "-U", "mk820", "-d", "mk820",
                    "-At", "-v", "ON_ERROR_STOP=1"], input=statement.encode()).decode().strip()

    def stage(name):
        report["stages"].append(name)
        print(json.dumps({"stage": name, "passed": True}), flush=True)

    def volume_hashes(name, seed=False):
        script = ("from pathlib import Path; import hashlib,json; root=Path('/data'); "
                  + ("(root/'mk820-preserved.bin').write_bytes(b'isolated image-volume fixture'); " if seed else "")
                  + "print(json.dumps({p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in root.iterdir() if p.is_file()}, sort_keys=True))")
        return json.loads(run(["docker", "run", "--rm", "--network", "none", "--label", "mk820.campaign=" + project,
                               "-v", project + "_" + name + ":/data" + ("" if seed else ":ro"),
                               "monkado-deployment-tests", "python", "-c", script]))

    def adapter(arguments, **kwargs):
        # Only host/systemd and image distribution differ from production; the real engine/runtime remain unmodified.
        if arguments[:2] == ["systemctl", "is-active"]:
            assert arguments == ["systemctl", "is-active", "--quiet", "monkado.slice"]
            return b""
        if arguments[:2] == ["python3", "-c"]:
            arguments = [*arguments[:2], arguments[2].replace("/opt/monkado-backup", "/source/src/Operations.Backups")
                         .replace("/opt/monkado/deployments/production", "/source/deployments/production")]
            return run(arguments, **kwargs)
        if arguments[:2] == ["docker", "pull"]:
            return run(["docker", "image", "inspect", "--format", "{{.Id}}", mapping[arguments[2]]])
        if arguments[:2] == ["docker", "compose"]:
            images_env = dict(line.split("=", 1) for line in (state / "attempt-images.env").read_text().splitlines())
            config["services"]["api"]["image"] = mapping[images_env["API_IMAGE"]]
            config["services"]["worker"]["image"] = mapping[images_env["WORKER_IMAGE"]]
            config["services"]["migrations"]["image"] = mapping[images_env["API_IMAGE"]]
            config["services"]["migrations"]["entrypoint"] = ["/app/intentionally-failed-migration"] if images_env["API_IMAGE"] == releases["d"]["apiImage"] else ["/app/efbundle"]
            atomic_write(compose, json.dumps(config).encode())
            offset = arguments.index(str(ROOT / "deployments/production/compose.production.yaml")) + 1
            return run(local_compose + arguments[offset:], **kwargs)
        translated = [mapping.get(item, item.replace("mon-kado-", project + "-", 1) if item.startswith("mon-kado-") else item) for item in arguments]
        return run(translated, **kwargs)

    try:
        run(local_compose + ["up", "-d", "--no-build", "postgres"], timeout=120)
        run(local_compose + ["run", "--rm", "migrations"], timeout=180)
        run(local_compose + ["up", "-d", "--no-deps", "--no-build", "api", "worker", "caddy"])
        run(["docker", "network", "connect", project + "_edge", project + "-harness"])
        address = json.loads(run(["docker", "inspect", "--format", "{{json .NetworkSettings.Networks}}", project + "-caddy-1"]))[project + "_edge"]["IPAddress"]
        assert address.startswith("172.29.182.")
        certificate = FIXTURE / "root.crt"
        deadline = time.monotonic() + 60
        while True:
            try:
                run(["docker", "cp", project + "-caddy-1:/data/caddy/pki/authorities/local/root.crt", str(certificate)])
                break
            except DeploymentError:
                assert time.monotonic() < deadline, "Local CA not ready"
                time.sleep(1)
        tls = ssl.create_default_context(cafile=certificate)

        class EdgeHttps(LocalHttps):
            @staticmethod
            def local_connection(address_and_port, timeout, source_address=None):
                return socket.create_connection((address, 443), timeout, source_address)

        clients = lambda: Client(connect=EdgeHttps, context=tls)
        runtime = Runtime(runner=adapter, client_factory=clients, root=ROOT, state=state, settings=settings,
                          observability=FIXTURE / "observability")
        deadline = time.monotonic() + 90
        while True:
            try:
                assert clients().request("GET", "/readiness")[0] == 200
                break
            except (DeploymentError, AssertionError):
                assert time.monotonic() < deadline, "Isolated API not ready"
                time.sleep(1)
        account = {"email": "mk820-" + secrets.token_hex(8) + "@example.invalid", "password": secrets.token_urlsafe(32)}
        client = clients()
        csrf, _ = client.json("GET", "/security/csrf-token")
        client.csrf = csrf["token"]
        status, _, _ = client.request("POST", "/api/v1/auth/registrations", payload=account | {"displayName": "Deployment fixture"})
        assert status == 202
        # This operation is only allowed in this disposable, labelled database; no real confirmation email is sent.
        assert sql("SELECT current_database();") == "mk820"
        account["memberId"] = sql("UPDATE users SET email_confirmed=true, unconfirmed_account_expires_at=NULL RETURNING id;")
        account["memberId"] = account["memberId"].splitlines()[0]
        atomic_write(settings / "deployment-smoke.json", json.dumps(account).encode())
        sql("DELETE FROM authentication_email_outbox;")
        seed_client = clients()
        FunctionalSmoke(seed_client, account, state / "unused-journal").login()
        preserved, _ = seed_client.json("POST", "/api/v1/wishlists", expected=201,
                                       payload={"name": "Preserved fixture", "occasion": "other", "eventDate": None,
                                                "message": "Must survive replacement and rollback"})
        seed_client.json("DELETE", "/api/v1/auth/sessions/current", expected=204)
        seed_client.clear()
        image_hashes = volume_hashes("gift_images", seed=True)
        key_hashes = volume_hashes("data_protection_keys")
        assert any(name.startswith("key-") for name in key_hashes)
        before = sql('SELECT count(*) FROM "__EFMigrationsHistory";')
        assert sql("SELECT column_name FROM information_schema.columns WHERE table_name='__EFMigrationsHistory' ORDER BY ordinal_position;") == "migration_id\nproduct_version"
        store = Store(state)
        utc = lambda: datetime.now(timezone.utc).isoformat()
        import smoke_technical
        redirect = smoke_technical.redirect
        connect = lambda host, port, timeout: http.client.HTTPConnection(address, port, timeout=timeout)
        with patch("smoke_technical.redirect", lambda: redirect(connect=connect)):
            baseline = runtime.observe_baseline()
            runtime.smoke(releases["a"])
            store.finish(initial_state() | {"phase": "succeeded", "current": releases["a"], "baseline": baseline})
            stage("baseline-https-authenticated-crud-etag-cleanup")
            result = Engine(store, runtime, utc).deploy(releases["b"])
            assert result["phase"] == "succeeded" and result["current"] == releases["b"]
            assert sql("SELECT count(*) FROM wishlists;") == "1"
            assert sql('SELECT count(*) FROM "__EFMigrationsHistory";') == before
            stage("a-to-b-no-migration-frontend-preserved")
            try:
                Engine(store, runtime, utc).deploy(releases["c"])
                raise AssertionError("Broken publication succeeded")
            except DeploymentError as error:
                assert error.code == "PUBLICATION_ROLLED_BACK", error.code
            assert store.load()["current"] == releases["b"]
            assert not (state / "in-progress.env").exists()
            assert sql("SELECT count(*) FROM users;") == "1"
            assert sql("SELECT id FROM wishlists;") == preserved["id"]
            assert volume_hashes("gift_images") == image_hashes
            assert volume_hashes("data_protection_keys") == key_hashes
            stage("broken-c-to-b-verified-rollback")
            stage("database-image-files-and-data-protection-preserved")
            try:
                Engine(store, runtime, utc).deploy(releases["c"])
                raise AssertionError("Rejected publication retried")
            except DeploymentError as error:
                assert error.code == "PUBLICATION_REJECTED"
            stage("rejected-publication-not-retried")
            try:
                Engine(store, runtime, utc).deploy(releases["d"])
                raise AssertionError("Broken migration succeeded")
            except DeploymentError as error:
                assert error.code == "RECOVERY_REQUIRED", error.code
            assert store.load()["phase"] == "recoveryRequired" and (state / "in-progress.env").exists()
            assert sql('SELECT count(*) FROM "__EFMigrationsHistory";') == before
            stage("migration-failure-no-automatic-rollback")
        report["passed"] = True
        print(json.dumps(report, sort_keys=True), flush=True)
    finally:
        # Compose scopes removal to this validated unique project; no host-wide prune or production names.
        subprocess.run(["docker", "network", "disconnect", project + "_edge", project + "-harness"],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30)
        run(local_compose + ["down", "--volumes", "--remove-orphans"], timeout=120)
        run(["docker", "image", "rm", *images], timeout=120)


if __name__ == "__main__":
    main()
