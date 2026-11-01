-- Keep the author as known when a reservation was made, without granting access to the source.
ALTER TABLE gift_reservation_histories
    ADD COLUMN author_id uuid REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN author_display_name varchar(100),
    ADD CONSTRAINT ck_reservation_history_author_pair CHECK ((author_id IS NULL) = (author_display_name IS NULL));

-- Existing history can only be attributed when its original list still exists.
UPDATE gift_reservation_histories h
SET author_id = w.owner_id, author_display_name = u.display_name
FROM wishlists w JOIN users u ON u.id = w.owner_id
WHERE h.wishlist_id = w.id;

CREATE INDEX ix_reservation_history_author ON gift_reservation_histories(author_id) WHERE author_id IS NOT NULL;

-- Erasure also covers histories whose source list was deleted before the account.
CREATE FUNCTION anonymize_reservation_history_author() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    UPDATE gift_reservation_histories
    SET author_id = NULL, author_display_name = NULL,
        wishlist_name = 'Deleted wishlist', wish_name = 'Deleted gift'
    WHERE author_id = OLD.id;
    RETURN OLD;
END;
$$;
CREATE TRIGGER anonymize_reservation_history_author_before_user_delete
BEFORE DELETE ON users FOR EACH ROW EXECUTE FUNCTION anonymize_reservation_history_author();
