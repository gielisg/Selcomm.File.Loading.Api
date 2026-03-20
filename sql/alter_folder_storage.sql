-- ============================================
-- Folder Storage Configuration Table
-- Stores domain-level storage mode (LOCAL/FTP) and optional FTP connection details.
-- ============================================

CREATE TABLE ntfl_folder_storage (
    storage_id          SERIAL PRIMARY KEY,
    domain              VARCHAR(32) NOT NULL,
    storage_mode        VARCHAR(8) NOT NULL DEFAULT 'LOCAL',
    protocol            VARCHAR(16),
    host                VARCHAR(255),
    port                INTEGER,
    auth_type           VARCHAR(16),
    username            VARCHAR(64),
    password_encrypted  LVARCHAR(512),
    certificate_path    VARCHAR(255),
    private_key_path    VARCHAR(255),
    base_path           VARCHAR(255) DEFAULT '/',
    temp_local_path     VARCHAR(255),
    created_dt          DATETIME YEAR TO SECOND DEFAULT CURRENT YEAR TO SECOND,
    updated_dt          DATETIME YEAR TO SECOND
);

CREATE UNIQUE INDEX idx_ntfl_storage_domain ON ntfl_folder_storage (domain);
