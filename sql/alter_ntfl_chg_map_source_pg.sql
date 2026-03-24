-- Add source tracking column to ntfl_chg_map
-- Values: USER (manual), AI_SUGGESTED (pending review), AI_ACCEPTED (reviewed)
ALTER TABLE ntfl_chg_map ADD COLUMN source VARCHAR(20) NOT NULL DEFAULT 'USER';
