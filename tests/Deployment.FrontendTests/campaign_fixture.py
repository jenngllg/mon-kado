"""Run only inside the disposable MK-936 network namespace; all releases are synthetic."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, "/source/src/Operations.Frontend")
import frontend_contract as contract
import frontend_runtime as runtime

ROOT = Path("/fixture")
STATE = ROOT / "state"
BACKEND = ROOT / "backend"
CONFIGURATION = "c" * 64
A, B, C, D, E = (letter * 40 for letter in "abdef")


def transport(url, destination, limit):
    """Never perform a provider request; mirror the approved public artifact contract locally."""
    if url == runtime.POINTER:
        manifest = json.loads((ROOT / "approved.json").read_text())
        destination.write_text(json.dumps({"tag_name": "frontend-production", "draft": False, "prerelease": False,
                                          "body": json.dumps(manifest)}))
    else:
        revision = url.split("frontend-", 1)[1].split("/", 1)[0]
        shutil.copyfile(ROOT / ("package-" + revision) / "frontend.tar.gz", destination)
    assert destination.stat().st_size <= limit


def operation(health=runtime.probe):
    return runtime.Deployment(STATE, BACKEND, CONFIGURATION, transport=transport, health=health)


def approve(revision):
    shutil.copyfile(ROOT / ("package-" + revision) / "manifest.json", ROOT / "approved.json")


def fail_corrupt_response(revision, directory, google_enabled):
    """Introduce an actual incorrect HTTPS asset response only after a switch, then repair the fixture."""
    asset = directory / "assets/main-abcdefgh.js"
    original = asset.read_bytes()
    with tempfile.TemporaryDirectory(dir=ROOT) as temporary:
        expected = Path(temporary) / "verified"
        shutil.copytree(directory, expected)
        try:
            asset.write_bytes(b"// simulated corrupt served bytes")
            runtime.probe(revision, expected, google_enabled)
            raise AssertionError("Smoke accepted corrupted response")
        finally:
            asset.write_bytes(original)


def expect_failure(action):
    try:
        action()
    except ValueError:
        return
    raise AssertionError("Expected publication failure")


def interrupt(revision, directory, google_enabled):
    runtime.probe(revision, directory, google_enabled)
    if revision == D:
        os._exit(17)  # A real child process exits with the activation journal still durable.


def campaign():
    STATE.mkdir()
    (STATE / "releases").mkdir()
    BACKEND.mkdir()
    (BACKEND / "current.env").write_text("RELEASE_REVISION=" + "b" * 40 + "\n")
    dist = ROOT / "dist"
    (dist / "assets").mkdir(parents=True)
    for page in contract.LEGAL_PAGES:
        (dist / page).write_text("<!doctype html><html><title>Synthetic legal fixture</title></html>")
    for revision in (A, B, C, D, E):
        reference = "missing-abcdefgh.js" if revision == C else "main-abcdefgh.js"
        (dist / "index.html").write_text('<!doctype html><html><title>Synthetic fixture</title>'
                                        '<script type="module" src="/assets/' + reference + '"></script>'
                                        '<link rel="stylesheet" href="/assets/main-abcdefgh.css"></html>')
        (dist / "assets/main-abcdefgh.js").write_text("// synthetic revision " + revision)
        (dist / "assets/main-abcdefgh.css").write_text("body{color:black}")
        contract.package_build(dist, ROOT / ("package-" + revision), revision, "b" * 40, CONFIGURATION, False)
    approve(A)
    assert operation().deploy() == "installed"
    approve(B)
    assert operation().deploy() == "installed"
    approve(C)
    expect_failure(operation().deploy)
    assert runtime.current_revision(STATE / "releases") == B
    assert operation().load()["phase"] == "rolledBack"
    assert operation().deploy(retry=True) == "paused"
    operation().verify(B, "b" * 40)
    assert operation().rollback(A) == "installed"
    assert operation().load()["paused"]
    approve(B)
    assert operation().resume(B) == "resumed"
    assert runtime.current_revision(STATE / "releases") == A
    assert operation().deploy() == "installed"
    approve(D)
    child = subprocess.run([sys.executable, __file__, "interrupt"], capture_output=True, timeout=90)
    assert child.returncode == 17
    assert runtime.current_revision(STATE / "releases") == D
    assert operation().deploy() == "recovered"
    assert runtime.current_revision(STATE / "releases") == B
    assert operation().load()["paused"]
    approve(E)
    operation().resume(E)

    def broken_fallback(revision, directory, google_enabled):
        if (STATE / "transition.json").exists():
            fail_corrupt_response(revision, directory, google_enabled)
        runtime.probe(revision, directory, google_enabled)

    expect_failure(operation(broken_fallback).deploy)
    assert operation().load()["phase"] == "recoveryRequired"
    assert (STATE / "transition.json").exists()
    assert operation().deploy(retry=True) == "paused"
    operation().recover()
    assert operation().load()["phase"] == "rolledBack"
    assert not (STATE / "transition.json").exists()
    operation().verify(B, "b" * 40)
    assert not (BACKEND / "in-progress.env").exists()
    assert (BACKEND / "current.env").read_text() == "RELEASE_REVISION=" + "b" * 40 + "\n"
    print(json.dumps({"schemaVersion": 1, "tlsVerified": True, "aToB": True, "invalidEntrypointRestoredB": True,
                      "manualRollbackPaused": True, "resumeDoesNotPublish": True, "processExitRecovered": True,
                      "failedRollbackRetainsJournal": True, "explicitRecoveryVerified": True,
                      "backendUntouched": True, "googleEnabled": False}))


if __name__ == "__main__":
    if sys.argv[1:] == ["interrupt"]:
        operation(interrupt).deploy()
    else:
        campaign()
