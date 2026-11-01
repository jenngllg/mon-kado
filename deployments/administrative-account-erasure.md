# Administrative account erasure

The administrator-only **Comptes** page at `/admin/members` lists accounts by display name and tag, including accounts hidden from public search and unconfirmed accounts. Search and pagination use `GET /api/v1/admin/members`; a fresh confirmation/outcome check uses `GET /api/v1/admin/members/{memberId}`. Both reads require the live `users.is_admin` boolean and the session's current verified second factor, and return only `id`, `displayName` and `displayNameTag`. Textual role names never grant access.

Select an account, enter the support request reference and copy its full displayed handle before confirming permanent deletion. The current administrator cannot select their own account. An uncertain write disables resubmission until an explicit read verifies the account's state; no mutation is retried automatically. The success notice confirms the command, not delivery to the recipient's inbox. Google and password sign-in share the same authenticated application session; no console or copied token is needed. This UI and its two read routes require a coordinated frontend/API release; no database migration is required.

`POST /api/v1/admin/members/{memberId}/erasure-requests` requires an administrator session with verified current two-factor authentication, a support request reference and matching confirmed member ID. Self-erasure through administration is rejected with 409; missing targets return 404.

The transaction records the audit event, removes the target's account and owned data, queues referenced image deletion, and retains only the deliberately anonymized historical data. A confirmed account's recipient is encrypted in a separate expiring outbox, allowing confirmation after the account is physically gone.

The worker owns attempts, backoff, leases and delivery status. Success marks Accepted; terminal failure or expiry marks Failed and removes the protected recipient. Expiry is processed even when email delivery is disabled. The notification lifetime is bounded and no recipient is written to logs.

Audit events remain available through `/api/v1/admin/audit-events`. Backups have an independent retention policy; erasure of live application data does not delete an external backup automatically.
