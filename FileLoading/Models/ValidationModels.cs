using System.Text.Json.Serialization;

namespace FileLoading.Models;

// ============================================
// VALIDATION CONFIGURATION MODELS
// ============================================

/// <summary>
/// Root validation configuration for a file type.
/// Can be loaded from appsettings.json or database.
/// </summary>
public class FileValidationConfig
{
    /// <summary>File type code this config applies to.</summary>
    public string FileType { get; set; } = string.Empty;

    /// <summary>File-level validation rules.</summary>
    public FileRules FileRules { get; set; } = new();

    /// <summary>Field-level validation rules.</summary>
    public List<FieldValidationRule> FieldRules { get; set; } = new();

    /// <summary>Error logging configuration.</summary>
    public ErrorLoggingConfig ErrorLogging { get; set; } = new();
}

/// <summary>
/// File-level validation rules.
/// Controls structure validation (header/footer/sequence).
/// </summary>
public class FileRules
{
    /// <summary>Require header record. Default: true.</summary>
    public bool RequireHeader { get; set; } = true;

    /// <summary>Require footer/trailer record. Default: true.</summary>
    public bool RequireFooter { get; set; } = true;

    /// <summary>Require sequence numbers to be contiguous (no gaps). Default: false.</summary>
    public bool SequenceMustBeContiguous { get; set; } = false;

    /// <summary>Footer record count must match actual detail count. Default: true.</summary>
    public bool FooterCountMustMatch { get; set; } = true;

    /// <summary>Header must be the first non-empty line. Default: true.</summary>
    public bool HeaderMustBeFirstLine { get; set; } = true;

    /// <summary>Footer must be the last non-empty line. Default: true.</summary>
    public bool FooterMustBeLastLine { get; set; } = true;

    /// <summary>Minimum number of detail records. Null = no minimum.</summary>
    public int? MinRecords { get; set; }

    /// <summary>Maximum number of detail records. Null = no maximum.</summary>
    public int? MaxRecords { get; set; }
}

/// <summary>
/// Field-level validation rule.
/// Defines validation constraints for a single field.
/// </summary>
public class FieldValidationRule
{
    /// <summary>Field name (column name in database/parsed record).</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>Human-readable field label for AI-friendly error messages.</summary>
    public string FieldLabel { get; set; } = string.Empty;

    /// <summary>Position index in delimited file (0-based).</summary>
    public int FieldIndex { get; set; }

    /// <summary>Expected data type.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FieldType Type { get; set; } = FieldType.String;

    /// <summary>Field is required (cannot be null/empty).</summary>
    public bool Required { get; set; } = false;

    // --- DateTime constraints ---

    /// <summary>Date format string (e.g., "yyyy-MM-dd HH:mm:ss").</summary>
    public string? DateFormat { get; set; }

    /// <summary>Date must be in the past (before now).</summary>
    public bool? DateMustBeInPast { get; set; }

    /// <summary>Date must be in the future (after now).</summary>
    public bool? DateMustBeInFuture { get; set; }

    /// <summary>Minimum allowed date value.</summary>
    public DateTime? DateMinValue { get; set; }

    /// <summary>Maximum allowed date value.</summary>
    public DateTime? DateMaxValue { get; set; }

    // --- Numeric constraints ---

    /// <summary>Minimum numeric value.</summary>
    public decimal? MinValue { get; set; }

    /// <summary>Maximum numeric value.</summary>
    public decimal? MaxValue { get; set; }

    /// <summary>Value must be non-negative (>= 0).</summary>
    public bool? MustBeNonNegative { get; set; }

    /// <summary>Value must be positive (> 0).</summary>
    public bool? MustBePositive { get; set; }

    // --- String constraints ---

    /// <summary>Minimum string length.</summary>
    public int? MinLength { get; set; }

    /// <summary>Maximum string length.</summary>
    public int? MaxLength { get; set; }

    /// <summary>Regex pattern the value must match.</summary>
    public string? RegexPattern { get; set; }

    /// <summary>List of allowed values (enum validation).</summary>
    public List<string>? AllowedValues { get; set; }

    /// <summary>
    /// Gets the effective label (FieldLabel if set, otherwise FieldName).
    /// </summary>
    public string EffectiveLabel => string.IsNullOrEmpty(FieldLabel) ? FieldName : FieldLabel;
}

/// <summary>
/// Field data types for validation.
/// </summary>
public enum FieldType
{
    /// <summary>String/text field.</summary>
    String,

    /// <summary>32-bit integer.</summary>
    Integer,

    /// <summary>64-bit integer.</summary>
    Long,

    /// <summary>Decimal number.</summary>
    Decimal,

    /// <summary>Date/time value.</summary>
    DateTime,

    /// <summary>Boolean (true/false).</summary>
    Boolean
}

/// <summary>
/// Error logging configuration.
/// Controls how errors are aggregated and reported.
/// </summary>
public class ErrorLoggingConfig
{
    /// <summary>
    /// Maximum individual errors to log with full detail.
    /// After this threshold, errors are aggregated by type.
    /// Default: 100.
    /// </summary>
    public int MaxDetailedErrors { get; set; } = 100;

    /// <summary>
    /// Aggregate errors after MaxDetailedErrors threshold.
    /// Default: true.
    /// </summary>
    public bool AggregateAfterMax { get; set; } = true;

    /// <summary>
    /// Include raw data in error logs (may be large).
    /// Default: true.
    /// </summary>
    public bool IncludeRawData { get; set; } = true;

    /// <summary>
    /// Maximum length of raw data to include in error logs.
    /// Default: 500 characters.
    /// </summary>
    public int MaxRawDataLength { get; set; } = 500;

    /// <summary>
    /// Maximum sample values to keep per aggregated error.
    /// Default: 5.
    /// </summary>
    public int MaxSampleValues { get; set; } = 5;
}

// ============================================
// VALIDATION ERROR MODELS (AI-FRIENDLY)
// ============================================

/// <summary>
/// Validation error with AI-friendly metadata.
/// Designed to be consumed by AI agents for conversational explanations.
/// </summary>
public class ValidationError
{
    // --- Core identification ---

    /// <summary>Error code (e.g., "FIELD_PARSE_DECIMAL", "FILE_NO_HEADER").</summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>Error category ("FILE_STRUCTURE", "FIELD_PARSE", "FIELD_CONSTRAINT").</summary>
    public string ErrorCategory { get; set; } = string.Empty;

    // --- Location ---

    /// <summary>Line number in the file (1-based). Null for file-level errors.</summary>
    public int? LineNumber { get; set; }

    /// <summary>Record number (detail record sequence). Null for file-level errors.</summary>
    public int? RecordNumber { get; set; }

    /// <summary>Field name that failed validation. Null for file-level errors.</summary>
    public string? FieldName { get; set; }

    /// <summary>Field index in delimited record. Null for file-level errors.</summary>
    public int? FieldIndex { get; set; }

    // --- Human-readable context (for AI) ---

    /// <summary>Human-readable field label (e.g., "Cost Amount" instead of "NtCost").</summary>
    public string FieldLabel { get; set; } = string.Empty;

    /// <summary>Technical error message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Plain English error message for end users.</summary>
    public string UserMessage { get; set; } = string.Empty;

    /// <summary>Suggestion on how to fix the error.</summary>
    public string? Suggestion { get; set; }

    // --- Raw data for debugging ---

    /// <summary>The raw value that failed validation.</summary>
    public string? RawValue { get; set; }

    /// <summary>The complete raw line from the file.</summary>
    public string? RawLine { get; set; }

    // --- Constraint details (for AI context) ---

    /// <summary>Expected data type (e.g., "Decimal", "DateTime").</summary>
    public string? ExpectedType { get; set; }

    /// <summary>Expected format (e.g., "yyyy-MM-dd HH:mm:ss").</summary>
    public string? ExpectedFormat { get; set; }

    /// <summary>Human-readable constraint description (e.g., "non-negative number up to 999999.99").</summary>
    public string? ConstraintDescription { get; set; }

    // --- Metadata for aggregation ---

    /// <summary>Key for grouping similar errors during aggregation.</summary>
    public string AggregationKey => $"{ErrorCode}:{FieldName ?? "FILE"}";

    /// <summary>True if this is a file-level error that rejects the entire file.</summary>
    public bool IsFileLevelError => ErrorCategory == ValidationErrorCategory.FileStructure;
}

/// <summary>
/// Aggregated error summary for a specific error type.
/// Created when errors exceed MaxDetailedErrors threshold.
/// </summary>
public class AggregatedError
{
    /// <summary>Error code shared by all aggregated errors.</summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>Field name (null for file-level errors).</summary>
    public string? FieldName { get; set; }

    /// <summary>Human-readable field label.</summary>
    public string FieldLabel { get; set; } = string.Empty;

    /// <summary>Representative user message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Total count of this error type.</summary>
    public int Count { get; set; }

    /// <summary>Sample line numbers where this error occurred (first few).</summary>
    public List<int> SampleLineNumbers { get; set; } = new();

    /// <summary>Sample raw values that caused this error (first few).</summary>
    public List<string?> SampleValues { get; set; } = new();
}

/// <summary>
/// Complete validation result with detailed and aggregated errors.
/// </summary>
public class FileValidationResult
{
    /// <summary>True if no validation errors were found.</summary>
    public bool IsValid { get; set; }

    /// <summary>File type code.</summary>
    public string FileType { get; set; } = string.Empty;

    /// <summary>Total records processed.</summary>
    public int TotalRecords { get; set; }

    /// <summary>Records that passed validation.</summary>
    public int ValidRecords { get; set; }

    /// <summary>Records that failed validation.</summary>
    public int InvalidRecords { get; set; }

    /// <summary>Total error count (including aggregated).</summary>
    public int TotalErrors { get; set; }

    /// <summary>Detailed errors (up to MaxDetailedErrors).</summary>
    public List<ValidationError> DetailedErrors { get; set; } = new();

    /// <summary>Aggregated errors (after threshold exceeded).</summary>
    public List<AggregatedError> AggregatedErrors { get; set; } = new();

    /// <summary>File-level errors (always stored with full detail).</summary>
    public List<ValidationError> FileLevelErrors { get; set; } = new();

    /// <summary>Summary string for logging/display.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>AI-consumable summary structure.</summary>
    public ValidationSummaryForAI AISummary { get; set; } = new();

    /// <summary>True if there are file-level errors that should reject the file.</summary>
    public bool HasFileLevelErrors => FileLevelErrors.Count > 0;

    /// <summary>True if there are any record-level errors.</summary>
    public bool HasRecordErrors => DetailedErrors.Count > 0 || AggregatedErrors.Count > 0;
}

/// <summary>
/// Structured summary designed for AI agent consumption.
/// Enables AI to explain errors conversationally.
/// </summary>
public class ValidationSummaryForAI
{
    /// <summary>Overall status in plain English (e.g., "File rejected due to validation errors").</summary>
    public string OverallStatus { get; set; } = string.Empty;

    /// <summary>Top issues in plain English sentences.</summary>
    public List<string> MainIssues { get; set; } = new();

    /// <summary>Error counts grouped by field name.</summary>
    public Dictionary<string, int> ErrorCountsByField { get; set; } = new();

    /// <summary>Error counts grouped by error code.</summary>
    public Dictionary<string, int> ErrorCountsByType { get; set; } = new();

    /// <summary>Suggested actions to fix the issues.</summary>
    public List<string> SuggestedActions { get; set; } = new();

    /// <summary>True if the file can be partially processed (some records valid).</summary>
    public bool CanPartiallyProcess { get; set; }
}

/// <summary>
/// Error category constants.
/// </summary>
public static class ValidationErrorCategory
{
    /// <summary>File structure errors (header/footer/sequence).</summary>
    public const string FileStructure = "FILE_STRUCTURE";

    /// <summary>Field parsing errors (type conversion failures).</summary>
    public const string FieldParse = "FIELD_PARSE";

    /// <summary>Field constraint errors (range, pattern, required).</summary>
    public const string FieldConstraint = "FIELD_CONSTRAINT";
}

/// <summary>
/// Validation error code constants.
/// </summary>
public static class ValidationErrorCodes
{
    // --- File-level errors ---
    public const string FileNoHeader = "FILE_NO_HEADER";
    public const string FileNoFooter = "FILE_NO_FOOTER";
    public const string FileFooterCount = "FILE_FOOTER_COUNT";
    public const string FileSequenceGap = "FILE_SEQ_GAP";
    public const string FileHeaderWrongPlace = "FILE_HEADER_WRONG_PLACE";
    public const string FileMultipleHeaders = "FILE_MULTIPLE_HEADERS";
    public const string FileMultipleFooters = "FILE_MULTIPLE_FOOTERS";
    public const string FileEmpty = "FILE_EMPTY";
    public const string FileTooFewRecords = "FILE_TOO_FEW_RECORDS";
    public const string FileTooManyRecords = "FILE_TOO_MANY_RECORDS";

    // --- Field-level errors ---
    public const string FieldRequired = "FIELD_REQUIRED";
    public const string FieldParseInteger = "FIELD_PARSE_INTEGER";
    public const string FieldParseLong = "FIELD_PARSE_LONG";
    public const string FieldParseDecimal = "FIELD_PARSE_DECIMAL";
    public const string FieldParseDateTime = "FIELD_PARSE_DATETIME";
    public const string FieldParseBoolean = "FIELD_PARSE_BOOLEAN";
    public const string FieldConstraintMin = "FIELD_CONSTRAINT_MIN";
    public const string FieldConstraintMax = "FIELD_CONSTRAINT_MAX";
    public const string FieldConstraintNegative = "FIELD_CONSTRAINT_NEGATIVE";
    public const string FieldConstraintNotPositive = "FIELD_CONSTRAINT_NOT_POSITIVE";
    public const string FieldConstraintDateFuture = "FIELD_CONSTRAINT_DATE_FUTURE";
    public const string FieldConstraintDatePast = "FIELD_CONSTRAINT_DATE_PAST";
    public const string FieldConstraintDateMin = "FIELD_CONSTRAINT_DATE_MIN";
    public const string FieldConstraintDateMax = "FIELD_CONSTRAINT_DATE_MAX";
    public const string FieldConstraintLength = "FIELD_CONSTRAINT_LENGTH";
    public const string FieldConstraintMinLength = "FIELD_CONSTRAINT_MIN_LENGTH";
    public const string FieldConstraintMaxLength = "FIELD_CONSTRAINT_MAX_LENGTH";
    public const string FieldConstraintPattern = "FIELD_CONSTRAINT_PATTERN";
    public const string FieldConstraintEnum = "FIELD_CONSTRAINT_ENUM";
}
