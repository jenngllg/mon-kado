-- Online additive expansion: bounded locks, no existing data rewritten.
SET LOCAL lock_timeout = '2s';
SET LOCAL statement_timeout = '30s';

CREATE TABLE member_notifications (
    id uuid PRIMARY KEY,
    member_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    kind text NOT NULL CHECK(kind IN ('wishesAdded','groupJoined','groupInvitation','occasionReminder','sharingReminder','groupWishlistShared','reservationUnavailable','wishReserved','suggestionAdded')),
    actor_id uuid REFERENCES users(id) ON DELETE SET NULL,
    participant_id uuid REFERENCES wishlist_participants(id) ON DELETE CASCADE,
    wishlist_id uuid REFERENCES wishlists(id) ON DELETE CASCADE,
    group_id uuid REFERENCES private_groups(id) ON DELETE CASCADE,
    subscription_id uuid REFERENCES wishlist_subscriptions(id) ON DELETE CASCADE,
    invitation_id uuid REFERENCES group_invitations(id) ON DELETE CASCADE,
    subject_id uuid,
    deduplication_key text,
    quantity integer NOT NULL DEFAULT 1 CHECK(quantity>0),
    days_remaining integer,
    event_date date,
    created_at timestamptz NOT NULL,
    read_at timestamptz,
    UNIQUE(member_id, deduplication_key)
);
CREATE INDEX ix_member_notifications_page ON member_notifications(member_id,created_at DESC,id DESC);
CREATE INDEX ix_member_notifications_unread ON member_notifications(member_id) WHERE read_at IS NULL;
ALTER TABLE wishlists ADD COLUMN sharing_reminder_dismissed boolean NOT NULL DEFAULT false;

-- PostgreSQL delivers only committed invalidations. No business content or secrets
-- travel through the channel; every connected member is authorized again on read.
CREATE FUNCTION invalidate_member_notifications() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM pg_notify('monkado_notifications', '');
    RETURN NULL;
END;
$$;
CREATE TRIGGER member_notifications_changed AFTER INSERT OR UPDATE OR DELETE ON member_notifications FOR EACH STATEMENT EXECUTE FUNCTION invalidate_member_notifications();
CREATE TRIGGER notification_wishlists_changed AFTER UPDATE OR DELETE ON wishlists FOR EACH STATEMENT EXECUTE FUNCTION invalidate_member_notifications();
CREATE TRIGGER notification_groups_changed AFTER UPDATE OR DELETE ON private_groups FOR EACH STATEMENT EXECUTE FUNCTION invalidate_member_notifications();
CREATE TRIGGER notification_members_changed AFTER INSERT OR DELETE ON group_members FOR EACH STATEMENT EXECUTE FUNCTION invalidate_member_notifications();
CREATE TRIGGER notification_invitations_changed AFTER DELETE ON group_invitations FOR EACH STATEMENT EXECUTE FUNCTION invalidate_member_notifications();
CREATE TRIGGER notification_subscriptions_changed AFTER DELETE ON wishlist_subscriptions FOR EACH STATEMENT EXECUTE FUNCTION invalidate_member_notifications();
CREATE TRIGGER notification_grants_changed AFTER INSERT OR DELETE ON group_wishlists FOR EACH STATEMENT EXECUTE FUNCTION invalidate_member_notifications();
CREATE TRIGGER notification_exclusions_changed AFTER INSERT OR DELETE ON group_wishlist_exclusions FOR EACH STATEMENT EXECUTE FUNCTION invalidate_member_notifications();
CREATE TRIGGER notification_shares_changed AFTER INSERT OR UPDATE OR DELETE ON wishlist_share_links FOR EACH STATEMENT EXECUTE FUNCTION invalidate_member_notifications();
CREATE TRIGGER notification_wishes_changed AFTER UPDATE OR DELETE ON wishes FOR EACH STATEMENT EXECUTE FUNCTION invalidate_member_notifications();
CREATE TRIGGER notification_suggestions_changed AFTER DELETE ON group_suggestions FOR EACH STATEMENT EXECUTE FUNCTION invalidate_member_notifications();
