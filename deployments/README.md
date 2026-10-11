# Native deployment

Build with Go 1.27.2 and CGO_ENABLED=0. Run PostgreSQL 18, Caddy, API and worker as native services. No Docker is required. Follow the [native production runbook](production/README.md) for protected publication, installation, adoption and recovery. Installing or enabling services is a separate deployment operation.

1. Create a dedicated non-login `monkado` user and a PostgreSQL application role/database. Enable SCRAM and use TLS if PostgreSQL is remote.
2. Install reviewed configuration under `/opt/monkado`; approved immutable releases live under `/opt/monkado/releases` with an atomic `/opt/monkado/current` pointer. Create private `/var/lib/monkado/gift-images` and `/var/lib/monkado/personal-data-exports` directories owned by the application user.
3. Put runtime variables in `/etc/monkado/production.env` with root:root 0600 permissions. Use the production environment example, replacing placeholders privately. Keep the encryption/signing keys stable and back them up securely. Only Caddy host/static-path settings belong in its separate private `caddy.env`.
4. The explicit approved migrator runs with the same private environment before starting/upgrading API and worker; neither migrates on startup. Initialization refuses a non-empty native database, and interrupted intent requires explicit recovery.
5. Adapt and install the API/worker systemd units. Bind `HTTP_ADDRESS=127.0.0.1:7000`, set production origins/hosts, and trust only the loopback Caddy address (`TRUSTED_PROXIES=127.0.0.1/32,::1/128`).
6. Configure Caddy's `API_HOST` with the public API hostname. Caddy terminates HTTPS and forwards to the native loopback listener. Register the exact Google callback `PUBLIC_API_ORIGIN/api/v1/auth/google/callback` if enabled.
7. Verify `/liveness`, `/readiness`, a protected API flow and the worker before routing normal traffic.

API and worker must share the same database and image/export filesystem paths. Do not serve these directories directly with Caddy. Image grants and archive authorization are checked by the API. Protect PostgreSQL from public access.

Back up PostgreSQL, durable image storage, runtime keys and configuration together. Exports are transient private files, not a database backup. Restore with matching encryption keys. Stop API/worker before an incompatible rollback; prefer forward migrations. Test restoration before relying on a backup.

Logs are structured JSON with correlation/trace identifiers. They exclude email addresses, credentials, share secrets and request contents. `EMAIL_PROVIDER=Disabled` disables network delivery while preserving queued messages; expired protected erasure recipients are still purged.

Production logging uses a compiled event catalog and a technical property allowlist. Raw messages and dependency error contents are discarded; unexpected HTTP exceptions retain their type and technical call chain without filesystem paths or argument values. Set `APP_VERSION` to the approved forty-character source revision.

Enable `OBSERVABILITY_ENABLED=true` for private monitoring evidence. API and worker replace their respective `/var/lib/monkado-observability/{api,worker}/snapshot.json` files with mode 0600 in mode 0700 directories. `OBSERVABILITY_INTERVAL` defaults to 30 seconds and must remain within 5–60 seconds. The versioned snapshots preserve HTTP outcome/latency counters and all ten worker operations, including confirmed terminal failures. They contain no user or resource labels. The systemd templates allow each process to write its own evidence directory; do not expose the directory through Caddy.

## Operational notes

- [Administrator two-factor authentication](administrator-two-factor.md)
- [Account erasure](administrative-account-erasure.md)
- [Session revocation](administrative-session-revocation.md)
- [Personal data exports](personal-data-exports.md)
- [Wishlist moderation](wishlist-moderation.md)
