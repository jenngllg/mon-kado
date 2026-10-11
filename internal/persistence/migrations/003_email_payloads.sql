
ALTER TABLE authentication_email_outbox ADD COLUMN IF NOT EXISTS protected_payload text;
ALTER TABLE authentication_email_outbox ADD COLUMN IF NOT EXISTS lease_id uuid;
