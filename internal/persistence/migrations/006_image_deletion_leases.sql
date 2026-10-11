ALTER TABLE gift_image_deletion_outbox ADD COLUMN lease_id uuid;
UPDATE gift_image_deletion_outbox SET locked_until=NULL;
ALTER TABLE gift_image_deletion_outbox ADD CONSTRAINT ck_gift_image_deletion_lease
CHECK ((lease_id IS NULL) = (locked_until IS NULL));
