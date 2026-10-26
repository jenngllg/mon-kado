-- Change only the default for future accounts; preserve every saved visibility choice.
ALTER TABLE users ALTER COLUMN is_visible_in_member_search SET DEFAULT true;
