-- ============================================
-- Migration: Add FK on file_type_code to file_type_nt
-- Informix SQL
-- ============================================

-- Add foreign key constraint from ntfl_transfer_source to file_type_nt
ALTER TABLE ntfl_transfer_source ADD CONSTRAINT FOREIGN KEY (file_type_code)
    REFERENCES file_type_nt (file_type_code);
