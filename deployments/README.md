# Deployment configuration

## Shared image storage

Gift images and member profile photos use the same persistent `gift_images` volume,
mounted at `/var/lib/mon-kado/gift-images` in both the API and Worker containers.
`GiftImages:StoragePath` must point to the same durable filesystem in every instance.
PostgreSQL stores references and normalized content hashes, not image files.

Profile photos are public for confirmed accounts. Their URLs contain the current
image identifier, and the API rechecks the account and image reference on every
read. Replacement, photo deletion and account deletion invalidate previous URLs
immediately; physical deletion runs asynchronously through the shared outbox.

## First profile-photo rollout (MK-876)

1. Apply the additive `AddProfileImages` database migration.
2. Upgrade **every Worker instance** before enabling profile-photo uploads in the API.
   The updated cleanup service checks references from both wishes and accounts.
   An older Worker could mistake a referenced profile photo with a pending marker
   for an orphan and delete its file.
3. Upgrade the API while retaining the existing shared volume and filesystem permissions.

Once photos have been uploaded, do not roll the Worker back to a version that only
recognizes wish-image references. Stop cleanup workers before such a rollback and
restore a compatible version before restarting them. Do not drop the profile-photo
columns while account references still need to be retained or cleaned up.

Container recreation does not replace an image-volume backup. Production backup
and restoration must include this volume as well as PostgreSQL; the operational
backup setup belongs to the deployment work, not this feature.
