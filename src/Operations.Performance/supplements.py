"""Bounded heavy-operation checks alongside k6 reads, on the same isolated network."""
import concurrent.futures
import hashlib
import http.cookiejar
import io
import json
import ssl
import sys
import time
import urllib.error
import urllib.request
from urllib.parse import urlsplit
import zipfile
from pathlib import Path

from policy import PerformanceError, require
from seed import png

ORIGIN = "https://mk816.test"
SHARE = "X-MonKado-Share-Token"


class ImageCaseFailure(PerformanceError):
    """Carry only completed public measurements when the next image case fails."""
    def __init__(self, name, kind, concurrency_count, results):
        super().__init__("IMAGE_CASE_FAILED_" + name.upper() + "_" + kind.upper())
        self.details = {"case": name, "kind": kind, "concurrency": concurrency_count, "completed": list(results)}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise PerformanceError("REDIRECT_REFUSED")


class Client:
    def __init__(self, account, password):
        self.account = account
        self.token = None
        self.csrf = None
        self.expires = 0
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPSHandler(context=ssl.create_default_context(cafile="/fixture/ca.crt")),
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()), NoRedirect())
        self.csrf = self.json_request("GET", "/security/csrf-token")[0]["token"]
        auth, _ = self.json_request("POST", "/api/v1/auth/sessions",
                                    {"email": account["email"], "password": password, "rememberMe": False})
        self.set_auth(auth)
        self.csrf = self.json_request("GET", "/security/csrf-token")[0]["token"]

    def set_auth(self, auth):
        self.token = auth["accessToken"]
        self.expires = time.monotonic() + auth["expiresIn"] - 60

    def request(self, method, path, body=None, headers=None):
        require(path.startswith("/api/v1/") or path == "/security/csrf-token", "UNSAFE_PATH")
        if self.token and time.monotonic() >= self.expires and path != "/api/v1/auth/sessions/refresh":
            auth, _ = self.json_request("POST", "/api/v1/auth/sessions/refresh")
            self.set_auth(auth)
            self.csrf = self.json_request("GET", "/security/csrf-token")[0]["token"]
        request_headers = {"Origin": ORIGIN, **(headers or {})}
        if self.token:
            request_headers["Authorization"] = "Bearer " + self.token
        if self.csrf:
            request_headers["X-CSRF-TOKEN"] = self.csrf
        request = urllib.request.Request(ORIGIN + path, body, request_headers, method=method)
        started = time.monotonic()
        try:
            response = self.opener.open(request, timeout=20)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            content = response.read(16 * 1024 * 1024 + 1)
            require(len(content) <= 16 * 1024 * 1024, "RESPONSE_TOO_LARGE")
            return response.code, content, response.headers, (time.monotonic() - started) * 1000

    def json_request(self, method, path, body=None, headers=None, expected=(200,)):
        data = None if body is None else json.dumps(body).encode()
        code, content, response_headers, _ = self.request(method, path, data, {"Content-Type": "application/json", **(headers or {})})
        require(code in expected, "UNEXPECTED_HTTP_STATUS")
        return json.loads(content) if content else None, response_headers


def parallel(actions):
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        return list(pool.map(lambda action: action(), actions))


def image_check(client, kind, fixture):
    wishlist = client.account["lists"][4]
    wish = wishlist["wishes"][0]
    source = "/api/v1/auth/sessions/current" if kind == "profile" else f"/api/v1/wishlists/{wishlist['id']}/wishes/{wish}"
    _, headers = client.json_request("GET", source)
    target = "/api/v1/members/current/profile/image" if kind == "profile" else source + "/image"
    boundary = "MK816ImageBoundary"
    data = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"image\"; filename=\"synthetic.png\"\r\nContent-Type: image/png\r\n\r\n".encode()
            + fixture + f"\r\n--{boundary}--\r\n".encode())
    code, uploaded, _, elapsed = client.request("PUT", target, data,
                                         {"If-Match": headers["ETag"], "Content-Type": f"multipart/form-data; boundary={boundary}"})
    require(code == 200, "IMAGE_UPLOAD_FAILED")
    location = json.loads(uploaded)["profileImageUrl" if kind == "profile" else "imageUrl"]
    parsed = urlsplit(location)
    require((not parsed.scheme and not parsed.netloc) or (parsed.scheme == "https" and parsed.netloc == "mk816.test"), "IMAGE_ORIGIN_REFUSED")
    download = parsed.path + "?" + parsed.query
    code, content, _, _ = client.request("GET", download)
    require(code == 200 and content[:4] == b"RIFF" and content[8:12] == b"WEBP", "IMAGE_INVALID")
    repeated = client.request("GET", download)
    require(repeated[0] == 200 and hashlib.sha256(content).digest() == hashlib.sha256(repeated[1]).digest(), "IMAGE_CHANGED")
    return {"kind": kind, "inputBytes": len(fixture), "outputBytes": len(content), "milliseconds": elapsed, "valid": True}


def images(clients):
    results = []
    for name, fixture in (("normal", png()), ("bytes", png(1800, 1900, compression=0)), ("pixels", png(8000, 5000))):
        for kind in ("gift", "profile"):
            concurrency_count = 1
            try:
                single = image_check(clients[0], kind, fixture)
                results.append({**single, "case": name, "concurrency": 1})
                concurrency_count = 2
                for result in parallel([lambda client=client: image_check(client, kind, fixture) for client in clients[:2]]):
                    results.append({**result, "case": name, "concurrency": 2})
            except PerformanceError:
                raise ImageCaseFailure(name, kind, concurrency_count, results) from None
    return results


def export_check(client):
    started = time.monotonic()
    record, _ = client.json_request("POST", "/api/v1/members/current/data-exports", expected=(202,))
    path = "/api/v1/members/current/data-exports/" + record["id"]
    while record["status"] in ("queued", "processing"):
        require(time.monotonic() - started < 600, "EXPORT_TIMEOUT")
        time.sleep(2)
        record, _ = client.json_request("GET", path)
    require(record["status"] == "ready", "EXPORT_FAILED")
    code, content, _, _ = client.request("GET", path + "/archive")
    require(code == 200 and len(content) == record["sizeInBytes"], "EXPORT_LENGTH")
    with zipfile.ZipFile(io.BytesIO(content)) as archive:
        require(archive.testzip() is None, "EXPORT_CRC")
        names = archive.namelist()
        require(any(name.endswith(".webp") for name in names), "EXPORT_IMAGES_MISSING")
        data_files = [name for name in names if name.endswith(".json")]
        require(len(data_files) == 1, "EXPORT_DATA_MISSING")
        data = json.loads(archive.read(data_files[0]))
        require(data.get("schemaVersion") == 1 and data.get("account", {}).get("profile", {}).get("id") == client.account["memberId"], "EXPORT_WRONG_MEMBER")
        require(len(data.get("wishlists", [])) == 5 and len(data.get("wishes", [])) == 100, "EXPORT_DATA_INCOMPLETE")
        image_paths = [data["account"]["profile"].get("imagePath"), *[wish.get("imagePath") for wish in data["wishes"]]]
        require(all(path in names for path in image_paths if path is not None), "EXPORT_IMAGES_MISSING")
    return {"milliseconds": (time.monotonic() - started) * 1000, "bytes": len(content), "valid": True}


def exports(clients):
    # Three different accounts ensure the pair creates new jobs, not a ready-archive reuse.
    first = export_check(clients[0])
    pair = parallel([lambda client=client: export_check(client) for client in clients[1:3]])
    return [{**first, "concurrency": 1}, *[{**item, "concurrency": 2} for item in pair]]


def quotas(client):
    limited = None
    for _ in range(310):
        code, _, headers, _ = client.request("GET", "/api/v1/wishlists")
        if code == 429:
            limited = int(headers.get("Retry-After", "0"))
            require(headers.get("Cache-Control") == "no-store", "QUOTA_CACHE")
            break
        require(code == 200, "QUOTA_UNEXPECTED_STATUS")
        time.sleep(.1)
    require(limited is not None and 1 <= limited <= 61, "QUOTA_NOT_ENFORCED")
    time.sleep(limited + 1)
    require(client.request("GET", "/api/v1/wishlists")[0] == 200, "QUOTA_NOT_RECOVERED")
    return [{"limited": True, "retryAfter": limited, "recovered": True}]


def concurrency(clients):
    owner, first, second = clients
    wishlist = owner.account["lists"][4]
    target = f"/api/v1/wishlists/{wishlist['id']}/wishes/{wishlist['wishes'][19]}"
    _, headers = owner.json_request("GET", target)
    original_tag = headers["ETag"]
    updates = parallel([lambda name=name: owner.request("PUT", target,
        json.dumps({"name": name, "note": None, "url": None, "price": 25, "quantity": 10}).encode(),
        {"Content-Type": "application/json", "If-Match": original_tag}) for name in ("Concurrent A", "Concurrent B")])
    require(sorted(item[0] for item in updates) == [200, 412], "ETAG_CONCURRENCY")
    persisted, _ = owner.json_request("GET", target)
    require(persisted["name"] in ("Concurrent A", "Concurrent B"), "UPDATE_NOT_PERSISTED")
    link, _ = owner.json_request("POST", f"/api/v1/wishlists/{wishlist['id']}/share-link", expected=(201,))
    proof = {SHARE: link["shareUrl"].split("#")[1]}
    public = "/api/v1/shared-wishlists/" + link["id"]
    for client in (first, second):
        client.json_request("POST", public + "/participants", {}, proof, expected=(201,))
    reservation = public + "/wishes/" + wishlist["wishes"][19] + "/reservations/current"
    reserved = parallel([lambda client=client: client.request("PUT", reservation,
        b'{"quantity":10}', {**proof, "Content-Type": "application/json"}) for client in (first, second)])
    require(sorted(item[0] for item in reserved) == [201, 409], "RESERVATION_CONCURRENCY")
    return [{"etagConflict": True, "reservationConflict": True, "valid": True}]


def main():
    fixture = json.loads(Path("/fixture/fixtures-0.json").read_text())
    profile = sys.argv[1]
    try:
        clients = [Client(account, fixture["password"]) for account in fixture["accounts"][7:10]]
        if profile == "images":
            results = images(clients[1:])
        elif profile == "exports":
            results = exports(clients)
        elif profile == "quotas":
            results = quotas(clients[0])
        elif profile == "concurrency":
            results = concurrency(clients)
        else:
            raise PerformanceError("INVALID_SUPPLEMENT")
        print(json.dumps({"profile": profile, "passed": True, "results": results}))
        return 0
    except (PerformanceError, OSError, ValueError, KeyError, zipfile.BadZipFile) as error:
        code = str(error) if isinstance(error, PerformanceError) else "SUPPLEMENT_FAILED"
        print(json.dumps({"profile": profile, "passed": False, "error": code, "details": getattr(error, "details", None)}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
