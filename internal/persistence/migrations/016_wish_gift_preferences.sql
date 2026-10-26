-- An open-ended wish uses name/note for its universe/preferences and price as
-- an optional maximum budget per item. Reservation quantities stay unchanged.
ALTER TABLE wishes
    ADD COLUMN is_surprise boolean NOT NULL DEFAULT false,
    ADD COLUMN accepts_second_hand boolean NOT NULL DEFAULT false;
