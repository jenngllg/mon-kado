# Native encrypted backup and restoration

The capture and restoration commands use native PostgreSQL 18, systemd, Restic
and rclone. They do not require Docker. Local tests cover a real Go-migrated schema,
dump/restore, byte integrity, encrypted Restic roundtrip, deduplication, wrong
passwords and corruption. The historical Drive qualification exercise and installer
sandbox still need native reconciliation before the final migration merge.
No real Drive exercise or production restoration has been performed.

## Coherent capture

Install the matching reviewed production bundle first, then the backup bundle with
`sudo bash deployments/backup/install.sh`. Existing credentials and status are
preserved; capture/transfer timers are never enabled implicitly. Configure the
dedicated Restic password, repository file and rclone credentials privately under
`/etc/monkado-backup`, root:root 0600. Keep the recovery password separately from
the encrypted repository. The production repository remains
`rclone:monkado:MonKado-backups/production-v1`.

The existing daily 03:00 Europe/Paris schedule, shared coordination locks, short
writer maintenance, interrupted-capture recovery, quota check, explicit transfer
attempts and fourteen-day retention remain. Failed upload/check never authorizes
pruning or deletion of the pending capture. Transfers happen after service recovery;
they do not extend the application maintenance window.

Capture schema 5 binds the complete approved native publication and SQL catalog,
actual API/worker executable digests, PostgreSQL/Caddy executable hashes, server
version and every captured file's hash/size. It includes:

- Native logical PostgreSQL dump.
- Durable flat UUID `.webp` images and `.pending` markers, excluding partial uploads.
- Exact approved API, worker and migration executable bytes.
- Reviewed configuration, private process/Caddy EnvironmentFiles, stable JWT and
  data-encryption keys, active/confirmed pointers and deployment state.
- Dedicated smoke credentials when present, inside the encrypted private capture.

Production capture rejects unconfirmed/interrupted deployments, wrong running
executables, divergent SQL history, missing cryptographic keys or an inconsistent
pointer. Before stopping writers it checks free space, approved retained binary
bytes, private configuration and all live services. It stops only Caddy/API/worker,
keeps PostgreSQL running and records durable maintenance intent before doing so.
Recovery starts exactly those native units without replaying migrations. A failure
to recover keeps the marker for explicit operator investigation.

Schemas 1–4 remain readable for historical integrity checks. Their .NET key files,
sharded images and container references are historical data, not native dependencies.
The native restoration command requires a schema-5 capture.

## Isolated restoration

Use an explicit complete snapshot hash; never restore `latest` implicitly:

```sh
sudo monkado-backup download SNAPSHOT_HASH /private/new-capture-directory
sudo monkado-backup restore /private/new-capture-directory monkado-restore-UNIQUEIDENTIFIER
```

The capture directory must be root-owned and mode 0700. Every file, approved
executable hash, configuration fingerprint, pointer and stable key is checked
before effects. A fresh project under `/var/lib/monkado-restores` is required;
existing resources are never overwritten. The new PostgreSQL cluster accepts only
a private Unix socket and peer authentication, with TCP disabled persistently.
It restores transactionally into `mon_kado_restore`, verifies complete native SQL
history and copies private images/configuration/binaries. It stops the database
even after a failed restore. Failed resources remain for investigation.

Restoration never starts the API, worker or a provider, uses production credentials
to connect, changes the production database, executes restored operational scripts,
or runs a down migration. An explicit private verification start must retain the
isolated cluster configuration. Review any later promotion separately; do not
substitute restoration for deployment rollback.

Local verification is not proof of offsite recoverability. Complete the native
synthetic offsite qualification and retained credential handoff before accepting
data to preserve or enabling automatic captures.
