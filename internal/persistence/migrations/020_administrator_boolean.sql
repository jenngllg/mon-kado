-- Refuse to discard privileges that cannot be represented by the boolean.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM roles WHERE normalized_name NOT IN ('MEMBER', 'ADMIN', 'ADMINISTRATOR'))
       OR EXISTS (SELECT 1 FROM role_claims) THEN
        RAISE EXCEPTION 'Role privileges require review before migrating administrator access';
    END IF;
END $$;

ALTER TABLE users ADD COLUMN is_admin boolean NOT NULL DEFAULT false;

UPDATE users u
SET is_admin = true
WHERE EXISTS (
    SELECT 1 FROM user_roles ur JOIN roles r ON r.id = ur.role_id
    WHERE ur.user_id = u.id AND r.normalized_name IN ('ADMIN', 'ADMINISTRATOR')
);

DROP TABLE user_roles;
DROP TABLE role_claims;
DROP TABLE roles;
