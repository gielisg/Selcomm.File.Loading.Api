-- Add source tracking column to ntfl_chg_map
-- Values: USER (manual), AI_SUGGESTED (pending review), AI_ACCEPTED (reviewed)
ALTER TABLE ntfl_chg_map ADD source VARCHAR(20) DEFAULT 'USER' NOT NULL;
