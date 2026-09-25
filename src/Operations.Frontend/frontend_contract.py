"""Strict public manifest and static archive boundary; downloaded code is never executed."""

import hashlib
import gzip
import json
import re
import tarfile

REPOSITORY = "jenngllg/mon-kado-front"
API_ORIGIN = "https://api.monkado.fr"
MAX_COMPRESSED = 50 * 1024 * 1024
MAX_EXTRACTED = 200 * 1024 * 1024
MAX_FILES = 10000
ENTRYPOINT = "index.html"
MARKER = "release.json"
SHA = r"[0-9a-f]{40}"
DIGEST = r"[0-9a-f]{64}"
FIELDS = {"schemaVersion", "revision", "backendRevision", "configurationHash", "apiOrigin", "archiveSha256"}
LEGAL_PAGES = {"legal-notice.html", "privacy-policy.html", "terms-of-use.html"}
ASSET = re.compile(r"assets/[A-Za-z0-9_-][A-Za-z0-9._-]*\.(?:js|css|woff2?|ttf|png|webp|jpe?g|svg|ico|avif)")


def require(condition):
    """Fail closed without echoing untrusted manifest fields or paths."""
    if not condition:
        raise ValueError("Invalid frontend publication")


def matches(pattern, value):
    """Reject non-string and non-canonical identifiers."""
    return isinstance(value, str) and re.fullmatch(pattern, value) is not None


def validate(value, configuration_hash, backend_revision):
    """Bind the static release to the reviewed host configuration and active backend."""
    require(isinstance(value, dict))
    version = value.get("schemaVersion")
    require(type(version) is int and version in (1, 2))
    require(set(value) == (FIELDS if version == 1 else FIELDS | {"googleEnabled"}))
    require(type(value.get("googleEnabled", False)) is bool)
    require(matches(SHA, value["revision"]) and matches(SHA, value["backendRevision"]))
    require(matches(DIGEST, value["configurationHash"]) and matches(DIGEST, value["archiveSha256"]))
    require(value["configurationHash"] == configuration_hash)
    require(value["backendRevision"] == backend_revision)
    require(value["apiOrigin"] == API_ORIGIN)
    return value


def from_release(release, configuration_hash, backend_revision):
    """Accept only the explicit, non-draft production pointer."""
    require(isinstance(release, dict))
    require(release.get("tag_name") == "frontend-production")
    require(release.get("draft") is False and release.get("prerelease") is False)
    body = release.get("body")
    require(isinstance(body, str) and len(body) <= 4096)
    return validate(json.loads(body), configuration_hash, backend_revision)


def archive_url(manifest):
    """Derive the only accepted artifact location from a validated revision."""
    require(matches(SHA, manifest["revision"]))
    return f'https://github.com/{REPOSITORY}/releases/download/frontend-{manifest["revision"]}/frontend.tar.gz'


def file_digest(path):
    """Hash a local file with bounded working memory."""
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def allowed_file(name):
    """Allow exactly the public documents, revision marker and flat static assets."""
    return name in {ENTRYPOINT, MARKER} | LEGAL_PAGES or ASSET.fullmatch(name) is not None


def release_marker(revision, google_enabled=False):
    """Build the exact public marker without accepting integer booleans."""
    require(type(google_enabled) is bool)
    return {"revision": revision, "apiOrigin": API_ORIGIN, "googleEnabled": google_enabled}


def extract(archive, destination, manifest):
    """Validate the whole archive before writing exclusively into a fresh staging directory."""
    require(archive.stat().st_size <= MAX_COMPRESSED)
    require(file_digest(archive) == manifest["archiveSha256"])
    require(not destination.exists() and not destination.is_symlink())
    with tarfile.open(archive, "r:gz") as package:
        entries = []
        seen = set()
        total = 0
        for entry in package:
            require(len(entries) < MAX_FILES)
            require(entry.isfile() and allowed_file(entry.name))
            require(entry.name.casefold() not in seen)
            require(entry.size >= 0)
            total += entry.size
            require(total <= MAX_EXTRACTED)
            seen.add(entry.name.casefold())
            entries.append(entry)
        require(ENTRYPOINT in seen and MARKER in seen)
        require(manifest["schemaVersion"] == 1 or LEGAL_PAGES <= seen)
        destination.mkdir(mode=0o755)
        for entry in entries:
            target = destination / entry.name
            target.parent.mkdir(mode=0o755, parents=True, exist_ok=True)
            with package.extractfile(entry) as source, target.open("xb") as output:
                while chunk := source.read(65536):
                    output.write(chunk)
            target.chmod(0o644)
        marker = json.loads((destination / MARKER).read_text(encoding="utf-8"))
        require(isinstance(marker, dict) and type(marker.get("googleEnabled")) is bool)
        require(marker == release_marker(manifest["revision"], manifest.get("googleEnabled", False)))


def package_build(dist, output, revision, backend_revision, configuration_hash, google_enabled=None):
    """Package only a reviewed Vite build, without inheriting environment secrets."""
    require(matches(SHA, revision) and matches(SHA, backend_revision))
    require(matches(DIGEST, configuration_hash))
    require(google_enabled is None or type(google_enabled) is bool)
    require(dist.is_dir() and not dist.is_symlink())
    require(not output.exists())
    files = sorted(path for path in dist.rglob("*") if not path.is_dir() or path.is_symlink())
    require(0 < len(files) < MAX_FILES)
    for path in files:
        require(path.is_file() and not path.is_symlink())
        require(allowed_file(path.relative_to(dist).as_posix()) and path.name != MARKER)
    require((dist / ENTRYPOINT).is_file())
    require(google_enabled is None or all((dist / name).is_file() for name in LEGAL_PAGES))
    require(sum(path.stat().st_size for path in files) < MAX_EXTRACTED)
    output.mkdir(parents=True)
    marker = output / MARKER
    marker.write_text(json.dumps(release_marker(revision, google_enabled is True)), encoding="utf-8")
    archive = output / "frontend.tar.gz"
    with archive.open("xb") as raw, gzip.GzipFile(fileobj=raw, mode="wb", filename="", mtime=0) as compressed:
        with tarfile.open(fileobj=compressed, mode="w", format=tarfile.PAX_FORMAT) as package:
            for path in files:
                package.add(path, arcname=path.relative_to(dist).as_posix(), recursive=False, filter=normalize_entry)
            package.add(marker, arcname=MARKER, recursive=False, filter=normalize_entry)
    require(archive.stat().st_size <= MAX_COMPRESSED)
    result = {"schemaVersion": 1, "revision": revision, "backendRevision": backend_revision,
              "configurationHash": configuration_hash, "apiOrigin": API_ORIGIN,
              "archiveSha256": file_digest(archive)}
    if google_enabled is not None:
        result.update(schemaVersion=2, googleEnabled=google_enabled)
    (output / "manifest.json").write_text(json.dumps(result, sort_keys=True), encoding="utf-8")
    return result


def normalize_entry(entry):
    """Exclude build-agent ownership/timestamps and make retried publication byte-identical."""
    entry.uid = entry.gid = entry.mtime = 0
    entry.uname = entry.gname = ""
    entry.mode = 0o644
    entry.pax_headers = {}
    return entry
