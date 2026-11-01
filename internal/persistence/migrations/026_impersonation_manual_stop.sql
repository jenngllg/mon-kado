-- Preserve terminal delegations; removing the deadline must not revive expired ones.
UPDATE administrator_impersonations
SET ended_at = expires_at
WHERE ended_at IS NULL AND expires_at <= statement_timestamp();

ALTER TABLE administrator_impersonations ALTER COLUMN expires_at DROP NOT NULL;
UPDATE administrator_impersonations SET expires_at = NULL;
DROP INDEX ix_administrator_impersonations_expiry;
CREATE INDEX ix_administrator_impersonations_ended ON administrator_impersonations(ended_at)
    WHERE ended_at IS NOT NULL;
