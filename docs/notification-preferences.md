# Member notification preferences

The account's **Notifications** page controls future in-app bell notifications.
All nine kinds are enabled by default, including for existing members without a
settings row. Security and account emails are independent of these settings.

| API field / stored kind | Notification |
| --- | --- |
| `wishesAdded` | New wishes in followed lists |
| `groupJoined` | New group members |
| `groupInvitation` | Group invitations |
| `occasionReminder` | Occasions seven days and one day away |
| `sharingReminder` | Reminder to share an eligible personal list |
| `groupWishlistShared` | Lists shared in a group |
| `reservationUnavailable` | Reserved wishes that become unavailable |
| `wishReserved` | Reservations of the member's own wishes, outside surprise mode |
| `suggestionAdded` | Private group surprise ideas |

`GET /api/v1/members/current/notification-preferences` returns all nine booleans
and a strong ETag. It never creates a settings row. `PUT` requires authentication,
CSRF, `If-Match` and all nine explicit booleans; missing or null values are invalid.
Both first and later writes are conditional, so concurrent editors receive 412
instead of silently losing preferences. Administrator impersonation edits the
simulated member's business settings while preserving the real audit author.

Preferences are checked in the existing notification insertion/grouping SQL.
Disabled events do not create notifications or increment grouped quantities.
The existing history, read state, access checks and surprise privacy remain in
effect. The worker excludes disabled reminders before its batch limit; one muted
member cannot hold up the rest of the batch. Re-enabling does not replay past
activity events; a reminder may still be generated if its ordinary eligibility
conditions remain true. Exports include `accountActivity.notificationPreferences`
as an array containing the member's `disabledKinds`. Account deletion cascades
to the settings row.

## Migration and publication

Append migration `028_notification_preferences.sql` without rewriting older
migrations. It creates one empty table with an account foreign key, bounded
statement/lock timeouts and a check for supported kinds. There is no backfill,
account update or application startup migration.

Local verification uses a separate native PostgreSQL database ending in `_test`.
Run the normal native quality gate (`scripts/Test-Native.ps1 -Coverage`), then
check the real local frontend/API together: keyboard toggling, explicit save,
reload persistence, and the nine visible controls on mobile and desktop.

Production publication still requires the reviewed compatible schema-expansion
procedure and a new coherent, verified, encrypted off-VPS backup. The existing
online coordinator's new-migration rejection must not be bypassed. This feature
alone does not qualify or execute that production transition. Test the previously
published API against the expanded schema before rollout, and upgrade both API
and worker so all producers enforce preferences.

Rollback retains the additive settings table and all member choices. Previous
binaries remain structurally compatible with it, but do not enforce these new
opt-outs; that behavior change must be considered before reverting. Do not drop
the table or reset member data as part of application rollback.

## Existing local prototype

The canonical local checkout has a separate historical migration sequence. Its
matching additive migration is `030_notification_preferences.sql`; migration 028
above identifies the isolated main-based feature branch. Existing local migration
files are preserved. The local API and worker are rebuilt from the canonical
backend, and the frontend is served from `../mon-kado-front`.
