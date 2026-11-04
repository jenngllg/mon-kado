# Mobile push notifications

The account table has one row per notification kind and exactly two channel columns: **Dans MonKado** and **Notifications push**. Push choices are shared across the account; other devices never add columns. Every type defaults to enabled. Choices are stored independently of browser permission, including opting out before any subscription exists. There is no device-name field or device-management flow. Technical subscription labels are generated internally. The server retains the private device infrastructure and its ten-subscription limit; administrative impersonation cannot manage subscriptions.

## Installation and consent

Use HTTPS (loopback works for local testing). Add MonKado to the Home Screen; on iPhone/iPad this is required for push and needs iOS/iPadOS 16.4 or later. Launch the installed app, open **Compte → Notifications**, enable the desired **Notifications push** switches and press **Enregistrer**. The first push save requests browser permission directly from that explicit action. Registration atomically enables only the selected types; later saves preserve independent in-app choices. Supporting Android and desktop browsers can also activate directly. Denied permission must be changed in browser/OS settings; the app never repeatedly prompts.

The manifest and PNG icons reuse the existing gift logo. The root service worker handles push and notification clicks only; it never caches authenticated pages or intercepts fetches. Empty RFC 8030 pushes show a generic French message without names, wish text or share links. Clicking opens the notification disclosure. With all in-app types muted, business details remain accessible through the relevant application pages. Delivery depends on OS/network/browser policies; automated tests do not prove physical-device delivery.

## Configuration

API and worker require the same stable P-256 VAPID pair:

- `PUSH_ENABLED=false` by default; enable only after configuring both keys and the subject.
- `PUSH_VAPID_PUBLIC_KEY`: base64url, uncompressed 65-byte P-256 public key.
- `PUSH_VAPID_PRIVATE_KEY`: base64url, 32-byte P-256 private key.
- `PUSH_VAPID_SUBJECT`: contact `mailto:` or HTTPS URL.

Keep these in private environment configuration, including ignored `.local/environment.json`; never commit or print the private key. Existing subscriptions depend on that pair: rotation requires fresh activation. No paid plan, new backend dependency or additional hosting is required. Browser vendors still process delivery metadata.

For a new local configuration, the following PowerShell 7 commands generate a pair privately, preserving existing settings and refusing to replace keys:

```powershell
$path = Join-Path (Get-Location) '.local/environment.json'
$settings = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
if ($settings.PUSH_VAPID_PRIVATE_KEY -or $settings.PUSH_VAPID_PUBLIC_KEY) { throw 'VAPID configuration already exists.' }
$key = [System.Security.Cryptography.ECDiffieHellman]::Create([System.Security.Cryptography.ECCurve]::NamedCurves.nistP256)
$parameters = $key.ExportParameters($true)
function To-Base64Url([byte[]] $bytes) { [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_') }
$settings.PUSH_VAPID_PRIVATE_KEY = To-Base64Url $parameters.D
$settings.PUSH_VAPID_PUBLIC_KEY = To-Base64Url ([byte[]](@(4) + $parameters.Q.X + $parameters.Q.Y))
$settings.PUSH_VAPID_SUBJECT = 'https://monkado.fr'
$settings.PUSH_ENABLED = 'true'
$settings | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding utf8
$key.Dispose()
```

Existing provider timeout, worker interval, attempt/retry, batch and lease configuration bounds apply. The provider sends one POST with VAPID, a five-minute TTL and a stable collapse topic. It accepts only explicitly allowed Apple, Mozilla and FCM HTTPS hosts, pins a checked public DNS result and disables proxies and redirects. A 404/410 removes an expired device. Only explicit 429/5xx responses schedule bounded exponential retries; transport failures and expired in-flight leases are terminal because their outcome is uncertain. Raw provider errors and capability URLs never reach logs.

## Persistence and privacy

Apply additive migrations `031_mobile_push.sql` and `032_push_channel_preferences.sql` explicitly before API/worker updates. New event rows snapshot the in-app choice; the single Push column uses account-level `member_push_preferences`; legacy device flags are preserved only as a fallback for older API clients. Existing visibility remains unchanged. Delivery outbox creation participates in the business transaction. Grouped additions alert a device once; activation never replays history. Before each attempt the worker checks current authorization, surprise mode, preferences, read state and live session. Account deletion cascades to devices/outbox; revoked/expired sessions are cleaned even when push is disabled. Accepted generic OS notifications cannot be recalled; TTL limits late delivery.

Capability URLs are encrypted at rest and absent from responses and personal-data exports. Device lists contain a SHA-256 fingerprint only. Rebinding a shared browser never transfers another account's queue. Device updates use strong xmin versions and mandatory `If-Match` (428 missing, 400 malformed, 412 stale). The UI reconciles uncertain writes with a bounded private read, without replaying them; confirmed partial saves are retained.

## Verification, publication and rollback

Run `scripts/Test-Native.ps1 -Coverage`, frontend lint/type checks, unit coverage and native browser journeys. The native browser runner gives the API a public disposable test key but disables push in its worker: CI never contacts push providers. Provider HTTP contracts use simulated responses. Mobile/desktop journeys use real API/PostgreSQL to check the two-channel table, defaults, saved opt-outs, keyboard access and overflow.

Closed-app regression coverage is included in the normal quality commands:

- Backend `internal/httpapi/push_background_integration_test.go` creates a real business event through HTTP, stops the HTTP server, and starts a fresh production worker instance using only PostgreSQL/configuration. An event-driven signal waits for completion. A fake push provider checks exactly-once delivery, context propagation, saved mute settings and no replay after a worker restart. There is no SSE or live browser connection.
- Frontend `e2e-native/pushClosed.spec.js` uses a full headless browser with its native notification service (installed Chrome on Windows; bundled Chromium on Linux). It loads the production `/push-sw.js`, closes every MonKado tab, stops the worker, and injects a vendor message through CDP. Chromium must wake the stopped worker and record a native notification while zero application clients exist. An empty message and an untrusted payload both produce the fixed private message; repeated delivery replaces the same notification tag. The test never replaces `showNotification` with a mock and uses browser events rather than polling or sleeps.
- The native browser runner includes this file in both mobile and desktop projects, invoked by the existing `native-browser` CI job. `playwright install --with-deps chromium` already installs the full Linux browser required for notification tests; Headless Shell is intentionally not used for this scenario because it does not provide the native notification service.

CDP injection proves the browser lifecycle but does not contact a vendor. External delivery is checked separately, only with explicitly enabled private configuration, and never in the quality gate. On 4 November 2026 a local check used the configured VAPID keys, a real Chrome/FCM subscription and the running MonKado API/worker: an invitation produced a native notification with every MonKado tab closed and its service worker stopped beforehand. The test subscription and group were then removed. This verifies desktop Chrome; reception on a physical iPhone or Android device remains unobserved. No claim is made for a browser forcibly terminated by the operating system or OS notification suppression.

Production activation follows the existing deployment procedure, including the verified encrypted off-VPS backup. Publish migration, API/worker and frontend together with private VAPID configuration. Verify manifest/worker MIME types; `/push-sw.js` must not receive the SPA fallback or immutable caching. Present the required release recap before merging main.

Rollback: disable `PUSH_ENABLED` in API/worker first. Retain the additive tables, keys and data. Restoring older code requires reviewing notification compatibility: an API older than the preparation release could display mobile-only rows, and an old worker unaware of shared push preferences could ignore muted channel choices. Prefer retaining the new API with push disabled until a compatible rollback is reviewed. Never drop migrations 030/031 or reset user data as a rollback shortcut.

Production uses the reviewed compatibility preparation described in `deployments/main-vps.md`: publish that binary first, keep the original 001–028 SQL immutable, then add 029 (extension usage), 030 (push) and 031 (shared push preferences). The exact expanded catalog is pinned; rollback targets must include the preparation notification filter.
