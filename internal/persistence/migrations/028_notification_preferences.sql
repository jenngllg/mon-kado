-- Additive member-private settings; existing accounts retain all notifications by default.
SET LOCAL lock_timeout = '2s';
SET LOCAL statement_timeout = '30s';

CREATE TABLE member_notification_preferences (
    member_id uuid PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    disabled_kinds text[] NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL,
    updated_at timestamptz,
    CHECK (array_position(disabled_kinds, NULL) IS NULL),
    CHECK (disabled_kinds <@ ARRAY['wishesAdded','groupJoined','groupInvitation','occasionReminder','sharingReminder','groupWishlistShared','reservationUnavailable','wishReserved','suggestionAdded']::text[])
);
