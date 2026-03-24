# UI Plan: AI Charge Map Seeding

## Overview

After AI analysis discovers charge descriptions in a file, the AI can suggest mappings from those descriptions to Selcomm charge codes. Suggestions are stored with reasoning so users can review, accept, reject, or modify them before they become active mappings.

---

## Workflow Summary

```
AI Analysis completes → ChargeType column discovered with sample values
        ↓
   Trigger AI Seeding (POST /ai-review/charge-map-seed/{ftc})
        ↓
   AI examines: file charge descriptions + charge_code.chg_narr + sibling file-type mappings
        ↓
   Suggestions created in ntfl_chg_map (source=AI_SUGGESTED) + reasoning in ntfl_chg_map_ai_reason
        ↓
   User reviews suggestions (GET /ai-review/charge-map-seed/{ftc}/suggestions)
        ↓
   Accept / Reject / Modify each suggestion
        ↓
   Accepted mappings become active (source=AI_ACCEPTED)
```

---

## Database Changes

### ALTER ntfl_chg_map — add `source` column

Distinguishes how the mapping was created.

```sql
-- Informix
ALTER TABLE ntfl_chg_map ADD source VARCHAR(20) DEFAULT 'USER' NOT NULL;

-- PostgreSQL
ALTER TABLE ntfl_chg_map ADD COLUMN source VARCHAR(20) NOT NULL DEFAULT 'USER';
```

| Value | Meaning |
|-------|---------|
| `USER` | Manually created by a user (default, all existing rows) |
| `AI_SUGGESTED` | Created by AI seeding, pending review |
| `AI_ACCEPTED` | AI suggestion reviewed and accepted by a user |

### NEW TABLE: ntfl_chg_map_ai_reason

Child table recording WHY the AI suggested each mapping.

```sql
-- Informix
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
```

```sql
-- PostgreSQL
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
    created_at          TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          VARCHAR(50) NOT NULL DEFAULT SESSION_USER,
    review_status       VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    reviewed_at         TIMESTAMP,
    reviewed_by         VARCHAR(50)
);

CREATE INDEX idx_chg_map_ai_reason_map ON ntfl_chg_map_ai_reason(chg_map_id);
CREATE INDEX idx_chg_map_ai_reason_analysis ON ntfl_chg_map_ai_reason(analysis_id);
```

#### Column Reference

| Column | Type | Description |
|--------|------|-------------|
| `reason_id` | SERIAL PK | Auto-increment ID |
| `chg_map_id` | INTEGER FK | Links to ntfl_chg_map.id |
| `analysis_id` | INTEGER | Links to ntfl_ai_analysis_result (nullable if seeded manually) |
| `file_chg_desc` | VARCHAR(500) | The raw charge description from the file |
| `matched_chg_code` | CHAR(4) | The charge code the AI chose |
| `matched_chg_narr` | VARCHAR(200) | Narrative of the matched charge code (denormalized for display) |
| `confidence` | VARCHAR(10) | `HIGH`, `MEDIUM`, `LOW` |
| `reasoning` | VARCHAR(2000) | AI's explanation of why this mapping was chosen |
| `match_method` | VARCHAR(30) | How the AI found this match (see below) |
| `cross_ref_file_type` | VARCHAR(10) | If matched via cross-reference, which file type provided the pattern |
| `sample_values` | VARCHAR(1000) | Sample charge descriptions from the file that led to this pattern |
| `review_status` | VARCHAR(20) | `PENDING`, `ACCEPTED`, `REJECTED`, `MODIFIED` |
| `reviewed_at` | TIMESTAMP | When the user reviewed this suggestion |
| `reviewed_by` | VARCHAR(50) | Who reviewed it |

#### Match Methods

| Value | Description |
|-------|-------------|
| `NARRATIVE_MATCH` | AI matched file description to charge_code.chg_narr |
| `CROSS_REFERENCE` | AI found a similar mapping in another file type of the same file class |
| `PATTERN_MATCH` | AI inferred the mapping from description patterns/keywords |

---

## API Endpoints

Base: `/api/v4/file-loading/ai-review`

| Method | Route | Request Body | Response | Purpose |
|--------|-------|-------------|----------|---------|
| POST | `/charge-map-seed/{file-type-code}` | `AiChargeMapSeedRequest` | `AiChargeMapSeedResponse` | Trigger AI seeding |
| GET | `/charge-map-seed/{file-type-code}/suggestions` | — | `AiChargeMapSuggestion[]` | List pending suggestions with reasoning |
| POST | `/charge-map-seed/{file-type-code}/review/{chg-map-id}` | `AiChargeMapReviewRequest` | `NtflChgMapRecord` | Accept/reject/modify one suggestion |
| POST | `/charge-map-seed/{file-type-code}/accept-all` | — | `{ "Accepted": int }` | Bulk accept all pending |
| POST | `/charge-map-seed/{file-type-code}/reject-all` | — | `{ "Rejected": int }` | Bulk reject all pending |
| GET | `/charge-map-seed/{file-type-code}/reasons/{chg-map-id}` | — | `ChgMapAiReasonRecord[]` | Get AI reasoning for a specific mapping |

### Existing Endpoints (unchanged, but `Source` field now included in responses)

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/charge-maps/{file-type-code}` | List all charge maps (now includes `Source` field) |
| GET | `/charge-maps/by-id/{id}` | Get single charge map |
| POST | `/charge-maps` | Create charge map (always `Source = "USER"`) |
| PATCH | `/charge-maps/{id}` | Update charge map |
| DELETE | `/charge-maps/{id}` | Delete charge map |
| GET | `/charge-maps/{file-type-code}/resolve?description=...` | Resolve charge description to code |

---

## Request / Response Models

### `AiChargeMapSeedRequest` (POST trigger)

```json
{
  "AnalysisId": 5,
  "UseCrossReference": true
}
```

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `AnalysisId` | int | No | Latest | Analysis result to seed from. If omitted, uses most recent for this file type |
| `UseCrossReference` | bool | No | `true` | Include existing mappings from other file types in same file class as AI context |

### `AiChargeMapSeedResponse` (POST response)

```json
{
  "FileTypeCode": "OPTUS_CHG",
  "SuggestionsCreated": 8,
  "SkippedExisting": 2,
  "Suggestions": [
    {
      "ChgMapId": 42,
      "FileChgDesc": "%Monthly Service%",
      "ChgCode": "MRC",
      "ChgNarr": "Monthly Recurring Charge",
      "Confidence": "HIGH",
      "Reasoning": "File contains charges described as 'Monthly Service Fee' and 'Monthly Service - Data'. The charge_code 'MRC' (Monthly Recurring Charge) is the standard code for recurring monthly charges. This pattern is also used in TELSTRA_CHG file type mappings.",
      "MatchMethod": "NARRATIVE_MATCH"
    },
    {
      "ChgMapId": 43,
      "FileChgDesc": "%Usage%",
      "ChgCode": "USG",
      "ChgNarr": "Usage Charge",
      "Confidence": "MEDIUM",
      "Reasoning": "Multiple charge descriptions contain 'Usage' (e.g., 'Data Usage', 'Voice Usage'). Mapped to 'USG' based on narrative match. Consider splitting into separate patterns for Data vs Voice if different charge codes are needed.",
      "MatchMethod": "PATTERN_MATCH"
    },
    {
      "ChgMapId": 44,
      "FileChgDesc": "%Equipment Lease%",
      "ChgCode": "EQL",
      "ChgNarr": "Equipment Lease",
      "Confidence": "HIGH",
      "Reasoning": "Direct narrative match. CRAYON_CHG file type uses the same mapping for 'Equipment Lease' charges.",
      "MatchMethod": "CROSS_REFERENCE"
    }
  ],
  "Usage": {
    "InputTokens": 2500,
    "OutputTokens": 800,
    "Model": "claude-sonnet-4-20250514"
  }
}
```

### `AiChargeMapSuggestion` (GET suggestions list item)

```json
{
  "ChgMapId": 42,
  "FileChgDesc": "%Monthly Service%",
  "ChgCode": "MRC",
  "ChgNarr": "Monthly Recurring Charge",
  "Confidence": "HIGH",
  "Reasoning": "File contains charges described as 'Monthly Service Fee'...",
  "MatchMethod": "NARRATIVE_MATCH"
}
```

### `AiChargeMapReviewRequest` (POST review)

```json
{
  "Action": "MODIFY",
  "CorrectedChgCode": "MRC2",
  "CorrectedFileChgDesc": "%Monthly Service Fee%"
}
```

| Field | Type | Required | Values | Description |
|-------|------|----------|--------|-------------|
| `Action` | string | Yes | `ACCEPT`, `REJECT`, `MODIFY` | Review action |
| `CorrectedChgCode` | string | Only for MODIFY | Valid charge code | Override the AI-suggested charge code |
| `CorrectedFileChgDesc` | string | Only for MODIFY | LIKE pattern | Override the AI-suggested description pattern |

#### Review Actions

| Action | ntfl_chg_map | ntfl_chg_map_ai_reason |
|--------|-------------|----------------------|
| `ACCEPT` | `source` → `AI_ACCEPTED` | `review_status` → `ACCEPTED` |
| `REJECT` | Row **deleted** | `review_status` → `REJECTED` (kept for audit) |
| `MODIFY` | `chg_code` and/or `file_chg_desc` updated, `source` → `AI_ACCEPTED` | `review_status` → `MODIFIED` |

### `NtflChgMapRecord` (updated — existing model + new field)

```json
{
  "Id": 42,
  "FileTypeCode": "OPTUS_CHG",
  "FileChgDesc": "%Monthly Service%",
  "SeqNo": 10,
  "ChgCode": "MRC",
  "AutoExclude": "N",
  "UseNetPrice": "N",
  "NetPrcProrated": "Y",
  "UpliftPerc": 0.0,
  "UpliftAmt": null,
  "UseNetDesc": "N",
  "Source": "AI_SUGGESTED",
  "LastUpdated": "2026-03-25T14:30:00",
  "UpdatedBy": "admin"
}
```

New field: `Source` — `USER`, `AI_SUGGESTED`, or `AI_ACCEPTED`.

### `ChgMapAiReasonRecord` (GET reasons)

```json
{
  "ReasonId": 1,
  "ChgMapId": 42,
  "AnalysisId": 5,
  "FileChgDesc": "Monthly Service Fee",
  "MatchedChgCode": "MRC",
  "MatchedChgNarr": "Monthly Recurring Charge",
  "Confidence": "HIGH",
  "Reasoning": "File contains charges described as 'Monthly Service Fee' and 'Monthly Service - Data'. The charge_code 'MRC' (Monthly Recurring Charge) is the standard code for recurring monthly charges.",
  "MatchMethod": "NARRATIVE_MATCH",
  "CrossRefFileType": null,
  "SampleValues": "Monthly Service Fee, Monthly Service - Data, Monthly Service - Voice",
  "CreatedAt": "2026-03-25T14:30:00",
  "CreatedBy": "admin",
  "ReviewStatus": "PENDING",
  "ReviewedAt": null,
  "ReviewedBy": null
}
```

---

## How the AI Builds Its Suggestions

The seeding endpoint sends the AI three pieces of context:

1. **File charge descriptions** — distinct values from the ChargeType column in the analysis results (e.g., "Monthly Service Fee", "Data Usage 10GB", "Equipment Lease")

2. **Charge code dictionary** — all rows from `charge_code` table with `chg_code` + `chg_narr` (e.g., MRC = "Monthly Recurring Charge", USG = "Usage Charge")

3. **Cross-reference mappings** — existing `ntfl_chg_map` entries from other file types in the same `file_class` (e.g., TELSTRA_CHG maps "%Monthly%" → MRC, CRAYON_CHG maps "%Equipment Lease%" → EQL)

The AI uses these to:
- Match descriptions to charge codes via narrative similarity
- Leverage patterns already established for sibling file types
- Generate LIKE patterns (with `%` wildcards) that will match variations
- Explain its reasoning for each suggestion

---

## UI Requirements

### Location

Add to the **File Type detail page**, in the Charge Mappings section. The AI seeding controls sit above the existing charge map CRUD table.

### Features

#### Seed Trigger
- **"AI Suggest Mappings" button** — enabled when:
  - At least one analysis result exists for this file type
  - The analysis discovered a ChargeType column
- Optional: analysis selector dropdown (if multiple analysis results exist)
- Checkbox: "Include cross-reference from similar file types" (default: checked)
- Loading spinner during AI processing (can take 10-30 seconds)

#### Suggestions Review Panel

After seeding completes (or when pending suggestions exist), show a review panel:

| # | File Description Pattern | Charge Code | Charge Narrative | Confidence | Method | Reasoning | Actions |
|---|------------------------|------------|-----------------|------------|--------|-----------|---------|
| 1 | %Monthly Service% | MRC | Monthly Recurring Charge | `HIGH` | Narrative | _expandable_ | `Accept` `Reject` `Modify` |
| 2 | %Usage% | USG | Usage Charge | `MEDIUM` | Pattern | _expandable_ | `Accept` `Reject` `Modify` |
| 3 | %Equipment Lease% | EQL | Equipment Lease | `HIGH` | Cross-ref | _expandable_ | `Accept` `Reject` `Modify` |

- **Confidence badges**: HIGH = green, MEDIUM = amber, LOW = red
- **Method badges**: Narrative = blue, Cross-ref = purple, Pattern = grey
- **Reasoning**: Expandable/collapsible per row — shows the AI's full explanation
- **Cross-ref indicator**: If method = Cross-ref, show which file type the pattern came from
- **Bulk actions**: "Accept All" and "Reject All" buttons above the table
- **Inline modify**: Clicking "Modify" makes the charge code and description pattern editable inline

#### Existing Charge Map Table

The existing charge map CRUD table (below the suggestions panel) now includes a `Source` column:

| # | Description Pattern | Seq | Charge Code | Source | Actions |
|---|-------------------|-----|-------------|--------|---------|
| 1 | %Monthly Service% | 10 | MRC | `AI_ACCEPTED` | Edit, Delete |
| 2 | %International Call% | 20 | IDD | `USER` | Edit, Delete |

- **Source badges**: USER = default/grey, AI_SUGGESTED = amber/pending, AI_ACCEPTED = green
- Filter dropdown: All / User-created / AI-suggested / AI-accepted

### UX Flow

```
1. User navigates to File Type → Charge Mappings tab
        ↓
2. If no suggestions pending: shows "AI Suggest Mappings" button + existing mappings table
        ↓
3. User clicks "AI Suggest Mappings"
        ↓
4. Loading spinner (10-30 seconds)
        ↓
5. Suggestions panel appears with AI suggestions
        ↓
6. User reviews each suggestion:
   - Accept → mapping becomes active (source=AI_ACCEPTED), moves to main table
   - Reject → mapping deleted, suggestion greyed out with "Rejected" badge
   - Modify → inline edit → save → becomes active (source=AI_ACCEPTED)
        ↓
7. Or use "Accept All" / "Reject All" for bulk review
        ↓
8. Once all reviewed, suggestions panel shows "All suggestions reviewed"
```

### Error States

| Code | Endpoint | Meaning | UI Action |
|------|----------|---------|-----------|
| 400 | seed | No ChargeType column in analysis | "Analysis did not discover a ChargeType column. Run analysis first." |
| 404 | seed | File type or analysis not found | "File type not found" or "No analysis results available" |
| 404 | review | Charge map record not found | "Suggestion no longer exists" |
| 409 | review | Already reviewed | "This suggestion has already been reviewed" |
| 502 | seed | AI gateway error | "AI service unavailable. Please try again later." |

---

## Status Codes

| Code | Meaning |
|------|---------|
| 200 | Success (GET, review, bulk actions) |
| 201 | Suggestions created (POST seed) |
| 400 | Validation error |
| 401 | Unauthorized |
| 404 | Resource not found |
| 409 | Conflict (already reviewed) |
| 429 | AI rate limit exceeded |
| 500 | Internal server error |
| 502 | AI gateway error |
