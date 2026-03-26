-- Add prorate_ratio column to ntfl_generic_detail
-- Pro-rate ratio for partial-period charges (e.g., 0.5 for half month, 0.666667 for 20/30 days)
-- NULL means no pro-ration specified (downstream treats as 1.0 = full period)
ALTER TABLE ntfl_generic_detail ADD COLUMN prorate_ratio DECIMAL(10,6) DEFAULT NULL;
