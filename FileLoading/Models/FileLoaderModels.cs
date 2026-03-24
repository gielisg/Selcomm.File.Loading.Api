namespace FileLoading.Models;

// ============================================
// Configuration Options
// ============================================

/// <summary>
/// Root configuration for file loader options, bound from the "FileLoaderOptions" section of appsettings.json.
/// Keyed by domain, then by file type (CDR, CHG, etc.) with hierarchical resolution.
///
/// Example appsettings.json:
/// {
///   "FileLoaderOptions": {
///     "Default": {
///       "Default": { "BatchSize": 1000, "TransactionBatchSize": 1000, "UseStreamingMode": true }
///     },
///     "domain1": {
///       "Default": { "BatchSize": 500 },
///       "CDR": { "BatchSize": 2000, "TransactionBatchSize": 2000 },
///       "CHG": { "BatchSize": 1000 }
///     },
///     "domain2": {
///       "CDR": { "BatchSize": 5000 }
///     }
///   }
/// }
///
/// Resolution order:
/// 1. domain -> fileType (exact match)
/// 2. domain -> "Default" (domain default)
/// 3. "Default" -> fileType (global file type default)
/// 4. "Default" -> "Default" or built-in defaults
/// </summary>
public class FileLoaderOptionsRoot
{
    /// <summary>
    /// Options keyed by domain name. Use "Default" for global defaults.
    /// </summary>
    public Dictionary<string, Dictionary<string, FileTypeOptions>> Options { get; set; } = new();

    /// <summary>
    /// Gets the effective options for a specific domain and file type.
    /// Follows resolution order: domain+fileType -> domain+Default -> Default+fileType -> Default+Default -> built-in
    /// </summary>
    public FileTypeOptions GetOptions(string? domain, string? fileType)
    {
        var result = new FileTypeOptions(); // Start with built-in defaults

        // Try "Default" domain, "Default" file type (global defaults)
        if (Options.TryGetValue("Default", out var globalDomain))
        {
            if (globalDomain.TryGetValue("Default", out var globalDefaults))
                result.MergeFrom(globalDefaults);

            // Try "Default" domain, specific file type
            if (!string.IsNullOrEmpty(fileType) && globalDomain.TryGetValue(fileType, out var globalFileType))
                result.MergeFrom(globalFileType);
        }

        // Try specific domain
        if (!string.IsNullOrEmpty(domain) && Options.TryGetValue(domain, out var domainOptions))
        {
            // Try domain "Default" file type
            if (domainOptions.TryGetValue("Default", out var domainDefaults))
                result.MergeFrom(domainDefaults);

            // Try domain + specific file type (highest priority)
            if (!string.IsNullOrEmpty(fileType) && domainOptions.TryGetValue(fileType, out var domainFileType))
                result.MergeFrom(domainFileType);
        }

        return result;
    }
}

/// <summary>
/// Configuration options for a specific file type's loading behaviour.
/// All properties are nullable to support hierarchical merging from global -> domain -> file-type levels.
/// </summary>
public class FileTypeOptions
{
    /// <summary>
    /// Number of records to buffer before flushing to the database.
    /// Default: 1000 records.
    /// </summary>
    /// <example>1000</example>
    public int? BatchSize { get; set; }

    /// <summary>
    /// Number of records per database transaction.
    /// Default: 1000 records.
    /// </summary>
    /// <example>1000</example>
    public int? TransactionBatchSize { get; set; }

    /// <summary>
    /// Enable streaming mode for large file processing.
    /// When enabled, uses a two-pass approach: validation then streaming insert.
    /// Default: true.
    /// </summary>
    /// <example>true</example>
    public bool? UseStreamingMode { get; set; }

    /// <summary>Effective batch size after resolving defaults (1000 if not configured).</summary>
    public int EffectiveBatchSize => BatchSize ?? 1000;

    /// <summary>Effective transaction batch size after resolving defaults (1000 if not configured).</summary>
    public int EffectiveTransactionBatchSize => TransactionBatchSize ?? 1000;

    /// <summary>Effective streaming mode flag after resolving defaults (true if not configured).</summary>
    public bool EffectiveUseStreamingMode => UseStreamingMode ?? true;

    /// <summary>
    /// Merges non-null values from another options instance (for hierarchical config resolution).
    /// </summary>
    public void MergeFrom(FileTypeOptions other)
    {
        if (other.BatchSize.HasValue)
            BatchSize = other.BatchSize;
        if (other.TransactionBatchSize.HasValue)
            TransactionBatchSize = other.TransactionBatchSize;
        if (other.UseStreamingMode.HasValue)
            UseStreamingMode = other.UseStreamingMode;
    }
}

// ============================================
// Request Models
// ============================================

/// <summary>
/// Request to load (parse and import) a file into the database.
/// </summary>
public class LoadFileRequest
{
    /// <summary>Full path to the file on the server.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Transfer/CDR_20250315_001.csv</example>
    public string FileName { get; set; } = string.Empty;

    /// <summary>File type code (from file_type table).</summary>
    /// <example>TEL_GSM</example>
    public string FileType { get; set; } = string.Empty;

    /// <summary>Network customer number identifying the source account.</summary>
    /// <example>CUST001</example>
    public string? NtCustNum { get; set; }

    /// <summary>File date (defaults to today if not specified).</summary>
    /// <example>2025-03-15</example>
    public DateTime? FileDate { get; set; }

    /// <summary>Expected sequence number for validation against the file header.</summary>
    /// <example>43</example>
    public int? ExpectedSequence { get; set; }
}

// ============================================
// Response Models
// ============================================

/// <summary>
/// Response returned after initiating a file load operation.
/// Contains the assigned file number and initial loading status.
/// </summary>
public class FileLoadResponse
{
    /// <summary>Assigned file number (nt_file_num) in the database.</summary>
    /// <example>12345</example>
    public int NtFileNum { get; set; }

    /// <summary>File name that was loaded.</summary>
    /// <example>CDR_20250315_001.csv</example>
    public string FileName { get; set; } = string.Empty;

    /// <summary>File type code.</summary>
    /// <example>TEL_GSM</example>
    public string FileType { get; set; } = string.Empty;

    /// <summary>Human-readable status description.</summary>
    /// <example>Pending</example>
    public string Status { get; set; } = "Pending";

    /// <summary>Numeric status ID from the nt_file_stat table.</summary>
    /// <example>1</example>
    public int StatusId { get; set; }

    /// <summary>Number of records successfully loaded.</summary>
    /// <example>15000</example>
    public int RecordsLoaded { get; set; }

    /// <summary>Number of records that failed to load.</summary>
    /// <example>3</example>
    public int RecordsFailed { get; set; }

    /// <summary>Timestamp when processing started.</summary>
    /// <example>2025-03-15T08:05:00Z</example>
    public DateTime? StartedAt { get; set; }

    /// <summary>Timestamp when processing completed.</summary>
    /// <example>2025-03-15T08:06:15Z</example>
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Detailed file status response including record counts, cost totals, and date ranges.
/// Returned by the GET /files/{ntFileNum} endpoint.
/// </summary>
public class FileStatusResponse
{
    /// <summary>File number (nt_file_num) — the primary key in nt_file.</summary>
    /// <example>12345</example>
    public int NtFileNum { get; set; }

    /// <summary>File name.</summary>
    /// <example>CDR_20250315_001.csv</example>
    public string FileName { get; set; } = string.Empty;

    /// <summary>File type code.</summary>
    /// <example>TEL_GSM</example>
    public string FileType { get; set; } = string.Empty;

    /// <summary>Network customer number.</summary>
    /// <example>CUST001</example>
    public string NtCustNum { get; set; } = string.Empty;

    /// <summary>File sequence number within the file type.</summary>
    /// <example>43</example>
    public int NtFileSeq { get; set; }

    /// <summary>Numeric status ID from the nt_file_stat table.</summary>
    /// <example>2</example>
    public int StatusId { get; set; }

    /// <summary>Human-readable status description.</summary>
    /// <example>Transactions loaded</example>
    public string Status { get; set; } = string.Empty;

    /// <summary>File date assigned at load time.</summary>
    /// <example>2025-03-15</example>
    public DateTime? NtFileDate { get; set; }

    /// <summary>Timestamp when the file record was created.</summary>
    /// <example>2025-03-15T08:05:00Z</example>
    public DateTime? CreatedTm { get; set; }

    /// <summary>Total number of detail records in the file.</summary>
    /// <example>15000</example>
    public int? TotalRecords { get; set; }

    /// <summary>Total cost value across all records in the file.</summary>
    /// <example>12500.75</example>
    public decimal? TotalCost { get; set; }

    /// <summary>Earliest call/transaction date found in the file records.</summary>
    /// <example>2025-03-01T00:00:12Z</example>
    public DateTime? EarliestCall { get; set; }

    /// <summary>Latest call/transaction date found in the file records.</summary>
    /// <example>2025-03-15T23:59:48Z</example>
    public DateTime? LatestCall { get; set; }
}

/// <summary>
/// Paginated response containing a list of file status records.
/// </summary>
public class FileListResponse
{
    /// <summary>List of file status records for the current page.</summary>
    public List<FileStatusResponse> Items { get; set; } = new();

    /// <summary>Total matching records (null if count was not requested).</summary>
    /// <example>150</example>
    public int? Count { get; set; }
}

/// <summary>
/// Lightweight result for NT file autocomplete/search.
/// </summary>
public class NtFileSearchResult
{
    /// <summary>NT file number.</summary>
    /// <example>12345</example>
    public int NtFileNum { get; set; }

    /// <summary>File name.</summary>
    /// <example>CDR_20250315_001.csv</example>
    public string FileName { get; set; } = string.Empty;

    /// <summary>File type code.</summary>
    /// <example>CDR</example>
    public string FileType { get; set; } = string.Empty;

    /// <summary>Status ID.</summary>
    /// <example>4</example>
    public int StatusId { get; set; }

    /// <summary>Status description.</summary>
    /// <example>Processed</example>
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Response containing a list of available file types.
/// </summary>
public class FileTypeListResponse
{
    /// <summary>List of file type information records.</summary>
    public List<FileTypeInfo> Items { get; set; } = new();
}

/// <summary>
/// File type summary information including class and network details.
/// </summary>
public class FileTypeInfo
{
    /// <summary>File type code (primary key).</summary>
    /// <example>TEL_GSM</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Human-readable file type description.</summary>
    /// <example>Telstra GSM CDR</example>
    public string FileType { get; set; } = string.Empty;

    /// <summary>File class code (CDR, CHG, etc.).</summary>
    /// <example>CDR</example>
    public string FileClassCode { get; set; } = string.Empty;

    /// <summary>Human-readable file class description.</summary>
    /// <example>Call Detail Records</example>
    public string FileClass { get; set; } = string.Empty;

    /// <summary>Network/vendor ID (null if not vendor-specific).</summary>
    /// <example>TL</example>
    public string? NetworkId { get; set; }

    /// <summary>Network/vendor display name.</summary>
    /// <example>Telstra</example>
    public string? Network { get; set; }
}

// ============================================
// Internal Models
// ============================================

/// <summary>
/// Context object passed to file parsers during processing.
/// Contains the file reference and metadata needed for parsing.
/// </summary>
public class ParseContext
{
    /// <summary>File reference number (nt_file_num) in the database.</summary>
    /// <example>12345</example>
    public int FileRef { get; set; }

    /// <summary>File type code being parsed.</summary>
    /// <example>TEL_GSM</example>
    public string FileType { get; set; } = string.Empty;

    /// <summary>Additional metadata key-value pairs for the parsing operation.</summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Result of a file parsing operation, containing parsed records and error information.
/// </summary>
public class ParseResult
{
    /// <summary>Whether the overall parsing operation succeeded.</summary>
    /// <example>true</example>
    public bool Success { get; set; }

    /// <summary>Number of records successfully parsed.</summary>
    /// <example>15000</example>
    public int RecordsParsed { get; set; }

    /// <summary>Number of records that failed to parse.</summary>
    /// <example>3</example>
    public int RecordsFailed { get; set; }

    /// <summary>List of successfully parsed records.</summary>
    public List<ParsedRecord> Records { get; set; } = new();

    /// <summary>Top-level error message if parsing failed entirely.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>List of all errors encountered during parsing (file-level and record-level).</summary>
    public List<ParseError> Errors { get; set; } = new();

    /// <summary>True if there are any errors (file-level or record-level).</summary>
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Represents a parsing error, either at the file level or the individual record level.
/// </summary>
public class ParseError
{
    /// <summary>Error code identifying the type of error (e.g., HDR_MISS, PARSE_ERR).</summary>
    /// <example>PARSE_ERR</example>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>Human-readable error message.</summary>
    /// <example>Invalid date format in column 5</example>
    public string Message { get; set; } = string.Empty;

    /// <summary>Line number where the error occurred. Null for file-level errors.</summary>
    /// <example>142</example>
    public int? LineNumber { get; set; }

    /// <summary>Raw data from the problematic line for debugging.</summary>
    /// <example>D,0412345678,INVALID_DATE,300,1.50</example>
    public string? RawData { get; set; }

    /// <summary>True if this is a file-level error that should reject the entire file.</summary>
    /// <example>false</example>
    public bool IsFileLevelError { get; set; }
}

/// <summary>
/// A single parsed record from a file, containing the extracted field values and validation status.
/// </summary>
public class ParsedRecord
{
    /// <summary>Record number within the file (1-based).</summary>
    /// <example>1</example>
    public int RecordNumber { get; set; }

    /// <summary>Record type identifier (e.g., H for header, D for detail, T for trailer).</summary>
    /// <example>D</example>
    public string RecordType { get; set; } = string.Empty;

    /// <summary>Extracted field values keyed by field name.</summary>
    public Dictionary<string, object> Fields { get; set; } = new();

    /// <summary>Whether the record passed validation.</summary>
    /// <example>true</example>
    public bool IsValid { get; set; } = true;

    /// <summary>Validation error message if the record is invalid.</summary>
    public string? ValidationError { get; set; }
}

/// <summary>
/// Result of file header/trailer validation before processing.
/// </summary>
public class ValidationResult
{
    /// <summary>Whether the file structure is valid.</summary>
    /// <example>true</example>
    public bool IsValid { get; set; }

    /// <summary>Sequence number extracted from the file header.</summary>
    /// <example>43</example>
    public int? SequenceNumber { get; set; }

    /// <summary>Record count extracted from the file trailer.</summary>
    /// <example>15000</example>
    public int? RecordCount { get; set; }

    /// <summary>Error message if validation failed.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result from the streaming file validation pass.
/// Contains validation status, record count, and any file-level errors found during the first pass.
/// </summary>
public class StreamingValidationResult
{
    /// <summary>True if the file structure is valid and ready for streaming load.</summary>
    /// <example>true</example>
    public bool IsValid { get; set; }

    /// <summary>Sequence number from the file header (if available).</summary>
    /// <example>43</example>
    public int? SequenceNumber { get; set; }

    /// <summary>Total detail record count found during validation.</summary>
    /// <example>15000</example>
    public int RecordCount { get; set; }

    /// <summary>Error message if validation failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>List of file-level errors found during the validation pass.</summary>
    public List<ParseError> Errors { get; set; } = new();
}

/// <summary>
/// Standard error response returned by all API endpoints when an error occurs.
/// </summary>
public class ErrorResponse
{
    /// <summary>Machine-readable error code for programmatic handling.</summary>
    /// <example>FileLoading.FileNotFound</example>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>Human-readable error message.</summary>
    /// <example>File with ID 12345 was not found.</example>
    public string Error { get; set; } = string.Empty;

    /// <summary>Additional error details (validation errors, nested error info, etc.).</summary>
    public object? Details { get; set; }

    /// <summary>
    /// Creates a new empty error response.
    /// </summary>
    public ErrorResponse() { }

    /// <summary>
    /// Creates a new error response with the specified message and optional error code.
    /// </summary>
    /// <param name="error">Human-readable error message.</param>
    /// <param name="errorCode">Optional machine-readable error code.</param>
    public ErrorResponse(string error, string? errorCode = null)
    {
        Error = error;
        ErrorCode = errorCode ?? string.Empty;
    }
}
