# File Loading API - User Manual

## Overview

The File Loading API (v4) provides file loading and processing functionality for the Selcomm platform. It is a modernized replacement for the legacy `ntfileload.4gl` application, offering a RESTful interface for:

- **File Loading** -- Loading and parsing network files (CDR, CHG, EBL, SVC, ORD, and more) into the Selcomm database.
- **File Transfer Management** -- Automated and manual file transfers from SFTP, FTP, and local file system sources.
- **Workflow Pipeline** -- A folder-based workflow that moves files through Transfer, Processing, Processed, Errors, and Skipped stages.
- **Validation** -- Configurable validation engine with AI-friendly error reporting and aggregation.
- **Configuration Management** -- CRUD operations for file types, file classes, vendors, transfer sources, parser configurations, and folder workflows.

**Base URL:** `https://{host}:5140/api/v4/file-loading`

---

## Authentication

All endpoints (except the health check) require authentication. The API supports two authentication methods, and the correct scheme is selected automatically based on the request headers.

### JWT Bearer Token

Obtain a JWT token from the Selcomm Authentication API and include it in the `Authorization` header:

```
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
```

### API Key

Alternatively, use an API key via the `X-API-Key` header:

```
X-API-Key: your-api-key-here
```

### Authentication Examples

```bash
# Using JWT Bearer token
curl -X GET "https://host:5140/api/v4/file-loading/files" \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIs..."

# Using API Key
curl -X GET "https://host:5140/api/v4/file-loading/files" \
  -H "X-API-Key: your-api-key-here"
```

---

## Conventions

### Query Parameters

Query parameters use **camelCase** naming:

| Parameter | Description |
|-----------|-------------|
| `fileType` | File type code filter |
| `fileTypeCode` | File type code filter (alternative name on some endpoints) |
| `maxRecords` | Maximum number of records to return |
| `ntCustNum` | Network customer number filter |
| `ntFileNum` | File number filter |
| `transferId` | Transfer record ID filter |
| `fromDate` | Date range start (inclusive) |
| `toDate` | Date range end (inclusive) |
| `skipTo` | Sequence number to skip to |

### Path Parameters

Path parameters use **kebab-case** naming:

| Parameter | Description |
|-----------|-------------|
| `nt-file-num` | Database record key assigned at file load time |
| `transfer-id` | Transfer workflow tracking key |
| `source-id` | Transfer source configuration ID (auto-generated integer) |
| `file-type-code` | File type code (e.g., `TEL_GSM`, `SSSWHLSCDR`) |
| `file-class-code` | File class code (e.g., `CDR`, `CHG`) |
| `network-id` | Vendor/network ID (CHAR(2)) |

### JSON Format

All JSON request and response bodies use **PascalCase** property names. Null properties are omitted from responses. Enum values are serialized as strings.

---

## Endpoints

### Health Check

#### GET /health-check

Returns API health status and database connectivity. This endpoint does **not** require authentication.

```bash
curl -X GET "https://host:5140/api/v4/file-loading/health-check"
```

**200 OK -- Healthy:**
```json
{
  "Status": "Healthy",
  "Timestamp": "2026-03-20T10:30:00Z",
  "Service": "File Loading API",
  "Version": "4.0.0",
  "Database": {
    "Status": "Connected",
    "ResponseTimeMs": 12
  }
}
```

**503 Service Unavailable -- Unhealthy:**
```json
{
  "Status": "Unhealthy",
  "Timestamp": "2026-03-20T10:30:00Z",
  "Service": "File Loading API",
  "Version": "4.0.0",
  "Database": {
    "Status": "Unreachable",
    "Message": "Connection timeout"
  }
}
```

---

### File Loading

#### POST /load

Load a network file for processing by specifying its path on the server.

```bash
curl -X POST "https://host:5140/api/v4/file-loading/load" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "FileName": "/data/incoming/cdr_20260320.csv",
    "FileType": "TEL_GSM",
    "NtCustNum": "CUST001",
    "FileDate": "2026-03-20",
    "ExpectedSequence": 42
  }'
```

**202 Accepted:**
```json
{
  "NtFileNum": 12345,
  "FileName": "/data/incoming/cdr_20260320.csv",
  "FileType": "TEL_GSM",
  "Status": "Pending",
  "StatusId": 0,
  "RecordsLoaded": 0,
  "RecordsFailed": 0,
  "StartedAt": "2026-03-20T10:30:00Z",
  "CompletedAt": null
}
```

**Request fields:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `FileName` | string | Yes | Full path to the file on the server |
| `FileType` | string | Yes | File type code (from the file_type table) |
| `NtCustNum` | string | No | Network customer number |
| `FileDate` | datetime | No | File date (defaults to today) |
| `ExpectedSequence` | int | No | Expected sequence number for validation |

---

#### POST /upload

Upload a file directly via multipart form data and load it.

```bash
curl -X POST "https://host:5140/api/v4/file-loading/upload?fileType=TEL_GSM" \
  -H "Authorization: Bearer {token}" \
  -F "file=@/local/path/cdr_20260320.csv"
```

**202 Accepted:** Same response format as POST /load.

---

#### GET /files/{nt-file-num}

Get the status of a loaded file by its database record key.

```bash
curl -X GET "https://host:5140/api/v4/file-loading/files/12345" \
  -H "Authorization: Bearer {token}"
```

**200 OK:**
```json
{
  "NtFileNum": 12345,
  "FileName": "cdr_20260320.csv",
  "FileType": "TEL_GSM",
  "NtCustNum": "CUST001",
  "NtFileSeq": 42,
  "StatusId": 4,
  "StatusDescription": "Processed",
  "NtFileDate": "2026-03-20",
  "CreatedTm": "2026-03-20T10:30:00Z",
  "TotalRecords": 1500,
  "TotalCost": 2340.50,
  "EarliestCall": "2026-03-19T00:01:00Z",
  "LatestCall": "2026-03-19T23:59:00Z"
}
```

---

#### GET /files

List loaded files with optional filtering.

```bash
# List all files
curl -X GET "https://host:5140/api/v4/file-loading/files" \
  -H "Authorization: Bearer {token}"

# Filter by file type and customer
curl -X GET "https://host:5140/api/v4/file-loading/files?fileTypeCode=TEL_GSM&ntCustNum=CUST001&maxRecords=50" \
  -H "Authorization: Bearer {token}"
```

**200 OK:**
```json
{
  "Items": [
    {
      "NtFileNum": 12345,
      "FileName": "cdr_20260320.csv",
      "FileType": "TEL_GSM",
      "NtCustNum": "CUST001",
      "NtFileSeq": 42,
      "StatusId": 4,
      "StatusDescription": "Processed",
      "NtFileDate": "2026-03-20",
      "CreatedTm": "2026-03-20T10:30:00Z",
      "TotalRecords": 1500,
      "TotalCost": 2340.50
    }
  ],
  "TotalCount": 1
}
```

**Query parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `fileTypeCode` | string | null | Filter by file type code |
| `ntCustNum` | string | null | Filter by customer number |
| `maxRecords` | int | 100 | Maximum records to return |

---

#### GET /file-types

Get the list of supported file types for loading.

```bash
curl -X GET "https://host:5140/api/v4/file-loading/file-types" \
  -H "Authorization: Bearer {token}"
```

**200 OK:**
```json
{
  "Items": [
    {
      "Code": "TEL_GSM",
      "Description": "Telstra GSM CDR",
      "FileClassCode": "CDR",
      "FileClassDescription": "Call Detail Record",
      "NtCustNum": "CUST001",
      "NtFileName": "TEL_GSM_*.csv",
      "SkipHdr": 1,
      "SkipTlr": 1
    }
  ]
}
```

---

#### POST /files/{nt-file-num}/reprocess

Reprocess a previously loaded file.

```bash
curl -X POST "https://host:5140/api/v4/file-loading/files/12345/reprocess" \
  -H "Authorization: Bearer {token}"
```

**202 Accepted:** Same response format as POST /load.

---

### File Management (Dashboard & Workflow)

#### GET /dashboard

Get dashboard summary data including file counts by workflow folder and transfer source statuses.

```bash
curl -X GET "https://host:5140/api/v4/file-loading/dashboard" \
  -H "Authorization: Bearer {token}"

# Filter by domain and file type
curl -X GET "https://host:5140/api/v4/file-loading/dashboard?domain=domain1&fileType=CDR" \
  -H "Authorization: Bearer {token}"
```

**200 OK:**
```json
{
  "FilesInTransfer": 5,
  "FilesInProcessing": 2,
  "FilesProcessedToday": 47,
  "FilesWithErrors": 3,
  "FilesSkipped": 1,
  "SourceStatuses": [
    {
      "SourceId": 1,
      "VendorName": "Telstra CDR Feed",
      "Domain": "domain1",
      "FileTypeCode": "TEL_GSM",
      "IsEnabled": true,
      "LastTransferAt": "2026-03-20T09:15:00Z",
      "FilesTransferredToday": 12
    }
  ]
}
```

---

#### GET /manager/files

List files in the transfer workflow with filtering. Supports filtering by domain, file type, folder, status, date range, and filename search.

```bash
# List all workflow files
curl -X GET "https://host:5140/api/v4/file-loading/manager/files" \
  -H "Authorization: Bearer {token}"

# Filter by folder and status
curl -X GET "https://host:5140/api/v4/file-loading/manager/files?Folder=Errors&Status=5&maxRecords=50" \
  -H "Authorization: Bearer {token}"

# Search by filename within a date range
curl -X GET "https://host:5140/api/v4/file-loading/manager/files?Search=telstra&fromDate=2026-03-01&toDate=2026-03-20" \
  -H "Authorization: Bearer {token}"
```

**Query parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Domain` | string | null | Filter by domain |
| `fileType` | string | null | Filter by file type code |
| `Folder` | string | null | Filter by folder (Transfer, Processing, Processed, Errors, Skipped) |
| `Status` | int | null | Filter by status (0=Pending, 1=Downloading, 2=Downloaded, 3=Processing, 4=Processed, 5=Error, 6=Skipped) |
| `fromDate` | datetime | null | Date range start (inclusive) |
| `toDate` | datetime | null | Date range end (inclusive) |
| `Search` | string | null | Partial filename match |
| `maxRecords` | int | 100 | Maximum records to return |

**200 OK:**
```json
{
  "Items": [
    {
      "TransferId": 100,
      "NtFileNum": 12345,
      "FileName": "cdr_20260320.csv",
      "FileTypeCode": "TEL_GSM",
      "Domain": "domain1",
      "CurrentFolder": "Processed",
      "Status": "Processed",
      "StatusDescription": "File has been successfully processed",
      "FileSize": 524288,
      "CreatedAt": "2026-03-20T09:00:00Z",
      "CompletedAt": "2026-03-20T09:05:00Z",
      "SourceId": 1
    }
  ],
  "TotalCount": 1
}
```

---

#### GET /manager/files/{transfer-id}

Get detailed information about a specific file in the workflow.

```bash
curl -X GET "https://host:5140/api/v4/file-loading/manager/files/100" \
  -H "Authorization: Bearer {token}"
```

**200 OK:** Returns a single `FileWithStatus` object (same structure as items in the list response above).

---

#### POST /manager/files/{transfer-id}/process

Process a file from the transfer workflow (move from Transfer to Processing and begin loading).

```bash
curl -X POST "https://host:5140/api/v4/file-loading/manager/files/100/process" \
  -H "Authorization: Bearer {token}"
```

---

#### POST /manager/files/{transfer-id}/retry

Retry processing a file that previously failed.

```bash
curl -X POST "https://host:5140/api/v4/file-loading/manager/files/100/retry" \
  -H "Authorization: Bearer {token}"
```

---

#### POST /manager/files/{transfer-id}/move

Move a file to a specific workflow folder.

```bash
curl -X POST "https://host:5140/api/v4/file-loading/manager/files/100/move?folder=Skipped&reason=Duplicate%20file" \
  -H "Authorization: Bearer {token}"
```

**Query parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `folder` | string | Yes | Target folder: Transfer, Processing, Processed, Errors, Skipped |
| `reason` | string | No | Reason for the move (logged to activity log) |

---

#### POST /manager/files/{nt-file-num}/unload

Unload (reverse) a previously loaded file. This uses `nt-file-num` because it operates on the database record created during loading.

```bash
curl -X POST "https://host:5140/api/v4/file-loading/manager/files/12345/unload" \
  -H "Authorization: Bearer {token}"
```

---

#### POST /manager/files/{nt-file-num}/skip-sequence

Force skip to a specific sequence number. This is used when a file is missing and you need to advance the sequence counter.

```bash
curl -X POST "https://host:5140/api/v4/file-loading/manager/files/12345/skip-sequence?skipTo=45&reason=Missing%20file%2043-44" \
  -H "Authorization: Bearer {token}"
```

**Query parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `skipTo` | int | Yes | Sequence number to skip to |
| `reason` | string | No | Reason for the skip |

---

#### GET /manager/files/{transfer-id}/download

Download a file to the browser. Returns the file content with appropriate content type headers.

```bash
curl -X GET "https://host:5140/api/v4/file-loading/manager/files/100/download" \
  -H "Authorization: Bearer {token}" \
  -o downloaded_file.csv
```

---

#### DELETE /manager/files/{transfer-id}

Delete a file from the workflow.

```bash
curl -X DELETE "https://host:5140/api/v4/file-loading/manager/files/100" \
  -H "Authorization: Bearer {token}"
```

---

### Transfer Operations

#### POST /transfers/{source-id}/fetch

Manually trigger a file fetch from a configured transfer source (SFTP/FTP/FileSystem).

```bash
curl -X POST "https://host:5140/api/v4/file-loading/transfers/1/fetch" \
  -H "Authorization: Bearer {token}"
```

**200 OK:**
```json
{
  "FilesFound": 5,
  "FilesDownloaded": 3,
  "FilesSkipped": 1,
  "FilesFailed": 1,
  "TransferRecords": [
    {
      "TransferId": 101,
      "SourceId": 1,
      "FileName": "cdr_20260320_001.csv",
      "Status": "Downloaded",
      "SourcePath": "/remote/outgoing/cdr_20260320_001.csv",
      "DestinationPath": "/data/transfer/cdr_20260320_001.csv",
      "CurrentFolder": "Transfer",
      "FileSize": 524288,
      "StartedAt": "2026-03-20T10:30:00Z",
      "CompletedAt": "2026-03-20T10:30:05Z",
      "RetryCount": 0,
      "CreatedAt": "2026-03-20T10:30:00Z"
    }
  ],
  "Errors": ["Failed to download cdr_20260320_005.csv: Connection reset"]
}
```

---

### Transfer Sources (Configuration)

#### GET /sources

List all transfer source configurations.

```bash
curl -X GET "https://host:5140/api/v4/file-loading/sources" \
  -H "Authorization: Bearer {token}"

# Filter by domain
curl -X GET "https://host:5140/api/v4/file-loading/sources?domain=domain1" \
  -H "Authorization: Bearer {token}"
```

**200 OK:**
```json
[
  {
    "SourceId": 1,
    "VendorName": "Telstra CDR Feed",
    "Domain": "domain1",
    "FileTypeCode": "TEL_GSM",
    "Protocol": "Sftp",
    "Host": "sftp.telstra.com.au",
    "Port": 22,
    "RemotePath": "/outgoing/cdr/",
    "AuthType": "PrivateKey",
    "Username": "selcomm",
    "FileNamePattern": "CDR_*.csv",
    "DeleteAfterDownload": true,
    "CompressOnArchive": true,
    "Compression": "GZip",
    "CronSchedule": "0 */15 * * * *",
    "IsEnabled": true,
    "ConnectionUrl": "sftp://selcomm@sftp.telstra.com.au/outgoing/cdr/",
    "CreatedAt": "2026-01-15T00:00:00Z",
    "UpdatedAt": "2026-03-10T08:00:00Z"
  }
]
```

---

#### GET /sources/{source-id}

Get a specific transfer source configuration.

```bash
curl -X GET "https://host:5140/api/v4/file-loading/sources/1" \
  -H "Authorization: Bearer {token}"
```

---

#### PUT /sources/{source-id}

Create or update a transfer source. Use `source-id=0` to create a new source (the ID is auto-generated).

```bash
# Create a new source
curl -X PUT "https://host:5140/api/v4/file-loading/sources/0" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "VendorName": "Optus CDR Feed",
    "Domain": "domain1",
    "FileTypeCode": "OPT_CDR",
    "Protocol": "Sftp",
    "Host": "sftp.optus.com.au",
    "Port": 22,
    "RemotePath": "/data/outgoing/",
    "AuthType": "Password",
    "Username": "selcomm_user",
    "Password": "secret123",
    "FileNamePattern": "OPTUS_CDR_*.csv",
    "DeleteAfterDownload": true,
    "CompressOnArchive": true,
    "Compression": "GZip",
    "CronSchedule": "0 0 */6 * * *",
    "IsEnabled": true
  }'

# Update an existing source
curl -X PUT "https://host:5140/api/v4/file-loading/sources/2" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "VendorName": "Optus CDR Feed (Updated)",
    "Domain": "domain1",
    "FileTypeCode": "OPT_CDR",
    "Protocol": "Sftp",
    "Host": "sftp-new.optus.com.au",
    "Port": 2222,
    "RemotePath": "/data/outgoing/",
    "AuthType": "PrivateKey",
    "Username": "selcomm_user",
    "PrivateKeyPath": "/etc/selcomm/keys/optus_rsa",
    "FileNamePattern": "OPTUS_CDR_*.csv",
    "DeleteAfterDownload": true,
    "CompressOnArchive": true,
    "Compression": "GZip",
    "CronSchedule": "0 0 */6 * * *",
    "IsEnabled": true
  }'
```

**Transfer source fields:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `VendorName` | string | Yes | Friendly name (e.g., "Telstra CDR Feed") |
| `Domain` | string | Yes | Domain this source belongs to |
| `FileTypeCode` | string | No | Associated file type code |
| `Protocol` | string | Yes | `Sftp`, `Ftp`, or `FileSystem` |
| `Host` | string | Yes* | Remote host address (*not needed for FileSystem) |
| `Port` | int | No | Remote port (default: 22 for SFTP, 21 for FTP) |
| `RemotePath` | string | Yes | Remote path to monitor |
| `AuthType` | string | Yes* | `Password`, `Certificate`, or `PrivateKey` |
| `Username` | string | Yes* | Authentication username |
| `Password` | string | No | Password (encrypted in database) |
| `CertificatePath` | string | No | Path to certificate file |
| `PrivateKeyPath` | string | No | Path to private key file |
| `FileNamePattern` | string | No | Glob pattern (default: `*.*`) |
| `SkipFilePattern` | string | No | Pattern for files to skip |
| `DeleteAfterDownload` | bool | No | Delete from source after download (default: true) |
| `CompressOnArchive` | bool | No | Compress when archiving (default: true) |
| `Compression` | string | No | `None`, `GZip`, or `Zip` (default: GZip) |
| `CronSchedule` | string | No | CRON expression for scheduling (6-part with seconds) |
| `IsEnabled` | bool | No | Enable for scheduled transfers (default: true) |

---

#### DELETE /sources/{source-id}

Delete a transfer source configuration.

```bash
curl -X DELETE "https://host:5140/api/v4/file-loading/sources/2" \
  -H "Authorization: Bearer {token}"
```

---

#### POST /sources/{source-id}/test

Test connection to a saved transfer source.

```bash
curl -X POST "https://host:5140/api/v4/file-loading/sources/1/test" \
  -H "Authorization: Bearer {token}"
```

**200 OK:**
```json
{
  "Success": true,
  "Message": "Connection successful"
}
```

**400 Bad Request:**
```json
{
  "ErrorCode": "CONNECTION_FAILED",
  "Error": "Connection test failed"
}
```

---

#### POST /sources/test

Test connection with a provided configuration (without saving it first).

```bash
curl -X POST "https://host:5140/api/v4/file-loading/sources/test" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "Protocol": "Sftp",
    "Host": "sftp.vendor.com",
    "Port": 22,
    "RemotePath": "/outgoing/",
    "AuthType": "Password",
    "Username": "user",
    "Password": "pass"
  }'
```

---

### Activity Log

#### GET /activity

Get activity log entries for audit trail. Filter by file number, transfer ID, or both.

```bash
# Get recent activity
curl -X GET "https://host:5140/api/v4/file-loading/activity?maxRecords=50" \
  -H "Authorization: Bearer {token}"

# Filter by file number
curl -X GET "https://host:5140/api/v4/file-loading/activity?ntFileNum=12345" \
  -H "Authorization: Bearer {token}"

# Filter by transfer ID
curl -X GET "https://host:5140/api/v4/file-loading/activity?transferId=100" \
  -H "Authorization: Bearer {token}"
```

**200 OK:**
```json
[
  {
    "ActivityId": 500,
    "NtFileNum": 12345,
    "TransferId": 100,
    "FileName": "cdr_20260320.csv",
    "ActivityType": "ProcessingCompleted",
    "Description": "File processing completed successfully",
    "Details": "{\"RecordsLoaded\": 1500, \"RecordsFailed\": 0}",
    "UserId": "admin",
    "Domain": "domain1",
    "ActivityAt": "2026-03-20T09:05:00Z"
  }
]
```

**Activity types:** Downloaded, MovedToProcessing, ProcessingStarted, ProcessingCompleted, ProcessingFailed, MovedToSkipped, MovedToErrors, MovedToProcessed, FileDeleted, FileUnloaded, SequenceSkipped, ManualDownload, BrowserDownload, SourceCreated, SourceModified, SourceDeleted.

---

### Validation

#### GET /files/{nt-file-num}/validation-summary

Get an AI-friendly validation summary for a loaded file. This endpoint provides structured error information designed for consumption by AI agents and UI displays.

```bash
curl -X GET "https://host:5140/api/v4/file-loading/files/12345/validation-summary" \
  -H "Authorization: Bearer {token}"
```

**200 OK:**
```json
{
  "OverallStatus": "File rejected due to validation errors",
  "MainIssues": [
    "85 records have invalid cost amounts (not a valid decimal number)",
    "12 records are missing the required ServiceId field"
  ],
  "ErrorCountsByField": {
    "CostAmount": 85,
    "ServiceId": 12
  },
  "ErrorCountsByType": {
    "FIELD_PARSE_DECIMAL": 85,
    "FIELD_REQUIRED": 12
  },
  "SuggestedActions": [
    "Check the CostAmount column for non-numeric values",
    "Ensure all records have a ServiceId value"
  ],
  "CanPartiallyProcess": true
}
```

---

### Exceptions

#### GET /exceptions/errors

Get files that have processing errors.

```bash
curl -X GET "https://host:5140/api/v4/file-loading/exceptions/errors?domain=domain1&maxRecords=50" \
  -H "Authorization: Bearer {token}"
```

**200 OK:** Returns a list of `FileWithStatus` objects (same structure as workflow file list items).

---

#### GET /exceptions/skipped

Get files that were skipped.

```bash
curl -X GET "https://host:5140/api/v4/file-loading/exceptions/skipped?fileType=CDR" \
  -H "Authorization: Bearer {token}"
```

---

### Vendors (CRUD)

#### GET /vendors

List all vendors (networks).

```bash
curl -X GET "https://host:5140/api/v4/file-loading/vendors" \
  -H "Authorization: Bearer {token}"
```

**200 OK:**
```json
[
  {
    "NetworkId": "TL",
    "NetworkNarr": "Telstra"
  },
  {
    "NetworkId": "OP",
    "NetworkNarr": "Optus"
  }
]
```

#### GET /vendors/{network-id}

```bash
curl -X GET "https://host:5140/api/v4/file-loading/vendors/TL" \
  -H "Authorization: Bearer {token}"
```

#### PUT /vendors/{network-id}

Create or update a vendor.

```bash
curl -X PUT "https://host:5140/api/v4/file-loading/vendors/VF" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "NetworkId": "VF",
    "NetworkNarr": "Vodafone"
  }'
```

#### DELETE /vendors/{network-id}

```bash
curl -X DELETE "https://host:5140/api/v4/file-loading/vendors/VF" \
  -H "Authorization: Bearer {token}"
```

---

### File Classes (CRUD)

File classes group related file types (e.g., CDR, CHG, EBL).

#### GET /file-classes

```bash
curl -X GET "https://host:5140/api/v4/file-loading/file-classes" \
  -H "Authorization: Bearer {token}"
```

**200 OK:**
```json
[
  {
    "FileClassCode": "CDR",
    "FileClassNarr": "Call Detail Record"
  },
  {
    "FileClassCode": "CHG",
    "FileClassNarr": "Charge File"
  }
]
```

#### GET /file-classes/{file-class-code}

```bash
curl -X GET "https://host:5140/api/v4/file-loading/file-classes/CDR" \
  -H "Authorization: Bearer {token}"
```

#### PUT /file-classes/{file-class-code}

```bash
curl -X PUT "https://host:5140/api/v4/file-loading/file-classes/EBL" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "FileClassCode": "EBL",
    "FileClassNarr": "Electronic Bill"
  }'
```

#### DELETE /file-classes/{file-class-code}

```bash
curl -X DELETE "https://host:5140/api/v4/file-loading/file-classes/EBL" \
  -H "Authorization: Bearer {token}"
```

---

### File Types (CRUD)

Each file type belongs to a file class and optionally a vendor.

#### GET /manager/file-types

```bash
curl -X GET "https://host:5140/api/v4/file-loading/manager/file-types" \
  -H "Authorization: Bearer {token}"
```

**200 OK:**
```json
[
  {
    "FileTypeCode": "TEL_GSM",
    "FileTypeNarr": "Telstra GSM CDR",
    "FileClassCode": "CDR",
    "NetworkId": "TL",
    "FileClassNarr": "Call Detail Record",
    "NetworkNarr": "Telstra"
  }
]
```

#### GET /file-types/{file-type-code}

```bash
curl -X GET "https://host:5140/api/v4/file-loading/file-types/TEL_GSM" \
  -H "Authorization: Bearer {token}"
```

#### PUT /file-types/{file-type-code}

```bash
curl -X PUT "https://host:5140/api/v4/file-loading/file-types/OPT_CDR" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "FileTypeCode": "OPT_CDR",
    "FileTypeNarr": "Optus CDR",
    "FileClassCode": "CDR",
    "NetworkId": "OP"
  }'
```

#### DELETE /file-types/{file-type-code}

```bash
curl -X DELETE "https://host:5140/api/v4/file-loading/file-types/OPT_CDR" \
  -H "Authorization: Bearer {token}"
```

---

### File Types NT (CRUD)

NT records map a file type to a customer number, filename pattern, and header/trailer skip counts used during loading.

#### GET /file-types-nt

```bash
# List all
curl -X GET "https://host:5140/api/v4/file-loading/file-types-nt" \
  -H "Authorization: Bearer {token}"

# Filter by file type
curl -X GET "https://host:5140/api/v4/file-loading/file-types-nt?fileType=TEL_GSM" \
  -H "Authorization: Bearer {token}"
```

**200 OK:**
```json
[
  {
    "FileTypeCode": "TEL_GSM",
    "NtCustNum": "CUST001",
    "NtFileName": "TEL_GSM_*.csv",
    "SkipHdr": 1,
    "SkipTlr": 1,
    "FileTypeNarr": "Telstra GSM CDR"
  }
]
```

#### GET /file-types-nt/{file-type-code}

```bash
curl -X GET "https://host:5140/api/v4/file-loading/file-types-nt/TEL_GSM" \
  -H "Authorization: Bearer {token}"
```

#### PUT /file-types-nt/{file-type-code}

```bash
curl -X PUT "https://host:5140/api/v4/file-loading/file-types-nt/TEL_GSM" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "FileTypeCode": "TEL_GSM",
    "NtCustNum": "CUST001",
    "NtFileName": "TEL_GSM_*.csv",
    "SkipHdr": 1,
    "SkipTlr": 1
  }'
```

#### DELETE /file-types-nt/{file-type-code}

```bash
curl -X DELETE "https://host:5140/api/v4/file-loading/file-types-nt/TEL_GSM" \
  -H "Authorization: Bearer {token}"
```

---

### Parser Configuration (CRUD)

Generic parser configurations define how arbitrary file formats are parsed into standard records.

#### GET /parsers

```bash
# List all parser configs
curl -X GET "https://host:5140/api/v4/file-loading/parsers" \
  -H "Authorization: Bearer {token}"

# Filter by active status
curl -X GET "https://host:5140/api/v4/file-loading/parsers?active=true" \
  -H "Authorization: Bearer {token}"
```

**200 OK:**
```json
[
  {
    "FileTypeCode": "CUSTOM_CHG",
    "FileFormat": "CSV",
    "Delimiter": ",",
    "HasHeaderRow": true,
    "SkipRowsTop": 0,
    "SkipRowsBottom": 0,
    "RowIdMode": "Position",
    "Active": true,
    "ColumnMappings": [
      {
        "FileTypeCode": "CUSTOM_CHG",
        "ColumnIndex": 0,
        "SourceColumnName": "Account",
        "TargetField": "AccountCode",
        "DataType": "String",
        "IsRequired": true,
        "MaxLength": 20
      },
      {
        "FileTypeCode": "CUSTOM_CHG",
        "ColumnIndex": 3,
        "SourceColumnName": "Amount",
        "TargetField": "CostAmount",
        "DataType": "Decimal",
        "IsRequired": true
      }
    ]
  }
]
```

#### GET /parsers/{file-type-code}

```bash
curl -X GET "https://host:5140/api/v4/file-loading/parsers/CUSTOM_CHG" \
  -H "Authorization: Bearer {token}"
```

#### PUT /parsers/{file-type-code}

```bash
curl -X PUT "https://host:5140/api/v4/file-loading/parsers/CUSTOM_CHG" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "FileTypeCode": "CUSTOM_CHG",
    "FileFormat": "CSV",
    "Delimiter": ",",
    "HasHeaderRow": true,
    "SkipRowsTop": 0,
    "SkipRowsBottom": 0,
    "RowIdMode": "POSITION",
    "Active": true,
    "ColumnMappings": [
      {
        "ColumnIndex": 0,
        "SourceColumnName": "Account",
        "TargetField": "AccountCode",
        "DataType": "String",
        "IsRequired": true,
        "MaxLength": 20
      },
      {
        "ColumnIndex": 1,
        "SourceColumnName": "Service",
        "TargetField": "ServiceId",
        "DataType": "String",
        "IsRequired": true
      },
      {
        "ColumnIndex": 2,
        "SourceColumnName": "ChargeDate",
        "TargetField": "FromDate",
        "DataType": "DateTime",
        "DateFormat": "yyyy-MM-dd"
      },
      {
        "ColumnIndex": 3,
        "SourceColumnName": "Amount",
        "TargetField": "CostAmount",
        "DataType": "Decimal",
        "IsRequired": true
      }
    ]
  }'
```

**Parser configuration fields:**

| Field | Type | Description |
|-------|------|-------------|
| `FileFormat` | string | `CSV`, `XLSX`, or `Delimited` |
| `Delimiter` | string | Column delimiter: `,`, `tab`, `pipe`, `semicolon`, or any single character |
| `HasHeaderRow` | bool | Whether the first data row is a header |
| `SkipRowsTop` | int | Number of rows to skip at the top |
| `SkipRowsBottom` | int | Number of rows to skip at the bottom |
| `RowIdMode` | string | `POSITION`, `INDICATOR`, or `PATTERN` |
| `RowIdColumn` | int | Column index used for row identification (Indicator mode) |
| `HeaderIndicator` | string | Value or regex to identify header rows |
| `TrailerIndicator` | string | Value or regex to identify trailer rows |
| `DetailIndicator` | string | Value or regex to identify detail rows |
| `SkipIndicator` | string | Value or regex to identify rows to skip |
| `SheetName` | string | Excel sheet name (XLSX format) |
| `SheetIndex` | int | Excel sheet index (XLSX format) |
| `DateFormat` | string | Default date format for the file |
| `CustomSpName` | string | Custom stored procedure name for loading |
| `Active` | bool | Whether this parser is active |

**Target fields for column mappings:** AccountCode, ServiceId, ChargeType, CostAmount, TaxAmount, Quantity, UOM, FromDate, ToDate, Description, ExternalRef, Generic01 through Generic20.

#### DELETE /parsers/{file-type-code}

```bash
curl -X DELETE "https://host:5140/api/v4/file-loading/parsers/CUSTOM_CHG" \
  -H "Authorization: Bearer {token}"
```

---

### Folder Configuration

Folder workflow configurations define the directory structure for file processing per domain.

#### GET /folders

Get folder configuration for a domain. Falls back to domain default if a file-type specific config is not found.

```bash
curl -X GET "https://host:5140/api/v4/file-loading/folders?domain=domain1" \
  -H "Authorization: Bearer {token}"

# With file type override
curl -X GET "https://host:5140/api/v4/file-loading/folders?domain=domain1&fileType=CDR" \
  -H "Authorization: Bearer {token}"
```

**200 OK:**
```json
{
  "ConfigId": 1,
  "Domain": "domain1",
  "FileTypeCode": null,
  "TransferFolder": "/data/domain1/transfer/",
  "ProcessingFolder": "/data/domain1/processing/",
  "ProcessedFolder": "/data/domain1/processed/",
  "ErrorsFolder": "/data/domain1/errors/",
  "SkippedFolder": "/data/domain1/skipped/",
  "CreatedAt": "2026-01-15T00:00:00Z"
}
```

#### PUT /folders

Create or update folder workflow configuration.

```bash
curl -X PUT "https://host:5140/api/v4/file-loading/folders" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "Domain": "domain1",
    "FileTypeCode": "CDR",
    "TransferFolder": "/data/domain1/cdr/transfer/",
    "ProcessingFolder": "/data/domain1/cdr/processing/",
    "ProcessedFolder": "/data/domain1/cdr/processed/",
    "ErrorsFolder": "/data/domain1/cdr/errors/",
    "SkippedFolder": "/data/domain1/cdr/skipped/"
  }'
```

---

## File Loading Workflow

The File Loading API implements a folder-based workflow pipeline:

```
Remote Source (SFTP/FTP/FileSystem)
        |
        v
  [Transfer Folder]   <-- Files downloaded here (automatically or manually)
        |
        v
  [Processing Folder]  <-- Files moved here during loading
        |
       / \
      /   \
     v     v
[Processed]  [Errors]   <-- Files moved based on loading result
                |
                v
            [Skipped]    <-- Files manually skipped or matching skip pattern
```

### Workflow Steps

1. **Transfer**: Files are fetched from configured remote sources (SFTP/FTP) or uploaded manually. They land in the Transfer folder.
2. **Processing**: When a file is ready to be loaded, it is moved to the Processing folder. The parser reads the file, validates records, and inserts data into the database.
3. **Processed**: Successfully loaded files are archived in the Processed folder (optionally compressed).
4. **Errors**: Files that fail during loading are moved to the Errors folder with error details logged.
5. **Skipped**: Files that match a skip pattern or are manually skipped are moved to the Skipped folder.

### Automated Transfers

The background `FileTransferWorker` runs on a 1-minute check interval. For each enabled transfer source with a CRON schedule, it evaluates whether the next scheduled run is due and triggers a fetch operation. CRON expressions use 6-part format with seconds (e.g., `0 */15 * * * *` for every 15 minutes).

---

## Error Handling

### Error Response Format

All error responses follow a consistent format:

```json
{
  "ErrorCode": "FILE_NOT_FOUND",
  "Error": "File with NtFileNum 99999 was not found"
}
```

### Common HTTP Status Codes

| Code | Meaning |
|------|---------|
| 200 | Success |
| 202 | Accepted (file load initiated asynchronously) |
| 204 | No content (empty result set) |
| 400 | Bad request (invalid parameters or connection test failure) |
| 401 | Unauthorized (missing or invalid authentication) |
| 404 | Not found (resource does not exist) |
| 503 | Service unavailable (health check failure) |

### Validation Error Codes

**File-level errors** (reject the entire file):

| Code | Description |
|------|-------------|
| `FILE_NO_HEADER` | Required header record is missing |
| `FILE_NO_FOOTER` | Required footer/trailer record is missing |
| `FILE_FOOTER_COUNT` | Footer record count does not match actual detail count |
| `FILE_SEQ_GAP` | Sequence number gap detected |
| `FILE_HEADER_WRONG_PLACE` | Header is not the first non-empty line |
| `FILE_MULTIPLE_HEADERS` | Multiple header records found |
| `FILE_MULTIPLE_FOOTERS` | Multiple footer records found |
| `FILE_EMPTY` | File contains no records |
| `FILE_TOO_FEW_RECORDS` | File has fewer records than the configured minimum |
| `FILE_TOO_MANY_RECORDS` | File has more records than the configured maximum |

**Field-level errors** (per-record):

| Code | Description |
|------|-------------|
| `FIELD_REQUIRED` | Required field is missing or empty |
| `FIELD_PARSE_INTEGER` | Value cannot be parsed as an integer |
| `FIELD_PARSE_LONG` | Value cannot be parsed as a long integer |
| `FIELD_PARSE_DECIMAL` | Value cannot be parsed as a decimal |
| `FIELD_PARSE_DATETIME` | Value cannot be parsed as a date/time |
| `FIELD_PARSE_BOOLEAN` | Value cannot be parsed as a boolean |
| `FIELD_CONSTRAINT_MIN` | Value is below the configured minimum |
| `FIELD_CONSTRAINT_MAX` | Value exceeds the configured maximum |
| `FIELD_CONSTRAINT_NEGATIVE` | Value is negative but must be non-negative |
| `FIELD_CONSTRAINT_NOT_POSITIVE` | Value is not positive but must be positive |
| `FIELD_CONSTRAINT_DATE_FUTURE` | Date is in the future but must be in the past |
| `FIELD_CONSTRAINT_DATE_PAST` | Date is in the past but must be in the future |
| `FIELD_CONSTRAINT_MIN_LENGTH` | String is shorter than the minimum length |
| `FIELD_CONSTRAINT_MAX_LENGTH` | String exceeds the maximum length |
| `FIELD_CONSTRAINT_PATTERN` | Value does not match the required regex pattern |
| `FIELD_CONSTRAINT_ENUM` | Value is not in the list of allowed values |

### Error Aggregation

When a file produces many errors, the validation engine aggregates them:

1. The first 100 errors (configurable) are stored with full detail including line number, raw value, and raw line.
2. After the threshold, errors are grouped by error code and field name into aggregated summaries with counts and sample values.
3. File-level errors are always stored with full detail regardless of the threshold.

---

## Supported File Parsers

The API includes built-in parsers for the following vendor-specific formats:

| Parser | File Type | Description |
|--------|-----------|-------------|
| GenericCdrParser | Generic CDR | Configurable CDR parser |
| TelstraGsmCdrParser | TEL_GSM | Telstra GSM CDR files |
| TelstraCdmaCdrParser | TEL_CDMA | Telstra CDMA CDR files |
| OptusCdrParser | OPT_CDR | Optus CDR files |
| AaptCdrParser | AAPT_CDR | AAPT CDR files |
| VodafoneCdrParser | VF_CDR | Vodafone CDR files |
| ChgFileParser | CHG | Charge files |
| SvcFileParser | SVC | Service files |
| OrdFileParser | ORD | Order files |
| EblFileParser | EBL | Electronic bill files |
| SssWhlsCdrParser | SSSWHLSCDR | SSS Wholesale CDR files |
| SssWhlsChgParser | SSSWHLSCHG | SSS Wholesale charge files |
| GenericFileParser | (configurable) | Configurable parser using database-driven column mappings |

For file types not covered by a built-in parser, use the **Generic File Parser** with a parser configuration (see the Parser Configuration endpoints).
