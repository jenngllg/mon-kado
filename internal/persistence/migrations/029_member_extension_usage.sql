-- Existing members have no reliable historical attribution; new members start unused.
SET LOCAL lock_timeout = '2s';
SET LOCAL statement_timeout = '30s';

CREATE TABLE member_extension_usage (
    member_id uuid PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    has_added boolean
);

INSERT INTO member_extension_usage(member_id, has_added)
SELECT id, NULL FROM users;
