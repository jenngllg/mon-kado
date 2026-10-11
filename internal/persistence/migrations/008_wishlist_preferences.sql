ALTER TABLE wishlists ADD COLUMN surprise_mode boolean NOT NULL DEFAULT true;
ALTER TABLE wishlists ADD COLUMN is_archived boolean NOT NULL DEFAULT false;
ALTER TABLE wishes ADD COLUMN is_favorite boolean NOT NULL DEFAULT false;
