-- ============================================
-- File Management Tables for Selcomm.FileLoader
-- Informix SQL
-- ============================================

-- Transfer source configuration
-- Stores FTP/SFTP/FileSystem source definitions
CREATE TABLE ntfl_transfer_source (
    source_id           SERIAL NOT NULL PRIMARY KEY,
    vendor_name         VARCHAR(128),  -- Friendly name for vendor/source
    domain              VARCHAR(32) NOT NULL,
    file_type_code      VARCHAR(10),
    protocol            VARCHAR(16) NOT NULL,  -- SFTP, FTP, FILESYSTEM
    host                VARCHAR(255),
    port                INTEGER DEFAULT 22,
    remote_path         VARCHAR(255) DEFAULT '/',
    auth_type           VARCHAR(16) DEFAULT 'PASSWORD',  -- PASSWORD, CERTIFICATE, PRIVATEKEY
    username            VARCHAR(64),
    password_encrypted  LVARCHAR(512),  -- AES encrypted
    certificate_path    VARCHAR(255),
    private_key_path    VARCHAR(255),
    file_name_pattern   VARCHAR(128) DEFAULT '*.*',
    skip_file_pattern   VARCHAR(128),
    delete_after_download CHAR(1) DEFAULT 'Y',
    compress_on_archive CHAR(1) DEFAULT 'Y',
    compression_method  VARCHAR(16) DEFAULT 'GZIP',  -- NONE, GZIP, ZIP
    cron_schedule       VARCHAR(64),
    is_enabled          CHAR(1) DEFAULT 'Y',
    created_dt          DATETIME YEAR TO SECOND DEFAULT CURRENT YEAR TO SECOND,
    updated_dt          DATETIME YEAR TO SECOND
);

CREATE INDEX idx_ntfl_source_domain ON ntfl_transfer_source (domain);
CREATE INDEX idx_ntfl_source_enabled ON ntfl_transfer_source (is_enabled);

ALTER TABLE ntfl_transfer_source
    ADD CONSTRAINT FOREIGN KEY (file_type_code) REFERENCES file_type(file_type_code);

-- Folder workflow configuration
-- Defines folder paths for each domain/file-type combination
CREATE TABLE ntfl_folder_config (
    config_id           SERIAL PRIMARY KEY,
    domain              VARCHAR(32) NOT NULL,
    file_type_code      VARCHAR(10),
    transfer_folder     VARCHAR(255) NOT NULL,
    processing_folder   VARCHAR(255) NOT NULL,
    processed_folder    VARCHAR(255) NOT NULL,
    errors_folder       VARCHAR(255) NOT NULL,
    skipped_folder      VARCHAR(255) NOT NULL,
    created_dt          DATETIME YEAR TO SECOND DEFAULT CURRENT YEAR TO SECOND,
    updated_dt          DATETIME YEAR TO SECOND
);

CREATE UNIQUE INDEX idx_ntfl_folder_unique ON ntfl_folder_config (domain, file_type_code);

ALTER TABLE ntfl_folder_config
    ADD CONSTRAINT FOREIGN KEY (file_type_code) REFERENCES file_type(file_type_code);

-- Downloaded files tracking (for no-delete vendors)
-- Prevents re-downloading files when we can't delete from source
CREATE TABLE ntfl_downloaded_files (
    download_id         SERIAL PRIMARY KEY,
    source_id           INTEGER NOT NULL,
    remote_file_name    VARCHAR(255) NOT NULL,
    remote_file_path    LVARCHAR(512),
    file_size           INTEGER,
    remote_modified_dt  DATETIME YEAR TO SECOND,
    file_hash           VARCHAR(64),  -- MD5 or SHA256 hash
    downloaded_dt       DATETIME YEAR TO SECOND DEFAULT CURRENT YEAR TO SECOND
);

CREATE UNIQUE INDEX idx_ntfl_downloaded_unique ON ntfl_downloaded_files (source_id, remote_file_name, remote_modified_dt);
CREATE INDEX idx_ntfl_downloaded_source ON ntfl_downloaded_files (source_id);

-- File transfer tracking
-- Tracks files through the workflow pipeline
CREATE TABLE ntfl_transfer (
    transfer_id         SERIAL PRIMARY KEY,
    source_id           INTEGER,
    nt_file_num         INTEGER,  -- FK to nt_file after file creation
    file_name           VARCHAR(255) NOT NULL,
    status_id           INTEGER NOT NULL DEFAULT 0,  -- 0=Pending, 1=Downloading, 2=Downloaded, 3=Processing, 4=Processed, 5=Error, 6=Skipped
    source_path         LVARCHAR(512),
    destination_path    LVARCHAR(512),
    current_folder      VARCHAR(32),  -- Transfer, Processing, Processed, Errors, Skipped
    file_size           INTEGER,
    started_dt          DATETIME YEAR TO SECOND,
    completed_dt        DATETIME YEAR TO SECOND,
    error_message       LVARCHAR(512),
    retry_count         INTEGER DEFAULT 0,
    created_by          VARCHAR(32),
    created_dt          DATETIME YEAR TO SECOND DEFAULT CURRENT YEAR TO SECOND
);

CREATE INDEX idx_ntfl_transfer_status ON ntfl_transfer (status_id);
CREATE INDEX idx_ntfl_transfer_file ON ntfl_transfer (nt_file_num);
CREATE INDEX idx_ntfl_transfer_source ON ntfl_transfer (source_id);
CREATE INDEX idx_ntfl_transfer_folder ON ntfl_transfer (current_folder);
CREATE INDEX idx_ntfl_transfer_dt ON ntfl_transfer (created_dt);

-- User activity log
-- Audit trail of all user actions on files
CREATE TABLE ntfl_activity_log (
    activity_id         SERIAL PRIMARY KEY,
    nt_file_num         INTEGER,
    transfer_id         INTEGER,
    file_name           VARCHAR(255),
    activity_type       INTEGER NOT NULL,  -- See FileActivityType enum
    description         LVARCHAR(512),
    details_json        TEXT,  -- JSON for additional context
    user_id             VARCHAR(32) NOT NULL,
    domain              VARCHAR(32) NOT NULL,
    activity_dt         DATETIME YEAR TO SECOND DEFAULT CURRENT YEAR TO SECOND
);

CREATE INDEX idx_ntfl_activity_file ON ntfl_activity_log (nt_file_num);
CREATE INDEX idx_ntfl_activity_transfer ON ntfl_activity_log (transfer_id);
CREATE INDEX idx_ntfl_activity_dt ON ntfl_activity_log (activity_dt);
CREATE INDEX idx_ntfl_activity_user ON ntfl_activity_log (user_id);
CREATE INDEX idx_ntfl_activity_domain ON ntfl_activity_log (domain);

-- ============================================
-- Activity Type Reference
-- ============================================
-- 1  = Downloaded         - File downloaded from remote source
-- 2  = MovedToProcessing  - File moved to Processing folder
-- 3  = ProcessingStarted  - File processing started
-- 4  = ProcessingCompleted - File processing completed successfully
-- 5  = ProcessingFailed   - File processing failed
-- 6  = MovedToSkipped     - File moved to Skipped folder
-- 7  = MovedToErrors      - File moved to Errors folder
-- 8  = MovedToProcessed   - File moved to Processed folder
-- 9  = FileDeleted        - File deleted
-- 10 = FileUnloaded       - Loaded file data unloaded/reversed
-- 11 = SequenceSkipped    - Sequence number skipped
-- 12 = ManualDownload     - File manually triggered for download
-- 13 = BrowserDownload    - File downloaded to user's browser
-- 14 = SourceCreated      - Transfer source configuration created
-- 15 = SourceModified     - Transfer source configuration modified
-- 16 = SourceDeleted      - Transfer source configuration deleted

-- ============================================
-- Transfer Status Reference
-- ============================================
-- 0 = Pending     - Transfer is pending
-- 1 = Downloading - File is being downloaded
-- 2 = Downloaded  - File has been downloaded to Transfer folder
-- 3 = Processing  - File is being processed
-- 4 = Processed   - File has been successfully processed
-- 5 = Error       - Transfer or processing encountered an error
-- 6 = Skipped     - File was skipped

-- ============================================
-- Sample Data for Testing
-- ============================================

-- Example transfer source configuration (source_id is auto-generated SERIAL)
-- INSERT INTO ntfl_transfer_source (
--     domain, file_type_code, protocol, host, port,
--     remote_path, auth_type, username, password_encrypted,
--     file_name_pattern, delete_after_download, cron_schedule, is_enabled
-- ) VALUES (
--     'domain1', 'CDR', 'SFTP', 'ftp.telstra.com.au', 22,
--     '/outgoing/cdr', 'PASSWORD', 'selcomm_user', 'encrypted_password_here',
--     'CDR_*.csv', 'Y', '0 */15 * * * *', 'Y'
-- );

-- Example folder configuration
-- INSERT INTO ntfl_folder_config (
--     domain, file_type_code, transfer_folder, processing_folder,
--     processed_folder, errors_folder, skipped_folder
-- ) VALUES (
--     'domain1', 'CDR',
--     '/data/fileloader/domain1/transfer',
--     '/data/fileloader/domain1/processing',
--     '/data/fileloader/domain1/processed',
--     '/data/fileloader/domain1/errors',
--     '/data/fileloader/domain1/skipped'
-- );

-- Default folder configuration (no specific file type)
-- INSERT INTO ntfl_folder_config (
--     domain, file_type_code, transfer_folder, processing_folder,
--     processed_folder, errors_folder, skipped_folder
-- ) VALUES (
--     'domain1', NULL,
--     '/data/fileloader/domain1/transfer',
--     '/data/fileloader/domain1/processing',
--     '/data/fileloader/domain1/processed',
--     '/data/fileloader/domain1/errors',
--     '/data/fileloader/domain1/skipped'
-- );
