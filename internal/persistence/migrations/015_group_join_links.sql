CREATE TABLE group_join_links (
    group_id uuid PRIMARY KEY REFERENCES private_groups(id) ON DELETE CASCADE,
    code_hash bytea NOT NULL UNIQUE CHECK (octet_length(code_hash) = 32),
    protected_code text NOT NULL,
    created_at timestamptz NOT NULL
);
