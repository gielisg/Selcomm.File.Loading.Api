namespace FileLoading.Models;

// ============================================
// Configuration Options
// ============================================

/// <summary>
/// Root configuration for file loader options.
/// Keyed by domain, then by file type (CDR, CHG, etc.).
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
/// Configuration options for a specific file type.
/// All properties are nullable to support hierarchical merging.
/// </summary>
public class FileTypeOptions
{
    /// <summary>
    /// Number of records to buffer before flushing to database.
    /// Default: 1000 records.
    /// </summary>
    public int? BatchSize { get; set; }

    /// <summary>
    /// Number of records per database transaction.
    /// Default: 1000 records.
    /// </summary>
    public int? TransactionBatchSize { get; set; }

    /// <summary>
    /// Enable streaming mode for large file processing.
    /// When enabled, uses two-pass approach: validation then streaming insert.
    /// Default: true.
    /// </summary>
    public bool? UseStreamingMode { get; set; }

    // Effective values with defaults
    public int EffectiveBatchSize => BatchSize ?? 1000;
    public int EffectiveTransactionBatchSize => TransactionBatchSize ?? 1000;
    public bool EffectiveUseStreamingMode => UseStreamingMode ?? true;

    /// <summary>
    /// Merges non-null values from another options instance (for hierarchical config).
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

public class LoadFileRequest
{
    /// <summary>Full path to file.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>File type code (from file_type table).</summary>
    public string FileType { get; set; } = string.Empty;

    /// <summary>Network customer number.</summary>
    public string? NtCustNum { get; set; }

    /// <summary>File date (defaults to today).</summary>
    public DateTime? FileDate { get; set; }

    /// <summary>Expected sequence number (for validation).</summary>
    public int? ExpectedSequence { get; set; }
}

// ============================================
// Response Models
// ============================================

public class FileLoadResponse
{
    /// <summary>File number (nt_file_num).</summary>
    public int NtFileNum { get; set; }

    /// <summary>File name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>File type code.</summary>
    public string FileType { get; set; } = string.Empty;

    /// <summary>Status description.</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>Status ID.</summary>
    public int StatusId { get; set; }

    /// <summary>Records loaded count.</summary>
    public int RecordsLoaded { get; set; }

    /// <summary>Records failed count.</summary>
    public int RecordsFailed { get; set; }

    /// <summary>Processing start time.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>Processing completion time.</summary>
    public DateTime? CompletedAt { get; set; }
}

public class FileStatusResponse
{
    /// <summary>File number (nt_file_num).</summary>
    public int NtFileNum { get; set; }

    /// <summary>File name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>File type code.</summary>
    public string FileType { get; set; } = string.Empty;

    /// <summary>Network customer number.</summary>
    public string NtCustNum { get; set; } = string.Empty;

    /// <summary>File sequence number.</summary>
    public int NtFileSeq { get; set; }

    /// <summary>Status ID.</summary>
    public int StatusId { get; set; }

    /// <summary>Status description.</summary>
    public string StatusDescription { get; set; } = string.Empty;

    /// <summary>File date.</summary>
    public DateTime? NtFileDate { get; set; }

    /// <summary>Created timestamp.</summary>
    public DateTime? CreatedTm { get; set; }

    /// <summary>Total records in file.</summary>
    public int? TotalRecords { get; set; }

    /// <summary>Total cost.</summary>
    public decimal? TotalCost { get; set; }

    /// <summary>Earliest call date in file.</summary>
    public DateTime? EarliestCall { get; set; }

    /// <summary>Latest call date in file.</summary>
    public DateTime? LatestCall { get; set; }
}

public class FileListResponse
{
    public List<FileStatusResponse> Items { get; set; } = new();

    /// <summary>Total matching records (null if count not requested).</summary>
    public int? Count { get; set; }
}

public class FileTypeListResponse
{
    public List<FileTypeInfo> Items { get; set; } = new();
}

public class FileTypeInfo
{
    /// <summary>File type code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>File type description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>File class code (CDR, CHG, etc.).</summary>
    public string FileClassCode { get; set; } = string.Empty;

    /// <summary>File class description.</summary>
    public string FileClassDescription { get; set; } = string.Empty;

    /// <summary>Network customer number.</summary>
    public string NtCustNum { get; set; } = string.Empty;

    /// <summary>File name pattern.</summary>
    public string NtFileName { get; set; } = string.Empty;

    /// <summary>Skip header records count.</summary>
    public int? SkipHdr { get; set; }

    /// <summary>Skip trailer records count.</summary>
    public int? SkipTlr { get; set; }
}

// ============================================
// Internal Models
// ============================================

public class ParseContext
{
    public int FileRef { get; set; }
    public string FileType { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class ParseResult
{
    public bool Success { get; set; }
    public int RecordsParsed { get; set; }
    public int RecordsFailed { get; set; }
    public List<ParsedRecord> Records { get; set; } = new();
    public string? ErrorMessage { get; set; }

    /// <summary>List of all errors encountered during parsing.</summary>
    public List<ParseError> Errors { get; set; } = new();

    /// <summary>True if there are any errors (file-level or record-level).</summary>
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Represents a parsing error (file-level or record-level).
/// </summary>
public class ParseError
{
    /// <summary>Error code (e.g., 'HDR_MISS', 'PARSE_ERR').</summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>Human-readable error message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Line number where error occurred. Null for file-level errors.</summary>
    public int? LineNumber { get; set; }

    /// <summary>Raw data from the problematic line.</summary>
    public string? RawData { get; set; }

    /// <summary>True if this is a file-level error that should reject the entire file.</summary>
    public bool IsFileLevelError { get; set; }
}

public class ParsedRecord
{
    public int RecordNumber { get; set; }
    public string RecordType { get; set; } = string.Empty;
    public Dictionary<string, object> Fields { get; set; } = new();
    public bool IsValid { get; set; } = true;
    public string? ValidationError { get; set; }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public int? SequenceNumber { get; set; }
    public int? RecordCount { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result from streaming file validation pass.
/// Contains validation status, record count, and any file-level errors.
/// </summary>
public class StreamingValidationResult
{
    /// <summary>True if file structure is valid.</summary>
    public bool IsValid { get; set; }

    /// <summary>Sequence number from header (if available).</summary>
    public int? SequenceNumber { get; set; }

    /// <summary>Total detail record count.</summary>
    public int RecordCount { get; set; }

    /// <summary>Error message if validation failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>List of file-level errors found during validation.</summary>
    public List<ParseError> Errors { get; set; } = new();
}

public class ErrorResponse
{
    public string ErrorCode { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public object? Details { get; set; }

    public ErrorResponse() { }

    public ErrorResponse(string error, string? errorCode = null)
    {
        Error = error;
        ErrorCode = errorCode ?? string.Empty;
    }
}
