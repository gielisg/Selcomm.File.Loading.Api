-- ============================================
-- Custom Table Metadata
-- Tracks versioned staging tables created from
-- generic parser column mappings
-- ============================================

CREATE TABLE ntfl_custom_table (
    custom_table_id     SERIAL NOT NULL,
    file_type_code      CHAR(10) NOT NULL,            -- FK to ntfl_file_format_config
    table_name          VARCHAR(64) NOT NULL,          -- Physical table name (e.g. ntfl_optus_chg_v1)
    version             INTEGER NOT NULL DEFAULT 1,
    status              CHAR(10) NOT NULL DEFAULT 'ACTIVE',  -- ACTIVE, RETIRED, DROPPED
    column_count        INTEGER NOT NULL,
    column_definition   LVARCHAR(4000),               -- JSON snapshot of column mappings at creation
    created_dt          DATETIME YEAR TO SECOND DEFAULT CURRENT YEAR TO SECOND,
    created_by          VARCHAR(30),
    dropped_dt          DATETIME YEAR TO SECOND,

    PRIMARY KEY (custom_table_id),
    UNIQUE (file_type_code, version),
    UNIQUE (table_name)
);

CREATE INDEX idx_ntfl_ct_ftc ON ntfl_custom_table (file_type_code);
