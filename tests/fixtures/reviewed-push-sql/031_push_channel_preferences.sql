SET LOCAL lock_timeout = '2s';
SET LOCAL statement_timeout = '30s';
-- One account-level Push column, independent of browser permission/subscription state.
CREATE TABLE member_push_preferences (
 member_id uuid PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
 disabled_kinds text[] NOT NULL DEFAULT '{}',
 created_at timestamptz NOT NULL,
 updated_at timestamptz,
 CHECK(array_position(disabled_kinds,NULL) IS NULL),
 CHECK(disabled_kinds <@ ARRAY['wishesAdded','groupJoined','groupInvitation','occasionReminder','sharingReminder','groupWishlistShared','reservationUnavailable','wishReserved','suggestionAdded']::text[])
);
-- Preserve explicitly muted legacy choices; unconfigured accounts keep every type enabled.
INSERT INTO member_push_preferences(member_id,disabled_kinds,created_at)
SELECT p.member_id,COALESCE(array_agg(DISTINCT kind) FILTER(WHERE kind IS NOT NULL),'{}'::text[]),min(p.created_at)
FROM member_push_subscriptions p LEFT JOIN LATERAL unnest(p.disabled_kinds) kind ON true GROUP BY p.member_id;
CREATE FUNCTION member_push_kind_enabled(member uuid,kind text,legacy_disabled text[]) RETURNS boolean
LANGUAGE sql STABLE AS $$
 SELECT COALESCE((SELECT NOT kind=ANY(disabled_kinds) FROM member_push_preferences WHERE member_id=member),NOT kind=ANY(legacy_disabled));
$$;
CREATE OR REPLACE FUNCTION enqueue_member_push() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 INSERT INTO member_push_deliveries(notification_id,subscription_id,due_at)
 SELECT NEW.id,p.id,NEW.created_at FROM member_push_subscriptions p
 JOIN authentication_sessions s ON s.id=p.session_id AND s.user_id=p.member_id
 WHERE p.member_id=NEW.member_id AND s.revoked_at IS NULL AND s.expires_at>NEW.created_at
 AND member_push_kind_enabled(p.member_id,NEW.kind,p.disabled_kinds);
 RETURN NEW;
END;
$$;
