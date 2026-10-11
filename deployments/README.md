# Native deployment

Build with Go 1.27 and CGO_ENABLED=0. Run PostgreSQL 18, Caddy, API and worker as native services. No Docker is required. The checked-in systemd units are templates; installing or enabling services is a separate deployment operation.

1. Create a dedicated non-login `monkado` user and a PostgreSQL application role/database. Enable SCRAM and use TLS if PostgreSQL is remote.
2. Copy binaries to `/opt/mon-kado/bin` and create private `/var/lib/mon-kado/gift-images` and `/var/lib/mon-kado/personal-data-exports` directories owned by the application user.
3. Put runtime variables in `/etc/mon-kado/environment` with restricted permissions. Use `.env.example` as a list of names, replacing placeholders privately. Keep the encryption/signing keys stable and back them up securely.
4. Execute `/opt/mon-kado/bin/migrate` with the same environment. Do this before starting/upgrading API and worker; neither migrates on startup.
5. Adapt and install the API/worker systemd units. Bind `HTTP_ADDRESS=127.0.0.1:7000`, set production origins/hosts, and trust only the loopback Caddy address (`TRUSTED_PROXIES=127.0.0.1/32,::1/128`).
6. Configure Caddy's `API_HOST` with the public API hostname. Caddy terminates HTTPS and forwards to the native loopback listener. Register the exact Google callback `PUBLIC_API_ORIGIN/api/v1/auth/google/callback` if enabled.
7. Verify `/liveness`, `/readiness`, a protected API flow and the worker before routing normal traffic.

API and worker must share the same database and image/export filesystem paths. Do not serve these directories directly with Caddy. Image grants and archive authorization are checked by the API. Protect PostgreSQL from public access.

Back up PostgreSQL, durable image storage, runtime keys and configuration together. Exports are transient private files, not a database backup. Restore with matching encryption keys. Stop API/worker before an incompatible rollback; prefer forward migrations. Test restoration before relying on a backup.

Logs are structured JSON with correlation/trace identifiers. They exclude email addresses, credentials, share secrets and request contents. `EMAIL_PROVIDER=Disabled` disables network delivery while preserving queued messages; expired protected erasure recipients are still purged.

## Operational notes

- [Administrator two-factor authentication](administrator-two-factor.md)
- [Account erasure](administrative-account-erasure.md)
- [Session revocation](administrative-session-revocation.md)
- [Personal data exports](personal-data-exports.md)
- [Wishlist moderation](wishlist-moderation.md)
