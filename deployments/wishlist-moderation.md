# Wishlist moderation

Public share visitors report a list with the dedicated share header and CSRF proof. Reports disclose no owner identity. Administrators inspect reported lists, review reports and suspend/reactivate lists through `/api/v1/admin` routes.

Review and moderation mutations require current ETag/If-Match values. Every transition produces an immutable event; state changes and notification outboxes commit atomically. Repeating an unchanged moderation state does not create duplicate events.

Suspended lists disappear from public sharing and reject owner mutations. Owners retain access to their moderation status. Reactivation restores access according to the current share link. Private reasons appear only to the authorized owner/administrator and in the relevant notification, never logs.

The worker delivers notifications with bounded leases and explicit retries. The administrative audit endpoint unifies moderation, account erasure, exports and session revocations.
