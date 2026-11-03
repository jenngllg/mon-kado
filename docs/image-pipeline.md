# Image pipeline

Uploads keep the ordinary 15-second browser deadline. Large image preparation and
server encoding reduce the work instead of extending that deadline.

## Processing and access

- The web client prepares files larger than 384 KiB locally, preserving aspect
  ratio, orientation and transparency. Product/suggestion images are bounded to
  1600 pixels per edge; member/group photos use 512 pixels. Small files remain
  untouched. Preparation is cancelled with the owning operation.
- The server independently checks every upload: static JPEG, PNG or WebP, at most
  10 MiB and 40 million pixels. It corrects orientation, bounds dimensions and
  removes metadata. Opaque photographs use JPEG quality 90. PNG sources and
  transparency use PNG with fast lossless compression.
- Private storage keeps its historical `UUID.webp` names independently of the
  encoded media type, so the installed backup/restore inventory retains every
  referenced file. HTTP MIME types and export suffixes use the actual bytes.
  The reader also accepts `.img` files created during local development.
- Private grants and current membership are verified before serving images or
  consulting the bounded thumbnail cache. Gallery thumbnails use at most 600
  pixels, including private group suggestions. Detail views use the full image.
- Exports retain their immutable snapshot and hash checks. Their image filenames
  and `data.json` paths match the actual JPEG, PNG or legacy WebP bytes.
- The legacy extension image-resolution contract still returns bounded WebP;
  it shares the same validation and is separate from ordinary upload encoding.
- Failed/uncertain image writes are never automatically replayed. After a
  confirmed wish creation, “Vérifier le souhait” opens the saved wish.

## Local verification

Start the native PostgreSQL/API/worker and the frontend as documented in the two
repositories. Run `./scripts/Test-Native.ps1` in the backend and `pnpm test`,
`pnpm typecheck`, `pnpm lint`, `pnpm api:types:check` and `pnpm build` in the
frontend. Browser checks cover desktop/mobile creation, editing, image loading,
uncertain-write recovery and persistence after reload. Exercise profile/group
photos, group suggestions, private/public reads and exports as well.

## Deployment and rollback

No SQL migration or rewriting of stored images is needed. Use two qualified
publications, each with the normal fresh verified encrypted offsite backup:

1. Publish compatible readers, exports and frontend with
   `IMAGE_FAST_ENCODING_ENABLED=false` (the default). New uploads stay WebP.
   Verify the exact running revisions and all access controls.
2. Only after that compatible revision is running, activate
   `IMAGE_FAST_ENCODING_ENABLED=true` in the private environment and publish the
   activation revision through the normal online coordinator. Its previous
   qualified binary already understands JPEG/PNG, so binary rollback remains safe.

The original group image JSON endpoint keeps its WebP contract for older tabs.
The additive `/image/content` endpoint serves current JPEG/PNG/WebP content and
authorized thumbnails. Existing OpenAPI operations and schemas remain unchanged.

To disable efficient encoding, set the flag to false and use another qualified
online publication; keep the compatible readers. Never roll back to a revision
predating step 1 after activation, or restore old member data to change encoding.
