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
SHA = r"[0-9a-f]{40}"
DIGEST = r"[0-9a-f]{64}"
FIELDS = {"schemaVersion", "revision", "backendRevision", "configurationHash", "apiOrigin", "archiveSha256"}
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
    require(isinstance(value, dict) and set(value) == FIELDS)
    require(type(value["schemaVersion"]) is int and value["schemaVersion"] == 1)
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
    """Only built entrypoint, revision marker and flat static assets can be served."""
    return name in {"index.html", "release.json"} or ASSET.fullmatch(name) is not None


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
        require("index.html" in seen and "release.json" in seen)
        destination.mkdir(mode=0o755)
        for entry in entries:
            target = destination / entry.name
            target.parent.mkdir(mode=0o755, parents=True, exist_ok=True)
            with package.extractfile(entry) as source, target.open("xb") as output:
                while chunk := source.read(65536):
                    output.write(chunk)
            target.chmod(0o644)
        marker = json.loads((destination / "release.json").read_text(encoding="utf-8"))
        require(marker == {"revision": manifest["revision"], "apiOrigin": API_ORIGIN, "googleEnabled": False})


def package_build(dist, output, revision, backend_revision, configuration_hash):
    """Package only a reviewed Vite build, without inheriting environment secrets."""
    require(matches(SHA, revision) and matches(SHA, backend_revision))
    require(matches(DIGEST, configuration_hash))
    require(dist.is_dir() and not dist.is_symlink())
    require(not output.exists())
    files = sorted(path for path in dist.rglob("*") if not path.is_dir() or path.is_symlink())
    require(0 < len(files) < MAX_FILES)
    for path in files:
        require(path.is_file() and not path.is_symlink())
        require(allowed_file(path.relative_to(dist).as_posix()) and path.name != "release.json")
    require((dist / "index.html").is_file())
    require(sum(path.stat().st_size for path in files) < MAX_EXTRACTED)
    output.mkdir(parents=True)
    marker = output / "release.json"
    marker.write_text(json.dumps({"revision": revision, "apiOrigin": API_ORIGIN, "googleEnabled": False}), encoding="utf-8")
    archive = output / "frontend.tar.gz"
    with archive.open("xb") as raw, gzip.GzipFile(fileobj=raw, mode="wb", filename="", mtime=0) as compressed:
        with tarfile.open(fileobj=compressed, mode="w", format=tarfile.PAX_FORMAT) as package:
            for path in files:
                package.add(path, arcname=path.relative_to(dist).as_posix(), recursive=False, filter=normalize_entry)
            package.add(marker, arcname="release.json", recursive=False, filter=normalize_entry)
    require(archive.stat().st_size <= MAX_COMPRESSED)
    result = {"schemaVersion": 1, "revision": revision, "backendRevision": backend_revision,
              "configurationHash": configuration_hash, "apiOrigin": API_ORIGIN,
              "archiveSha256": file_digest(archive)}
    (output / "manifest.json").write_text(json.dumps(result, sort_keys=True), encoding="utf-8")
    return result


def normalize_entry(entry):
    """Exclude build-agent ownership/timestamps and make retried publication byte-identical."""
    entry.uid = entry.gid = entry.mtime = 0
    entry.uname = entry.gname = ""
    entry.mode = 0o644
    entry.pax_headers = {}
    return entry
