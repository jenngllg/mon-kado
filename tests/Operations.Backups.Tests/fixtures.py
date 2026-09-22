"""Small deterministic disk fixtures; never read production credentials."""

from monkado_backup.capture import FILES, create_manifest


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
