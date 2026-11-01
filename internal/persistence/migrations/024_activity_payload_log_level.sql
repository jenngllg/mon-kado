ALTER TABLE member_activity_events
    ADD COLUMN log_level text NOT NULL DEFAULT 'info' CHECK (log_level IN ('info', 'error')),
    ADD COLUMN payload jsonb CHECK (payload IS NULL OR octet_length(payload::text) <= 65536);

CREATE INDEX ix_member_activity_events_level_time
    ON member_activity_events (log_level, occurred_at DESC, id DESC);
