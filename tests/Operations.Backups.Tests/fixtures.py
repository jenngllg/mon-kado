"""Small deterministic disk fixtures; never read production credentials."""

from monkado_backup.capture import create_manifest
from monkado_backup.policy import LEGACY_CURRENT_FILES as FILES


def capture(root):
    """Build the complete allowlisted capture with representative sharded images."""
    for name in FILES:
        path = root / "configuration" / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("fixture")
    (root / "configuration/production.env").write_text("fixture-only")
    (root / "configuration/current.env").write_text(
        "API_IMAGE=ghcr.io/jenngllg/mon-kado-api@sha256:" + "a" * 64
        + "\nWORKER_IMAGE=ghcr.io/jenngllg/mon-kado-worker@sha256:" + "b" * 64
        + "\nRELEASE_REVISION=" + "c" * 40 + "\n")
    (root / "postgres.dump").write_bytes(b"PGDMP fixture")
    image = root / "images/01/ab" / ("01ab" + "c" * 28 + ".webp")
    image.parent.mkdir(parents=True)
    image.write_bytes(b"RIFF fixture")
    keys = root / "keys"
    keys.mkdir()
    (keys / "key-00000000-0000-0000-0000-000000000001.xml").write_text("<key/>")
    return create_manifest(root, "2026-09-21T01:00:00+00:00", "postgres@sha256:" + "d" * 64,
                           "18.6", "caddy@sha256:" + "e" * 64)


def native_capture(root, catalog=None, migrator=None, revision=None):
    """Build native approval, private configuration, flat images and exact executable byte fixtures."""
    import hashlib
    import json
    from monkado_backup.capture import FILES as native_files, create_native_manifest
    from release_manifest import configuration_hash
    from release_catalog import fingerprint
    from deploy_policy import dotenv, initial_state

    for name in native_files:
        path = root / "configuration" / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("fixture")
    configuration = root / "configuration"
    import base64
    (configuration / "production.env").write_text("DATABASE_URL=postgres://mon_kado:fixture@127.0.0.1:5432/mon_kado?sslmode=disable\n"
        + "JWT_SIGNING_KEY=" + base64.b64encode(bytes(range(32))).decode() + "\n"
        + "DATA_ENCRYPTION_KEY=" + base64.b64encode(bytes(range(32, 64))).decode() + "\n")
    (configuration / "caddy.env").write_text("API_HOST=api.monkado.fr\n")
    revision = revision or "c" * 40
    catalog = catalog or {"schemaVersion": 2, "modelHash": "f" * 64,
                          "migrations": [{"id": "001_baseline.sql", "sha256": "a" * 64}]}
    binaries = {}
    (root / "binaries").mkdir()
    for component in ("api", "worker", "migrate"):
        body = migrator.read_bytes() if component == "migrate" and migrator is not None else ("native-fixture-" + component).encode()
        (root / "binaries" / component).write_bytes(body)
        binaries[component] = {"sha256": hashlib.sha256(body).hexdigest(), "size": len(body),
            "url": f"https://github.com/jenngllg/mon-kado/releases/download/backend-{revision}-123-1/monkado-{component}-linux-amd64"}
    release = {"schemaVersion": 3, "revision": revision, "publicationId": "123-1",
               "configurationHash": configuration_hash(configuration), "migrationCatalog": catalog,
               "migrationHash": fingerprint(catalog), "rollbackAllowed": True, "binaries": binaries}
    for name in ("current.env", "active.env"):
        (configuration / name).write_text(dotenv(release))
    state = initial_state() | {"phase": "succeeded", "current": release}
    (configuration / "deployment-status.json").write_text(json.dumps(state))
    (root / "postgres.dump").write_bytes(b"PGDMP fixture")
    (root / "images").mkdir()
    (root / "images/019a1b11-2233-7444-8555-66778899aabb.webp").write_bytes(b"RIFF fixture")
    return create_native_manifest(root, "2026-09-21T01:00:00+00:00", "d" * 64, "18.6", "e" * 64)
