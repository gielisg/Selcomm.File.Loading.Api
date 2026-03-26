# UI Plan: Duplicate Files View

## Overview

Add a "Duplicates" section to the File Loading dashboard that displays groups of files with identical content (same SHA-256 hash) but different names, and allows users to dismiss false positives.

## API Endpoints Available

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/api/v4/file-loading/exceptions/duplicates` | List duplicate file groups (paged) |
| GET | `/api/v4/file-loading/dashboard` | Includes `duplicateFileCount` |
| POST | `/api/v4/file-loading/exceptions/duplicates/{fileHash}/ignore` | Dismiss a duplicate group |
| DELETE | `/api/v4/file-loading/exceptions/duplicates/{fileHash}/ignore` | Un-dismiss a duplicate group |

### Query Parameters (GET /exceptions/duplicates)

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| fileType | string | null | Filter by file type code |
| includeIgnored | bool | false | Include dismissed duplicates |
| skipRecords | int | 0 | Pagination offset |
| takeRecords | int | 20 | Page size |
| countRecords | string | F | Y=always count, N=never, F=first page only |

### Response Shape (GET /exceptions/duplicates)

```json
{
  "items": [
    {
      "fileHash": "a3f2b8c9d1e4...",
      "duplicateCount": 3,
      "files": [
        {
          "ntFileNum": 12300,
          "fileName": "CDR_20250315_001.csv",
          "fileType": "TEL_GSM",
          "statusId": 4,
          "status": "Processing Completed",
          "createdTm": "2025-03-15T08:05:00Z"
        },
        {
          "ntFileNum": 12345,
          "fileName": "CDR_20250315_002.csv",
          "fileType": "TEL_GSM",
          "statusId": 4,
          "status": "Processing Completed",
          "createdTm": "2025-03-15T10:30:00Z"
        }
      ]
    }
  ],
  "count": 5
}
```

### Dismiss Request (POST /exceptions/duplicates/{fileHash}/ignore)

```json
{
  "ntFileNum": 12345,
  "reason": "Vendor resend - confirmed with supplier"
}
```

---

## UI Components

### 1. Dashboard Card

Add a "Duplicates" card alongside the existing Error and Skipped cards.

```
+-------------------+
|  Duplicates       |
|       2           |
|  groups detected  |
+-------------------+
```

- Source: `GET /dashboard` -> `duplicateFileCount`
- Colour: Amber/warning (not red - duplicates are informational, not errors)
- Click navigates to the Duplicates list view
- Shows 0 when no duplicates (grey/muted)

### 2. Duplicates List View (`/exceptions/duplicates`)

Accordion-style list where each row is a duplicate group (hash), expandable to show individual files.

```
+----------------------------------------------------------------------+
| Duplicates                                    [Filter by type v]     |
|                                               [x] Show dismissed     |
+----------------------------------------------------------------------+
| > a3f2b8c9... (3 files)          TEL_GSM     First: 15 Mar 2025     |
|   +--------------------------------------------------------------+  |
|   | # 12300  CDR_001.csv   Loaded    15 Mar 08:05   [Keep]       |  |
|   | # 12345  CDR_002.csv   Loaded    15 Mar 10:30                |  |
|   | # 12400  CDR_003.csv   Loaded    15 Mar 14:15                |  |
|   +--------------------------------------------------------------+  |
|   [ Dismiss Group ]                                                  |
+----------------------------------------------------------------------+
| > b7e1f2a3... (2 files)          CHG_MSP     First: 14 Mar 2025     |
+----------------------------------------------------------------------+
```

**Collapsed row shows:**
- Truncated hash (first 12 chars)
- File count
- File type (common across group, or "Mixed" if different types)
- Earliest creation date

**Expanded row shows:**
- Table of all files in the group
- Each row: file number, file name, status, created date
- "Keep" button on one file (marks it as the accepted copy)
- "Dismiss Group" button at the bottom

### 3. Dismiss Dialog

When user clicks "Dismiss Group":

```
+------------------------------------------+
|  Dismiss Duplicate Group                 |
|                                          |
|  Keep file: [dropdown of files in group] |
|  Reason:    [text input               ]  |
|                                          |
|  Suggested reasons:                      |
|  [Vendor resend] [Corrected file]        |
|  [Test upload]   [Known duplicate]       |
|                                          |
|            [Cancel]  [Dismiss]           |
+------------------------------------------+
```

- "Keep file" dropdown pre-selects whichever file the user clicked "Keep" on (or the newest file by default)
- Quick-select reason chips for common cases
- Free-text reason field
- Calls `POST /exceptions/duplicates/{hash}/ignore`

### 4. Dismissed State

Dismissed groups:
- Hidden by default
- Visible when "Show dismissed" checkbox is ticked
- Shown with strikethrough/muted styling and a green "Dismissed" badge
- "Un-dismiss" button to reverse (calls `DELETE .../ignore`)

### 5. Load/Upload Response Toast

When a file is loaded via the UI and duplicates are detected in the response:

```
+--------------------------------------------------+
| ! File loaded successfully                       |
|   CDR_20250315_002.csv -> File #12345            |
|                                                  |
|   Warning: This file has identical content to:   |
|   - #12300 CDR_20250315_001.csv (Loaded)         |
|                                                  |
|   [View Duplicates]  [Dismiss]                   |
+--------------------------------------------------+
```

- Amber warning toast (not blocking)
- Shows existing matching files from `duplicateFiles` in the load response
- "View Duplicates" navigates to the duplicates list
- "Dismiss" calls the ignore endpoint inline

---

## User Workflow

### Scenario 1: Dashboard review
1. User sees "Duplicates: 2" on dashboard
2. Clicks through to duplicates list
3. Expands a group, reviews the files
4. Decides it's a vendor resend -> clicks "Dismiss Group", selects reason
5. Group disappears from list, dashboard count decrements

### Scenario 2: During file processing
1. User processes a file from the Transfer folder
2. Load response includes `duplicateFiles` with 1 match
3. Toast appears warning of duplicate
4. User clicks "Dismiss" on the toast -> done
5. Or clicks "View Duplicates" to investigate further

### Scenario 3: FTP automated transfer
1. Vendor FTPs a file that's a duplicate of an existing one
2. File is auto-processed by the transfer worker
3. Dashboard count increments to show new duplicate
4. User reviews during next dashboard check

---

## Implementation Notes

- File hash is displayed truncated (12 chars) in the UI with a copy-to-clipboard icon for full hash
- Paging: use the standard `skipRecords`/`takeRecords` pattern matching other exception views
- The `GET /files/{id}` endpoint also returns `fileHash` so the individual file detail view can show the hash
- No file hash is shown for old files loaded before this feature (hash will be null)
