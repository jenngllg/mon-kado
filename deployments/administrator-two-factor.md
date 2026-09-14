# Administrator authenticator security

This backend change must not be deployed before the frontend supports the complete second-factor protocol below. Simulated provider tests do not replace a real HTTPS browser smoke test. No Google Cloud setting or production account is changed by this document.

## Required frontend contract

- Password login, Google completion and explicit Google linking may return **202**, not an access token: `{ "flow": "…", "requiredAction": "enroll|verify|replace|complete", "expiresAt": "…Z" }`.
- Keep the opaque flow and any returned credentials only in memory. Never put them in a URL, analytics, browser storage or logs. The original five-minute deadline never extends.
- Google has already validated its provider proof before handing over to MFA. A pending Google link is not committed until MFA sign-in completes. A 202 clears the consumed temporary Google cookie but must preserve an existing MonKado refresh cookie.
- `POST /api/v1/auth/two-factor/setup` with `{ "flow": "…" }` returns a manual key and an `otpauth` URI. Render the QR code locally, not using a third-party QR service. Do not cache or retain either value.
- `POST /api/v1/auth/two-factor/setup/confirmations` with `{ "flow": "…", "code": "123456" }` confirms the candidate and returns ten recovery codes **once**. Ask the member to save them offline before continuing. Repeating the operation does not redisplay the codes.
- Finish initial enrollment or sign-in recovery with `POST /api/v1/auth/two-factor/completions` and `{ "flow": "…" }`. Do not resubmit the OTP already consumed by confirmation.
- For ordinary enrolled sign-in, submit `{ "flow": "…", "code": "123456" }` to completions. Only its 200 response creates a full session and refresh cookie.
- A recovery code submitted instead of `code` returns 202 `replace`. It does **not** authorize a session or recovery-code regeneration. Complete replacement first. Abandoning the flow keeps the old authenticator and releases the exclusive recovery-code reservation when the original flow expires.
- Setup and sign-in continuation POSTs require the current antiforgery cookie/header pair. Management setup additionally requires the same authenticated Bearer session that obtained the management grant. Reauthentication and recovery-code regeneration are Bearer-only and do not require antiforgery.
- `GET /api/v1/members/current/two-factor` returns only enrollment status and a count of remaining codes.
- `POST /api/v1/members/current/two-factor/reauthentications` accepts the current OTP and `purpose: "replaceAuthenticator"` or `"regenerateRecoveryCodes"`. A recovery code is allowed only for replacement. The resulting grant is bound to that operation and its original Bearer session; it is not a substitute for authentication.
- Regenerate codes with `POST /api/v1/auth/two-factor/recovery-codes/regenerations` and `{ "flow": "…" }`. After replacement or regeneration, all old sessions are revoked. Display the new recovery codes, then require a new full sign-in.

Handle structured 400 validation/antiforgery errors, 401 invalid or consumed proof, 403 missing management access, 409 wrong flow state/operation, 429 quota/lockout and 503 unavailable storage/key ring. Never fall back to single-factor sign-in after an error. Continue to handle common 413, 415 and 500 responses. No response containing authentication material may be cached.

## Enforcement and operational bounds

Administrator membership is read from PostgreSQL, never from JWT roles. Promoting a member immediately makes any single-factor sessions unusable. Removing the administrator role does not disable an already enrolled authenticator. There is no MFA-disable endpoint.

JWT claims and access/refresh lifetimes are unchanged. JWT and refresh validation requires current authenticator-version proof for administrators and enrolled members. Refresh does not ask for another OTP. Revocation starts at the next request validation; an already authorized response is not interrupted.

TOTP uses six digits, SHA-1, a 30-second period and one adjacent interval on either side. The highest accepted interval is stored under the account lock: the same interval cannot authenticate another flow or another API instance. Keep host clocks synchronized. Recovery codes contain 128 random bits; only SHA-256 hashes are stored.

Password, Google completion/link and management reauthentication share a ten-attempt per-minute IP quota within each API instance, in addition to their existing endpoint quotas. PostgreSQL enforces ten verification attempts per minute per account and a fifteen-minute lockout after five incorrect codes across flows and instances. A successful password check does not reset MFA failures. A multi-instance deployment must also apply an aggregate perimeter IP quota if an IP-wide limit across all instances is required; do not describe the in-process limiter as distributed.

Security notifications for enrollment, replacement, regenerated codes and recovery-code use are committed with the operation in the existing authentication email outbox. They contain an event and timestamp, no key, code, binding or sign-in link. Existing delivery retry/retention rules apply; delivery failure does not undo an already committed credential change.

The existing session-cleanup worker removes expired challenge rows in bounded batches after a one-hour grace period. Consumption and invalidation erase staged authenticator and Google material immediately. Account deletion cascades the MFA state. Authenticator credentials, recovery hashes and pending proofs must never be added to personal-data exports or administrative audit responses.

## Deployment and key management

1. Validate all frontend paths above, including Google, cancellation, failed codes, recovery, replacement and fresh login after regeneration. Keep real provider smoke tests separate from fake-provider CI tests.
2. Back up PostgreSQL and the persistent ASP.NET Core Data Protection key ring. Test their coordinated restoration with a non-production enrolled account. API and Worker instances must share the intended key-ring/application discriminator configuration.
3. Apply the MK-801 schema and notification-kind migrations. Coordinate API and Worker replacement; do not leave old APIs that bypass MFA or old workers that cannot interpret the new outbox kinds.
4. Expect existing administrator sessions without MFA proof to receive 401. Those administrators must complete enrollment; old refresh tokens cannot upgrade themselves to an MFA session.
5. Test a new administrator enrollment, ordinary TOTP sign-in, Google deferred linking, recovery replacement, notification delivery and immediate rejection of revoked JWTs over real HTTPS.
6. Confirm worker cleanup, aggregate proxy quotas where applicable and monitoring without logging any credential material.

Keep old Data Protection keys needed by persisted authenticator secrets. Deleting the key ring is not a rotation strategy. If a key is missing or a protected credential is unreadable, the API must fail closed with 503; restore the correct key material before considering account recovery. Never change the protection purpose or application discriminator without an explicit credential migration strategy. Rolling back to a pre-MFA API would bypass the new requirement and is not a safe availability workaround.

The notification migration cannot be downgraded while MFA notification rows remain: its former check constraint does not accept those kinds. Do not delete queued or retained notifications just to force a downgrade. Prefer a forward fix; any exceptional rollback requires a separately reviewed data-preservation and security plan before changing the schema or application versions.

## Lost authenticator and all recovery codes

There is no email-reset shortcut, other-administrator UI or automatic support bypass. No operational reset tool is delivered in this US.

A controlled technical intervention requires a recorded support request, an approved identity/account-control verification process and a privileged operator. A display name, a copied email or an identity card alone does not prove control of a MonKado account. If ownership cannot be established, do not reset the factor.

Before any approved database intervention, restrict access to the recovery operation, investigate suspected first-factor compromise and back up the affected security state using the controlled backup process. Have a reviewed, account-specific transaction that locks the user before related security rows, revokes all sessions, invalidates pending challenges, discards old recovery credentials and prepares mandatory enrollment without granting a session. Do not copy keys/codes into the support ticket or console output. Keep the administrator role subject to the same mandatory enrollment rule; do not disable the enforcement policy globally.

Complete enrollment with the verified account holder before restoring normal access. Confirm old JWTs, refresh tokens, flows, authenticator and recovery codes no longer work; verify a fresh full sign-in and the notification. Record the operator, approved request reference, target technical identifier, timing and verification result in the restricted operational record, not a fabricated application audit event. If the intervention cannot be completed safely, keep access restricted and escalate rather than bypass MFA.
