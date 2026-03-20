-- ============================================
-- Remove redundant 'domain' column from all tables
-- In multi-tenant architecture, each domain has its own database,
-- so storing domain in row data is redundant.
-- ============================================

-- 1. ntfl_transfer_source: drop domain column and index
DROP INDEX idx_ntfl_source_domain;
ALTER TABLE ntfl_transfer_source DROP COLUMN domain;

-- 2. ntfl_folder_config: drop domain column, rebuild unique index on file_type_code only
DROP INDEX idx_ntfl_folder_unique;
ALTER TABLE ntfl_folder_config DROP COLUMN domain;
CREATE UNIQUE INDEX idx_ntfl_folder_unique ON ntfl_folder_config (file_type_code);

-- 3. ntfl_activity_log: drop domain column and index
DROP INDEX idx_ntfl_activity_domain;
ALTER TABLE ntfl_activity_log DROP COLUMN domain;

-- 4. ntfl_folder_storage: drop domain column and index
DROP INDEX idx_ntfl_storage_domain;
ALTER TABLE ntfl_folder_storage DROP COLUMN domain;

-- 5. ntfl_ai_domain_config: recreate as single-row config table
-- Step a: Preserve existing data
CREATE TABLE ntfl_ai_domain_config_bak AS SELECT * FROM ntfl_ai_domain_config;
DROP TABLE ntfl_ai_domain_config;

-- Step b: Recreate with config_id PK instead of domain PK
CREATE TABLE ntfl_ai_domain_config (
    config_id        SERIAL PRIMARY KEY,
    api_key          VARCHAR(200) NOT NULL,
    model            VARCHAR(50) DEFAULT 'claude-sonnet-4-20250514',
    enabled          CHAR(1) DEFAULT 'Y',
    max_reviews_day  INTEGER DEFAULT 50,
    max_output_tokens INTEGER DEFAULT 4096,
    reviews_today    INTEGER DEFAULT 0,
    reviews_reset_dt DATE,
    created_at       DATETIME YEAR TO SECOND,
    created_by       VARCHAR(50),
    updated_at       DATETIME YEAR TO SECOND,
    updated_by       VARCHAR(50)
);

-- Step c: Migrate first row of data (single-row table)
INSERT INTO ntfl_ai_domain_config (
    api_key, model, enabled, max_reviews_day, max_output_tokens,
    reviews_today, reviews_reset_dt, created_at, created_by, updated_at, updated_by
)
SELECT FIRST 1
    api_key, model, enabled, max_reviews_day, max_output_tokens,
    reviews_today, reviews_reset_dt, created_at, created_by, updated_at, updated_by
FROM ntfl_ai_domain_config_bak;

-- Step d: Drop backup table
DROP TABLE ntfl_ai_domain_config_bak;
