"""Explicit opt-in synthetic Drive exercise; never opens the production repository."""

import asyncio
import base64
import json
import os
import shutil
import sys
import time
from pathlib import Path

from monkado_backup.capture import create_manifest, digest, verify_manifest
from monkado_backup.operations import Operations, command, private_file
from monkado_backup.policy import snapshot_id
from monkado_backup.restore import Restore


def main():
    """Run a synthetic dump through an isolated encrypted repository and new volumes."""
    if os.environ.get("MONKADO_DRIVE_EXERCISE") != "1":
        raise RuntimeError("EXPLICIT_OPT_IN_REQUIRED")
    sys.path.insert(0, "/workspace/tests/Operations.Backups.Tests")
    from fixtures import capture
    from test_postgres_integration import wait_for_healthy

    os.umask(0o077)
    started = time.monotonic()
    name = "monkado-restore-813fixture20260921"
    source = name + "-source"
    root = Path("/exercise")
    original = root / "original"
    restored = root / "restored"
    if original.exists() or restored.exists():
        raise RuntimeError("EXERCISE_ALREADY_EXISTS")
    original.mkdir(mode=0o700)
    fixture = capture(original)
    (original / "keys/key-00000000-0000-0000-0000-000000000001.xml").unlink()
    for key in Path("/fixture/keys").glob("*.xml"):
        shutil.copyfile(key, original / "keys" / key.name)
    image = next((original / "images").rglob("*.webp"))
    image.write_bytes(base64.b64decode("UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA"))
    row = Path("/fixture/row.json").read_bytes()
    image_digest = digest(image)
    postgres_image = "postgres:18.6-alpine"
    command(["docker", "run", "-d", "--name", source, "--network", "none",
             "--label", "monkado.restore=true", "-e", "POSTGRES_HOST_AUTH_METHOD=trust",
             "--health-cmd", "pg_isready -h 127.0.0.1 -U postgres", "--health-interval", "1s", postgres_image])
    try:
        asyncio.run(wait_for_healthy(source))
        sql = root / "fixture.sql"
        sql.write_text("CREATE TABLE backup_probe (id integer PRIMARY KEY, administrator jsonb, image_hash text);"
                       "INSERT INTO backup_probe VALUES (1, convert_from(decode('"
                       + row.hex() + "','hex'),'UTF8')::jsonb, '" + image_digest + "');")
        with sql.open("rb") as stream:
            command(["docker", "exec", "-i", source, "psql", "-v", "ON_ERROR_STOP=1", "-U", "postgres"], input_file=stream)
        with (original / "postgres.dump").open("wb") as stream:
            command(["docker", "exec", source, "pg_dump", "-U", "postgres", "--format=custom",
                     "--no-owner", "--no-acl"], output=stream)
        pg_digest = command(["docker", "image", "inspect", "--format", "{{index .RepoDigests 0}}", postgres_image]).strip()
        manifest = create_manifest(original, fixture["createdAt"], pg_digest, "18.6", fixture["caddyImage"])
    finally:
        command(["docker", "stop", source])

    for filename in ("password", "rclone.conf"):
        private_file(Path("/etc/monkado-backup") / filename)
    repository = "rclone:monkado:MonKado-backups/exercise-813-20260921"
    prefix = ["restic", "--repo", repository, "--password-file", "/etc/monkado-backup/password",
              "--cache-dir", "/exercise/cache", "-o", "rclone.args=serve restic --stdio --drive-use-trash=false"]

    def restic(*arguments, cwd=None):
        return command(prefix + list(arguments), cwd=cwd, timeout=3600,
                       extra_env={"RCLONE_CONFIG": "/etc/monkado-backup/rclone.conf", "GOMEMLIMIT": "128MiB"})

    restic("init")
    report = restic("backup", ".", "--json", "--tag", "synthetic-exercise", cwd=original)
    summary = next(json.loads(line) for line in report.splitlines() if json.loads(line).get("message_type") == "summary")
    identifier = snapshot_id(summary["snapshot_id"])
    repeated = restic("backup", ".", "--json", "--tag", "synthetic-exercise", cwd=original)
    repeated_summary = next(json.loads(line) for line in repeated.splitlines() if json.loads(line).get("message_type") == "summary")
    assert repeated_summary["data_added"] == 0
    restic("check", "--read-data")
    original.rename(root / "original-not-used-for-restore")
    restic("restore", identifier, "--target", str(restored))
    assert manifest == verify_manifest(restored)
    result = Restore(Operations()).volumes(restored, name)
    database = name + "-postgres"
    command(["docker", "start", database])
    try:
        asyncio.run(wait_for_healthy(database))
        restored_row = command(["docker", "exec", database, "psql", "-U", "mon_kado", "-d", "mon_kado",
                                "-Atc", "SELECT administrator FROM backup_probe WHERE id=1"])
        assert json.loads(restored_row) == json.loads(row)
        restored_hash = command(["docker", "exec", database, "psql", "-U", "mon_kado", "-d", "mon_kado",
                                 "-Atc", "SELECT image_hash FROM backup_probe WHERE id=1"]).strip()
        assert restored_hash == image_digest
        output = root / "verification"
        output.mkdir(mode=0o700)
        (output / "row.json").write_text(restored_row)
        shutil.copytree(restored / "keys", output / "keys")
        for relative, metadata in manifest["files"].items():
            if not relative.startswith(("keys/", "images/")):
                continue
            folder, filename = relative.split("/", 1)
            actual = command(["docker", "run", "--rm", "--network", "none", "--user", "1654:1654", "--read-only",
                              "--mount", "type=volume,src=" + name + "-" + folder + ",dst=/restored,readonly",
                              "--entrypoint", "sha256sum", pg_digest, "/restored/" + filename]).split()[0]
            assert actual == metadata["sha256"]
    finally:
        command(["docker", "stop", database])
    result.update(snapshotId=identifier, deduplicationVerified=True, hashesVerified=True,
                  durationSeconds=round(time.monotonic() - started, 1))
    (root / "result.json").write_text(json.dumps(result))
    print(json.dumps(result))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        print('{"event":"synthetic_exercise_failed"}', file=sys.stderr)
        sys.exit(1)
