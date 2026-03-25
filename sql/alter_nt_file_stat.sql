-- ============================================
-- File Status Remap
-- Renames existing statuses and adds new error statuses
-- ============================================

-- Rename existing statuses to match new workflow
UPDATE nt_file_stat SET status_narr = 'Transferred' WHERE status_id = 1;
UPDATE nt_file_stat SET status_narr = 'Validated' WHERE status_id = 2;
UPDATE nt_file_stat SET status_narr = 'Loaded' WHERE status_id = 3;

-- Insert new error statuses
INSERT INTO nt_file_stat (status_id, status_narr) VALUES (6, 'Validation Error');
INSERT INTO nt_file_stat (status_id, status_narr) VALUES (7, 'Load Error');
