-- ============================================
-- Add charge processing columns
-- These columns are loaded as NULL by File Loading
-- and populated by the Charges Module during processing.
-- ============================================

-- ntfl_generic_detail: add all three charge columns
ALTER TABLE ntfl_generic_detail ADD contact_code VARCHAR(20);
ALTER TABLE ntfl_generic_detail ADD sp_cn_ref INTEGER;
ALTER TABLE ntfl_generic_detail ADD chg_code VARCHAR(20);

-- ntfl_chgdtl: already has sp_cn_ref and chg_code, add contact_code only
ALTER TABLE ntfl_chgdtl ADD contact_code VARCHAR(20);
