SET LOCAL lock_timeout = '2s';
SET LOCAL statement_timeout = '30s';
-- Preserve per-event in-app choices while independently enabling mobile devices.
ALTER TABLE member_notifications ADD COLUMN in_app boolean NOT NULL DEFAULT true;
CREATE TABLE member_push_subscriptions (
 id uuid PRIMARY KEY,
 member_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
 session_id uuid NOT NULL REFERENCES authentication_sessions(id) ON DELETE CASCADE,
 name text NOT NULL CHECK(char_length(name) BETWEEN 1 AND 60),
 endpoint_hash text NOT NULL UNIQUE,
 protected_endpoint text NOT NULL,
 disabled_kinds text[] NOT NULL DEFAULT '{}',
 created_at timestamptz NOT NULL
);
CREATE INDEX ix_push_member ON member_push_subscriptions(member_id);
CREATE TABLE member_push_deliveries (
 notification_id uuid NOT NULL REFERENCES member_notifications(id) ON DELETE CASCADE,
 subscription_id uuid NOT NULL REFERENCES member_push_subscriptions(id) ON DELETE CASCADE,
 status text NOT NULL DEFAULT 'pending' CHECK(status IN('pending','sending','sent','cancelled','failed')),
 attempts integer NOT NULL DEFAULT 0,
 due_at timestamptz NOT NULL,
 lease_id uuid,
 lease_expires_at timestamptz,
 PRIMARY KEY(notification_id,subscription_id)
);
CREATE INDEX ix_push_due ON member_push_deliveries(due_at) WHERE status='pending';
-- Enqueue inside the business transaction; regrouping does not alert twice.
CREATE FUNCTION enqueue_member_push() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 INSERT INTO member_push_deliveries(notification_id,subscription_id,due_at)
 SELECT NEW.id,p.id,NEW.created_at FROM member_push_subscriptions p
 JOIN authentication_sessions s ON s.id=p.session_id AND s.user_id=p.member_id
 WHERE p.member_id=NEW.member_id AND s.revoked_at IS NULL AND s.expires_at>NEW.created_at AND NOT NEW.kind=ANY(p.disabled_kinds);
 RETURN NEW;
END;
$$;
CREATE TRIGGER member_push_enqueue AFTER INSERT ON member_notifications FOR EACH ROW EXECUTE FUNCTION enqueue_member_push();
