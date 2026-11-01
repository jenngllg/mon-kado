INSERT INTO roles(id,name,normalized_name,concurrency_stamp)
VALUES ('0198d027-51c0-7000-8000-000000000004','Administrator','ADMINISTRATOR','0198d027-51c0-7000-8000-000000000005')
ON CONFLICT (normalized_name) DO NOTHING;
