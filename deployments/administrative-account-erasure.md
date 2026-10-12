# Administrative account erasure

`POST /api/v1/admin/members/{memberId}/erasure-requests` requires an administrator session with verified current two-factor authentication, a support request reference and matching confirmed member ID. Self-erasure through administration is rejected with 409; missing targets return 404.

The transaction records the audit event, removes the target's account and owned data, queues referenced image deletion, and retains only the deliberately anonymized historical data. A confirmed account's recipient is encrypted in a separate expiring outbox, allowing confirmation after the account is physically gone.

The worker owns attempts, backoff, leases and delivery status. Success marks Accepted; terminal failure or expiry marks Failed and removes the protected recipient. Expiry is processed even when email delivery is disabled. The notification lifetime is bounded and no recipient is written to logs.

Audit events remain available through `/api/v1/admin/audit-events`. Backups have an independent retention policy; erasure of live application data does not delete an external backup automatically.
