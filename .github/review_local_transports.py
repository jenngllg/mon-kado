"""Record a one-time review of immutable, qualified local transport exceptions."""
import base64
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request

TREES = (
    ("internal", "38bc880565a829898a4cdcbdac44b87ed428834a"),
    ("cmd", "26e60ba2584860f2d734c03ceeac4a1230f043cf"),
    ("src/Tools.GmailOAuthBootstrap", "514d3ca53803cdea79e68bd0f12a799d96ce9725"),
    ("go.mod", "75f9db7b6b688516ad06f9bb555a98287165c5f3"),
    ("go.sum", "ae5237bf78d84aeef7e6a4e6dac37226d2f1bb6f"),
)
CASES = (
    ("AaEtF3mwWCv6d85MrHcS", "go:S2092", "internal/httpapi/cookies.go",
     "22f73d33aaf447bef07571e91a011bbdd44644fab62cd3a16eb5375c9884a8f9",
     "Reviewed exact source and cookie-policy tests. Production always sets Secure, HttpOnly, SameSite Strict and __Host- names, including the trusted TLS-proxy hop. Only non-production HTTP retains the required local-development cookie policy. No insecure production cookie path; contextual false positive."),
    ("AaEtF3utWCv6d85MrHd7", "go:S5332", "src/Tools.GmailOAuthBootstrap/broker.go",
     "8b344b21725a94325e1f5783e74949c0ce19b8be15cc622578e9237dec896549",
     "Reviewed exact source and native OAuth tests. RFC 8252 sections 7.3/8.3 permit HTTP loopback redirection. Listener is 127.0.0.1 on an ephemeral port, with exact Host, PKCE S256, state and replay checks. Google authorization/token endpoints are HTTPS. No external plaintext transport; contextual false positive."),
)


def verify_source():
    for path, expected in TREES:
        actual = subprocess.check_output(["git", "rev-parse", "HEAD:" + path], text=True).strip()
        if actual != expected:
            raise ValueError("SOURCE_REVIEW_NO_LONGER_APPLIES")
    for _, _, path, expected, _ in CASES:
        if hashlib.sha256(Path(path).read_bytes()).hexdigest() != expected:
            raise ValueError("SOURCE_REVIEW_NO_LONGER_APPLIES")


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ValueError("UNEXPECTED_SONAR_REDIRECT")


def request(path, token, values=None):
    body = urllib.parse.urlencode(values).encode() if values is not None else None
    message = urllib.request.Request("https://sonarcloud.io/api/issues/" + path, data=body)
    credentials = base64.b64encode((token + ":").encode()).decode()
    message.add_header("Authorization", "Basic " + credentials)
    if body is not None:
        message.add_header("Content-Type", "application/x-www-form-urlencoded")
    opener = urllib.request.build_opener(NoRedirect())
    with opener.open(message, timeout=30) as response:
        data = response.read(1024 * 1024 + 1)
        if len(data) > 1024 * 1024:
            raise ValueError("SONAR_RESPONSE_LIMIT_EXCEEDED")
        return json.loads(data)


def main():
    verify_source()
    if sys.argv[1:] == ["--check-source"]:
        print("Pinned native source matches the recorded security review.")
        return
    token = os.environ.get("SONAR_TOKEN")
    if not token:
        raise ValueError("REVIEW_CREDENTIAL_UNAVAILABLE")
    query = urllib.parse.urlencode({"issues": ",".join(case[0] for case in CASES),
                                   "componentKeys": "jenngllg_mon-kado", "pullRequest": "127"})
    issues = {issue["key"]: issue for issue in request("search?" + query, token)["issues"]}
    # Check both identities before changing either issue. Future findings are never reviewed here.
    for key, rule, path, _, _ in CASES:
        issue = issues.get(key, {})
        if issue.get("rule") != rule or issue.get("component") != "jenngllg_mon-kado:" + path:
            raise ValueError("UNEXPECTED_SECURITY_FINDING")
    for key, _, _, _, reason in CASES:
        if issues[key].get("resolution") == "FALSE-POSITIVE":
            print(key + ": previously reviewed")
            continue
        result = request("do_transition", token, {"issue": key, "transition": "falsepositive", "comment": reason})
        if result.get("issue", {}).get("resolution") != "FALSE-POSITIVE":
            raise ValueError("REVIEW_NOT_RECORDED")
        print(key + ": recorded contextual false-positive review")


if __name__ == "__main__":
    try:
        main()
    except urllib.error.HTTPError as error:
        print("SONAR_REVIEW_REJECTED_HTTP_" + str(error.code), file=sys.stderr)
        sys.exit(1)
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError):
        print("SONAR_REVIEW_NOT_COMPLETED", file=sys.stderr)
        sys.exit(1)
