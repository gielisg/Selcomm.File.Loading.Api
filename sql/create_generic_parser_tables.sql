-- ============================================
-- Generic Configurable File Parser Tables
-- Supports data-driven parsing of ad-hoc vendor files
-- ============================================

-- ============================================
-- 1. File Format Configuration
-- One row per file type, defines how to read the file
-- ============================================
CREATE TABLE ntfl_file_format_config (
    file_type_code      CHAR(10) NOT NULL,          -- Matches file_type table
    file_format         CHAR(10) NOT NULL,          -- CSV, XLSX, DELIMITED
    delimiter           CHAR(5),                     -- comma, pipe, tab, semicolon (null for XLSX)
    has_header_row      CHAR(1) DEFAULT 'N',        -- Y/N - first data row is column names
    skip_rows_top       INTEGER DEFAULT 0,          -- Rows to skip at top (report titles, blanks)
    skip_rows_bottom    INTEGER DEFAULT 0,          -- Rows to skip at bottom
    row_id_mode         CHAR(10) DEFAULT 'POSITION', -- POSITION, INDICATOR, PATTERN
    row_id_column       INTEGER DEFAULT 0,          -- 0-based column index for INDICATOR mode
    header_indicator    VARCHAR(50),                -- Value/pattern for header row identification
    trailer_indicator   VARCHAR(50),                -- Value/pattern for trailer row identification
    detail_indicator    VARCHAR(50),                -- Value/pattern for detail row identification
    skip_indicator      VARCHAR(50),                -- Value/pattern for skip row identification
    total_column_index  INTEGER,                    -- Column in trailer row containing control total
    total_type          CHAR(5),                    -- SUM (reconcile cost) or COUNT (reconcile record count)
    sheet_name          VARCHAR(64),                -- For XLSX files - sheet name
    sheet_index         INTEGER DEFAULT 0,          -- For XLSX files - sheet index (0-based)
    date_format         VARCHAR(30),                -- Default date format for this file type
    custom_sp_name      VARCHAR(64),                -- Optional SP for complex validation
    active              CHAR(1) DEFAULT 'Y',        -- Y/N
    created_dt          DATETIME YEAR TO SECOND DEFAULT CURRENT YEAR TO SECOND,
    updated_dt          DATETIME YEAR TO SECOND DEFAULT CURRENT YEAR TO SECOND,

    PRIMARY KEY (file_type_code)
);

-- ============================================
-- 2. Column Mapping Configuration
-- One row per column per file type
-- ============================================
CREATE TABLE ntfl_column_mapping (
    file_type_code      CHAR(10) NOT NULL,          -- FK to ntfl_file_format_config
    column_index        INTEGER NOT NULL,            -- 0-based column position in source file
    source_column_name  VARCHAR(64),                -- Header name (for header-row files)
    target_field        VARCHAR(30) NOT NULL,        -- Standard name: AccountCode, ServiceId, etc.
    data_type           CHAR(10) DEFAULT 'String',  -- String, Integer, Decimal, DateTime, Boolean
    date_format         VARCHAR(30),                -- Override date format for this column
    is_required         CHAR(1) DEFAULT 'N',        -- Y/N
    default_value       VARCHAR(64),                -- Default value if source is empty
    regex_pattern       VARCHAR(128),               -- Optional validation regex
    max_length          INTEGER,                    -- Max string length

    PRIMARY KEY (file_type_code, column_index),
    FOREIGN KEY (file_type_code) REFERENCES ntfl_file_format_config (file_type_code)
);

-- ============================================
-- 3. Generic Detail Staging Table
-- Single staging table for all generic vendor records
-- ============================================
CREATE TABLE ntfl_generic_detail (
    nt_file_num         INTEGER NOT NULL,            -- FK to nt_file
    nt_file_rec_num     INTEGER NOT NULL,            -- Record number within file

    -- Standard mapped columns
    account_code        VARCHAR(64),
    service_id          VARCHAR(64),
    charge_type         VARCHAR(30),
    cost_amount         DECIMAL(16,6),
    tax_amount          DECIMAL(16,6),
    quantity            DECIMAL(16,6),
    uom                 VARCHAR(10),
    from_date           DATETIME YEAR TO SECOND,
    to_date             DATETIME YEAR TO SECOND,
    description         VARCHAR(256),
    external_ref        VARCHAR(64),

    -- Generic overflow columns
    generic_01          VARCHAR(128),
    generic_02          VARCHAR(128),
    generic_03          VARCHAR(128),
    generic_04          VARCHAR(128),
    generic_05          VARCHAR(128),
    generic_06          VARCHAR(128),
    generic_07          VARCHAR(128),
    generic_08          VARCHAR(128),
    generic_09          VARCHAR(128),
    generic_10          VARCHAR(128),
    generic_11          VARCHAR(256),
    generic_12          VARCHAR(256),
    generic_13          VARCHAR(256),
    generic_14          VARCHAR(256),
    generic_15          VARCHAR(256),
    generic_16          VARCHAR(256),
    generic_17          VARCHAR(256),
    generic_18          VARCHAR(256),
    generic_19          VARCHAR(256),
    generic_20          VARCHAR(256),

    -- Original row for debugging
    raw_data            VARCHAR(2000),

    -- Status tracking
    status_id           INTEGER DEFAULT 1,

    PRIMARY KEY (nt_file_num, nt_file_rec_num)
);

CREATE INDEX idx_ntfl_gen_acct ON ntfl_generic_detail (account_code);
CREATE INDEX idx_ntfl_gen_svc ON ntfl_generic_detail (service_id);
