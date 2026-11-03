# Notification preference workflows — 3 November 2026

The nine kinds were tested independently, with the other eight enabled. Each
case saves a disabled choice, reloads it through the public API, performs the
business action and verifies that its notification is absent. It then re-enables
the choice, performs a fresh action, verifies delivery and persisted read state,
and disables it again without removing the received history.

| Kind | Business trigger | Disabled / re-enabled / history |
| --- | --- | --- |
| `wishesAdded` | Add a wish to a followed list | Passed |
| `groupJoined` | Accept a group invitation | Passed |
| `groupInvitation` | Send a new invitation to the member | Passed |
| `occasionReminder` | Run the reminder job at J−7 | Passed |
| `sharingReminder` | Run the reminder job at the 48-hour threshold | Passed |
| `groupWishlistShared` | Share a new list with the group | Passed |
| `reservationUnavailable` | Reserve a wish, then remove it as its owner | Passed |
| `wishReserved` | Reserve the member's wish outside surprise mode | Passed |
| `suggestionAdded` | Propose a private group surprise idea | Passed |

The shared-list scenario also proves that muting `groupWishlistShared` leaves
`wishesAdded` enabled. Suggestion recipients still exclude the author and the
list owner. Invitations, group membership, list access and reservations retain
their normal HTTP results even while their notification is muted.

## Environments and reproducibility

`internal/httpapi/notification_preferences_workflows_integration_test.go` runs
against native PostgreSQL through real HTTP handlers and public contracts. The
same matrix passed in the isolated main-based feature checkout and in the
canonical local backend, each using a distinct database ending in `_test`.
Only reminder generation uses an injected UTC clock; no running service's date
or production data was changed. External email providers remain test fakes.

```sh
MONKADO_TEST_DATABASE_URL=... go test -count=1 -tags=integration ./internal/httpapi -run '^TestNotificationPreferenceWorkflows'
```

Separate browser checks used the actual local site and API, with the existing
private test account, at 390px and 1440px. All nine switches were disabled and
re-enabled individually, with persisted state and unchanged neighboring choices
checked after fresh reads. Full reloads confirmed every disabled value; ordinary
account navigation confirmed re-enabled values. The all-disabled and all-enabled
states also persisted. The account was restored after the checks, without browser
console errors or horizontal overflow. Evidence remains in ignored
`.local/notification-preferences-20261103/all-choices-results.json` and the
workflow logs.

The dense reload harness initially reached the existing 30/minute session refresh
limit. The corrected harness uses ordinary account navigation between enabled
checks; the limit and session protections were not changed.

The frontend's committed `e2e/notificationPreferences.spec.js` now gives every
kind its own mobile and desktop save/reload/keyboard scenario, alongside failure,
concurrency and multi-tab coverage. Those HTTP boundaries are controlled fixtures
and complement the real local browser/API checks described above.

No production publication is part of this verification.
