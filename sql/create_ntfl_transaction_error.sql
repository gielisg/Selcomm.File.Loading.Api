-- ============================================
-- Transaction Error Child Table
-- Schema owned by File Loading module.
-- Populated by the Charges Module during transaction processing.
-- ============================================

CREATE TABLE ntfl_transaction_error (
    error_id            SERIAL NOT NULL,
    nt_file_num         INTEGER NOT NULL,           -- FK to nt_file
    nt_file_rec_num     INTEGER NOT NULL,           -- Record number within file
    source_table        VARCHAR(64) NOT NULL,       -- e.g. ntfl_generic_detail, ntfl_chgdtl, custom table name
    error_code          VARCHAR(30) NOT NULL,       -- e.g. NO_MATCH, INVALID_ACCOUNT, DUPLICATE
    error_message       VARCHAR(255),               -- Human-readable error description
    error_detail        LVARCHAR(2000),             -- Extended detail (JSON or text)
    created_dt          DATETIME YEAR TO SECOND DEFAULT CURRENT YEAR TO SECOND,
    created_by          VARCHAR(64),

    PRIMARY KEY (error_id)
);

CREATE INDEX idx_ntfl_txn_err_file ON ntfl_transaction_error (nt_file_num);
CREATE INDEX idx_ntfl_txn_err_rec ON ntfl_transaction_error (nt_file_num, nt_file_rec_num);
