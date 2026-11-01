
CREATE TABLE IF NOT EXISTS account_action_tokens (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    purpose text NOT NULL,
    token_hash bytea NOT NULL CHECK (octet_length(token_hash) = 32),
    security_stamp text NOT NULL,
    payload jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz,
    CHECK (expires_at > created_at)
);
CREATE INDEX IF NOT EXISTS ix_account_action_tokens_expiry ON account_action_tokens(expires_at);
