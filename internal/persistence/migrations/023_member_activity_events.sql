CREATE EXTENSION IF NOT EXISTS unaccent;

CREATE TABLE member_activity_events (
    id uuid PRIMARY KEY,
    member_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    occurred_at timestamptz NOT NULL,
    kind text NOT NULL CHECK (kind IN ('pageVisit', 'action')),
    activity text NOT NULL CHECK (length(activity) BETWEEN 1 AND 255)
);
CREATE INDEX ix_member_activity_events_occurred_at ON member_activity_events (occurred_at DESC, id DESC);
CREATE INDEX ix_member_activity_events_member ON member_activity_events (member_id, occurred_at DESC);
