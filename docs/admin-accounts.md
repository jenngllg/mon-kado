# Administrative account table

The authenticated administrator list returns registration dates and current counts
of owned lists (including archived lists), group memberships and wish entries in
owned lists. Quantities and private suggestions are not added to the wish count.
Queries preserve newest-first ordering, hidden profiles, pagination and the existing
live administrator/MFA checks. Single-account erasure identity responses stay minimal.

`hasAddedViaExtension` is true after a successful direct wish or private-idea addition
reported by the extension. It is stored atomically with that addition, survives
removal of the item and is deleted with its account. The separate usage table leaves
profile ETags unchanged. Existing accounts start at null because earlier requests
carried no attribution. New accounts start at false. Opening the extension, resolving
images or filling a draft does not count as a completed addition. Usage also appears
in the member's own data export.

## Migration and rollback

Migration `029_member_extension_usage.sql` only creates a child table and records
unknown usage for existing accounts. Apply it explicitly under the usual PostgreSQL
migration lock, with the required fresh verified encrypted offsite backup. Existing
rows and earlier migrations are unchanged. The current online coordinator rejects
changed SQL catalogs; its gate must remain in place. Review the additive migration
procedure before a production release rather than bypassing this check.

Deploy the backend before the frontend. Application rollback can retain the new
table and its data; do not drop it or down-migrate account data. The older account
identity API remains compatible, and the frontend can render unavailable statistics
as unknown during a rolling transition. Verify table counts, administrator MFA,
simulation return, confirmed erasure and extension attribution before publication.
