-- ============================================
-- AI Charge Map Reasoning Table (PostgreSQL)
-- Records WHY the AI suggested each charge mapping.
-- Child table of ntfl_chg_map.
-- ============================================

CREATE TABLE ntfl_chg_map_ai_reason (
    reason_id           SERIAL PRIMARY KEY,
    chg_map_id          INTEGER NOT NULL REFERENCES ntfl_chg_map(id),
    analysis_id         INTEGER,
    file_chg_desc       VARCHAR(500) NOT NULL,
    matched_chg_code    CHAR(4) NOT NULL,
    matched_chg_narr    VARCHAR(200),
    confidence          VARCHAR(10) NOT NULL,
    reasoning           VARCHAR(2000) NOT NULL,
    match_method        VARCHAR(30) NOT NULL,
    cross_ref_file_type VARCHAR(10),
    sample_values       VARCHAR(1000),
    created_at          TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          VARCHAR(50) NOT NULL DEFAULT SESSION_USER,
    review_status       VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    reviewed_at         TIMESTAMP WITHOUT TIME ZONE,
    reviewed_by         VARCHAR(50)
);

CREATE INDEX idx_chg_map_ai_reason_map ON ntfl_chg_map_ai_reason(chg_map_id);
CREATE INDEX idx_chg_map_ai_reason_analysis ON ntfl_chg_map_ai_reason(analysis_id);
