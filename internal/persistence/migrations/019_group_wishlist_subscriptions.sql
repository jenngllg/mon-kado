-- A private follow retains its group authorization rather than minting a public link.
ALTER TABLE wishlist_subscriptions
    ALTER COLUMN share_link_id DROP NOT NULL,
    ALTER COLUMN share_secret_hash DROP NOT NULL,
    ADD COLUMN group_id uuid,
    ADD CONSTRAINT fk_wishlist_subscriptions_group_member
        FOREIGN KEY(group_id, member_id) REFERENCES group_members(group_id, member_id) ON DELETE CASCADE,
    ADD CONSTRAINT ck_wishlist_subscriptions_source CHECK (
        (group_id IS NULL AND share_link_id IS NOT NULL AND share_secret_hash IS NOT NULL) OR
        (group_id IS NOT NULL AND share_link_id IS NULL AND share_secret_hash IS NULL)
    );
CREATE INDEX ix_wishlist_subscriptions_group ON wishlist_subscriptions(group_id, wishlist_id) WHERE group_id IS NOT NULL;

-- Check the final audience, including atomic visibility changes that replace grants.
-- Revocation must not silently revive a follow when access is later restored.
CREATE FUNCTION revoke_unavailable_group_list_subscriptions() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    DELETE FROM wishlist_subscriptions s
    WHERE s.group_id IS NOT NULL AND s.wishlist_id = COALESCE(NEW.wishlist_id, OLD.wishlist_id)
      AND NOT EXISTS(SELECT 1 FROM visible_group_wishlists gw WHERE gw.group_id=s.group_id AND gw.wishlist_id=s.wishlist_id);
    RETURN NULL;
END;
$$;
CREATE CONSTRAINT TRIGGER revoke_group_list_subscriptions_on_grant
AFTER DELETE ON group_wishlists DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION revoke_unavailable_group_list_subscriptions();
CREATE CONSTRAINT TRIGGER revoke_group_list_subscriptions_on_exclusion
AFTER INSERT ON group_wishlist_exclusions DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION revoke_unavailable_group_list_subscriptions();
CREATE CONSTRAINT TRIGGER revoke_group_list_subscriptions_on_public_withdrawal
AFTER DELETE ON wishlist_share_links DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION revoke_unavailable_group_list_subscriptions();

CREATE FUNCTION revoke_departed_owner_group_subscriptions() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    DELETE FROM wishlist_subscriptions s USING wishlists w
    WHERE s.group_id=OLD.group_id AND s.wishlist_id=w.id AND w.owner_id=OLD.member_id;
    RETURN NULL;
END;
$$;
CREATE TRIGGER revoke_departed_owner_group_subscriptions
AFTER DELETE ON group_members
FOR EACH ROW EXECUTE FUNCTION revoke_departed_owner_group_subscriptions();
