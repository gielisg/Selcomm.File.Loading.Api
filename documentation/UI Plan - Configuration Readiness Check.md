# UI Plan: Configuration Readiness Check

## Overview

A single endpoint that returns a holistic view of whether a file type is fully configured and ready for production loading. Shows what's configured, what's missing, and a tiered readiness score.

---

## API Endpoint

Base: `/api/v4/file-loading`

| Method | Route | Request Body | Response | Purpose |
|--------|-------|-------------|----------|---------|
| GET | `/readiness/{file-type-code}` | — | `FileTypeReadinessResponse` | Get configuration readiness status |

---

## Response Model: `FileTypeReadinessResponse`

```json
{
  "FileTypeCode": "OPTUS_CHG",
  "FileType": "Optus Wholesale Charges",
  "FileClassCode": "CHG",
  "ReadinessLevel": "PARTIAL",
  "ReadinessScore": 65,
  "Tiers": [
    {
      "Tier": 1,
      "Name": "Core Identity",
      "Status": "READY",
      "Checks": [
        {
          "Item": "File Type Record",
          "IsConfigured": true,
          "IsRequired": true,
          "Detail": "OPTUS_CHG - Optus Wholesale Charges"
        },
        {
          "Item": "File Type NT Record",
          "IsConfigured": true,
          "IsRequired": true,
          "Detail": "Customer 1, Business Unit MAIN"
        }
      ]
    },
    {
      "Tier": 2,
      "Name": "Parser Configuration",
      "Status": "READY",
      "Checks": [
        {
          "Item": "Parser Config",
          "IsConfigured": true,
          "IsRequired": true,
          "Detail": "CSV, comma-delimited, 12 column mappings, active"
        },
        {
          "Item": "Custom Staging Table",
          "IsConfigured": true,
          "IsRequired": false,
          "Detail": "ntfl_optus_chg_v1 (ACTIVE, 12 columns)"
        }
      ]
    },
    {
      "Tier": 3,
      "Name": "Transfer & Folders",
      "Status": "PARTIAL",
      "Checks": [
        {
          "Item": "Transfer Source",
          "IsConfigured": false,
          "IsRequired": true,
          "Detail": "No transfer source configured for this file type"
        },
        {
          "Item": "Folder Configuration",
          "IsConfigured": true,
          "IsRequired": true,
          "Detail": "Using default folder paths"
        }
      ]
    },
    {
      "Tier": 4,
      "Name": "Charge Mappings",
      "Status": "NOT_CONFIGURED",
      "Checks": [
        {
          "Item": "Charge Maps",
          "IsConfigured": false,
          "IsRequired": true,
          "Detail": "No charge mappings configured (required for CHG file class)"
        }
      ]
    },
    {
      "Tier": 5,
      "Name": "AI Configuration",
      "Status": "PARTIAL",
      "Checks": [
        {
          "Item": "Example Files",
          "IsConfigured": true,
          "IsRequired": false,
          "Detail": "2 example files uploaded"
        },
        {
          "Item": "Analysis Result",
          "IsConfigured": true,
          "IsRequired": false,
          "Detail": "Latest analysis: HIGH readiness (2026-03-24)"
        },
        {
          "Item": "Active Prompt",
          "IsConfigured": false,
          "IsRequired": false,
          "Detail": "No active file-type prompt"
        }
      ]
    }
  ],
  "MissingSteps": [
    "Configure a transfer source for automated file delivery (Tier 3)",
    "Add charge mappings to map vendor charges to Selcomm charge codes (Tier 4)"
  ],
  "CompletedSteps": [
    "File type record exists",
    "File type NT record configured",
    "Parser configuration active with 12 column mappings",
    "Custom staging table ntfl_optus_chg_v1 active",
    "Folder configuration available (using defaults)",
    "2 example files uploaded",
    "AI analysis completed (HIGH readiness)"
  ]
}
```

---

## Response Fields

### Top-Level

| Field | Type | Description |
|-------|------|-------------|
| `FileTypeCode` | string | File type code |
| `FileType` | string | File type description |
| `FileClassCode` | string | File class (CHG, CDR, PAY, etc.) |
| `ReadinessLevel` | string | `READY`, `PARTIAL`, `NOT_CONFIGURED` |
| `ReadinessScore` | int | 0-100 percentage of required items configured |
| `Tiers` | `ReadinessTier[]` | Tier-by-tier breakdown |
| `MissingSteps` | `string[]` | Human-readable list of what's still needed |
| `CompletedSteps` | `string[]` | Human-readable list of what's done |

### ReadinessTier

| Field | Type | Description |
|-------|------|-------------|
| `Tier` | int | Tier number (1-5) |
| `Name` | string | Tier name |
| `Status` | string | `READY`, `PARTIAL`, `NOT_CONFIGURED`, `NOT_APPLICABLE` |
| `Checks` | `ReadinessCheck[]` | Individual checks within this tier |

### ReadinessCheck

| Field | Type | Description |
|-------|------|-------------|
| `Item` | string | Configuration item name |
| `IsConfigured` | bool | Whether this item is configured |
| `IsRequired` | bool | Whether this item is required for this file type |
| `Detail` | string | Status detail or reason it's missing |

---

## Readiness Tiers

### Tier 1: Core Identity (always required)

| Check | Source Table | Required | Detail when configured |
|-------|-------------|----------|----------------------|
| File Type Record | `file_type` | Always | File type code + description |
| File Type NT Record | `file_type_nt` | Always | Customer number, business unit |

### Tier 2: Parser Configuration (required for generic file types)

| Check | Source Table | Required | Detail when configured |
|-------|-------------|----------|----------------------|
| Parser Config | `ntfl_file_format_config` | Yes (generic types) | Format, delimiter, column count, active status |
| Custom Staging Table | `ntfl_custom_table` | No (optional) | Table name, version, column count |

### Tier 3: Transfer & Folders (required for automated loading)

| Check | Source Table | Required | Detail when configured |
|-------|-------------|----------|----------------------|
| Transfer Source | `ntfl_transfer_source` | Yes (for automation) | Protocol, host, enabled status |
| Folder Configuration | `ntfl_folder_config` | Yes | Paths or "using defaults" |

### Tier 4: Charge Mappings (conditional on file class)

| Check | Source Table | Required | Detail when configured |
|-------|-------------|----------|----------------------|
| Charge Maps | `ntfl_chg_map` | Yes (CHG class only) | Count of mappings, pending AI suggestions count |

### Tier 5: AI Configuration (always optional)

| Check | Source Table | Required | Detail when configured |
|-------|-------------|----------|----------------------|
| Example Files | `ntfl_ai_example_file` | No | Count of example files |
| Analysis Result | `ntfl_ai_analysis_result` | No | Latest readiness rating + date |
| Active Prompt | `ntfl_ai_file_type_prompt` | No | Version number, source (AI/USER) |

---

## Readiness Scoring

### Score Calculation
- Only **required** items count toward the score
- Items marked `NOT_APPLICABLE` are excluded from the denominator
- Score = (configured required items / total required items) * 100

### ReadinessLevel Rules

| Level | Condition | Colour |
|-------|-----------|--------|
| `READY` | All required items configured (score = 100) | Green |
| `PARTIAL` | Some required items configured (score 1-99) | Amber |
| `NOT_CONFIGURED` | No required items beyond file type record (score = 0 effectively) | Red |

### Conditional Requirements

The "required" flag varies by file type:

| Condition | Effect |
|-----------|--------|
| `file_class_code = 'CHG'` | Charge Maps → required |
| `file_class_code != 'CHG'` | Charge Maps → not required, tier status = NOT_APPLICABLE |
| Generic parser file type | Parser Config → required |
| Custom-coded file type (has comp_dll) | Parser Config → not required, tier status = NOT_APPLICABLE |

---

## UI Requirements

### Location

Two placement options:

1. **File Type detail page** — a "Readiness" badge/panel at the top, always visible
2. **File Type list page** — a readiness column showing the badge for each file type

### Features

#### Readiness Badge (compact)
Shows at the top of the File Type detail page:

```
[PARTIAL - 65%]  2 items need attention
```

- Colour-coded: Green (READY), Amber (PARTIAL), Red (NOT_CONFIGURED)
- Click to expand full readiness panel

#### Readiness Panel (expanded)

Shows the tier-by-tier breakdown as an accordion or checklist:

```
✅ Tier 1: Core Identity                    READY
   ✅ File Type Record — OPTUS_CHG - Optus Wholesale Charges
   ✅ File Type NT Record — Customer 1, Business Unit MAIN

✅ Tier 2: Parser Configuration             READY
   ✅ Parser Config — CSV, comma-delimited, 12 column mappings, active
   ✅ Custom Staging Table — ntfl_optus_chg_v1 (ACTIVE, 12 columns)

⚠️ Tier 3: Transfer & Folders              PARTIAL
   ❌ Transfer Source — No transfer source configured
   ✅ Folder Configuration — Using default folder paths

❌ Tier 4: Charge Mappings                  NOT_CONFIGURED
   ❌ Charge Maps — No charge mappings configured (required for CHG class)

⚠️ Tier 5: AI Configuration (Optional)     PARTIAL
   ✅ Example Files — 2 example files uploaded
   ✅ Analysis Result — HIGH readiness (2026-03-24)
   ❌ Active Prompt — No active file-type prompt
```

- Each check shows ✅/❌ icon + item name + detail text
- Optional items shown with lighter styling
- Missing required items highlighted
- Each missing item could link to the relevant configuration page/tab

#### Missing Steps Call-to-Action

Below the tier breakdown, show actionable next steps:

```
What's needed:
  → Configure a transfer source for automated file delivery
  → Add charge mappings to map vendor charges to Selcomm charge codes
```

Each step could be a clickable link navigating to the relevant section.

#### File Type List Integration (optional)

On the file types list page, add a "Status" column:

| File Type | Class | Vendor | Status |
|-----------|-------|--------|--------|
| OPTUS_CHG | CHG | Optus | `PARTIAL 65%` |
| TELSTRA_CDR | CDR | Telstra | `READY 100%` |
| CRAYON_SUB | CHG | Crayon | `NOT_CONFIGURED 15%` |

This requires calling the readiness endpoint for each file type, or adding a bulk endpoint:

| Method | Route | Response | Purpose |
|--------|-------|----------|---------|
| GET | `/readiness` | `FileTypeReadinessSummary[]` | Bulk readiness for all file types |

Where `FileTypeReadinessSummary` is a lightweight version:

```json
{
  "FileTypeCode": "OPTUS_CHG",
  "FileType": "Optus Wholesale Charges",
  "FileClassCode": "CHG",
  "ReadinessLevel": "PARTIAL",
  "ReadinessScore": 65,
  "MissingCount": 2,
  "TotalRequired": 6
}
```

### UX Flow

```
1. User navigates to File Types list
        ↓
2. Sees readiness status badge per file type
        ↓
3. Clicks into a file type
        ↓
4. Readiness panel at top shows tier breakdown
        ↓
5. Missing steps link to relevant configuration sections
        ↓
6. User completes configuration → readiness updates on next load
```

---

## Error States

| Code | Meaning | UI Action |
|------|---------|-----------|
| 200 | Success | Display readiness panel |
| 401 | Unauthorized | Redirect to login |
| 404 | File type not found | "File type {code} not found" |
| 500 | Internal server error | "Unable to load readiness status" |

---

## Status Codes

| Code | Meaning |
|------|---------|
| 200 | Success |
| 401 | Unauthorized |
| 404 | File type not found |
| 500 | Internal server error |
