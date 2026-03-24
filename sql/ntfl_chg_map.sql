-- ============================================
-- Generic Charge Mapping Table
-- Maps file charge descriptions to Selcomm charge codes per file type.
-- Replaces vendor-specific *_chg_map tables with a single generic table.
-- ============================================

CREATE TABLE ntfl_chg_map (
    id               SERIAL PRIMARY KEY,
    file_type_code   CHAR(10) NOT NULL,              -- FK file_type
    file_chg_desc    LVARCHAR(500) NOT NULL,          -- charge description pattern (supports % wildcards)
    seq_no           INTEGER NOT NULL DEFAULT 0,       -- match order (lower = try first)
    chg_code         CHAR(4) NOT NULL,                 -- FK charge_code
    auto_exclude     CHAR(1) NOT NULL DEFAULT 'N',
    use_net_price    CHAR(1) NOT NULL DEFAULT 'N',
    net_prc_prorated CHAR(1) NOT NULL DEFAULT 'Y',
    uplift_perc      DECIMAL(10,6) NOT NULL DEFAULT 0,
    uplift_amt       DECIMAL(10,4),
    use_net_desc     CHAR(1) NOT NULL DEFAULT 'N',
    last_updated     DATETIME YEAR TO SECOND DEFAULT CURRENT YEAR TO SECOND NOT NULL,
    updated_by       VARCHAR(18) DEFAULT USER NOT NULL
);

CREATE UNIQUE INDEX idx_ntfl_chg_map_desc ON ntfl_chg_map(file_type_code, file_chg_desc);
CREATE INDEX idx_ntfl_chg_map_seq ON ntfl_chg_map(file_type_code, seq_no);

ALTER TABLE ntfl_chg_map
    ADD CONSTRAINT FOREIGN KEY (file_type_code) REFERENCES file_type(file_type_code)
    CONSTRAINT fk_ntfl_chg_map_ftype;

ALTER TABLE ntfl_chg_map
    ADD CONSTRAINT FOREIGN KEY (chg_code) REFERENCES charge_code(chg_code)
    CONSTRAINT fk_ntfl_chg_map_chgcode;
