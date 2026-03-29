-- Service mapping table for file loading
-- Maps service identifier values from loaded files to sp_connection records
-- Multiple mappings per file_type are supported, resolved by seq_no order

CREATE TABLE ntfl_svc_map (
    id               SERIAL PRIMARY KEY,
    file_type_code   CHAR(10) NOT NULL,
    service_reference INTEGER NOT NULL,
    mapping_string   VARCHAR(120) NOT NULL,
    seq_no           INTEGER NOT NULL DEFAULT 0,
    last_updated     TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_by       VARCHAR(18) DEFAULT SESSION_USER NOT NULL
);

CREATE UNIQUE INDEX idx_ntfl_svc_map_str ON ntfl_svc_map(file_type_code, mapping_string);
CREATE INDEX idx_ntfl_svc_map_seq ON ntfl_svc_map(file_type_code, seq_no);

ALTER TABLE ntfl_svc_map
    ADD CONSTRAINT fk_ntfl_svc_map_ftype
    FOREIGN KEY (file_type_code) REFERENCES file_type(file_type_code);

ALTER TABLE ntfl_svc_map
    ADD CONSTRAINT fk_ntfl_svc_map_spcn
    FOREIGN KEY (service_reference) REFERENCES sp_connection(sp_cn_ref);
