CREATE TABLE private_groups (
    id uuid PRIMARY KEY,
    owner_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name varchar(100) NOT NULL CHECK (length(btrim(name)) > 0),
    created_at timestamptz NOT NULL,
    updated_at timestamptz
);
CREATE INDEX ix_private_groups_owner ON private_groups(owner_id);

CREATE TABLE group_members (
    group_id uuid NOT NULL REFERENCES private_groups(id) ON DELETE CASCADE,
    member_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    joined_at timestamptz NOT NULL,
    PRIMARY KEY(group_id, member_id)
);
CREATE INDEX ix_group_members_member ON group_members(member_id, group_id);

CREATE TABLE group_invitations (
    id uuid PRIMARY KEY,
    group_id uuid NOT NULL REFERENCES private_groups(id) ON DELETE CASCADE,
    member_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL CHECK (expires_at > created_at),
    UNIQUE(group_id, member_id)
);
CREATE INDEX ix_group_invitations_member ON group_invitations(member_id, expires_at);

ALTER TABLE wishlists ADD CONSTRAINT ux_wishlists_id_owner UNIQUE(id, owner_id);
CREATE TABLE group_wishlists (
    group_id uuid NOT NULL,
    wishlist_id uuid NOT NULL,
    owner_id uuid NOT NULL,
    created_at timestamptz NOT NULL,
    PRIMARY KEY(group_id, wishlist_id),
    FOREIGN KEY(group_id, owner_id) REFERENCES group_members(group_id, member_id) ON DELETE CASCADE,
    FOREIGN KEY(wishlist_id, owner_id) REFERENCES wishlists(id, owner_id) ON DELETE CASCADE
);
CREATE INDEX ix_group_wishlists_wishlist ON group_wishlists(wishlist_id, group_id);

-- A group reservation must never mint a public sharing secret through history.
ALTER TABLE gift_reservation_histories ADD COLUMN is_group_reservation boolean NOT NULL DEFAULT false;
