CREATE TABLE google_authentication_flows (
 id uuid PRIMARY KEY, state_hash bytea NOT NULL UNIQUE CHECK(octet_length(state_hash)=32),
 binding_hash bytea NOT NULL CHECK(octet_length(binding_hash)=32),
 completion_hash bytea UNIQUE CHECK(completion_hash IS NULL OR octet_length(completion_hash)=32),
 protected_context text NOT NULL, created_at timestamptz NOT NULL, expires_at timestamptz NOT NULL,
 callback_claimed_at timestamptz, consumed_at timestamptz
);
CREATE INDEX ix_google_flows_expiry ON google_authentication_flows(expires_at);
