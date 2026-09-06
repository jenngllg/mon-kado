# Wishlist moderation operations

Moderation is an administrator-only restriction, not owner-controlled archiving.
Suspension preserves the wishlist, gifts, participants and reservations. Owners retain private read access but cannot
change or delete the suspended wishlist or its contents. Public links and public signed image grants return 404.
Reactivation reuses the existing link; expired guest credentials and authentication sessions are not renewed.
Account deletion and technical retention/cleanup continue to operate.

## Coordinated deployment

1. Deploy the migration containing the suspension state, decision history, notification outbox and unassigned Admin role.
2. Deploy the updated Worker and configure its existing Gmail credentials through secrets.
3. Replace every API instance and drain old requests before granting administrator privileges or permitting moderation.
   An old API does not enforce suspension and must not remain accessible.
4. Smoke-test administrator and owner access, public denial, reactivation, notification processing and retention.

There is no automatic administrator grant. Do not add roles to access JWTs: authorization reads current PostgreSQL
assignments. Restrict the database operator connection and never grant privileges based only on an unverified display name.

## Operator role assignment

Identify the intended existing, confirmed account by its stable member UUID through an authenticated operator process.
Replace MEMBER_UUID below manually; do not use an email or display-name search to grant the role to multiple accounts.
The account row lock must be retained until the role change commits, matching moderation and account-deletion locking.

```sql
BEGIN;
SELECT id, email_confirmed
FROM public.users
WHERE id = 'MEMBER_UUID'::uuid
FOR UPDATE;
-- Verify exactly one intended, confirmed account before continuing.
INSERT INTO public.user_roles (user_id, role_id)
SELECT member.id, role.id
FROM public.users AS member
CROSS JOIN public.roles AS role
WHERE member.id = 'MEMBER_UUID'::uuid
  AND member.email_confirmed
  AND role.normalized_name = 'ADMIN'
ON CONFLICT DO NOTHING;
COMMIT;
```

To revoke, acquire the same account row lock in a transaction, then delete only that account's Admin assignment:

```sql
DELETE FROM public.user_roles AS assignment
USING public.roles AS role
WHERE assignment.role_id = role.id
  AND role.normalized_name = 'ADMIN'
  AND assignment.user_id = 'MEMBER_UUID'::uuid;
```

Revocation applies to the next administrator request even when its access JWT has not expired.
Administration does not grant permission to edit another member's wishlist contents.

## Notifications and audit retention

Each actual decision and its single notification intent commit atomically. Identical requests with the current ETag do
not create an event or a notification. Amending a reason creates a new event while retaining the original suspension date.
The original owner-visible reason and all email content remain private.

The dedicated Worker reuses the existing AuthenticationEmail:Provider enablement and Gmail settings. Production already
requires Gmail delivery. Local Disabled mode leaves moderation notifications pending; it does not discard them.
Configure the independent WishlistModerationEmail settings in the Worker's appsettings or environment overrides.
Defaults: batches of 20, two-minute leases, ten provider attempts, delays of 1/5/15/60/360 minutes, Retry-After capped at
24 hours, and 30-day retention of processed outbox rows. The whole provider operation is cancelled at lease expiration.
Keep leases comfortably longer than normal Gmail authentication and send latency.

Processing is ordered by the durable decision sequence within each wishlist. An earlier retry blocks later decisions for
that list; other lists can progress. A lease token fences late acknowledgements from old Worker instances. Expired leases
are recoverable after restart. After the last failed attempt, the delivery is terminal; inspect allowlisted technical
failure classifications and correct provider configuration. Do not add transparent retries around Gmail POST requests.

Recipients are resolved from the current confirmed owner at delivery time. Deleted or unconfirmed accounts and deleted
wishlists are not sent new notifications. Delivery is at least once: a stable event-based Message-ID aids tracing but is
not a Gmail idempotency key; a lost provider acknowledgement can result in a duplicate email.

Processed email-row cleanup never purges moderation history. History survives reactivation and lasts for the wishlist's
lifetime. Deleting an administrator sets its historical actor ID to null; deleting a wishlist cascades its events and
remaining notification rows. There is no new legal hold on account or wishlist data.
