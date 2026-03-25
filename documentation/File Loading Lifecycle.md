# File Loading Lifecycle

## Overview

The File Loading API processes files through a three-stage pipeline:

1. **Acquisition** — files arrive via SFTP transfer, direct upload, or manual trigger
2. **Parsing & Validation** — two-pass streaming approach (validate then insert)
3. **Database Persistence** — batch inserts with transaction batching into staging tables

---

## Entry Points

| Method | Endpoint / Trigger | Description |
|--------|-------------------|-------------|
| Background Worker | `FileTransferWorker` | Polls SFTP/FTP sources on CRON schedule |
| Process Transfer | `POST /manager/files/{transferId}/process` | User triggers processing of a transferred file |
| Direct Upload | `POST /upload` | Upload and immediately load a file |
| Direct Load | `POST /load` | Load a file already on the server |
| Test Load | `POST /parsers/{code}/custom-table/test-load` | Test a file against a custom staging table |

---

## File Status Lifecycle

```
┌──────────────────┐
│  1 - Transferred │  ← CreateNtFileAsync (sp_file_loading_nt_file_api)
└────────┬─────────┘
         │
         ▼
   ┌─────────────┐
   │  Validate   │  ← Pass 1: ValidateFileStreamingAsync
   │   (Pass 1)  │
   └──────┬──────┘
          │
     ┌────┴────┐
     │         │
  VALID     INVALID
     │         │
     ▼         ▼
┌──────────────┐  ┌──────────────────────┐
│ 2 - Validated│  │ 6 - Validation Error │  → auto-move to Errors folder
└──────┬───────┘  └──────────────────────┘
       │
       ▼
  ┌──────────┐
  │  Insert  │  ← Pass 2: streaming batch insert
  │ (Pass 2) │
  └────┬─────┘
       │
   ┌───┴───┐
   │       │
  ALL    ANY FAIL
  OK       │
   │       ▼
   │  ┌─────────────────┐
   │  │ 7 - Load Error  │  → rollback all records, auto-move to Errors folder
   │  └─────────────────┘
   ▼
┌──────────────┐
│ 3 - Loaded   │  ← All records loaded with status = NEW
└──────┬───────┘
       │
       ▼  (Charges Module takes over)
┌──────────────────────┐
│ 4 - Processing       │
│     Completed        │
└──────────────────────┘
```

**Key rule: All-or-nothing.** Either ALL records in a file are loaded, or the entire file is rejected. No partial loads.

### File Statuses

| StatusId | Name | Set By | Meaning |
|----------|------|--------|---------|
| 1 | Transferred | File Loading | File record created, ready for processing |
| 2 | Validated | File Loading | Pass 1 structure validation passed |
| 3 | Loaded | File Loading | All transactions loaded successfully |
| 4 | Processing Completed | Charges Module | Downstream processing completed |
| 5 | File Discarded | Operator/UI | File manually discarded |
| 6 | Validation Error | File Loading | File structure validation failed |
| 7 | Load Error | File Loading | Record insertion failed (all records rolled back) |
| 10 | File Generation In Progress | Other | Output file being generated |
| 11 | File Generation Complete | Other | Output file generated |
| 12 | Response Some Errors | Other | Downstream response with some errors |
| 13 | Response No Errors | Other | Downstream response with no errors |

### Transaction Statuses

Each loaded record has a `status_id` (VARCHAR) column tracking its processing state:

| Status | Set By | Meaning |
|--------|--------|---------|
| NEW | File Loading | Transaction loaded, awaiting processing |
| PROCESSING | Charges Module | Transaction is being processed |
| PROCESSED | Charges Module | Transaction processed successfully |
| AUTO_WRITEOFF | Charges Module | Transaction automatically written off |
| WRITEOFF | Charges Module | Transaction manually written off |
| ERROR | Charges Module | Transaction processing encountered an error |

Transaction errors are stored in `ntfl_transaction_error` (child table, populated by Charges Module).

---

## Stage 1: File Record Creation

**Method:** `FileLoaderService.LoadFileAsync()`
**SP:** `sp_file_loading_nt_file_api`

1. Validate file exists on disk
2. Call SP to create `nt_file` record with status = 1 (Transferred)
   - SP looks up `file_type_nt` for the next sequence number
   - Substitutes `<SEQUENCE>` and `<YYYYMMDD>` placeholders in filename
   - Inserts into `nt_file` and increments `file_type_nt.last_seq`
3. Returns `nt_file_num` (primary key for this file through the rest of the pipeline)
4. Determine `file_class_code` from `file_type` table (CDR, CHG, or GEN)

**Tables affected:** `nt_file`, `file_type_nt`

---

## Stage 2: Parsing & Validation (Pass 1)

**Method:** `ProcessFileStreamingAsync()` → parser's `ValidateFileStreamingAsync()`

The parser reads the file and validates structure:

- **CDR/CHG parsers (BaseFileParser):** Check header present, no duplicate headers/trailers, trailer record count matches actual count
- **Generic parser (GenericFileParser):** Applies configured skip rows, row identification mode, column mappings, regex validation, required field checks

Returns a `StreamingValidationResult`:
- If invalid → log errors to `ntfl_error_log`, set status = 6 (Validation Error), auto-move file to Errors folder, stop processing
- If valid → set status = 2 (Validated), proceed to Pass 2

**Tables affected:** `ntfl_error_log` (on failure)

---

## Stage 3: Record Insertion (Pass 2)

**Method:** `ProcessFileStreamingAsync()` → parser's `ParseRecordsStreamingAsync()`

Records are streamed from the parser and routed to the appropriate staging table based on `file_class_code`:

### CDR Files (file_class_code = CDR)

**Target table:** `cl_detail`
**Sub-type records:** Optional dual-insert (e.g., `ssswhls_cdr`)

### Charge Files (file_class_code = CHG)

**Target table:** `ntfl_chgdtl`
**Charge columns:** `contact_code`, `sp_cn_ref` (ServiceReference), `chg_code` — loaded as NULL, populated by Charges Module

### Generic Files (file_class_code = GEN)

**Routing decision:**
```
Has active custom table for this file type?
  YES → InsertCustomTableBatchAsync() → custom table (e.g., ntfl_mcr_v6)
  NO  → InsertGenericDetailBatchOptimizedAsync() → ntfl_generic_detail
```

The custom table insert uses `ParsedFields` dictionary from the parser, which holds all field values keyed by `TargetField` name from the column mapping configuration.

### Batch Insert Strategy

Records are accumulated in memory batches (default 1000), then inserted within database transactions:

```
foreach batch of 1000 records:
    BEGIN TRANSACTION
    foreach record in batch:
        INSERT INTO staging_table (...)
    COMMIT
```

This reduces commit overhead from N commits to N/1000 commits.

---

## Stage 4: Finalization

After all records are processed:

1. **Update trailer:** `nt_fl_trailer` with total records, total cost, earliest/latest dates
2. **Store validation summary:** `ntfl_validation_summary` (JSON summary for AI review)
3. **Run custom validation SP:** If configured in parser config (`custom_sp_name`)
4. **Set final status (all-or-nothing):**
   - All records loaded → status = 3 (Loaded), all transaction records have `status_id = 'NEW'`
   - Any record failed → rollback all inserted records, status = 7 (Load Error), auto-move file to Errors folder
5. **Update file process record:** `nt_fl_process` completion timestamp

### Downstream Processing (Charges Module)

For Charge-type files (`file_class_code = CHG` or `GEN`), the Charges Module picks up files with status = 3 (Loaded) and processes individual transactions:

1. Sets transaction `status_id` from `NEW` → `PROCESSING` → `PROCESSED` (or `ERROR`, `WRITEOFF`, `AUTO_WRITEOFF`)
2. Populates `contact_code`, `sp_cn_ref`, `chg_code` on each transaction
3. Logs per-transaction errors to `ntfl_transaction_error`
4. Sets file status to 4 (Processing Completed) when all transactions are done

---

## File Transfer Workflow

When files arrive via SFTP/FTP (separate from the loading pipeline):

```
Transfer Status Flow:
  Pending → Downloading → Downloaded → Processing → Processed
                                      ↓
                                     Error → file moved to Errors folder
```

The `FileTransferWorker` background service:
1. Checks configured sources every 60 seconds
2. Matches CRON schedule on each source
3. Connects via SFTP/FTP, downloads new files
4. Creates transfer records with status tracking
5. Files land in the Downloaded folder, awaiting processing

Processing is triggered separately (user action or automation), which calls `LoadFileAsync()` and follows the pipeline above.

---

## Database Tables Summary

| Table | Purpose |
|-------|---------|
| `nt_file` | Master file record (one per loaded file) |
| `file_type` | File type configuration (type code, class, description) |
| `file_type_nt` | Sequence tracking per file type / customer |
| `nt_fl_trailer` | File totals (record count, cost, date range) |
| `nt_fl_process` | Processing audit trail |
| `cl_detail` | CDR staging records |
| `ntfl_chgdtl` | Charge staging records (includes contact_code, sp_cn_ref, chg_code) |
| `ntfl_generic_detail` | Generic staging records (20 overflow columns + charge columns) |
| `ntfl_custom_table` | Custom table metadata (version, status, DDL) |
| `ntfl_mcr_v*` (etc.) | Custom staging tables (auto-include status_id, contact_code, sp_cn_ref, chg_code) |
| `ntfl_transaction_error` | Per-transaction errors (populated by Charges Module) |
| `nt_cl_not_load` | Failed/rejected records (legacy) |
| `ntfl_error_log` | Parse and validation errors |
| `ntfl_validation_summary` | JSON validation summary for AI review |
| `ntfl_file_format_config` | Generic parser configuration |
| `ntfl_file_format_config_columns` | Column mappings for generic parser |
| `ntfl_transfer_source` | SFTP/FTP source configuration |
| `ntfl_file_transfer` | Transfer tracking records |

---

## Configuration

### Parser Config (Generic Files)

Stored in `ntfl_file_format_config`:
- File format (CSV, XLSX, TEXT)
- Delimiter, header row, skip rows
- Row identification mode (Position, Indicator, Pattern)
- Trailer validation (COUNT, SUM)
- Date format
- Custom validation SP name

### Column Mappings

Stored in `ntfl_file_format_config_columns`:
- Source column index → target field name
- Data type (String, Integer, Decimal, DateTime, GUID)
- Required flag, default value, regex validation, max length

### Batch Options

Configurable per domain and file type in `appsettings.json`:
- `BatchSize` — records held in memory before flushing (default: 1000)
- `TransactionBatchSize` — records per DB transaction (default: 1000)
- `UseStreamingMode` — two-pass streaming vs legacy in-memory (default: true)
