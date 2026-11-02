# Discord delivery publication

Jenn requested automatic publication on 2 November 2026. The MonKado server has
read-only `releases` (1566851303324254358) and `extension` (1566852359395483848)
channels. Members can read; posting and creating/replying to threads are denied
for `@everyone`. Feedback stays in `bug-report` and `suggestions`.

The existing protected deployment workflows call `scripts/discord_release.py`
only **after** `Deploy-MainVPS.sh` succeeds. Artifact creation alone never posts.
No new bot, paid service, production permission or deployment bypass is needed.
The frontend uses the publisher from its explicitly paired backend revision.

## Preparing each delivery

Update `docs/discord-release-notes.md` in the delivered repository with concise,
public French notes (at most 1500 characters). Do not include credentials,
user information, private payloads or security-sensitive implementation details.
The exact delivered revision is linked in the announcement.

For an extension change, bump `extension/manifest.json` and update the matching
version in `extension/INSTALLATION.md`. The publisher normalizes text to LF and builds a deterministic ZIP
from an explicit file allowlist and attaches that guide separately. Both come
from the same qualified frontend revision. A changed package with an already
published version is rejected; unchanged versions are not posted again.
Receipts keep both the sent attachments' SHA-256 and a source fingerprint,
independent of ZIP compression and Windows/Linux text line endings.

Secrets: `DISCORD_RELEASES_WEBHOOK` in both repositories and
`DISCORD_EXTENSION_WEBHOOK` in the frontend repository. Each incoming webhook is
scoped to its channel. Keep URLs only in GitHub secrets or ignored private local
configuration. Never print them. Rotate a webhook through Discord if exposed,
then replace its GitHub secret. Disabling/deleting the corresponding secret
stops future posts without changing the site or its data.

## Verification and recovery

Run `python3 -m unittest discover -s tests/Operations.DeploymentTests -p test_discord_release.py`.
Preview without network or secrets:
`python3 scripts/discord_release.py frontend --source ../mon-kado-front --revision FULL_SHA --dry-run`.

Every post first creates a GitHub prerelease receipt named
`discord-frontend-SHA`, `discord-backend-SHA` or `discord-extension-VERSION`.
The receipt moves from `pending` to `delivered` only after Discord returns its
message ID. Receipt creation also serializes competing attempts. These receipts
are bookkeeping, not deployment approval, and must never drive the production
pointer. No automatic retry is performed on a webhook POST.

If an attempt remains pending, inspect the named Discord channel and workflow
before recovering. If the message exists, update that receipt with `state:
delivered`, `messageId` and `channelId`, preserving its revision and hash. If its
absence is established, delete only that pending receipt and tag, then rerun the
publisher for the same delivered revision. Never rerun a production deployment
just to resend a notification. A notification failure does not roll back a
successful deployment. To disable the integration permanently, remove the final
Discord steps from both deployment workflows; the normal backup/deploy gates stay.
