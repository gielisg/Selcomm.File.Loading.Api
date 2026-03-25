-- ============================================
-- Transaction Status: INTEGER to VARCHAR(20)
-- Informix cannot ALTER column type directly.
-- Strategy: add new column, migrate data, drop old, rename new.
-- ============================================

-- 1. ntfl_generic_detail
ALTER TABLE ntfl_generic_detail ADD status VARCHAR(20) DEFAULT 'NEW';
UPDATE ntfl_generic_detail SET status = 'NEW' WHERE status_id = 1;
UPDATE ntfl_generic_detail SET status = 'NEW' WHERE status_id IS NULL;
ALTER TABLE ntfl_generic_detail DROP status_id;
RENAME COLUMN ntfl_generic_detail.status TO status_id;

-- 2. ntfl_chgdtl
ALTER TABLE ntfl_chgdtl ADD status VARCHAR(20) DEFAULT 'NEW';
UPDATE ntfl_chgdtl SET status = 'NEW' WHERE status_id = 1;
UPDATE ntfl_chgdtl SET status = 'NEW' WHERE status_id IS NULL;
ALTER TABLE ntfl_chgdtl DROP status_id;
RENAME COLUMN ntfl_chgdtl.status TO status_id;
