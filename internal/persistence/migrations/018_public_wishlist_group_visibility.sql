CREATE TABLE wishlist_group_visibility (
    wishlist_id uuid PRIMARY KEY REFERENCES wishlists(id) ON DELETE CASCADE,
    updated_at timestamptz
);
INSERT INTO wishlist_group_visibility(wishlist_id) SELECT id FROM wishlists;

CREATE TABLE group_wishlist_exclusions (
    group_id uuid NOT NULL,
    wishlist_id uuid NOT NULL,
    owner_id uuid NOT NULL,
    created_at timestamptz NOT NULL,
    PRIMARY KEY(group_id, wishlist_id),
    FOREIGN KEY(group_id, owner_id) REFERENCES group_members(group_id, member_id) ON DELETE CASCADE,
    FOREIGN KEY(wishlist_id, owner_id) REFERENCES wishlists(id, owner_id) ON DELETE CASCADE
);
CREATE INDEX ix_group_wishlist_exclusions_wishlist ON group_wishlist_exclusions(wishlist_id, group_id);

-- One effective grant source is shared by list reads, images and reservation history.
-- Explicit private grants remain independent of public-link revocation.
CREATE VIEW visible_group_wishlists AS
SELECT gw.group_id,gw.wishlist_id,gw.owner_id,gw.created_at
FROM group_wishlists gw
UNION ALL
SELECT m.group_id,w.id,w.owner_id,GREATEST(m.joined_at,l.created_at)
FROM wishlists w
JOIN wishlist_share_links l ON l.wishlist_id=w.id
JOIN group_members m ON m.member_id=w.owner_id
WHERE NOT EXISTS(SELECT 1 FROM group_wishlists gw WHERE gw.group_id=m.group_id AND gw.wishlist_id=w.id)
  AND NOT EXISTS(SELECT 1 FROM group_wishlist_exclusions e WHERE e.group_id=m.group_id AND e.wishlist_id=w.id);
