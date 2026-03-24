# UI Plan: Parser Configuration & Custom Staging Tables

## Overview

After AI analysis discovers a file-type's structure, the user configures the generic parser and optionally creates a versioned custom staging table. This document covers the two workstreams:

1. **Parser Configuration UI** — save/edit the parser config derived from AI analysis
2. **Custom Staging Table UI** — propose, create, test-load, and version custom tables

---

## Workflow Summary

```
AI Analysis completes → SuggestedParserConfig returned
        ↓
   Save Parser Config (POST /parsers)
        ↓
   Review / Edit Config (GET + PATCH /parsers/{ftc})
        ↓
   [Optional] Propose Custom Table (POST /parsers/{ftc}/custom-table/propose)
        ↓
   [Optional] Create Custom Table (POST /parsers/{ftc}/custom-table)
        ↓
   [Optional] Test Load File (POST /parsers/{ftc}/custom-table/test-load)
        ↓
   Production ready
```

---

## 1. Parser Configuration

### API Endpoints

Base: `/api/v4/file-loading`

| Method | Route | Request Body | Response | Purpose |
|--------|-------|-------------|----------|---------|
| GET | `/parsers` | — | `GenericFileFormatConfig[]` | List all parser configs |
| GET | `/parsers?active=true` | — | `GenericFileFormatConfig[]` | List active configs only |
| GET | `/parsers/{file-type-code}` | — | `GenericFileFormatConfig` | Get config with column mappings |
| POST | `/parsers` | `GenericParserConfigRequest` | `GenericFileFormatConfig` (201) | Create/upsert parser config |
| PATCH | `/parsers/{file-type-code}` | `GenericParserConfigRequest` | `GenericFileFormatConfig` | Update parser config |
| DELETE | `/parsers/{file-type-code}` | — | 200 OK | Delete config + mappings |

### Request Model: `GenericParserConfigRequest`

```json
{
  "FileTypeCode": "OPTUS_CHG",
  "FileFormat": "CSV",
  "Delimiter": ",",
  "HasHeaderRow": true,
  "SkipRowsTop": 0,
  "SkipRowsBottom": 0,
  "RowIdMode": "Position",
  "RowIdColumn": 0,
  "HeaderIndicator": null,
  "TrailerIndicator": null,
  "DetailIndicator": null,
  "SkipIndicator": null,
  "TotalColumnIndex": null,
  "TotalType": null,
  "SheetName": null,
  "SheetIndex": 0,
  "DateFormat": "yyyy-MM-dd",
  "CustomSpName": null,
  "Active": true,
  "ColumnMappings": [
    {
      "ColumnIndex": 0,
      "SourceColumnName": "ResellerName",
      "TargetField": "AccountCode",
      "DataType": "String",
      "DateFormat": null,
      "IsRequired": true,
      "DefaultValue": null,
      "RegexPattern": null,
      "MaxLength": 64
    },
    {
      "ColumnIndex": 5,
      "SourceColumnName": "SubTotal",
      "TargetField": "CostAmount",
      "DataType": "Decimal",
      "DateFormat": null,
      "IsRequired": true,
      "DefaultValue": null,
      "RegexPattern": null,
      "MaxLength": null
    }
  ]
}
```

### Field Reference

#### Parser Config Fields

| Field | Type | Required | Values | Notes |
|-------|------|----------|--------|-------|
| `FileTypeCode` | string | Yes | — | Must match an existing file type |
| `FileFormat` | string | Yes | `CSV`, `XLSX`, `Delimited` | — |
| `Delimiter` | string | No | e.g. `,`, `\t`, `|` | Default: comma. Required for CSV/Delimited |
| `HasHeaderRow` | bool | Yes | — | Whether first data row is headers |
| `SkipRowsTop` | int | No | — | Rows to skip at top of file |
| `SkipRowsBottom` | int | No | — | Rows to skip at bottom of file |
| `RowIdMode` | string | Yes | `Position`, `Indicator`, `Pattern` | How to identify row types |
| `RowIdColumn` | int | No | — | Column index for indicator/pattern matching |
| `HeaderIndicator` | string | No | — | Value in RowIdColumn that marks header rows |
| `TrailerIndicator` | string | No | — | Value in RowIdColumn that marks trailer rows |
| `DetailIndicator` | string | No | — | Value in RowIdColumn that marks detail rows |
| `SkipIndicator` | string | No | — | Value in RowIdColumn that marks rows to skip |
| `TotalColumnIndex` | int | No | — | Column containing record total for validation |
| `TotalType` | string | No | — | Type of total (count, sum, etc.) |
| `SheetName` | string | No | — | Excel sheet name (XLSX only) |
| `SheetIndex` | int | No | — | Excel sheet index, 0-based (XLSX only) |
| `DateFormat` | string | No | e.g. `yyyy-MM-dd` | Default date format for date columns |
| `CustomSpName` | string | No | — | Custom stored procedure for post-processing |
| `Active` | bool | Yes | — | Whether this config is active |

#### Column Mapping Fields

| Field | Type | Required | Values | Notes |
|-------|------|----------|--------|-------|
| `ColumnIndex` | int | Yes | 0-based | Position of column in source file |
| `SourceColumnName` | string | No | — | Header name from source file (for display) |
| `TargetField` | string | Yes | See enum below | Which field this column maps to |
| `DataType` | string | Yes | `String`, `Int`, `Decimal`, `Date`, `DateTime` | Data type for parsing/validation |
| `DateFormat` | string | No | e.g. `dd/MM/yyyy` | Overrides config-level DateFormat |
| `IsRequired` | bool | No | — | Fail row if value is empty |
| `DefaultValue` | string | No | — | Value to use when source is empty |
| `RegexPattern` | string | No | — | Validation regex |
| `MaxLength` | int | No | — | Maximum string length |

#### `TargetField` Values (GenericTargetField enum)

**Well-known billing fields:**

| Value | Description |
|-------|-------------|
| `AccountCode` | Customer/account identifier |
| `ServiceId` | Service or subscription identifier |
| `ChargeType` | Charge type/description from vendor |
| `CostAmount` | Cost/charge amount |
| `TaxAmount` | Tax amount |
| `Quantity` | Quantity / units |
| `UOM` | Unit of measure |
| `FromDate` | Period start date |
| `ToDate` | Period end date |
| `Description` | Line item description |
| `ExternalRef` | External reference number |

**Generic overflow columns** (for vendor-specific data):

`Generic01` through `Generic20` — 20 additional VARCHAR columns for data that doesn't map to a well-known field.

### Response Model: `GenericFileFormatConfig`

Same fields as the request, plus:
- `CreatedBy`: string — user who created the config
- `UpdatedBy`: string — user who last updated
- `ColumnMappings`: `GenericColumnMapping[]` — includes `FileTypeCode` on each mapping

### Translating AI Analysis → Parser Config

When the user clicks "Apply Configuration" from the AI analysis results, the UI should:

1. Take the `SuggestedParserConfig` from `AiFileAnalysisResponse`
2. Map fields directly:
   - `SuggestedParserConfig.FileFormat` → `GenericParserConfigRequest.FileFormat`
   - `SuggestedParserConfig.Delimiter` → `GenericParserConfigRequest.Delimiter`
   - `SuggestedParserConfig.HasHeaderRow` → `GenericParserConfigRequest.HasHeaderRow`
   - `SuggestedParserConfig.SkipRowsTop` → `GenericParserConfigRequest.SkipRowsTop`
   - `SuggestedParserConfig.SkipRowsBottom` → `GenericParserConfigRequest.SkipRowsBottom`
   - `SuggestedParserConfig.RowIdMode` → `GenericParserConfigRequest.RowIdMode`
   - `SuggestedParserConfig.DateFormat` → `GenericParserConfigRequest.DateFormat`
3. Map each `SuggestedColumnMapping` → `GenericColumnMappingRequest`:
   - `ColumnIndex` → `ColumnIndex`
   - `SourceColumnName` → `SourceColumnName`
   - `TargetField` → `TargetField` (already uses the same names: AccountCode, CostAmount, etc.)
   - `DataType` → `DataType`
   - `DateFormat` → `DateFormat`
   - `IsRequired` → `IsRequired`
4. Add `FileTypeCode` from the current file type context
5. Set `Active = true`
6. POST to `/parsers`

### UI Requirements

#### Location
Add to the **File Type detail page** — a new "Parser Configuration" tab/section, visible after example files and analysis.

#### Features

##### Parser Config Form
- **Format section**: File format dropdown (CSV/XLSX/Delimited), delimiter input, header row checkbox
- **Row identification section**: RowIdMode dropdown, conditional fields for indicators/patterns
- **Excel section** (visible only when format=XLSX): Sheet name, sheet index
- **Advanced section** (collapsible): Skip rows top/bottom, date format, custom SP name, total column
- **Active toggle**

##### Column Mapping Table
Editable table:

| # | Source Column | Target Field | Data Type | Date Format | Required | Default | Max Length |
|---|-------------|-------------|-----------|-------------|----------|---------|-----------|
| 0 | ResellerName | `[dropdown]` | `[dropdown]` | | `[checkbox]` | | 64 |
| 1 | CustomerName | `[dropdown]` | `[dropdown]` | | `[checkbox]` | | |

- **Target Field dropdown**: All `GenericTargetField` enum values (AccountCode, ServiceId, ChargeType, CostAmount, TaxAmount, Quantity, UOM, FromDate, ToDate, Description, ExternalRef, Generic01-20)
- **Data Type dropdown**: String, Int, Decimal, Date, DateTime
- **Date Format**: Only enabled when DataType = Date or DateTime
- Add/remove column mapping rows
- Drag to reorder (ColumnIndex updates automatically)

##### Actions
- **Save** → POST `/parsers` (new) or PATCH `/parsers/{file-type-code}` (existing)
- **Delete** → DELETE `/parsers/{file-type-code}` with confirmation dialog
- **Apply from Analysis** → Pre-fill form from `SuggestedParserConfig` (see translation above)

#### UX Flow
1. User completes AI analysis → clicks "Apply Configuration" in analysis results
2. Parser config form pre-fills with suggested values
3. User reviews column mappings, adjusts target fields if needed
4. User clicks "Save Parser Configuration"
5. Success toast: "Parser configuration saved for {fileTypeCode}"
6. Custom Table section becomes available (see below)

#### Error States
- 400: Validation error — show field-level errors from response
- 404: File type not found
- 409: Config already exists (when POST) — offer to update instead

---

## 2. Custom Staging Tables

### API Endpoints

Base: `/api/v4/file-loading`

| Method | Route | Content-Type | Request Body | Response | Purpose |
|--------|-------|-------------|-------------|----------|---------|
| GET | `/parsers/{ftc}/custom-table` | — | — | `CustomTableInfo` | Get all versions |
| POST | `/parsers/{ftc}/custom-table/propose` | — | — | `CustomTableProposal` | Preview DDL (no DB changes) |
| POST | `/parsers/{ftc}/custom-table` | — | — | `CustomTableMetadata` (201) | Create physical table |
| POST | `/parsers/{ftc}/custom-table/new-version` | — | — | `CustomTableMetadata` (201) | Create new version, retire old |
| DELETE | `/parsers/{ftc}/custom-table/{version}` | — | — | 200 OK | Drop table version (must be empty) |
| GET | `/parsers/{ftc}/custom-table/{version}/count` | — | — | `int` | Get live record count |
| POST | `/parsers/{ftc}/custom-table/test-load` | `multipart/form-data` | `file` (IFormFile) | `TestLoadResult` (201) | Test load a file |
| DELETE | `/parsers/{ftc}/custom-table/test-load/{nt-file-num}` | — | — | 200 OK | Delete test load data |

> `{ftc}` = `{file-type-code}`

### Response Models

#### `CustomTableInfo` (GET all versions)

```json
{
  "FileTypeCode": "OPTUS_CHG",
  "ActiveVersion": {
    "CustomTableId": 1,
    "FileTypeCode": "OPTUS_CHG",
    "TableName": "ntfl_optus_chg_v1",
    "Version": 1,
    "Status": "ACTIVE",
    "ColumnCount": 8,
    "ColumnDefinition": "[{\"ColumnName\":\"account_code\",\"SqlType\":\"VARCHAR(64)\",\"SourceField\":\"AccountCode\",\"DataType\":\"String\"}]",
    "CreatedDt": "2026-03-23T10:30:00",
    "CreatedBy": "admin",
    "DroppedDt": null
  },
  "AllVersions": [
    { "Version": 1, "Status": "ACTIVE", "..." : "..." }
  ]
}
```

#### `CustomTableProposal` (POST propose)

```json
{
  "FileTypeCode": "OPTUS_CHG",
  "TableName": "ntfl_optus_chg_v1",
  "ProposedVersion": 1,
  "Ddl": "CREATE TABLE ntfl_optus_chg_v1 (\n  nt_file_num INTEGER NOT NULL,\n  nt_file_rec_num INTEGER NOT NULL,\n  account_code VARCHAR(64),\n  service_id VARCHAR(64),\n  charge_type VARCHAR(128),\n  cost_amount DECIMAL(18,6),\n  from_date DATE,\n  description VARCHAR(256),\n  reseller_name VARCHAR(128),\n  billable_ratio DECIMAL(18,6),\n  status_id INTEGER NOT NULL DEFAULT 0,\n  PRIMARY KEY (nt_file_num, nt_file_rec_num)\n)",
  "Columns": [
    {
      "ColumnName": "account_code",
      "SqlType": "VARCHAR(64)",
      "IsRequired": true,
      "SourceField": "AccountCode",
      "DataType": "String"
    },
    {
      "ColumnName": "cost_amount",
      "SqlType": "DECIMAL(18,6)",
      "IsRequired": true,
      "SourceField": "CostAmount",
      "DataType": "Decimal"
    },
    {
      "ColumnName": "reseller_name",
      "SqlType": "VARCHAR(128)",
      "IsRequired": false,
      "SourceField": "Generic01",
      "DataType": "String"
    }
  ]
}
```

#### `CustomTableMetadata` (POST create / new-version)

```json
{
  "CustomTableId": 1,
  "FileTypeCode": "OPTUS_CHG",
  "TableName": "ntfl_optus_chg_v1",
  "Version": 1,
  "Status": "ACTIVE",
  "ColumnCount": 8,
  "ColumnDefinition": "[...]",
  "CreatedDt": "2026-03-23T10:30:00",
  "CreatedBy": "admin",
  "DroppedDt": null
}
```

#### `TestLoadResult` (POST test-load)

```json
{
  "NtFileNum": 12345,
  "RecordsLoaded": 150,
  "RecordsFailed": 2,
  "Errors": [
    "Row 45: Invalid date format in column 'from_date'",
    "Row 112: Required field 'account_code' is empty"
  ]
}
```

### Table Naming Convention

Tables are named automatically: `ntfl_{file_type_code_lowercase}_v{version}`

Examples:
- `OPTUS_CHG` v1 → `ntfl_optus_chg_v1`
- `OPTUS_CHG` v2 → `ntfl_optus_chg_v2`
- `CRAYON_SUB` v1 → `ntfl_crayon_sub_v1`

### Column Derivation

Custom table columns are derived from the parser config's column mappings:

| Parser TargetField | Table Column Name | SQL Type |
|-------------------|-------------------|----------|
| `AccountCode` | `account_code` | `VARCHAR(64)` |
| `CostAmount` | `cost_amount` | `DECIMAL(18,6)` |
| `FromDate` | `from_date` | `DATE` |
| `Generic01` | Uses `SourceColumnName` snake_cased, e.g. `reseller_name` | Based on `DataType` |

Every custom table always includes these system columns:
- `nt_file_num` INTEGER NOT NULL (PK)
- `nt_file_rec_num` INTEGER NOT NULL (PK)
- `status_id` INTEGER NOT NULL DEFAULT 0

### Version Lifecycle

```
                    ┌──── New column mappings ────┐
                    │                             ▼
  [No table] → v1 ACTIVE  ──────────►  v1 RETIRED + v2 ACTIVE
                                                │
                                         (if v1 empty)
                                                ▼
                                          v1 DROPPED
```

| Status | Meaning | Can delete? |
|--------|---------|------------|
| `ACTIVE` | Current version, receives new file loads | No (create new-version first) |
| `RETIRED` | Superseded by newer version, may still have data | Only if record count = 0 |
| `DROPPED` | Physical table deleted | N/A (already gone) |

### UI Requirements

#### Location
On the **File Type detail page**, as a section below Parser Configuration. Only enabled when a parser config with column mappings exists.

#### Features

##### Table Status Panel
Show current state:
- **No custom table**: "No custom staging table configured. Using generic detail table with Generic01-20 columns."
  - Button: "Create Custom Table"
- **Active table exists**: Show table name, version, column count, record count
  - Badge: `ACTIVE v1` (green)
  - Column list (from `ColumnDefinition` JSON)

##### Propose / Preview Flow
1. User clicks "Create Custom Table" (or "Create New Version" if one exists)
2. UI calls `POST /parsers/{ftc}/custom-table/propose`
3. Display the proposal:
   - Proposed table name and version
   - Column table:
     | Column Name | SQL Type | Required | Source Field | Data Type |
     |------------|----------|----------|-------------|-----------|
     | account_code | VARCHAR(64) | Yes | AccountCode | String |
     | cost_amount | DECIMAL(18,6) | Yes | CostAmount | Decimal |
   - DDL preview in a code block (monospace, read-only)
4. User reviews and clicks "Confirm & Create"
5. UI calls `POST /parsers/{ftc}/custom-table` (or `/new-version`)
6. Success toast: "Custom table ntfl_optus_chg_v1 created"

##### Version History
When multiple versions exist, show a version history list:

| Version | Table Name | Status | Columns | Records | Created | Actions |
|---------|-----------|--------|---------|---------|---------|---------|
| v2 | ntfl_optus_chg_v2 | `ACTIVE` | 10 | 1,250 | 2026-03-25 | — |
| v1 | ntfl_optus_chg_v1 | `RETIRED` | 8 | 0 | 2026-03-20 | `Drop` |

- "Records" column: fetched via `GET /parsers/{ftc}/custom-table/{version}/count`
- "Drop" button: only visible when status = RETIRED and record count = 0
  - Confirmation dialog: "This will permanently drop table ntfl_optus_chg_v1. Continue?"

##### Test Load Panel
Available when an ACTIVE custom table exists:

1. **Upload area**: File picker (drag & drop or browse)
2. Click "Test Load" → uploads file as `multipart/form-data` to `POST /parsers/{ftc}/custom-table/test-load`
3. **Results display**:
   - Success: "Loaded {RecordsLoaded} records into {tableName}" (green)
   - Partial: "Loaded {RecordsLoaded} records, {RecordsFailed} failed" (amber)
   - Error list (if any):
     ```
     Row 45: Invalid date format in column 'from_date'
     Row 112: Required field 'account_code' is empty
     ```
4. **Cleanup button**: "Delete Test Data" → calls `DELETE /parsers/{ftc}/custom-table/test-load/{ntFileNum}`
   - Uses the `NtFileNum` returned from the test load response

#### UX Flow (Full Sequence)

```
1. AI Analysis completes
        ↓
2. "Apply Configuration" → saves parser config
        ↓
3. Parser Config tab shows saved config with column mappings
        ↓
4. Custom Table section appears → "Create Custom Table" button
        ↓
5. Click → shows DDL proposal with column preview
        ↓
6. "Confirm & Create" → table created, status shown
        ↓
7. "Test Load" → upload a sample file
        ↓
8. Results shown → verify data loaded correctly
        ↓
9. "Delete Test Data" → clean up
        ↓
10. File type is ready for production loading
```

#### Error States

| Code | Endpoint | Meaning | UI Action |
|------|----------|---------|-----------|
| 400 | Any | Validation error | Show error message from response |
| 404 | propose/create | Parser config not found | Prompt user to save parser config first |
| 409 | create | Active table already exists | Show "already exists" message, offer "New Version" instead |
| 409 | propose | Active table already exists | Same as above |
| 400 | drop | Table has records | Show "Cannot drop — table contains {n} records" |

---

## 3. API Status Codes (all endpoints)

All endpoints use the standard error response:

```json
{
  "Code": "VALIDATION_ERROR",
  "Message": "FileTypeCode is required",
  "Details": null
}
```

Common status codes:
- **200** — Success
- **201** — Created (POST create/new-version/test-load)
- **204** — No content (GET custom-table when none exist)
- **400** — Validation error
- **401** — Unauthorized
- **404** — Resource not found
- **409** — Conflict (table already exists)
- **500** — Internal server error
