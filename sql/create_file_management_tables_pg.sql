-- ============================================
-- File Management Tables for Selcomm.FileLoader
-- PostgreSQL SQL
-- ============================================

-- Transfer source configuration
-- Stores FTP/SFTP/FileSystem source definitions
CREATE TABLE IF NOT EXISTS ntfl_transfer_source (
    source_id           SERIAL NOT NULL PRIMARY KEY,
    vendor_name         VARCHAR(128),
    file_type_code      VARCHAR(10),
    protocol            VARCHAR(16) NOT NULL,
    host                VARCHAR(255),
    port                INTEGER DEFAULT 22,
    remote_path         VARCHAR(255) DEFAULT '/',
    auth_type           VARCHAR(16) DEFAULT 'PASSWORD',
    username            VARCHAR(64),
    password_encrypted  VARCHAR(512),
    certificate_path    VARCHAR(255),
    private_key_path    VARCHAR(255),
    file_name_pattern   VARCHAR(128) DEFAULT '*.*',
    skip_file_pattern   VARCHAR(128),
    delete_after_download CHAR(1) DEFAULT 'Y',
    compress_on_archive CHAR(1) DEFAULT 'Y',
    compression_method  VARCHAR(16) DEFAULT 'GZIP',
    cron_schedule       VARCHAR(64),
    is_enabled          CHAR(1) DEFAULT 'Y',
    created_dt          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_dt          TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_ntfl_source_enabled ON ntfl_transfer_source (is_enabled);

ALTER TABLE ntfl_transfer_source
    ADD CONSTRAINT fk_transfer_source_file_type
    FOREIGN KEY (file_type_code) REFERENCES file_type(file_type_code);

-- Folder workflow configuration
CREATE TABLE IF NOT EXISTS ntfl_folder_config (
    config_id           SERIAL PRIMARY KEY,
    file_type_code      VARCHAR(10),
    transfer_folder     VARCHAR(255) NOT NULL,
    processing_folder   VARCHAR(255) NOT NULL,
    processed_folder    VARCHAR(255) NOT NULL,
    errors_folder       VARCHAR(255) NOT NULL,
    skipped_folder      VARCHAR(255) NOT NULL,
    created_dt          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_dt          TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_ntfl_folder_unique ON ntfl_folder_config (file_type_code);

ALTER TABLE ntfl_folder_config
    ADD CONSTRAINT fk_folder_config_file_type
    FOREIGN KEY (file_type_code) REFERENCES file_type(file_type_code);

-- Downloaded files tracking (for no-delete vendors)
CREATE TABLE IF NOT EXISTS ntfl_downloaded_files (
    download_id         SERIAL PRIMARY KEY,
    source_id           INTEGER NOT NULL,
    remote_file_name    VARCHAR(255) NOT NULL,
    remote_file_path    VARCHAR(512),
    file_size           INTEGER,
    remote_modified_dt  TIMESTAMP,
    file_hash           VARCHAR(64),
    downloaded_dt       TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_ntfl_downloaded_unique ON ntfl_downloaded_files (source_id, remote_file_name, remote_modified_dt);
CREATE INDEX IF NOT EXISTS idx_ntfl_downloaded_source ON ntfl_downloaded_files (source_id);

-- File transfer tracking
CREATE TABLE IF NOT EXISTS ntfl_transfer (
    transfer_id         SERIAL PRIMARY KEY,
    source_id           INTEGER,
    nt_file_num         INTEGER,
    file_name           VARCHAR(255) NOT NULL,
    status_id           INTEGER NOT NULL DEFAULT 0,
    source_path         VARCHAR(512),
    destination_path    VARCHAR(512),
    current_folder      VARCHAR(32),
    file_size           INTEGER,
    started_dt          TIMESTAMP,
    completed_dt        TIMESTAMP,
    error_message       VARCHAR(512),
    retry_count         INTEGER DEFAULT 0,
    created_by          VARCHAR(32),
    created_dt          TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_ntfl_transfer_status ON ntfl_transfer (status_id);
CREATE INDEX IF NOT EXISTS idx_ntfl_transfer_file ON ntfl_transfer (nt_file_num);
CREATE INDEX IF NOT EXISTS idx_ntfl_transfer_source ON ntfl_transfer (source_id);
CREATE INDEX IF NOT EXISTS idx_ntfl_transfer_folder ON ntfl_transfer (current_folder);
CREATE INDEX IF NOT EXISTS idx_ntfl_transfer_dt ON ntfl_transfer (created_dt);

-- User activity log
CREATE TABLE IF NOT EXISTS ntfl_activity_log (
    activity_id         SERIAL PRIMARY KEY,
    nt_file_num         INTEGER,
    transfer_id         INTEGER,
    file_name           VARCHAR(255),
    activity_type       INTEGER NOT NULL,
    description         VARCHAR(512),
    details_json        TEXT,
    user_id             VARCHAR(32) NOT NULL,
    activity_dt         TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_ntfl_activity_file ON ntfl_activity_log (nt_file_num);
CREATE INDEX IF NOT EXISTS idx_ntfl_activity_transfer ON ntfl_activity_log (transfer_id);
CREATE INDEX IF NOT EXISTS idx_ntfl_activity_dt ON ntfl_activity_log (activity_dt);
CREATE INDEX IF NOT EXISTS idx_ntfl_activity_user ON ntfl_activity_log (user_id);
