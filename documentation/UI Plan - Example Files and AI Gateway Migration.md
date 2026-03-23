# UI Plan: Example Files, AI Analysis, and AI Gateway Migration

## Overview

Three workstreams:
1. **Example File Management UI** — upload, view, delete example files per file-type
2. **AI File Analysis UI** — trigger analysis, review results, accept/edit parser config
3. **AI Config Migration** — move AI config from file-loading API to AI gateway API

---

## 1. Example File Management

### Current API Endpoints (file-loading, port 5140)

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/api/v4/file-loading/ai-review/example-files` | List all example files |
| GET | `/api/v4/file-loading/ai-review/example-files/{file-type-code}` | List example files for a file type |
| POST | `/api/v4/file-loading/ai-review/example-files/{file-type-code}` | Upload example file (multipart/form-data) |
| DELETE | `/api/v4/file-loading/ai-review/example-files/{example-file-id}` | Delete example file |

### UI Requirements

#### Location
Add to the **File Type detail/edit page** — a new "Example Files" tab or section below the file type configuration.

#### Features
- **List view**: Show example files for the current file type (file name, description, uploaded date, uploaded by)
- **Upload**: File picker + optional description field. POST as `multipart/form-data`
- **Delete**: Delete button per row with confirmation dialog
- **Preview**: Optional — show first ~50 lines of the example file content inline (read from server path)
- **Multiple files**: A file type can have multiple example files (e.g. different months of data from the same supplier)

#### UX Flow
1. User navigates to File Types → selects a file type → "Example Files" tab
2. Sees list of uploaded examples (or empty state with upload prompt)
3. Clicks "Upload Example" → file picker opens → selects file → optional description → Submit
4. File appears in list
5. Can delete any example file

---

## 2. AI File Analysis

### Current API Endpoints (file-loading, port 5140)

| Method | Route | Purpose |
|--------|-------|---------|
| POST | `/api/v4/file-loading/ai-review/analyse/{file-type-code}` | Trigger AI analysis of example files |

#### Request Body (optional)
```json
{
  "FileClass": "Charge",        // Charge, Usage, or Payment
  "FocusAreas": ["pricing", "date formats"]
}
```

#### Response: `AiFileAnalysisResponse`
- `IngestionReadiness` — HIGH / MEDIUM / LOW
- `Summary` — prose description
- `DetectedFormat` — file format, delimiter, header/trailer rows
- `Columns[]` — discovered column structure with data types and sample values
- `BillingConceptMappings[]` — which columns map to which billing concepts (with confidence)
- `DataQualityIssues[]` — problems found
- `Observations[]` — key findings
- `SuggestedParserConfig` — ready-to-save parser configuration
- `Usage` — token usage (input/output tokens, model)

### UI Requirements

#### Location
On the **File Type detail page**, next to or below the Example Files section. Only enabled when example files exist.

#### Features

##### Analysis Trigger
- "Analyse Examples" button (disabled if no example files uploaded)
- File class selector: dropdown with Charge / Usage / Payment / Auto-detect
- Optional focus areas: text input or tag picker
- Loading spinner during analysis (can take 15-60 seconds)

##### Results Display — Tabbed or Accordion Layout

**Tab 1: Summary**
- Ingestion readiness badge (colour-coded: GREEN=HIGH, AMBER=MEDIUM, RED=LOW)
- Summary text
- Key observations as bullet list
- Token usage (input/output tokens) — small footer info

**Tab 2: Column Structure**
- Table showing discovered columns:
  | # | Column Name | Data Type | Sample Values | Suggested Mapping | Confidence |
  |---|------------|-----------|---------------|-------------------|------------|
  | 0 | ResellerName | String | Manage Protect Pty Ltd | AccountCode | HIGH |
- Each row should have an editable "Target Field" dropdown (GenericTargetField enum values + Generic01-20)
- Confidence shown as badge (HIGH=green, MEDIUM=amber, LOW=red)

**Tab 3: Billing Concept Mappings**
- Visual mapping: billing concept → source column
  | Billing Concept | Source Column | Column # | Confidence |
  |----------------|---------------|----------|------------|
  | Customer Name | CustomerName | 15 | HIGH |
  | Buy Price | SubTotal | 12 | HIGH |
  | Period Start | ChargeStartDate | 4 | HIGH |

**Tab 4: Data Quality Issues**
- List of issues with severity icons (Critical/Warning/Info)
- Each issue: description, affected field, examples, suggestion
- Same format as the existing AI review issues display

**Tab 5: Suggested Parser Config**
- Read-only preview of the suggested parser configuration
- Shows: file format, delimiter, header row, skip rows, date format
- Column mappings table (editable before applying)
- **"Apply Configuration" button** — saves to the parser config for this file type
  - Could call existing parser config PATCH endpoint
  - Confirmation dialog: "This will update the parser configuration for {fileType}. Continue?"

##### Apply Flow
1. User reviews analysis results
2. Optionally edits column mappings in Tab 2
3. Clicks "Apply Configuration" in Tab 5
4. System saves the `SuggestedParserConfig` via the existing generic parser config endpoints
5. Success toast: "Parser configuration updated for {fileType}"

---

## 3. AI Config Migration — From File-Loading to AI Gateway

### Current State
- AI config (API key, model, limits) is stored per-domain in the **file-loading** database (`ntfl_ai_domain_config`)
- UI calls file-loading endpoints: `GET/PUT/DELETE /api/v4/file-loading/ai-review/config`

### Target State
- AI config moves to the **AI Gateway** (`Selcomm.Ai.Api`, port 5142) in table `ntai_gateway_config`
- All apps share one config per domain — single API key, single rate limit
- Usage tracking is in `ntai_usage_log` (same database)

### New AI Gateway Endpoints (port 5142)

| Method | Route | Purpose |
|--------|-------|---------|
| POST | `/api/v4/ai/completions` | Proxy Claude API (internal, called by other APIs) |
| GET | `/api/v4/ai/usage` | Token usage logs (filterable by date, app, agent) |
| GET | `/api/v4/ai/usage/summary` | Aggregated billing summary with app/agent breakdown |

**Config endpoints to add to the gateway (TODO):**

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/api/v4/ai/config` | Get domain AI config |
| PUT | `/api/v4/ai/config` | Create/update domain AI config |
| DELETE | `/api/v4/ai/config` | Delete domain AI config |
| GET | `/api/v4/ai/config/status` | Check if AI is configured and enabled |

### Migration Steps

#### Phase 1: Add config endpoints to gateway (backend)
- Add `GatewayConfigController` to `Selcomm.Ai.Api` with the 4 config endpoints above
- These read/write `ntai_gateway_config` (already used by `CompletionService`)
- Deploy gateway update

#### Phase 2: Update UI to call gateway for config
- Change the AI settings page to call gateway endpoints (`/api/v4/ai/config`) instead of file-loading endpoints (`/api/v4/file-loading/ai-review/config`)
- Add usage dashboard UI calling `/api/v4/ai/usage/summary`

#### Phase 3: Remove config from file-loading
- Remove `GET/PUT/DELETE /ai-review/config` and `GET /ai-review/config/status` from `AiReviewController`
- Remove `GetDomainConfigAsync`, `SaveDomainConfigAsync`, `DeleteDomainConfigAsync`, `GetConfigStatusAsync` from `AiReviewService`
- Remove `ntfl_ai_domain_config` table
- Remove related repository methods

### UI for Usage Dashboard

#### Location
New section in the AI settings page or a dedicated "AI Usage" page.

#### Features
- **Summary cards**: Total requests, total tokens, estimated cost for current period
- **Period selector**: Last 7 days / 30 days / custom range
- **Breakdown table**: Usage by app/agent
  | App | Agent | Requests | Input Tokens | Output Tokens | Cost (USD) |
  |-----|-------|----------|-------------|--------------|------------|
  | file-loading | file-review | 42 | 125,000 | 35,000 | $0.90 |
  | file-loading | file-analysis | 8 | 45,000 | 22,000 | $0.47 |
- **Usage log**: Expandable table of individual requests (paginated, filterable)

---

## API Routing (nginx)

The AI gateway needs a route in the nginx config on the Linux server:

```nginx
location /api/v4/ai/ {
    proxy_pass http://localhost:5142;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_read_timeout 120s;
}
```

---

## Priority Order

1. **Example File Management UI** — prerequisite for analysis
2. **AI File Analysis UI** — the core feature
3. **AI Config migration to gateway** — cleanup/consolidation
4. **Usage Dashboard** — billing visibility
