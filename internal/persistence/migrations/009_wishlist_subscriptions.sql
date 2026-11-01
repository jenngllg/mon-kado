CREATE TABLE wishlist_subscriptions (
    id uuid PRIMARY KEY,
    member_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    wishlist_id uuid NOT NULL REFERENCES wishlists(id) ON DELETE CASCADE,
    share_link_id uuid NOT NULL REFERENCES wishlist_share_links(id) ON DELETE CASCADE,
    share_secret_hash bytea NOT NULL,
    created_at timestamptz NOT NULL,
    CONSTRAINT ck_wishlist_subscriptions_secret_hash CHECK (octet_length(share_secret_hash) = 32)
);
CREATE UNIQUE INDEX ux_wishlist_subscriptions_member_wishlist ON wishlist_subscriptions(member_id,wishlist_id);
CREATE INDEX ix_wishlist_subscriptions_share_link ON wishlist_subscriptions(share_link_id);
