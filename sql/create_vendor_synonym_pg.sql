-- ============================================
-- Create vendor view for networks table
-- PostgreSQL (no synonym support — use a view)
-- ============================================
CREATE OR REPLACE VIEW vendors AS SELECT * FROM networks;
