-- A suggestion is not a wish: it must never be included in an owner's list,
-- export or public share simply because it belongs to the same wishlist.
CREATE TABLE group_suggestions (
    id uuid PRIMARY KEY,
    group_id uuid NOT NULL REFERENCES private_groups(id) ON DELETE CASCADE,
    wishlist_id uuid NOT NULL REFERENCES wishlists(id) ON DELETE CASCADE,
    author_id uuid REFERENCES users(id) ON DELETE SET NULL,
    name varchar(100) NOT NULL CHECK (length(btrim(name)) > 0),
    note varchar(500),
    url varchar(2048),
    price numeric(10,2) CHECK (price > 0),
    quantity integer NOT NULL CHECK (quantity BETWEEN 1 AND 100),
    image_content bytea CHECK (octet_length(image_content) <= 10485760),
    image_id uuid,
    created_at timestamptz NOT NULL,
    updated_at timestamptz,
    CHECK ((image_content IS NULL) = (image_id IS NULL)),
    CHECK (updated_at IS NULL OR updated_at >= created_at)
);
CREATE INDEX ix_group_suggestions_scope ON group_suggestions(group_id, wishlist_id, created_at, id);

-- Reservations survive a departure just as reservations on ordinary group
-- wishes do; they disappear when the reserving account is deleted.
CREATE TABLE group_suggestion_reservations (
    id uuid PRIMARY KEY,
    suggestion_id uuid NOT NULL REFERENCES group_suggestions(id) ON DELETE CASCADE,
    member_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    quantity integer NOT NULL CHECK (quantity BETWEEN 1 AND 100),
    created_at timestamptz NOT NULL,
    updated_at timestamptz,
    CHECK (updated_at IS NULL OR updated_at >= created_at),
    UNIQUE (suggestion_id, member_id)
);

-- A retained history row is still subject to the original group's live access.
ALTER TABLE gift_reservation_histories ADD COLUMN suggestion_group_id uuid;

CREATE FUNCTION mark_suggestion_reservation_history_unavailable() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    UPDATE gift_reservation_histories
    SET status='Unavailable', last_activity_at=GREATEST(statement_timestamp(), created_at),
        ended_at=GREATEST(statement_timestamp(), created_at)
    WHERE wish_id=OLD.id AND wishlist_id=OLD.wishlist_id AND status='Active';
    RETURN OLD;
END;
$$;
CREATE TRIGGER tr_suggestions_mark_reservation_history_unavailable
BEFORE DELETE ON group_suggestions FOR EACH ROW
EXECUTE FUNCTION mark_suggestion_reservation_history_unavailable();
