# Administrative session revocation

`POST /api/v1/admin/members/{memberId}/session-revocations` requires verified administrator two-factor authentication, a support reference and matching confirmed member ID. It rejects self-targeting and atomically revokes all target sessions, invalidates unfinished factor challenges and records an immutable audit event.

Every authenticated request checks the live database session; previously issued access and refresh tokens stop working after revocation. The member can subsequently sign in with valid credentials. No email or token is copied into the audit.

Use `/api/v1/admin/audit-events` to filter by action, member, administrator, reference and half-open UTC time boundaries. When a member is later erased, references are anonymized according to the persistence contract.
