ALTER TABLE wishes
    ADD COLUMN color varchar(100),
    ADD COLUMN size varchar(100),
    ADD CONSTRAINT ck_wishes_color_valid CHECK (color IS NULL OR (char_length(btrim(color)) > 0 AND color !~ U&'[[:cntrl:]\2028\2029]')),
    ADD CONSTRAINT ck_wishes_size_valid CHECK (size IS NULL OR (char_length(btrim(size)) > 0 AND size !~ U&'[[:cntrl:]\2028\2029]'));
