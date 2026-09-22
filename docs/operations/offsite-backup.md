# Offsite backup and disaster recovery (MK-813)

## Activation status

This runbook is not evidence that backups are active. Do not enable the nightly
timer before a reviewed installation, a successful Drive capture, and an isolated
restore using the Bitwarden recovery copy. Record the first unattended night and
weekly integrity result before closing MK-813.

## Scope and limits

The root-only tool captures a PostgreSQL 18 logical dump, current gift/profile
images, pending image markers, the complete XML Data Protection key ring, deployed
configuration (including `production.env`), and immutable application references.
It excludes the raw PostgreSQL volume, temporary personal-data export archives,
logs, caches, image write temporaries, and the Restic password.

New captures use manifest version 2. Their configuration inventory matches the
current release hash contract, including frontend routing and monitoring code;
a regression test rejects drift between these inventories. The manifest also
records the running Caddy image digest alongside PostgreSQL and application
references. Schema 1 snapshots remain verifiable/restorable as historical data
captures, but lack the newer ancillary configuration and Caddy digest. For those
snapshots, obtain the missing reviewed files from their recorded Git revision
before rebuilding a host; never infer an immutable image from a mutable tag.
Existing remote snapshots are not rewritten or deleted by this format change.

Caddy, API and Worker are stopped during the capture. PostgreSQL remains running.
The application must be healthy again before any remote transfer. A durable
maintenance marker allows service/boot recovery; the shared deployment lock and
deployment marker prevent capture during migrations. The installer adds a guarded
deployment entry point and systemd drop-in without modifying the published
deployment files. Both paths acquire the coordination lock before the deployment
lock, and deployment refuses an outstanding backup maintenance marker.

Snapshots are encrypted by Restic and stored through rclone in the dedicated
Drive remote `monkado:MonKado-backups/production-v1`. No paid storage is purchased.
Quota is shared with Gmail; insufficient free space is an explicit failure. The
preflight estimate is deliberately conservative and may refuse a capture even
when deduplication would reduce its eventual transfer size.

Retention is fourteen elapsed days, not fourteen successful backup dates. Purging
is permitted only after a new snapshot has passed verification. The verified
snapshot is retained even if old; no pruning follows failed transfer/checks.
Three total transfer attempts are separated by fifteen minutes, without another
maintenance. `transfer` retries an existing local capture manually. The
`monkado-backup-transfer.service` also promotes a validated interrupted capture
without stopping writers. If neither capture exists, it reports
`NO_PENDING_CAPTURE` without changing previous success or error metadata.
A completed `capture.new` is never discarded by the next nightly capture, even
after a restart or promotion failure. `INTERRUPTED_CAPTURE_REQUIRES_TRANSFER`
requires `resume` before another maintenance. When both a pending capture and a
completed candidate exist, resume the pending transfer first, then resume again
to promote and transfer the candidate; no service shutdown is needed.

This is not immutable storage. Compromise of the VPS and its Drive authorization
can destroy both copies. Daily success gives approximately a 24-hour recovery
point objective, not a guarantee. Restore duration must be measured in an exercise.

## Credentials and prerequisites

Use Ubuntu 24.04 with Python 3.12+, Docker/Compose, curl, Restic and rclone. Validate
the selected package versions and memory consumption on the 1 GiB VPS before
activation. Local tests use Restic 0.18.0; they do not qualify every other release.

Create a separate Google OAuth desktop client for backup access, not the Gmail
sender grant. Select only `drive.file`, authorize `monkado.app@gmail.com`, and use
the remote name `monkado`. The OAuth application must not remain in Testing with
a seven-day refresh grant. Confirm refresh after the access token expires. Keep
the OAuth client configuration recoverable independently of the lost VPS; the
same client must retain access to files created with its restricted grant.

Follow the [rclone Drive configuration guide](https://rclone.org/drive/) without
printing credentials into chat, CI logs or shell history. The grant and local
configuration are credentials even when the repository itself is encrypted.
Do not reuse an old full-Drive grant merely by editing its configured scope.

Required files in `/etc/monkado-backup`, all regular files owned by root with mode
0600, in a root-owned 0700 directory:

- `password`: the 64-character randomly generated Bitwarden secret. Install using
  `sudo monkado-backup credentials`, which prompts twice without echo and refuses
  to overwrite an existing password. Never put the password in an argument.
- `rclone.conf`: the dedicated OAuth remote. Allow rclone to persist refreshed
  tokens privately. Do not add it to the capture or repository.
- `repository`: the literal `rclone:monkado:MonKado-backups/production-v1`.

Losing both password copies makes the encrypted repository unrecoverable; see
[Restic repository setup](https://restic.readthedocs.io/en/stable/030_preparing_a_new_repo.html).
Keep Bitwarden recovery access independent of this VPS as well.

## Reviewed installation and first backup

From the reviewed checkout, run `sudo bash deployments/backup/install.sh`. This
installs code and boot recovery, but deliberately leaves capture/check timers
disabled. Prepare the three private files above. Initialize an empty repository
only once with `sudo monkado-backup initialize`; never initialize over an uncertain
or damaged existing repository.

Arrange the initial short maintenance, then run:

```sh
sudo systemctl start monkado-backup.service
sudo monkado-backup status
sudo systemctl show monkado-backup.service -p Result -p ExecMainStatus
```

`status` exposes capture age, last remote success, exact snapshot identifier,
capture size, integrity timestamp, a bounded technical error and a missed-schedule
flag. A remote capture older than thirty hours is degraded. Weekly integrity
success does not erase a preceding upload error. No external notifications are
configured here (#815).

After the complete recovery exercise succeeds, enable both timers explicitly:

```sh
sudo systemctl enable --now monkado-backup.timer monkado-backup-check.timer
sudo systemctl list-timers 'monkado-backup*'
```

The capture timer is 03:00 Europe/Paris with `Persistent=false`: a missed run must
be investigated, not caught up by stopping production during the day. Both Paris
clock changes preserve a single 03:00 local schedule. The weekly full-data check
is independent and does not stop application services.

## Restore without the original VPS

Use an isolated Linux environment on the PC with Docker access, restored backup
tooling and a fresh authorized Drive configuration. Retrieve the secret from
Bitwarden using hidden input, not from the VPS. Never enable the production
backup timer on the recovery machine.

Choose an exact 64-hex snapshot ID, not `latest` or a short prefix:

```sh
sudo monkado-backup download EXACT_SNAPSHOT_ID /private/new-recovery-directory
sudo monkado-backup restore /private/new-recovery-directory monkado-restore-0123456789abcdef
```

The destination must not exist. The tool accepts both supported manifest versions,
verifies every manifest hash and creates
fresh named volumes plus an internal network; it refuses existing resources.
It imports the dump atomically, restores image/key ownership to UID 1654, and
leaves the database stopped. It publishes no ports, starts no API/Worker and
loads no production environment into a running application. The downloaded
capture DOES contain production secrets: preserve private permissions and remove
it securely according to the workstation storage policy after validation.

Before declaring recovery successful, verify representative database data,
images, and decryption of a test administrator authenticator using the saved
Data Protection application name and original purpose identifiers. A hash check
alone does not establish that application-level cryptography works. Use a local
API only with no outbound connectivity, synthetic credentials, loopback-only
ports and the recorded application revision. Never start the production Worker
or allow Gmail/Google traffic during an exercise.

## Manual production recovery approval

Do not switch production to restored volumes automatically. Before approval:

1. Reconcile deletions since the snapshot; do not resurrect erased accounts or
   content without examining the deletion history available outside that point.
2. Revoke old member sessions and temporary authentication proofs through a
   reviewed, version-compatible operation. Review guest/share access separately.
3. Review queued/outbox e-mails to avoid replaying obsolete messages.
4. Invalidate personal-data exports whose archive files were intentionally not
   backed up, respecting database constraints and existing audit retention.
5. Review credentials, DNS, TLS, mounts, permissions and immutable image versions.
6. Record operator approval before restoring public traffic or external workers.

Keep the old volumes intact until the new installation is verified and the
operator explicitly approves their disposal. No general cleanup command is part
of this procedure.

## Acceptance record (not yet completed)

### Observed exercise on 2026-09-21

Snapshot `79703f5b1b41c55eb3e097f37d08800aa406498cafb414aab0157f8de5cdf26b`
was downloaded from Drive on the PC using the separately entered Bitwarden
password, without retrieving that password from the VPS. Manifest verification
and PostgreSQL restoration succeeded into new Docker volumes using application
revision `692517fa5438f0e88a8831bb77c82faf5509376c`.

The restored database contained zero users, wishlists and wishes. The snapshot
contained one Data Protection file and no images. Its restored hash and UID 1654
ownership matched. The database had no published ports, used an internal Docker
network and was stopped after verification; no API or Worker was started.
The restore command took approximately ten seconds locally, excluding download
and verification; this is not a production recovery-time guarantee.

Separate synthetic PostgreSQL/image restoration and copied-key-ring authenticator
tests passed. Additionally, a populated synthetic exercise completed through the
separate Drive repository `MonKado-backups/exercise-813-20260921`, snapshot
`a42c00b41575b586283fc9c2bbff8c4a05b8682427feec22cd8e560fa96b3978`.
Its PostgreSQL probe row contained a fictitious administrator's protected TOTP
secret and an image hash. The dump, valid 1x1 WebP and separate generated key ring
were uploaded, checked with `--read-data`, downloaded and restored into fresh
isolated volumes. File hashes matched when read as UID 1654. The restored database
row and restored key ring then passed `TwoFactorCryptography.UnprotectSecret` and
the RFC TOTP vector in a separate process. No production member was created.

The synthetic roundtrip took 84 seconds, excluding final application-level TOTP
verification. A second unchanged backup added zero data bytes. The production
repository also passed a full `check --read-data` from the recovery PC. Neither
exercise started an API or Worker; both restored databases are stopped and have
no published ports. This verifies backup primitives and cryptography, not a full
member-facing application recovery. The first unattended night is recorded below;
the first scheduled weekly check remains pending.

### Observed unattended run on 2026-09-22

The operator supplied the root-only backup status after the first scheduled run.
The capture was created at `2026-09-22T01:00:08.529641+00:00` (03:00 in Paris)
and published remotely at `2026-09-22T01:01:10.728829+00:00`, with snapshot
`2ea6b22f1618276e0b5690f134db26973dfccaae26b7145f3cd9ecae2bbc660a`.
The captured payload was 141,028 bytes. `error` was null, `health` was `healthy`,
and `missedScheduledCapture` was false. This is evidence of the scheduled capture
and verified remote transfer, not a measurement of the application outage duration.

The MK-815 monitor sent the actual `backup.failed` incident and subsequently its
recovery at `2026-09-22T01:04:03.283515+00:00`; the operator confirmed receipt of
both emails. At `2026-09-22T08:20:03.630627+00:00`, its status was healthy with
notifications enabled, no open incidents and no unknown checks. The historical
backup error was not manually cleared to obtain this result.

Both backup timers are enabled. The first scheduled weekly full-data integrity
check is due on 2026-09-27 at 05:00 UTC. The earlier manual full-data check is
recorded above; it does not establish that the weekly timer has executed.

### Local compatibility review on 2026-09-22

The delivery branch was updated to `develop` revision
`b2f94e05be57a7fe625840c4fbbae8e50af035c1`. The schema 2 correction has not been
installed on production: the observed scheduled snapshot above still uses schema
1. A reviewed reinstall and a subsequent schema 2 capture must be checked before
claiming the newer configuration inventory is protected in production.

The offline Python suite passed 83 tests (three opt-in tests were skipped and
passed separately), covering all backup source files including provisioning,
authorization, diagnostic and exercise entry points: 790/790 statements and
246/246 branches, both 100%. This run had no network and was limited to 192 MiB,
no swap and half a CPU. It validates the local processing budget, not peak VPS
consumption during an actual Drive upload. The separate real PostgreSQL Docker
exercise also passed, including
logical import, image/key hash verification as UID 1654, stopped database,
isolated resources and refusal to overwrite existing restore volumes. This
does not replace final CI. Two additional installer tests passed in a disposable
offline container, verifying credentials/timer preservation and refusal to
overwrite installed code while either backup or deployment holds its lock.

Local C# coverage aggregation (Windows solution run plus Linux storage tests)
passed with 23,698/23,698 lines and 4,799/4,799 branches covered. Formatting,
ShellCheck and the .NET vulnerability audit passed. A redacted scan of the staged
changes found no secrets. These local results do not replace final remote
CI/Sonar review.

- [ ] Reviewed CI and shell validation on the final revision.
- [x] Local PostgreSQL + image + Data Protection/MFA primitive exercise.
- [ ] Drive OAuth refresh and shared quota verified.
- [ ] First actual Drive backup, with measured VPS memory and maintenance duration.
- [x] Restore downloaded from Drive using the Bitwarden copy, measured duration.
- [x] First unattended 03:00 run, followed by the real monitoring recovery email.
- [ ] First scheduled weekly full-data integrity check (2026-09-27 05:00 UTC).
