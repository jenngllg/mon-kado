# Approved frontend hosting — MK-811

The static frontend uses the existing VPS and Caddy, not GitHub Pages. No extra
subscription, Node process, new public port, SSH deployment key or production
secret in GitHub Actions is required. API/database/Worker deployment stays separate.

## Release trust boundary

The frontend repository's manually dispatched publication workflow runs only on
`develop`, requires successful quality checks for the exact commit, and requires
the `production` environment to have an approval rule. Configure that environment
only with explicit authorization; a merge alone never publishes a build.

The workflow uses the backend revision from the explicit `backend-production`
release, checks the public deployed OpenAPI contract and packages a Vite build
with the public API origin and Google flag reviewed in the frontend's versioned
`publication.json` (Google remains disabled until its approved smoke test). The backend
hosting changes must therefore be merged, installed and published first.

Archives and manifests are published under immutable `frontend-<commit>` releases.
The `frontend-production` release body is the promotion pointer. A repeated build
must match the existing archive and manifest byte-for-byte; partial or conflicting
releases require investigation, never an overwrite. The server only reads fixed
GitHub repositories over HTTPS, validates the configuration hash and active
backend revision, and never executes anything downloaded with a release.

Archives are restricted to index.html, release.json, the three named legal HTML
documents and flat assets. Manifest v2 requires all three documents and binds an
explicit boolean `googleEnabled` to the release marker. Legacy v1 remains readable
with Google disabled; do not rewrite existing immutable releases. The compressed
limit is 50 MiB and the extracted limit is 200 MiB, with a 10,000-member cap.
Links, duplicate paths, source maps, private paths and unexpected files are refused.
Allow an additional 64 MiB of free space beyond the archive and extraction limits.

## First installation — operator approval required

1. Confirm backend and frontend MRs, their green gates and the approved revisions.
   Do not import the unmerged MK-813 checkout or bypass the configuration hash.
2. Inspect Hostinger DNS before editing: apex A should target `164.92.239.220`,
   `www` remains a CNAME to `monkado.fr`. Resolve conflicting apex A/AAAA records
   explicitly; preserve api, mail and verification TXT records.
3. Reinstall reviewed backend deployment code using the existing installation
   procedure. The installer adds the static site, read-only mount and frontend
   units, but does not start them. Confirm that the MK-813 guarded deployment
   drop-in, backup timers and locks remain unchanged.
4. Approve a backend publication with the new configuration fingerprint and run
   its first rollout. Existing backend maintenance rules still apply to this
   one-time Caddy/Compose installation, not subsequent static frontend updates.
5. Verify public certificates for `monkado.fr`, `www.monkado.fr` and the unchanged
   API. Approve a frontend publication, then run the frontend service manually.

```sh
sudo systemctl start monkado-frontend.service
sudo python3 /opt/monkado/src/Operations.Frontend/frontend_cli.py status
sudo systemctl show monkado-frontend.service -p Result -p ExecMainStatus
```

After successful public browser smoke tests and review:

```sh
sudo systemctl enable --now monkado-frontend.timer
sudo systemctl list-timers monkado-frontend.timer --no-pager
```

The timer checks every five minutes and does not compile, migrate, restart the
backend or stop traffic. It shares MK-813's backup-coordination then deployment
lock order. Busy locks, an interrupted backend rollout, insufficient space or a
bad download leave the current site unchanged. Failed checks after a switch restore
the previous link. An interrupted switch is recovered before another release.

Failed activated revisions are not automatically retried. Investigate the bounded
systemd error first; `deploy --retry` is an explicit operator action after repair.
Never print expanded Compose configuration, production.env, cookies or raw tokens.

## Return to a previous build

Stop the frontend timer first to prevent the production pointer reapplying the
newer version. Select a previously recorded exact commit, not a mutable branch:

```sh
sudo systemctl stop monkado-frontend.timer
sudo python3 /opt/monkado/src/Operations.Frontend/frontend_cli.py rollback --revision <40-character-commit>
```

Rollback downloads and verifies the approved archive again and must remain
compatible with the currently installed backend/configuration. It never rolls
back PostgreSQL or backend containers. Reapprove the intended production pointer
before re-enabling the timer. Record the incident and selected versions.

Static release files contain no member data. They are reconstructible from public
immutable releases; preserve the reviewed hosting configuration and release
references when restoring the VPS. Do not alter MK-813 backup secrets, retention
or timers to install the frontend. External notifications remain MK-815 work.

## Browser acceptance before opening to users

Check canonical apex/www and HTTP/HTTPS redirects, direct navigation/refresh on
login, owner and shared-list routes, and fragment-based confirmation/reset links.
Fragments must not enter query strings, storage, access logs or error reports.
Verify CSP without unsafe scripts, no-referrer, denied embedding, no-store on
documents, immutable caching only on fingerprinted existing assets and true 404s
for missing assets and private paths. CORS remains www-only; do not widen it.

Use agreed synthetic accounts for real credentialed CSRF/session tests, logout,
owner list management and guest participation/reservation. Browser traces must
not capture real tokens or personal data. Local fakes do not prove real cookie
behavior. No load test, provider activation or real secret rotation is included.

Google remains disabled pending its separately approved smoke test. MK-828 adds
the private MFA/export/deletion frontend flows and native static legal routes;
the frontend publication guard refuses draft legal documents. Its operator
approval checklist is `deployments/publication-readiness.md` in the frontend repo.
Install and publish this backend revision before attempting manifest-v2 frontend
publication; the workflow consumes tools from `backend-production`, not develop.
Reinstall the reviewed Caddy configuration through normal fingerprint validation,
without altering MK-813 backup units, locks or credentials. Technical deployment
does not by itself complete legal review or authorize general public opening.
