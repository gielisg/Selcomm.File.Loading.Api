-- ============================================
-- AI Charge Map Reasoning Table
-- Records WHY the AI suggested each charge mapping.
-- Child table of ntfl_chg_map.
-- ============================================

CREATE TABLE ntfl_chg_map_ai_reason (
    reason_id           SERIAL PRIMARY KEY,
    chg_map_id          INTEGER NOT NULL,
    analysis_id         INTEGER,
    file_chg_desc       LVARCHAR(500) NOT NULL,
    matched_chg_code    CHAR(4) NOT NULL,
    matched_chg_narr    VARCHAR(200),
    confidence          VARCHAR(10) NOT NULL,
    reasoning           LVARCHAR(2000) NOT NULL,
    match_method        VARCHAR(30) NOT NULL,
    cross_ref_file_type VARCHAR(10),
    sample_values       LVARCHAR(1000),
    created_at          DATETIME YEAR TO SECOND DEFAULT CURRENT YEAR TO SECOND NOT NULL,
    created_by          VARCHAR(50) DEFAULT USER NOT NULL,
    review_status       VARCHAR(20) DEFAULT 'PENDING' NOT NULL,
    reviewed_at         DATETIME YEAR TO SECOND,
    reviewed_by         VARCHAR(50)
);

CREATE INDEX idx_chg_map_ai_reason_map ON ntfl_chg_map_ai_reason(chg_map_id);
CREATE INDEX idx_chg_map_ai_reason_analysis ON ntfl_chg_map_ai_reason(analysis_id);

ALTER TABLE ntfl_chg_map_ai_reason
    ADD CONSTRAINT FOREIGN KEY (chg_map_id) REFERENCES ntfl_chg_map(id)
    CONSTRAINT fk_chg_map_ai_reason_map;
