# Personal data exports

The backend prepares a private ZIP asynchronously. This is not a public file URL,
an account backup or a reimport format. The frontend export controls are a separate
delivery; the notification points to the existing `/profile` page.

## Member contract

All routes require a valid Bearer token and revalidate the current confirmed
account in PostgreSQL. They never authorize an administrator to read another
member's export and never modify authentication cookies. No additional password,
antiforgery token or `If-Match` is required.

| Route | Result |
| --- | --- |
| `POST /api/v1/members/current/data-exports` (no body) | `202` for a new or pending request; `200` when reusing a ready archive |
| `GET /api/v1/members/current/data-exports/latest` | Latest retained request, or `404` |
| `GET /api/v1/members/current/data-exports/{id}` | Owned lifecycle metadata, or `404` |
| `GET /api/v1/members/current/data-exports/{id}/archive` | Authenticated ZIP attachment |

The POST response includes `Location`, pointing to the status resource. Metadata
contains exactly `id`, `status`, `createdAt`, `snapshotAt`, `readyAt`, `expiresAt`,
`sizeInBytes` and `errorCode`; unavailable values are explicitly null. Lifecycle
values are `queued`, `processing`, `ready`, `failed` and `expired`.

An unfinished or failed download returns `409 MEMBER_DATA_EXPORT_NOT_READY`.
Unknown, foreign and expired downloads return `404 MEMBER_DATA_EXPORT_NOT_FOUND`.
A deleted account returns `401`, even if its JWT has not expired. Storage or
database outages return a structured `503` before streaming starts; a storage
failure after headers are sent aborts the connection instead of appending JSON to
the ZIP. Downloads already started may finish after the deadline.

Responses are `Cache-Control: no-store`; ZIP responses additionally set
`X-Content-Type-Options: nosniff` and an identifier-only attachment filename.
The configured frontend CORS policy exposes `Location`, `Content-Disposition`
and `Retry-After`. The OpenAPI document describes the binary ZIP contract.

## Administrative contract

The separate `ExportMemberData` policy checks the actor's current administrator
role in PostgreSQL. These routes accept any existing target account, including an
unconfirmed account; they do not relax confirmation or ownership on member routes.

| Route | Result |
| --- | --- |
| `POST /api/v1/admin/members/{memberId}/data-exports` | `202` pending or `200` ready; creates a durable administrative request |
| `GET /api/v1/admin/members/{memberId}/data-exports/latest` | Latest retained administratively requested export |
| `GET /api/v1/admin/members/{memberId}/data-exports/{exportId}` | Metadata for that target and administrative request |
| `GET /api/v1/admin/members/{memberId}/data-exports/{exportId}/archive` | Authenticated ZIP, with durable download-start audit |

The POST requires `{ "requestReference": "SUPPORT-807" }`: a nonblank technical
ticket reference, trimmed, at most 128 characters, without control characters.
Never put a name, email, identity-document number or other personal information in
this reference. It is stored in the audit, not returned in metadata or logged.
`Location` points to the administrative status route. The exact metadata and ZIP
format remain the same as for member exports. Both callers share one active export
and the same target-account quota; reuse does not extend archive availability.

An administrator must first POST even when the member already generated an archive.
This records why administrative access was requested before any administrative GET
or download is allowed. Current administrator access and target existence are
checked again before release. Audit failure blocks release unless an independent
read confirms the exact commit. No new download URL, authentication cookie,
antiforgery requirement, password check or creator-only restriction is introduced.

Audit rows record actor, target, export identifier, request reference, UTC time and
`Requested` or `DownloadStarted`. A download-start event is **not proof of receipt**
or a completed transfer. They are retained for six calendar months independently
of ZIP expiry, then purged in bounded Worker batches. Deleting an account nulls its
audit foreign keys; removing an export does not remove these audit events.

No identity document is collected by this feature. A civil identity document alone
does not establish ownership of a pseudonymous account. Prefer delivery through the
authenticated member flow; any handover outside that flow needs a separate support
verification linking the requester to the account. This tool does not establish
that link or replace that procedure.

## Archive contents and boundaries

The ZIP contains `data.json`, `README.txt` and the current stored WebP images under
`images/profile.webp` and `images/wishes/{wishId}.webp`. JSON uses camelCase,
`schemaVersion: 1`, UTC dates, a coherent `snapshotAt`, and relative `imagePath`
values instead of signed image URLs. No original uploaded images are retained or
reconstructed.

The explicit projections include the account profile, roles and external-account
association metadata; owned lists and gifts (including suspended lists); sharing
metadata without its capability secrets; the member's participations, own active
reservations and retained reservation history; retained session lifecycle dates,
email-change/deletion-request metadata and notification metadata. Previously guest
participations attached to the member keep their retained guest display name.

Passwords, credential hashes, security stamps, bearer/refresh/confirmation tokens,
sharing secrets, local file paths and other people's reservations are never
exported. Image hashes exist only in an internal disk-backed manifest that is
removed and is not a ZIP entry. Each image is checked against that snapshot hash
while copying; a missing, unreadable or mismatching image fails the whole attempt.
A retry takes a fresh snapshot instead of mixing records and newer images.

Only still-retained records can be exported. This automatic feature does not
claim to exhaust every possible data-access request: raw operational logs and
internal moderation material require a complementary access-request review,
including third-party rights and security considerations. Keep the privacy notice
and support process aligned with this scope before production activation.

## Configuration and private storage

API and Worker must mount the same dedicated `personal_data_exports` volume at
`/var/lib/mon-kado/personal-data-exports`, configured with
`PersonalDataExports__StoragePath`. Compose and both container images provide this
mount with the non-root application identity. It is separate from `gift_images`
and must never be exposed by Caddy, static-file middleware or a public volume mount.
The host checks path safety and read/write permissions during startup. Both
containers must use the same UID; Unix directory permissions are owner-only.

Local defaults use `.local/personal-data-exports` relative to the process working
directory. When starting API and Worker from different directories, override both
with the same absolute path. On Windows, restrict the directory ACL to the account
running these processes. Do not use a network filesystem that lacks the required
cross-process file-sharing locks and atomic same-directory rename semantics.

Do not include this temporary export volume in backups. The source PostgreSQL and
image volumes follow their own backup policy. Do not clean these files with an
independent cron job: the Worker coordinates publication, live writers and cleanup.

| `PersonalDataExports` setting | Default |
| --- | --- |
| `ArchiveLifetime` | 24 hours from confirmed publication |
| `MaximumRequests` / `RequestWindow` | 3 new requests per member per rolling 24 hours |
| `MaximumAttempts` | 5 |
| `RetryDelays` | 1 minute, 5 minutes, 15 minutes, 1 hour |
| `MaximumArchiveBytes` | 1 GiB, including ZIP metadata |
| `AttemptTimeout` | 10 minutes |
| `LeaseDuration` / `LeaseRenewalInterval` | 2 minutes / 30 seconds |
| `PollInterval` / `FailureInterval` | 10 seconds / 1 minute |
| `CleanupBatchSize` / `TemporaryGracePeriod` | 100 / 1 hour |

Reuse of a pending or ready request consumes no new quota and never refreshes its
snapshot or extends its lifetime. The size limit fails the request explicitly with
`MEMBER_DATA_EXPORT_TOO_LARGE`; other exhausted failures use
`MEMBER_DATA_EXPORT_GENERATION_FAILED`. There is no partial-success archive.

Generation uses PostgreSQL leases and an immutable filename per attempt. A late
worker cannot publish a superseded attempt. Publication and one email outbox entry
commit atomically when the target email is confirmed at publication. Unconfirmed
targets receive no notification, and later confirmation does not create one
retroactively. An ambiguous commit is verified independently without blind
replay. The email dispatcher checks the current confirmed address and archive
availability again, includes the fixed expiration, and never sends the ZIP itself.
Its existing explicit Gmail retry policy applies; delivery failure does not block
download or extend availability.

Expiration is enforced on reads without waiting for cleanup. Account deletion
detaches the export job through `ON DELETE SET NULL`, preserving only the identity
needed to remove its files. Terminal metadata is purged once physical cleanup and
the rolling request quota no longer need it; this is not a permanent export history.

## Deployment and smoke checks

1. Apply `AddMemberPersonalDataExports` and `AddAdministrativeDataExportEvents`
   before starting the updated API and Worker.
2. Mount the private volume with the shared application UID and verify startup
   permission checks on both containers. Keep Gmail credentials in secrets.
3. Request an export as a confirmed test member; follow `Location` until ready.
4. Download it using Bearer, inspect the ZIP structure and current owned images,
   and verify that another member and an anonymous client cannot download it.
5. Check the notification's account-page link and exact expiration. Simulated
   email tests do not validate real Gmail delivery or the future frontend UI.
6. Test expiry and cleanup in a disposable environment, including volume outage
   and restart. Never manipulate production request timestamps for a smoke test.

Rollback removes this feature's jobs and ready notifications, not source personal
data. Drain export requests and stop generation before rolling back; the Worker
must clean the private volume before removing the feature if its files are to be
removed automatically.

## Cross-platform quality gate

The main quality job runs the complete suite on Linux. A separate Windows job runs
only the export storage unit suite, covering Windows directory creation and file
deletion semantics. Its OpenCover report is downloaded into the same workflow run
and source paths are normalized to the Linux checkout before coverage verification
and Sonar analysis. Visit counts and executable branches are not filtered; the
combined gate still requires 100% of lines and branches.
