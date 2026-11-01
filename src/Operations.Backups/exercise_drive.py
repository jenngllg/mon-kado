"""Explicit synthetic Drive qualification with a native Go schema and private Unix sockets."""

import base64
import json
import os
from pathlib import Path
import secrets
import shutil
import sys
import tempfile
import time

from monkado_backup.capture import create_native_manifest, digest, verify_manifest
from monkado_backup.operations import Operations, command, private_file
from monkado_backup.policy import snapshot_id
from monkado_backup.restore import Restore, PG_BIN, SOCKET_PORT


def prepare_capture(root, migrator):
    """Migrate a disposable native database and capture synthetic protected bytes only."""
    sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tests/Operations.Backups.Tests"))
    from fixtures import native_capture

    binary = root / "migrate"
    shutil.copyfile(migrator, binary)
    binary.chmod(0o755)
    metadata = json.loads(command([str(binary), "--version"]))
    catalog = json.loads(command([str(binary), "--catalog"]))
    original = root / "original"
    original.mkdir(mode=0o700)
    fixture = native_capture(original, catalog=catalog, migrator=binary, revision=metadata["revision"])
    image = next((original / "images").glob("*.webp"))
    image.write_bytes(base64.b64decode("UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA"))
    image_hash = digest(image)
    # Opaque synthetic ciphertext exercises preservation without any real account or token.
    payload = {"protectedSecret": base64.b64encode(secrets.token_bytes(64)).decode()}
    source, socket = root / "source", root / "socket"
    prefix = ["runuser", "-u", "postgres", "--"]
    command(["install", "-d", "-m", "0700", "-o", "postgres", "-g", "postgres", str(source), str(socket)])
    command([*prefix, str(PG_BIN / "initdb"), "--pgdata=" + str(source), "--auth-local=peer",
             "--auth-host=reject", "--encoding=UTF8", "--locale=C.UTF-8", "--no-instructions"])
    control = [*prefix, str(PG_BIN / "pg_ctl"), "--pgdata=" + str(source), "--wait", "--timeout=60"]
    options = ("-c listen_addresses='' -c unix_socket_directories=" + str(socket)
               + " -c unix_socket_permissions=0700 -c port=" + SOCKET_PORT
               + " -c shared_buffers=32MB -c max_connections=10")
    try:
        command([*control, "start", "--log=" + str(source / "startup.log"), "--options=" + options])
        connection = ["--host=" + str(socket), "--port=" + SOCKET_PORT, "--username=postgres", "--no-password"]
        command([*prefix, str(PG_BIN / "createdb"), *connection, "--template=template0", "mon_kado_fixture"])
        command([*prefix, str(binary)], extra_env={
            "DATABASE_URL": "postgres://postgres@/mon_kado_fixture?host=" + str(socket) + "&port=" + SOCKET_PORT + "&sslmode=disable",
            "JWT_SIGNING_KEY": base64.b64encode(bytes(range(32))).decode(),
            "DATA_ENCRYPTION_KEY": base64.b64encode(bytes(range(32, 64))).decode(),
            "APP_ENV": "local", "EMAIL_PROVIDER": "Disabled"})
        psql = [*prefix, str(PG_BIN / "psql"), *connection, "--dbname=mon_kado_fixture", "-X", "-At", "-v", "ON_ERROR_STOP=1", "-c"]
        command([*psql, "CREATE TABLE backup_probe(id integer PRIMARY KEY, protected_payload jsonb NOT NULL, image_hash text NOT NULL);"
                 "INSERT INTO backup_probe VALUES(1,convert_from(decode('" + json.dumps(payload).encode().hex()
                 + "','hex'),'UTF8')::jsonb,'" + image_hash + "');"])
        with (original / "postgres.dump").open("wb") as dump:
            command([*prefix, str(PG_BIN / "pg_dump"), *connection, "--dbname=mon_kado_fixture",
                     "--format=custom", "--compress=0", "--no-owner", "--no-acl"], output=dump)
        manifest = create_native_manifest(original, fixture["createdAt"], fixture["postgresSHA256"], "18.6", fixture["caddySHA256"])
    finally:
        command([*control, "stop", "--mode=fast"])
    return manifest, payload, image_hash


def verify_restoration(root, restored, name, manifest, payload, image_hash):
    """Read the restored probe and hashes, then leave the isolated database stopped."""
    restore = Restore(Operations(), root=root / "r")
    result = restore.volumes(restored, name)
    target = restore.root / name
    prefix = ["runuser", "-u", "postgres", "--"]
    control = [*prefix, str(PG_BIN / "pg_ctl"), "--pgdata=" + str(target / "postgres"), "--wait", "--timeout=60"]
    try:
        command([*control, "start", "--log=" + str(target / "postgres/verification.log")])
        psql = [*prefix, str(PG_BIN / "psql"), "--host=" + str(target / "socket"), "--port=" + SOCKET_PORT,
                "--username=postgres", "--no-password", "--dbname=mon_kado_restore", "-X", "-At", "-v", "ON_ERROR_STOP=1", "-c"]
        actual = command([*psql, "SELECT protected_payload FROM backup_probe WHERE id=1;"])
        if json.loads(actual) != payload or command([*psql, "SELECT image_hash FROM backup_probe WHERE id=1;"]).strip() != image_hash:
            raise RuntimeError("RESTORED_PROBE_DIVERGED")
        for relative, item in manifest["files"].items():
            if relative.startswith(("images/", "configuration/", "binaries/")) and digest(target / relative) != item["sha256"]:
                raise RuntimeError("RESTORED_FILE_DIVERGED")
    finally:
        command([*control, "stop", "--mode=fast"])
    return result


def main():
    """Require explicit provider opt-in and keep every resource separate from production."""
    if os.environ.get("MONKADO_DRIVE_EXERCISE") != "1":
        raise RuntimeError("EXPLICIT_OPT_IN_REQUIRED")
    if os.geteuid() != 0:
        raise RuntimeError("ROOT_REQUIRED")
    migrator = Path(os.environ["MONKADO_NATIVE_MIGRATOR"]).resolve(strict=True)
    for filename in ("password", "rclone.conf"):
        private_file(Path("/etc/monkado-backup") / filename)
    os.umask(0o077)
    started = time.monotonic()
    # Keep the private result and stopped clusters for inspection; never reuse a repository.
    root = Path(tempfile.mkdtemp(prefix="mk-drive-"))
    root.chmod(0o711)
    name = "monkado-restore-" + secrets.token_hex(8)
    manifest, payload, image_hash = prepare_capture(root, migrator)
    repository = "rclone:monkado:MonKado-backups/exercise-native-" + name.removeprefix("monkado-restore-")
    prefix = ["restic", "--repo", repository, "--password-file", "/etc/monkado-backup/password",
              "--cache-dir", str(root / "cache"), "-o", "rclone.args=serve restic --stdio --drive-use-trash=false"]

    def restic(*arguments, cwd=None):
        return command(prefix + list(arguments), cwd=cwd, timeout=3600,
                       extra_env={"RCLONE_CONFIG": "/etc/monkado-backup/rclone.conf", "GOMEMLIMIT": "128MiB"})

    restic("init")
    summaries = []
    for _ in range(2):
        report = restic("backup", ".", "--json", "--tag", "synthetic-native-exercise", cwd=root / "original")
        summaries.append(next(json.loads(line) for line in report.splitlines() if json.loads(line).get("message_type") == "summary"))
    identifier = snapshot_id(summaries[0]["snapshot_id"])
    if summaries[1]["data_added"] != 0:
        raise RuntimeError("DEDUPLICATION_FAILED")
    restic("check", "--read-data")
    (root / "original").rename(root / "original-not-used-for-restore")
    restored = root / "restored"
    restic("restore", identifier, "--target", str(restored))
    if manifest != verify_manifest(restored):
        raise RuntimeError("RESTORED_CAPTURE_DIVERGED")
    result = verify_restoration(root, restored, name, manifest, payload, image_hash)
    result.update(exerciseDirectory=str(root), snapshotId=identifier, deduplicationVerified=True, hashesVerified=True,
                  durationSeconds=round(time.monotonic() - started, 1))
    (root / "result.json").write_text(json.dumps(result), encoding="utf-8")
    print(json.dumps(result))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        print('{"event":"synthetic_exercise_failed"}', file=sys.stderr)
        sys.exit(1)
