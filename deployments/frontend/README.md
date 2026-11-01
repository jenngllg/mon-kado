# Approved frontend hosting — MK-811 / MK-936

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

Any failed attempt durably suspends publication, including failures before activation.
Neither rebooting nor enabling the timer nor `deploy --retry` bypasses this suspension.
Investigate the bounded status first. A restored working site does **not** turn a
failed publication into a success. No-op timer runs revalidate the archive, installed
files and HTTPS responses; an old revision marker is not proof of current health.
Never print expanded Compose configuration, production.env, cookies or raw tokens.

## Return to a previous build

Select a previously successful exact commit, not a mutable branch. Rollback durably
pauses publication **before** changing the site, even if the timer is enabled.
Stopping the timer is optional operational hygiene, not the safety mechanism:

```sh
sudo systemctl stop monkado-frontend.timer
sudo python3 /opt/monkado/src/Operations.Frontend/frontend_cli.py rollback --revision <40-character-commit>
sudo python3 /opt/monkado/src/Operations.Frontend/frontend_cli.py status
```

Rollback verifies the retained cached archive (or its immutable public source) and must remain
compatible with the currently installed backend/configuration. It never rolls
back PostgreSQL or backend containers. A broken active site's HTTP response does
not prevent manual rollback, but the restored site must pass all checks.
Only the active plus two previous successful versions are retained. A failed
candidate does not evict the fallback. Cleanup happens after final state persistence
and journal removal; a cleanup failure still suspends further publication.

Reapprove the intended production pointer through the protected frontend workflow,
then authorize that exact manifest locally. Resume does **not** publish:

```sh
sudo python3 /opt/monkado/src/Operations.Frontend/frontend_cli.py pause
sudo python3 /opt/monkado/src/Operations.Frontend/frontend_cli.py resume --revision <approved-40-character-commit>
sudo systemctl start monkado-frontend.service
sudo python3 /opt/monkado/src/Operations.Frontend/frontend_cli.py status
```

Resume rechecks the active installation and fully validates the currently approved
candidate. If the remote manifest changes afterwards, publication fails closed and
pauses again. Record the incident and versions; only re-enable the timer after review.

### Interruption and failed recovery

`status` exposes only schema version, phases, SHA identifiers, check booleans,
UTC timestamp, a fixed error code and suspension/recovery flags. Its schema is 2.
Legacy healthy metadata is unverified until fresh checks succeed; historical
failures stay paused. `lastVerifiedRevision` is evidence, not current availability.

The transition journal is committed before switching the symlink. After interruption,
the next invocation performs one recovery and pauses without publishing again.
A committed successful switch whose journal remains is reverified, not blindly
reverted. Failed recovery keeps its journal and enters `recoveryRequired`; timers
refuse further attempts. After investigation and repair, explicitly request:

```sh
sudo python3 /opt/monkado/src/Operations.Frontend/frontend_cli.py recover
sudo python3 /opt/monkado/src/Operations.Frontend/frontend_cli.py status
```

Do not delete/edit operational state to force a retry. Without a previous release,
recovery removes the candidate pointer and reports `unavailable` with
`NO_PREVIOUS_RELEASE`. A rollback that cannot be verified remains an incident even
when some requests succeed. Resume requires resolution of pending recovery first.

### Compatibility and installation boundary

The exact backend/configuration binding is deliberately unchanged. An old frontend
is not automatically compatible with a new backend: an incompatible candidate **or
fallback** blocks automatic publication. A coordinated upgrade therefore requires a
separately reviewed release/recovery decision; do not edit immutable manifests,
bypass hashes or roll back the database to work around this guard.

MK-936 adds installed modules `frontend_state` and `frontend_probe`, changing the
configuration fingerprint. It does not change Caddy/Compose routes, headers or
network exposure. Reinstall through the reviewed production installer and publish
the matching backend configuration first. Do not install during a deployment,
capture or monitor check. The installer stops publication timers; enable them only
after review. MK-820 baseline adoption and smoke-account prerequisites still apply.

Upgrade reviewed backup tooling in the same approved maintenance window: capture
schema **4** includes these modules; restore still accepts schemas 1–3 with their
original inventories and hashes. Avoid captures between mismatched tool versions.
Preserve backup credentials, keys, locks, retention and timers. This MR performs
neither a production installation nor timer activation.

### Checks and monitoring

The bounded probe verifies certificates on local Caddy's production virtual hosts
(no public DNS dependency, redirects, login, cookie or member-data requests). It
compares served HTML, the exact Google/revision marker, JS/CSS and legal documents
with the verified artifact, checks entrypoint references, MIME types, CSP/HSTS and
cache policy, then the API's existing `/readiness`. Its total deadline is 120 seconds
with per-request/body/header limits. It does not execute JavaScript or replace
browser authentication acceptance.

When frontend monitoring is explicitly enabled, MK-815 reads this state and exposes
`frontend.deployment.failed`, `.recoveryRequired`, `.rollbackFailed` and unusable
evidence (`.unavailable`). A deliberate healthy pause alone is not an incident;
successful automatic rollback retains the failure. Notification credentials and
settings are unchanged; frontend monitoring is not automatically enabled.

Static release files contain no member data. They are reconstructible from public
immutable releases; preserve the reviewed hosting configuration and release
references when restoring the VPS. Do not alter MK-813 backup secrets, retention
or timers to install the frontend. External delivery uses the existing MK-815 channel.

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
