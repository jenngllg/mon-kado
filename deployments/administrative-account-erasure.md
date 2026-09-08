# Administrative account erasure

## Verified requests, not identity collection

`POST /api/v1/admin/members/{memberId}/erasure-requests` executes a support request
that an administrator has already verified. It does not establish ownership of
an account, collect identity documents, or implement a legal eligibility decision.
An external request reference and an explicitly confirmed target are mandatory:

```json
{
  "requestReference": "SUPPORT-1234",
  "confirmedMemberId": "01900000-0000-7000-8000-000000000001"
}
```

The confirmation must match the route. The reference is trimmed, nonblank, at
most 128 characters, and must not contain control characters. Never include a
name, email address, identity-document number, or account content in it.

Prefer the existing member self-service deletion flow when the person can use
their account. Otherwise, support must establish the relationship between the
requester and the account using proportionate evidence. A public display name
or a civil identity document alone does not establish control of a pseudonymous
account. If the link cannot be established reliably, do not execute this endpoint.
Any legal exception or partial-erasure request requires manual assessment before
using this whole-account operation.

## API contract and effects

Both authentication policies explicitly require JWT Bearer. Administrator roles
are read from PostgreSQL, not the JWT. A refresh cookie alone cannot authorize
erasure; no antiforgery token or If-Match is required. The caller's cookies are
preserved even on failures. An administrator cannot erase their own account by
this route (`409 ACCOUNT_SELF_ERASURE_NOT_ALLOWED`); the member deletion flow
remains unchanged. Other existing accounts, including administrators and accounts
without a confirmed email address, can be targeted.

Requests are limited to five attempts per minute per authenticated administrator
and a 4 KiB JSON body. Exceeding these limits returns structured `429` or `413`.

`204 No Content` with `Cache-Control: no-store` confirms the account erasure
transaction, including its audit and durable cleanup/notification requests. It
does **not** promise that files have already been physically deleted or that an
email was received. A nonexistent target, including a repeated request after
success, returns `404 ACCOUNT_ERASURE_TARGET_NOT_FOUND`.

The shared deletion engine removes the account, its sessions and dependent data,
owned wishlists, participations and reservations. Other members' retained
reservation history keeps its existing placeholder treatment for deleted wishlists
and gifts. Gift and profile images are queued in the existing deletion outbox.
Personal-data exports become inaccessible immediately and are detached for the
existing file cleanup worker. No filesystem or Gmail operation blocks erasure.

Audit insertion, notification staging and account removal commit together.
Ambiguous commit recovery verifies the exact audit event and account absence in
an independent context; it does not blindly replay an irreversible request.
An unconfirmed outcome returns `503` and must not be interpreted as proof that
the account still exists.

## Audit and notification retention

The dedicated audit retains only the operation identifier, administrator,
pseudonymous target identifier, technical request reference, UTC erasure date,
and bounded notification status. It survives target deletion; the actor reference
is cleared if that actor is later erased. Audit rows become eligible for bounded
purge after **six calendar months**. This is the project's approved retention
choice, not a duration automatically prescribed by GDPR. Access to the support
request and audit must remain restricted to authorized operators.

A separate outbox contains a Data Protection-protected recipient only when the
target had a confirmed address at erasure. Protection purposes bind the payload
to its operation. The API and Worker must use the existing shared key ring and
application name. No replacement address is accepted from the administrator.

The generic French email confirms account removal without including profile or
wishlist content, request references or secret links. It distinguishes account
erasure from asynchronous file cleanup and backup retention. No confirmation
click is required, and delivery failure never restores the erased account.

The recipient is removed as soon as provider acceptance or terminal abandonment
is durably recorded. Attempts stop at an absolute **24-hour** deadline measured
from erasure; expired rows are purged at the next available worker cycle even
when Gmail is disabled. A stopped worker or unavailable database may delay the
physical purge, but never extends the permitted sending window.

Delivery uses leased, fenced attempts with at most ten claims. Default backoff
is 1 minute, 5 minutes, 15 minutes, 1 hour, then 6 hours, always bounded by the
recipient deadline. No transparent retry is added to Gmail's POST. A stable
Message-ID does not guarantee provider deduplication: an ambiguous provider
acknowledgement or database acknowledgement can cause a duplicate notification.

Nontransient provider rejections and unreadable protected recipients are terminal;
their addresses are discarded without scheduling another attempt.

Audit notification statuses are `NotApplicable`, `Pending`, `Accepted`, and
`Failed`. `Accepted` means provider acceptance, not inbox receipt. Failures are
logged using technical identifiers/classifications only. Support must follow up
through the original request channel when no confirmed address was available,
the mailbox is inaccessible, or delivery failed.

Purging this application's recipient does not delete the delivered email or
copies held by Gmail. Those copies, operational logs, and backups follow their
own documented retention policies. This feature does not implement a new backup
erasure mechanism and must not be advertised as one.

## Configuration and deployment

Apply `AddAdministrativeAccountErasure` before starting updated API and Worker
instances. It creates the audit and notification tables without changing account
or authentication-email cascade behavior. Rolling back the migration discards
these new audit records and pending notifications; do not roll it back after
production use without an explicit operational decision.

`AccountErasureProcessing` supports `BatchSize` (default 20), `MaximumAttempts`
(default 10, maximum 10), `LeaseDuration` (2 minutes), `PollInterval` (10 seconds),
`FailureInterval` (1 minute), `RetryDelays`, and `MaximumRetryDelay` (24 hours).
Options are validated at startup. Audit retention and recipient lifetime are
fixed policy boundaries, not tunable extensions. Existing `AuthenticationEmail`
and Gmail secret configuration control provider enablement and transport.

The erasure worker always runs maintenance, including in local environments with
Gmail disabled. Image and export workers must continue running to complete
physical cleanup. Use only disposable accounts for smoke tests: this operation
has no restoration flow. Test provider delivery separately and explicitly; the
automated quality gate uses simulated providers and cannot prove real delivery.

No frontend screen is included. A future administrative screen must identify the
target unambiguously, warn about irreversible whole-account deletion, collect the
external request reference and require explicit confirmation before sending the
target identifier. Do not use display names as account identifiers.
