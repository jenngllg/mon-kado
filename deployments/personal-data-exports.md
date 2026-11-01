# Personal data exports

Members request their own export under `/api/v1/members/current/data-exports`. Administrators use the member-scoped administration routes with a support reference and verified factor session. Administrative requests and downloads are audited.

The native worker claims requests with renewable leases and streams a repeatable-read PostgreSQL snapshot into a private ZIP containing `data.json`, a README and referenced normalized images. The schema preserves account/list/wish/sharing/participation/reservation/history/activity data while excluding credentials, proofs, share secrets, internal moderation data and other participants' private reservations.

Archives have bounded size, attempt duration and lifetime; defaults are 1 GiB, 10 minutes and 24 hours. Configure the EXPORT_* environment variables before starting processes. API and worker share EXPORT_PATH. Downloads are streamed only after an authorization and ready/expiry check; files are never exposed through Caddy.

States are queued, processing, ready, expired or failed. Active requests are reused and requests are quota-limited. A lost commit acknowledgement does not cause immediate deletion of a potentially published archive. The worker reconciles abandoned files and removes expired/orphaned archives.
