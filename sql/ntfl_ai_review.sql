-- ============================================
-- AI Review Tables
-- ============================================

-- Cached AI review results
CREATE TABLE ntfl_ai_review (
    review_id        SERIAL PRIMARY KEY,
    nt_file_num      INTEGER NOT NULL,
    file_type_code   VARCHAR(20),
    overall_assessment VARCHAR(20),
    summary          LVARCHAR(4000),
    issues_json      LVARCHAR(32000),
    records_sampled  INTEGER,
    total_records    INTEGER,
    input_tokens     INTEGER,
    output_tokens    INTEGER,
    model_used       VARCHAR(50),
    reviewed_at      DATETIME YEAR TO SECOND,
    reviewed_by      VARCHAR(50),
    expires_at       DATETIME YEAR TO SECOND
);
CREATE INDEX idx_ntfl_ai_review_file ON ntfl_ai_review(nt_file_num);

-- Example files per file type (for AI comparison)
CREATE TABLE ntfl_ai_example_file (
    file_type_code   VARCHAR(20) PRIMARY KEY,
    file_path        VARCHAR(500) NOT NULL,
    description      VARCHAR(200),
    updated_at       DATETIME YEAR TO SECOND,
    updated_by       VARCHAR(50)
);

-- AI configuration (API keys, limits) — single-row table
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
