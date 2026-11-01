-- Match the consolidated .NET contract: new accounts are hidden until they opt in.
-- Preserve any preferences explicitly saved during local Go development.
ALTER TABLE users ALTER COLUMN is_visible_in_member_search SET DEFAULT false;
