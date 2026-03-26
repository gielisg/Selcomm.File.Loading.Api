# Selcomm.File.Loading.Api - Architecture Document

> **Module:** file-loading | **Port:** 5140 | **Framework:** .NET 10.0 | **Database:** Informix (ODBC)

## Table of Contents

1. [Overview](#overview)
2. [High-Level Architecture](#high-level-architecture)
3. [Project Structure](#project-structure)
4. [API Surface](#api-surface)
5. [Core Subsystems](#core-subsystems)
6. [Data Model](#data-model)
7. [Authentication & Security](#authentication--security)
8. [Configuration](#configuration)
9. [Dependencies](#dependencies)
10. [Deployment](#deployment)

---

## Overview

The File Loading API is a V4 REST API that modernises the legacy `ntfileload.4gl` system. It provides:

- **File loading** - Parse and load CDR, CHG, EBL, SVC, and ORD files into the Selcomm database
- **File transfer management** - Automated and on-demand file downloads from SFTP/FTP/FileSystem sources
- **Workflow management** - Track files through Transfer > Processing > Processed/Errors/Skipped folders
- **Validation engine** - Configurable file-level and field-level validation with AI-friendly error reporting
- **Activity auditing** - Full audit trail of all file operations

The API converts a batch-oriented 4GL program into an event-driven, memory-efficient REST service with streaming two-pass file processing.

---

## High-Level Architecture

```
                                    +------------------+
                                    |   Swagger UI     |
                                    |   /swagger       |
                                    +--------+---------+
                                             |
                          +------------------+------------------+
                          |        ASP.NET Core Pipeline        |
                          |  (Auth, CORS, HTTPS, Routing)       |
                          +------------------+------------------+
                                             |
                    +------------------------+------------------------+
                    |                                                 |
         +----------+---------+                          +------------+----------+
         | FileLoaderController|                          | FileManagementController|
         | /api/v4/fileloader  |                          | /api/v4/filemanager     |
         +----------+---------+                          +------------+----------+
                    |                                                 |
         +----------+---------+              +------------------------+----------+
         | IFileLoaderService  |              | IFileManagementService             |
         +----------+---------+              | IFileTransferService               |
                    |                        +------------------------+----------+
                    |                                                 |
         +----------+-----------------------------------------+-------+
         |                    Shared Infrastructure                    |
         |  +----------------+  +------------------+  +-------------+ |
         |  | File Parsers   |  | Validation       |  | Transfer    | |
         |  | (Strategy)     |  | Engine           |  | Clients     | |
         |  +----------------+  +------------------+  +-------------+ |
         |  +----------------+  +------------------+  +-------------+ |
         |  | Error          |  | Compression      |  | Background  | |
         |  | Aggregator     |  | Helper           |  | Worker      | |
         |  +----------------+  +------------------+  +-------------+ |
         +------------------------------+------------------------------+
                                        |
                           +------------+------------+
                           | IFileLoaderRepository    |
                           | (Dapper / ODBC)          |
                           +------------+------------+
                                        |
                           +------------+------------+
                           |  Informix Database       |
                           |  (nt_file, cl_detail,    |
                           |   ntfl_transfer, etc.)   |
                           +--------------------------+
```

---

## Project Structure

```
FileLoading/
 ├── Controllers/
 │   ├── FileLoaderController.cs       # Core file loading endpoints (6 endpoints)
 │   └── FileManagementController.cs   # Transfer/workflow management (22 endpoints)
 │
 ├── Interfaces/
 │   ├── IFileLoaderService.cs         # File loading service contract
 │   ├── IFileManagementService.cs     # File management service contract
 │   └── IFileTransferService.cs       # File transfer service contract
 │
 ├── Services/
 │   ├── FileLoaderService.cs          # Parsing, validation, and DB insertion
 │   ├── FileManagementService.cs      # Workflow operations and dashboard
 │   └── FileTransferService.cs        # Remote file downloads and source config
 │
 ├── Repositories/
 │   ├── IFileLoaderRepository.cs      # Data access contract (50+ methods)
 │   └── FileLoaderRepository.cs       # ODBC/Dapper implementation
 │
 ├── Models/
 │   ├── FileLoaderModels.cs           # Requests, responses, configuration
 │   ├── StagingModels.cs              # Database record models (cl_detail, ntfl_chgdtl)
 │   ├── GenericParserModels.cs        # Generic parser config, column mapping, detail record
 │   ├── TransferModels.cs             # Transfer source, folder, activity models
 │   └── ValidationModels.cs           # Validation rules, results, AI summaries
 │
 ├── Parsers/
 │   ├── BaseFileParser.cs             # Abstract template method base
 │   ├── ChgFileParser.cs              # Charge file parser
 │   ├── EblFileParser.cs              # Equipment/billing parser
 │   ├── OrdFileParser.cs              # Order file parser
 │   ├── SvcFileParser.cs              # Service record parser
 │   ├── GenericFileParser.cs          # Data-driven configurable parser
 │   ├── FileRowReaders.cs            # Row reader abstraction (text/Excel)
 │   └── Cdr/
 │       ├── GenericCdrParser.cs        # Selcomm pipe-delimited CDR
 │       ├── TelstraGsmCdrParser.cs     # Telstra GSM format
 │       ├── TelstraCdmaCdrParser.cs    # Telstra CDMA format
 │       ├── OptusCdrParser.cs          # Optus format
 │       ├── AaptCdrParser.cs           # AAPT format
 │       └── VodafoneCdrParser.cs       # Vodafone format
 │
 ├── Transfer/
 │   ├── ITransferClient.cs            # Protocol-agnostic transfer interface
 │   ├── TransferClientFactory.cs      # Factory for protocol selection
 │   ├── SftpTransferClient.cs         # SSH.NET-based SFTP client
 │   ├── FtpTransferClient.cs          # FluentFTP-based FTP client
 │   ├── FileSystemTransferClient.cs   # Local/network path client
 │   └── CompressionHelper.cs          # GZip and Zip support
 │
 ├── Validation/
 │   ├── ValidationEngine.cs           # Field-level validation logic
 │   ├── IValidationConfigProvider.cs  # Validation config source
 │   ├── ValidationConfigProvider.cs   # Config from appsettings/DB
 │   └── ErrorAggregator.cs            # Smart error summarisation
 │
 ├── Workers/
 │   └── FileTransferWorker.cs         # Hosted service for scheduled downloads
 │
 ├── Data/
 │   └── FileLoaderDbContext.cs         # ODBC database context
 │
 ├── Program.cs                         # Application startup and DI
 └── appsettings.json                   # Module configuration
```

---

## API Surface

### FileLoaderController (`/api/v4/fileloader`)

Core file loading — the modernised replacement for `ntfileload.4gl`.

| Method | Route | Purpose |
|--------|-------|---------|
| `POST` | `/load` | Load a network file by path |
| `POST` | `/upload` | Upload a file via multipart form and load it |
| `GET` | `/files/{nt-file-num}` | Get load status for a file |
| `GET` | `/files` | List loaded files with filtering |
| `GET` | `/file-types` | List supported file type codes |
| `POST` | `/files/{nt-file-num}/reprocess` | Reprocess a previously loaded file |

### FileManagementController (`/api/v4/filemanager`)

File transfer management, workflow, and operational monitoring.

| Method | Route | Purpose |
|--------|-------|---------|
| **Dashboard** | | |
| `GET` | `/dashboard` | Summary counts and status overview |
| **Files** | | |
| `GET` | `/files` | List files with filtering and pagination |
| `GET` | `/files/{transfer-id}` | Get file details by transfer ID |
| `POST` | `/files/{transfer-id}/process` | Trigger processing of a transferred file |
| `POST` | `/files/{transfer-id}/retry` | Retry a failed file |
| `POST` | `/files/{transfer-id}/move` | Move file to a workflow folder |
| `POST` | `/files/{nt-file-num}/unload` | Reverse a file load (delete inserted records) |
| `POST` | `/files/{nt-file-num}/skip-sequence` | Force skip to a sequence number |
| `GET` | `/files/{transfer-id}/download` | Download file content to browser |
| `DELETE` | `/files/{transfer-id}` | Delete a file record |
| **Activity** | | |
| `GET` | `/activity` | Query activity audit log |
| **Validation** | | |
| `GET` | `/files/{nt-file-num}/validation-summary` | AI-friendly validation error summary |
| **Exceptions** | | |
| `GET` | `/exceptions/errors` | List files with processing errors |
| `GET` | `/exceptions/skipped` | List manually skipped files |
| **Transfer Sources** | | |
| `GET` | `/sources` | List configured transfer sources |
| `GET` | `/sources/{source-id}` | Get source configuration |
| `PUT` | `/sources/{source-id}` | Create or update a source |
| `DELETE` | `/sources/{source-id}` | Delete a source |
| `POST` | `/sources/{source-id}/test` | Test connection to a saved source |
| `POST` | `/sources/test` | Test connection with ad-hoc config |
| **Parser Configuration** | | |
| `GET` | `/parsers` | List all generic parser configs |
| `GET` | `/parsers/{file-type-code}` | Get a parser config with column mappings |
| `PUT` | `/parsers/{file-type-code}` | Create or update a parser config |
| `DELETE` | `/parsers/{file-type-code}` | Delete a parser config |
| **Folders** | | |
| `GET` | `/folders` | Get folder workflow configuration |
| `PUT` | `/folders` | Save folder workflow configuration |

---

## Core Subsystems

### 1. File Parsing Engine

**Pattern:** Strategy + Template Method

The parsing subsystem uses an abstract `BaseFileParser` that defines the two-pass streaming workflow, with concrete implementations for each file format.

```
IFileParser (interface)
  └── BaseFileParser (abstract - template method)
        ├── GenericCdrParser          H|D|T pipe-delimited
        ├── TelstraGsmCdrParser       Telstra GSM
        ├── TelstraCdmaCdrParser      Telstra CDMA
        ├── OptusCdrParser            Optus
        ├── AaptCdrParser             AAPT
        ├── VodafoneCdrParser         Vodafone
        ├── ChgFileParser             Charge details
        ├── SvcFileParser             Service records
        ├── OrdFileParser             Order records
        ├── EblFileParser             Equipment/billing
        └── GenericFileParser         Data-driven configurable parser (CSV/XLSX/delimited)
```

**Two-Pass Streaming Processing:**

1. **Pass 1 — Validate:** Stream the file and validate structure (header, trailer, sequence, field constraints) without loading all records into memory
2. **Pass 2 — Insert:** Re-read the file and insert valid records in configurable batches with explicit transaction batching

This approach reduces memory usage from ~280MB to ~10-50MB for a 400K-record file.

**File Record Types:**
- `H` — Header (file metadata, sequence numbers)
- `D` — Detail (data records — CDRs, charges, etc.)
- `T` — Trailer (record counts, cost totals for reconciliation)

### 2. Validation Engine

**Components:**

| Component | Responsibility |
|-----------|----------------|
| `ValidationEngine` | Executes field-level validation rules against parsed records |
| `ValidationConfigProvider` | Loads validation configuration from appsettings or database |
| `ErrorAggregator` | Collects errors, aggregates after threshold, produces AI summaries |

**Validation Levels:**

- **File-level:** Header/trailer presence, sequence contiguity, footer count matching, min/max record counts
- **Field-level:** Type parsing (string, int, long, decimal, datetime, bool), required checks, range constraints, regex patterns, allowed value sets, date boundary checks

**Error Aggregation Strategy:**

1. First 100 errors are stored with full detail (line number, raw data, field name)
2. Beyond threshold, errors are aggregated by `(ErrorCode, FieldName)` with sample line numbers
3. File-level errors always retain full detail
4. Raw data is truncated to 500 characters

**AI-Friendly Output (`ValidationSummaryForAI`):**

```json
{
  "OverallStatus": "File has 47 errors across 3 fields",
  "MainIssues": [
    "23 records have invalid date format in CallStartDate (expected yyyy-MM-dd HH:mm:ss)",
    "18 records have negative values in Cost field"
  ],
  "ErrorCountsByField": { "CallStartDate": 23, "Cost": 18, "Duration": 6 },
  "SuggestedActions": ["Fix date format in source system", "Verify cost calculations"],
  "CanPartiallyProcess": true
}
```

### 3. File Transfer System

**Pattern:** Factory + Strategy

```
ITransferClient (interface)
  ├── SftpTransferClient    (SSH.NET library)
  ├── FtpTransferClient     (FluentFTP library)
  └── FileSystemTransferClient (System.IO)
```

**Transfer Workflow:**

```
Remote Source ──fetch──> Transfer Folder ──process──> Processing Folder
                                                          │
                                              ┌───────────┼───────────┐
                                              ▼           ▼           ▼
                                          Processed    Errors      Skipped
```

**Capabilities:**
- Protocol-agnostic file listing with glob pattern matching
- Skip-pattern support (ignore files matching pattern)
- Duplicate detection via `ntfl_downloaded_files` table (prevents re-downloading from sources where deletion isn't possible)
- Optional compression on archive (GZip or Zip)
- Connection testing (both saved and ad-hoc configurations)

### 4. Background Worker (`FileTransferWorker`)

A `BackgroundService` that runs on a 1-minute tick:

1. Queries all enabled transfer sources
2. Evaluates each source's CRON schedule (via `Cronos` library)
3. When a schedule fires, executes `FetchFilesFromSourceAsync`
4. Creates a system-level security context for automated operations
5. Logs results and recovers from errors without stopping the worker

**CRON Format:** Standard 5-field (minute, hour, day-of-month, month, day-of-week)

### 5. Activity Audit System

Every significant operation is logged to `ntfl_activity_log` with:

| Field | Description |
|-------|-------------|
| `ActivityType` | Enum (Downloaded, ProcessingStarted, ProcessingCompleted, etc.) |
| `NtFileNum` | Associated file number (nullable) |
| `TransferId` | Associated transfer record (nullable) |
| `UserId` | User who performed the action |
| `Domain` | Domain context |
| `Details` | JSON payload with operation-specific data |

**Activity Types (16):**

1. Downloaded, 2. MovedToProcessing, 3. ProcessingStarted, 4. ProcessingCompleted, 5. ProcessingFailed, 6. MovedToSkipped, 7. MovedToErrors, 8. MovedToProcessed, 9. FileDeleted, 10. FileUnloaded, 11. SequenceSkipped, 12. ManualDownload, 13. BrowserDownload, 14. SourceCreated, 15. SourceModified, 16. SourceDeleted

### 6. Generic Configurable Parser

**Purpose:** Support many small vendors/networks that send files as CSV, Excel, or text without requiring a new parser class and DB table per vendor. Configuration is entirely data-driven — add a vendor by inserting database config rows, not by writing code.

**Architecture:**

```
GenericFileParser (extends BaseFileParser)
    │
    ├── Loads config from: ntfl_file_format_config + ntfl_column_mapping
    ├── Uses:  IFileRowReader abstraction (Strategy pattern)
    │            ├── DelimitedTextRowReader (CSV, pipe, tab, semicolon)
    │            └── ExcelRowReader (ClosedXML - .xlsx files)
    └── Inserts into: ntfl_generic_detail (single staging table)
```

**Database-Driven Configuration:**

- **`ntfl_file_format_config`** — one row per file type: format (CSV/XLSX/Delimited), delimiter, header row flag, skip rows top/bottom, row identification mode, trailer total configuration, optional custom SP
- **`ntfl_column_mapping`** — one row per column per file type: source column index → target field name, data type, validation (required, regex, max length), date format override, default value

**Row Identification Modes:**

| Mode | How it works | Best for |
|------|-------------|----------|
| **Position** | Skip first N rows, skip last N. First non-skipped row is header if `has_header_row=Y`. Trailer detected by indicator pattern. Everything else is detail. | Simple CSV exports with fixed structure |
| **Indicator** | Read value in column `row_id_column`. Compare against header/trailer/skip/detail indicator strings. | Files with a record type column (H/D/T format) |
| **Pattern** | Apply regex from indicator fields against the full raw line. | Complex files where row type is determined by content patterns |

**Standard Target Fields:**
`AccountCode`, `ServiceId`, `ChargeType`, `CostAmount`, `TaxAmount`, `Quantity`, `UOM`, `FromDate`, `ToDate`, `Description`, `ExternalRef` — plus `Generic01`..`Generic20` overflow columns for vendor-specific fields.

**Total Reconciliation:**
Supports both `SUM` (reconcile cost total) and `COUNT` (reconcile record count) against a configurable trailer column.

**Custom Stored Procedure Hook:**
After generic records are inserted, an optional stored procedure (`custom_sp_name`) can be called for complex validation or business logic specific to a vendor.

**Excel Support:**
Uses ClosedXML (MIT licensed) for .xlsx file reading. Supports sheet selection by name or index. Cells are converted to string arrays for uniform processing through the same column mapping pipeline.

---

## Data Model

### Database Tables

#### Legacy Tables (from 4GL system)

| Table | Purpose |
|-------|---------|
| `nt_file` | Master file records (file number, type, status, dates) |
| `nt_file_stat` | File status lookup values |
| `file_type` | File type definitions |
| `cl_detail` | Call detail records (CDR data) |
| `nt_fl_trailer` | File trailer totals (reconciliation) |
| `nt_cl_not_load` | Records that failed to load (with error reason) |

#### V4 Tables (new for this module)

| Table | Purpose |
|-------|---------|
| `ntfl_transfer_source` | Transfer source configurations (SFTP/FTP/FS) |
| `ntfl_transfer` | File transfer tracking (status, folder, timestamps) |
| `ntfl_folder_config` | Folder workflow paths per domain/file-type |
| `ntfl_downloaded_files` | Downloaded file cache (prevents re-downloads) |
| `ntfl_activity_log` | Audit trail for all operations |
| `ntfl_validation_summary` | AI-friendly validation results (JSON) |
| `ntfl_error_log` | Detailed parse/validation errors |
| `ntfl_file_format_config` | Generic parser file format configuration |
| `ntfl_column_mapping` | Generic parser column-to-field mappings |
| `ntfl_generic_detail` | Generic parser staging records (all vendor types) |

### File Status IDs

| ID | Status | Set By | Description |
|----|--------|--------|-------------|
| 1 | Transferred | File Loading | File record created, ready for processing |
| 2 | Validated | File Loading | Pass 1 structure validation passed |
| 3 | Loaded | File Loading | All transactions loaded successfully (all-or-nothing) |
| 4 | Processing Completed | Charges Module | Downstream processing completed |
| 5 | File Discarded | Operator/UI | File rejected or discarded |
| 6 | Validation Error | File Loading | File structure validation failed |
| 7 | Load Error | File Loading | Record insertion failed (all records rolled back) |
| 10 | File Generation In Progress | Other | Output file being generated |
| 11 | File Generation Complete | Other | Output file ready |
| 12 | Response - Some Errors | Other | Response file has partial errors |
| 13 | Response - No Errors | Other | Response file clean |

### Transaction Status (per-record)

| Status | Set By | Description |
|--------|--------|-------------|
| NEW | File Loading | Transaction loaded, awaiting processing |
| PROCESSING | Charges Module | Transaction being processed |
| PROCESSED | Charges Module | Transaction processed successfully |
| AUTO_WRITEOFF | Charges Module | Transaction automatically written off |
| WRITEOFF | Charges Module | Transaction manually written off |
| ERROR | Charges Module | Transaction processing error (details in ntfl_transaction_error) |

### Transfer Status Enum

| Value | Name | Description |
|-------|------|-------------|
| 0 | Pending | Transfer queued |
| 1 | Downloading | File being downloaded from source |
| 2 | Downloaded | File in Transfer folder |
| 3 | Processing | File being parsed and loaded |
| 4 | Processed | Successfully completed |
| 5 | Error | Transfer or processing failed |
| 6 | Skipped | Manually skipped by user |

### Key Data Records

#### ClDetailRecord (CDR)
Maps to `cl_detail` — the primary call/usage detail record. Fields include: InvRef, SpCnRef, SpPlanRef, NumCalled, TarClassCode, ClStartDt, Unit, UnitQuantity, ClDuration, NtCost (ex/tax), retail discount/non-discount amounts, TimebandCode, BpartyDestn, and processing references.

#### NtflChgdtlRecord (Charges)
Maps to `ntfl_chgdtl` — charge detail records. Fields include: PhoneNum, SpCnRef, ChgCode, StartDate/EndDate, CalcAmount/CalcGst, ManAmount/ManGst, CostAmount/CostGst, UnitQuantity, and pricing fields (UseNetPrice, ProrateRatio, UpliftPerc).

#### GenericDetailRecord (Generic)
Maps to `ntfl_generic_detail` — generic vendor records. Standard fields: AccountCode, ServiceId, ChargeType, CostAmount, TaxAmount, Quantity, UOM, FromDate, ToDate, Description, ExternalRef. Plus Generic01..Generic20 overflow columns and RawData for debugging.

#### NtFlTrailerRecord (Trailer)
Maps to `nt_fl_trailer` — file reconciliation totals: earliest/latest call dates, total quantity, total cost, total record count.

### Stored Procedures

| Procedure | Purpose |
|-----------|---------|
| `sp_new_nt_file` | Create a new file record, allocate file number, resolve placeholders |
| `ss_nt_file` | List files with filtering (legacy 4GL query) |
| `sunt_file` | Update file status |

---

## Authentication & Security

### Multi-Scheme Authentication

The API supports two authentication methods via a `MultiAuth` policy scheme:

```
Request
  ├── Has X-API-Key header? ──> ApiKeyAuthenticationHandler
  │                               └── Validates via Authentication API endpoint
  └── Otherwise ──> JwtBearerHandler
                      ├── Symmetric key validation
                      ├── Multiple valid issuers (global + domain-specific)
                      ├── Audience validation
                      └── Token lifetime validation
```

**JWT Configuration:**
- Secret key from shared config (`JwtSettings:SecretKey`)
- Global issuer + per-domain issuers from `DomainJwtSettings` section
- Standard bearer token format: `Authorization: Bearer {token}`

**API Key:**
- Header: `X-API-Key: {key}`
- Validated via `DbContextApiKeyAuthentication` (database-aware)
- Supports domain isolation

### Controller Security

All controllers extend `DbControllerBase<FileLoaderDbContext>` which provides:
- `CreateSecurityContext(operationId)` — creates a security context with user, domain, and endpoint info
- Security context is passed to all service methods for authorization and auditing

---

## Configuration

### Configuration Hierarchy

```
1. Shared config (required):
   - Windows: C:\Selcomm\configuration\appsettings.shared.json
   - Linux:   /etc/selcomm/appsettings.shared.json
   - Override: SELCOMM_CONFIG_PATH environment variable

2. Local appsettings.json (optional, module-specific overrides)

3. Environment-specific appsettings.{Environment}.json (optional)

4. Environment variables (highest priority)
```

### FileLoaderOptions (Batch Configuration)

Hierarchical configuration keyed by `(domain, fileType)`:

```json
{
  "FileLoaderOptions": {
    "Default": {
      "Default": {
        "BatchSize": 1000,
        "TransactionBatchSize": 1000,
        "UseStreamingMode": true
      },
      "CDR": { "BatchSize": 5000 },
      "CHG": { "BatchSize": 2000 }
    },
    "domain1": {
      "CDR": { "BatchSize": 10000 }
    }
  }
}
```

**Resolution order:** domain+fileType > domain+Default > Default+fileType > Default+Default > built-in defaults

### Key Settings (from shared config)

| Setting | Source | Description |
|---------|--------|-------------|
| `ConnectionStrings:Selcomm` | Shared | Informix ODBC connection string |
| `JwtSettings:SecretKey` | Shared | JWT signing key |
| `JwtSettings:Issuer` | Shared | JWT issuer |
| `JwtSettings:Audience` | Shared | JWT audience |
| `ApiKeySettings:ValidationEndpoint` | Shared | API key validation URL |
| `DomainJwtSettings:{domain}:Issuer` | Shared | Per-domain JWT issuers |

### Logging (Serilog)

- **Console sink** — structured output `[HH:mm:ss LVL] Message`
- **File sink** — daily rolling at `logs/fileloading-{date}.log`
- **Enrichment** — Application name ("FileLoading"), LogContext properties

---

## Dependencies

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.0 | JWT bearer authentication |
| Microsoft.IdentityModel.Tokens | 8.4.0 | Token validation |
| System.IdentityModel.Tokens.Jwt | 8.4.0 | JWT token handling |
| System.Data.Odbc | 10.0.0 | ODBC database connectivity |
| Swashbuckle.AspNetCore | 7.2.0 | Swagger/OpenAPI generation |
| Swashbuckle.AspNetCore.Annotations | 7.2.0 | Swagger annotations |
| Serilog | 4.2.0 | Structured logging |
| Serilog.AspNetCore | 8.0.3 | ASP.NET Core integration |
| Serilog.Sinks.Async | 2.1.0 | Async log writing |
| Serilog.Sinks.Console | 6.0.0 | Console output |
| Serilog.Sinks.File | 6.0.0 | File output with rolling |
| SSH.NET | 2024.1.0 | SFTP client |
| FluentFTP | 50.0.1 | FTP client |
| SharpZipLib | 1.4.2 | GZip and Zip compression |
| Cronos | 0.8.4 | CRON expression parsing |
| ClosedXML | 0.104.1 | Excel (.xlsx) file reading for generic parser |

### Project References

| Project | Purpose |
|---------|---------|
| `Selcomm.Data.Common` | OdbcDbContext, DbControllerBase, DataResult, stored procedure execution, batch operations |
| `Selcomm.Authentication.Common` | ApiKeyAuthenticationHandler, AddApiKeyAuthentication, AddDbContextApiKeyAuthentication extensions |

---

## Deployment

### Port Assignment

This module is assigned **port 5140** in the standard port allocation.

### Deployment Options

| Method | Use Case |
|--------|----------|
| **Linux Systemd** | Production deployment on `10.1.20.55` (LinWebProd0) |
| **Docker** | Containerised deployment |
| **Local .NET** | Development (`dotnet run --urls=http://localhost:5140`) |

### Linux Production Deployment

- **Installation path:** `/var/www/api/v4/file_loading_api/`
- **Service name:** `file_loading_api`
- **Runs as:** `weblocal:webusers`
- **Port:** Configured via `ASPNETCORE_URLS=http://0.0.0.0:5140`
- **Shared config:** `/etc/selcomm/appsettings.shared.json`
- **Logs:** `/var/log/file-loading-api/` (journald) + `/var/www/api/v4/file_loading_api/logs/` (Serilog)

### Middleware Pipeline Order

```
1. Swagger (all environments)
2. HTTPS Redirection
3. CORS (AllowAny — configurable)
4. Authentication
5. Authorization
6. Controller routing
```

### JSON Serialisation

- Null values omitted (`WhenWritingNull`)
- Enums serialised as strings (`JsonStringEnumConverter`)
- Property naming: PascalCase (no camelCase policy)

---

## Design Patterns Summary

| Pattern | Where Used |
|---------|------------|
| **Repository** | `IFileLoaderRepository` / `FileLoaderRepository` abstracts all database access |
| **Strategy** | `IFileParser` implementations for each file format; `ITransferClient` for each protocol; `IFileRowReader` for text vs Excel row reading |
| **Template Method** | `BaseFileParser` defines the two-pass streaming workflow |
| **Factory** | `TransferClientFactory` selects protocol-specific client |
| **Dependency Injection** | All services, parsers, transfer clients registered in DI container |
| **Options Pattern** | `FileLoaderOptionsRoot` with hierarchical domain/fileType resolution |
| **Hosted Service** | `FileTransferWorker` as `BackgroundService` for scheduled transfers |
| **DataResult<T>** | Consistent response wrapping with status code, data, and error info |

---

## Key Architectural Decisions

### 1. Two-Pass Streaming over In-Memory Loading
**Decision:** Parse files in two passes (validate, then insert) instead of loading all records into memory.
**Rationale:** A 400K-record CDR file would consume ~280MB in memory. Streaming reduces this to ~10-50MB while still providing comprehensive validation before any database writes.

### 2. Configurable Batch Sizes per Domain/FileType
**Decision:** Allow batch sizes and streaming mode to be configured hierarchically by domain and file type.
**Rationale:** Different vendors produce files of vastly different sizes. Telstra CDR files may have 400K+ records while charge files may have 50. One-size-fits-all batching wastes resources or risks timeouts.

### 3. AI-Friendly Validation Summaries
**Decision:** Generate plain-English validation summaries alongside machine-readable error data.
**Rationale:** Enables AI agents and LLM-powered support tools to understand and explain file loading failures to users without parsing raw error codes.

### 4. Non-Breaking Error Collection
**Decision:** Collect all errors during parsing rather than failing on the first error.
**Rationale:** Operators need to see the full scope of issues in a file to determine whether to fix and retry, partially process, or reject. Early termination hides the true error count.

### 5. Folder-Based Workflow over State Machine
**Decision:** Use physical folder locations (Transfer, Processing, Processed, Errors, Skipped) to represent file lifecycle state.
**Rationale:** Makes the system state visible and debuggable via the filesystem. Operators can inspect, move, or recover files using standard file tools without database changes.
