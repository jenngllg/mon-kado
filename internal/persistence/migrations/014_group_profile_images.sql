ALTER TABLE private_groups
    ADD COLUMN profile_image_id uuid,
    ADD COLUMN profile_image_hash bytea,
    ADD CONSTRAINT ck_groups_profile_image_consistent CHECK (
        (profile_image_id IS NULL AND profile_image_hash IS NULL) OR
        (profile_image_id IS NOT NULL AND profile_image_hash IS NOT NULL AND octet_length(profile_image_hash) = 32)
    );

CREATE UNIQUE INDEX ux_groups_profile_image_id ON private_groups(profile_image_id) WHERE profile_image_id IS NOT NULL;
