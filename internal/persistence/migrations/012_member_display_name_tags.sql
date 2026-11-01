-- The display name remains editable; only its normalized name/tag pair is unique.
ALTER TABLE users ADD COLUMN display_name_tag text;
ALTER TABLE users ADD COLUMN normalized_display_name text
    GENERATED ALWAYS AS (lower(normalize(btrim(display_name), NFC) COLLATE "pg_unicode_fast")) STORED;

DO $migration$
BEGIN
    IF EXISTS (SELECT 1 FROM users GROUP BY normalized_display_name HAVING count(*) > 9999) THEN
        RAISE EXCEPTION 'Member display name tag capacity exceeded.';
    END IF;
END;
$migration$;

WITH assigned AS (
    SELECT id, lpad(row_number() OVER (PARTITION BY normalized_display_name ORDER BY id)::text, 4, '0') AS tag
    FROM users
)
UPDATE users SET display_name_tag = assigned.tag FROM assigned WHERE users.id = assigned.id;

ALTER TABLE users ALTER COLUMN display_name_tag SET NOT NULL;
ALTER TABLE users ADD CONSTRAINT ck_users_display_name_tag
    CHECK (display_name_tag ~ '^[0-9]{4}$' AND display_name_tag <> '0000');
CREATE UNIQUE INDEX ux_users_display_name_tag ON users(normalized_display_name, display_name_tag);

-- Serialize allocation for the target name, including concurrent registrations and renames.
-- The unique index remains the final invariant; the trigger covers every creation provider.
CREATE FUNCTION assign_member_display_name_tag() RETURNS trigger LANGUAGE plpgsql AS $function$
DECLARE
    tag_capacity CONSTANT integer := 9999;
    normalized_name text := lower(normalize(btrim(NEW.display_name), NFC) COLLATE "pg_unicode_fast");
    start_at integer;
BEGIN
    PERFORM pg_advisory_xact_lock(771206490, hashtext(normalized_name));
    IF TG_OP = 'UPDATE' THEN
        NEW.display_name_tag := OLD.display_name_tag;
        RETURN NEW;
    END IF;

    start_at := floor(random() * tag_capacity)::integer;
    SELECT lpad(((candidate % tag_capacity) + 1)::text, 4, '0') INTO NEW.display_name_tag
    FROM generate_series(start_at, start_at + tag_capacity - 1) AS candidate
    WHERE NOT EXISTS (
        SELECT 1 FROM users WHERE normalized_display_name = normalized_name
            AND display_name_tag = lpad(((candidate % tag_capacity) + 1)::text, 4, '0') AND id <> NEW.id
    ) ORDER BY candidate LIMIT 1;

    IF NEW.display_name_tag IS NULL THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001', CONSTRAINT = 'member_display_name_tags_exhausted',
            MESSAGE = 'No display name tag is available.';
    END IF;
    RETURN NEW;
END;
$function$;

CREATE TRIGGER users_assign_display_name_tag BEFORE INSERT OR UPDATE OF display_name, display_name_tag
    ON users FOR EACH ROW EXECUTE FUNCTION assign_member_display_name_tag();
