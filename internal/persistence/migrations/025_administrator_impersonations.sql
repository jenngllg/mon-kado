ALTER TABLE member_activity_events
    ADD COLUMN actor_member_id uuid REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN is_impersonated boolean NOT NULL DEFAULT false;
UPDATE member_activity_events SET actor_member_id=member_id;

CREATE TABLE administrator_impersonations (
    id uuid PRIMARY KEY,
    administrator_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    member_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    session_id uuid NOT NULL REFERENCES authentication_sessions(id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    ended_at timestamptz,
    CHECK (administrator_id <> member_id)
);
CREATE INDEX ix_administrator_impersonations_expiry ON administrator_impersonations(expires_at);
