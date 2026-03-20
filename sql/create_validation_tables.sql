-- ============================================
-- Validation Framework Database Tables
-- For Informix Database
-- ============================================

-- --------------------------------------------
-- Table: ntfl_validation_summary
-- Stores AI-friendly validation summaries as JSON
-- for later retrieval by AI agents or reporting
-- --------------------------------------------
CREATE TABLE ntfl_validation_summary (
    nt_file_num         INTEGER NOT NULL,       -- FK to nt_file
    summary_json        TEXT,                   -- Full JSON summary (ValidationSummaryForAI)
    overall_status      VARCHAR(256),           -- Brief status message
    total_errors        INTEGER,                -- Total error count
    can_partially_process CHAR(1),              -- Y/N - can file be partially processed
    created_dt          DATETIME YEAR TO SECOND DEFAULT CURRENT YEAR TO SECOND,
    updated_dt          DATETIME YEAR TO SECOND,
    PRIMARY KEY (nt_file_num)
);

-- Index for querying files with errors
CREATE INDEX idx_ntfl_val_sum_errors ON ntfl_validation_summary (total_errors);

-- --------------------------------------------
-- Table: ntfl_error_log (if not exists)
-- Stores individual validation/parsing errors
-- This table may already exist - check before running
-- --------------------------------------------
-- CREATE TABLE ntfl_error_log (
--     nt_file_num     INTEGER NOT NULL,       -- FK to nt_file
--     error_seq       INTEGER NOT NULL,       -- Error sequence within file
--     error_code      CHAR(10),               -- Error code (e.g., FIELD_PARSE_DECIMAL)
--     error_message   VARCHAR(256),           -- Error message with details
--     line_number     INTEGER,                -- Line number in source file
--     raw_data        VARCHAR(500),           -- Raw data that caused the error
--     created_dt      DATETIME YEAR TO SECOND DEFAULT CURRENT YEAR TO SECOND,
--     PRIMARY KEY (nt_file_num, error_seq)
-- );
--
-- CREATE INDEX idx_ntfl_err_code ON ntfl_error_log (error_code);
-- CREATE INDEX idx_ntfl_err_line ON ntfl_error_log (nt_file_num, line_number);

-- --------------------------------------------
-- Grant permissions (adjust as needed)
-- --------------------------------------------
-- GRANT SELECT, INSERT, UPDATE, DELETE ON ntfl_validation_summary TO PUBLIC;
-- GRANT SELECT, INSERT, UPDATE, DELETE ON ntfl_error_log TO PUBLIC;

-- ============================================
-- Verification Queries
-- ============================================

-- Check table structure
-- SELECT * FROM systables WHERE tabname = 'ntfl_validation_summary';
-- SELECT * FROM syscolumns WHERE tabid = (SELECT tabid FROM systables WHERE tabname = 'ntfl_validation_summary');

-- Check if ntfl_error_log exists
-- SELECT * FROM systables WHERE tabname = 'ntfl_error_log';

-- ============================================
-- Sample Queries
-- ============================================

-- Get validation summary for a file
-- SELECT nt_file_num, overall_status, total_errors, can_partially_process, created_dt
-- FROM ntfl_validation_summary
-- WHERE nt_file_num = ?;

-- Get files with validation errors
-- SELECT f.nt_file_num, f.nt_file_name, v.total_errors, v.overall_status
-- FROM nt_file f
-- JOIN ntfl_validation_summary v ON f.nt_file_num = v.nt_file_num
-- WHERE v.total_errors > 0
-- ORDER BY v.created_dt DESC;

-- Get detailed errors for a file
-- SELECT error_seq, error_code, error_message, line_number, raw_data
-- FROM ntfl_error_log
-- WHERE nt_file_num = ?
-- ORDER BY error_seq;

-- Get error counts by type for a file
-- SELECT error_code, COUNT(*) as error_count
-- FROM ntfl_error_log
-- WHERE nt_file_num = ?
-- GROUP BY error_code
-- ORDER BY error_count DESC;
