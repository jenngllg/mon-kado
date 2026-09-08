# Immediate authentication-session revocation

## Deployment (MK-809)

Apply `AddImmediateSessionRevocation` before deploying the API. Replace **all** API instances in a coordinated rollout. Do not leave a previous instance serving authenticated requests: it does not enforce the access-token registry. Do not enable a temporary bypass or attempt to reconstruct registrations for old JWTs.

Existing refresh sessions are preserved. JWTs issued before the rollout return `401` without deleting a separate refresh cookie; a still-valid refresh cookie can obtain a new, registered JWT through the existing refresh endpoint. The frontend must use its normal refresh flow and must not attach the rejected Bearer to its refresh request.

The JWT response and claims remain unchanged. PostgreSQL stores only the token's unique `jti`, session identifier, issuance timestamp and exact expiration timestamp, never the signed JWT. Every password login, Google completion, explicit Google link and refresh registers its JWT before returning credentials. Ambiguous commits are verified against the exact attempted registration.

## Security semantics

`POST /api/v1/admin/members/{memberId}/session-revocations` requires a currently authorized administrator, explicit target confirmation and a support reference. The reference is trimmed, limited to 128 characters and must not contain control characters. It is **not** an idempotency key. Repeated requests create separate audit entries and can revoke sessions established since the previous request.

The endpoint returns `204` after a confirmed commit, including when the target has no active sessions. It does not change the administrator's cookies, the target's credentials, account status or roles, and it sends no email. Self-revocation is forbidden. The dedicated limit is ten requests per minute per administrator.

Revocation invalidates all JWTs associated with the revoked sessions on their next authentication validation, including on optionally authenticated endpoints. PostgreSQL unavailability fails closed with `503`; invalid or revoked credentials return `401` rather than becoming a guest identity. No authorization cache delays revocation.

Normal refresh rotation does not invalidate previous JWTs of the same session. Logout revokes only its browser session. Existing global security revocations, including password changes, password resets and refresh-token reuse detection, immediately invalidate the affected JWTs as well. New logins ordered after an administrative revocation remain allowed.

Requests and downloads already authorized before the revocation commit are not interrupted. Guest sessions, sharing links and image URLs independent of Bearer authentication are outside this operation.

If a committed login is revoked while its lost commit acknowledgement is being verified, the backend rejects that login rather than automatically creating replacement credentials. The user can initiate another login explicitly.

## Audit and retention

The existing administrative journal exposes `memberSessionsRevoked` with the operation identifier, current administrator identity, target identifier, timestamp and support reference. No credentials are stored in this event. Deleting an actor or target nulls its foreign key without deleting the event. The actor's display name is resolved at consultation time.

Revocation events are eligible for deletion after **six calendar months**. Access-token metadata is eligible only after the JWT expiration plus the shared **30-second clock skew**, independently of the refresh session's lifetime. Deleting a refresh session also cascades to its access-token metadata.

Both cleanups reuse the authentication-session cleanup worker and bounded database batches. The configured worker schedule determines actual deletion latency; the backend default interval is 24 hours, and outages can postpone deletion until a later successful run. These are backend defaults, not a claim about deployed production settings. Concurrent cleaners are safe to repeat.

Audit references are private administrative data. Application logs contain technical actor, target and operation identifiers only, not the reference, signed JWTs or refresh tokens. Backup retention remains part of the deployment's separate backup policy; deleting live rows does not erase existing backups.
