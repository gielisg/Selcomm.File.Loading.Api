-- Add mapping-related columns to file_type table
-- auto_create_service: FK to srvctype - auto-create a service of this type if no mapping match
-- map_to_account: Y/N - charge directly to the account instead of a service

ALTER TABLE file_type ADD COLUMN auto_create_service CHAR(4);
ALTER TABLE file_type ADD COLUMN map_to_account CHAR(1) NOT NULL DEFAULT 'N';

ALTER TABLE file_type
    ADD CONSTRAINT fk_file_type_srvctype
    FOREIGN KEY (auto_create_service) REFERENCES srvctype(srvctypecode);
