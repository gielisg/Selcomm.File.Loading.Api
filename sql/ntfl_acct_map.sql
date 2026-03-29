-- Account mapping table for file loading
-- Maps "account num" values from loaded files to accounts in the account table
-- Multiple mappings per file_type are supported, resolved by seq_no order

CREATE TABLE ntfl_acct_map (
    id               SERIAL PRIMARY KEY,
    file_type_code   CHAR(10) NOT NULL,
    account_code     CHAR(10) NOT NULL,
    mapping_string   VARCHAR(120) NOT NULL,
    seq_no           INTEGER NOT NULL DEFAULT 0,
    last_updated     TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_by       VARCHAR(18) DEFAULT SESSION_USER NOT NULL
);

CREATE UNIQUE INDEX idx_ntfl_acct_map_str ON ntfl_acct_map(file_type_code, mapping_string);
CREATE INDEX idx_ntfl_acct_map_seq ON ntfl_acct_map(file_type_code, seq_no);

ALTER TABLE ntfl_acct_map
    ADD CONSTRAINT fk_ntfl_acct_map_ftype
    FOREIGN KEY (file_type_code) REFERENCES file_type(file_type_code);

ALTER TABLE ntfl_acct_map
    ADD CONSTRAINT fk_ntfl_acct_map_acct
    FOREIGN KEY (account_code) REFERENCES account(debtor_code);
