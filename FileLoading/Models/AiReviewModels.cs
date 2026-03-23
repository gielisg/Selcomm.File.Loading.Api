namespace FileLoading.Models;

// ============================================
// Configuration
// ============================================

/// <summary>
/// Operational defaults for the AI review feature, bound from the "AiReview" section of appsettings.json.
/// Per-domain configuration (API key, model, limits) is stored in the database.
/// </summary>
public class AiReviewOptions
{
    /// <summary>Base URL for the AI Gateway (Selcomm.Ai.Api).</summary>
    /// <example>http://localhost:5300</example>
    public string GatewayUrl { get; set; } = "http://localhost:5142";

    /// <summary>Maximum number of sample records to send for AI review.</summary>
    /// <example>80</example>
    public int MaxSampleRecords { get; set; } = 80;

    /// <summary>HTTP request timeout in seconds for AI gateway calls.</summary>
    /// <example>120</example>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Duration in minutes to cache AI review results before requiring a refresh.</summary>
    /// <example>1440</example>
    public int CacheDurationMinutes { get; set; } = 1440;
}

// ============================================
// Request Models
// ============================================

/// <summary>
/// Request body for POST /ai-review/files/{ntFileNum}.
/// Triggers an AI review of a loaded file's detail records.
/// </summary>
public class AiReviewRequest
{
    /// <summary>Optional example file content to use instead of the stored example for comparison.</summary>
    public string? ExampleFileContent { get; set; }

    /// <summary>Optional list of focus areas for the review (e.g., "date formats", "missing fields").</summary>
    public List<string>? FocusAreas { get; set; }

    /// <summary>Force a new review even if a cached result exists.</summary>
    /// <example>false</example>
    public bool ForceRefresh { get; set; }
}

/// <summary>
/// Request body for POST /ai-review/content.
/// Allows reviewing pasted or uploaded file content directly without loading into the database first.
/// </summary>
public class AiContentReviewRequest
{
    /// <summary>The raw file content to review.</summary>
    /// <example>H,TEL_GSM,20250315,43\nD,0412345678,20250315083000,300,1.50\nT,1,1.50</example>
    public string FileContent { get; set; } = string.Empty;

    /// <summary>Optional file type code for looking up the expected file specification.</summary>
    /// <example>TEL_GSM</example>
    public string? FileTypeCode { get; set; }

    /// <summary>Optional file name for display in the review results.</summary>
    /// <example>CDR_20250315_001.csv</example>
    public string? FileName { get; set; }

    /// <summary>Optional example file content to compare the reviewed content against.</summary>
    public string? ExampleFileContent { get; set; }

    /// <summary>Optional list of focus areas for the review.</summary>
    public List<string>? FocusAreas { get; set; }
}

/// <summary>
/// Request body for PUT /ai-review/example-files/{fileTypeCode}.
/// Sets or updates the example file used as a reference for AI reviews of the specified file type.
/// </summary>
public class ExampleFileRequest
{
    /// <summary>Server-side path to the example file.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Example/CDR_example.csv</example>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Human-readable description of the example file.</summary>
    /// <example>Standard Telstra GSM CDR format with header, 10 detail records, and trailer</example>
    public string? Description { get; set; }
}

/// <summary>
/// Request body for PUT /ai-review/config.
/// Creates or updates the per-domain AI review configuration.
/// </summary>
public class AiDomainConfigRequest
{
    /// <summary>Anthropic API key for making Claude API requests.</summary>
    /// <example>sk-ant-api03-...</example>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Claude model to use for reviews (null to keep default).</summary>
    /// <example>claude-sonnet-4-20250514</example>
    public string? Model { get; set; }

    /// <summary>Whether AI review is enabled for this domain.</summary>
    /// <example>true</example>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum number of AI reviews allowed per day (rate limiting).</summary>
    /// <example>50</example>
    public int? MaxReviewsPerDay { get; set; }

    /// <summary>Maximum output tokens for the Claude API response.</summary>
    /// <example>4096</example>
    public int? MaxOutputTokens { get; set; }
}

// ============================================
// Response Models
// ============================================

/// <summary>
/// AI review response returned by both POST (trigger review) and GET (retrieve cached review) endpoints.
/// Contains the AI's assessment, summary, and list of identified issues.
/// </summary>
public class AiReviewResponse
{
    /// <summary>File number that was reviewed.</summary>
    /// <example>12345</example>
    public int NtFileNum { get; set; }

    /// <summary>File type code of the reviewed file.</summary>
    /// <example>TEL_GSM</example>
    public string FileType { get; set; } = string.Empty;

    /// <summary>Overall assessment (e.g., Acceptable, Warning, Rejected).</summary>
    /// <example>Acceptable</example>
    public string OverallAssessment { get; set; } = string.Empty;

    /// <summary>AI-generated summary of the file review findings.</summary>
    /// <example>File structure is valid with standard header/trailer. All 15000 detail records parsed correctly with no anomalies detected.</example>
    public string Summary { get; set; } = string.Empty;

    /// <summary>List of specific issues identified by the AI review.</summary>
    public List<AiReviewIssue> Issues { get; set; } = new();

    /// <summary>Number of records sampled for the review.</summary>
    /// <example>80</example>
    public int RecordsSampled { get; set; }

    /// <summary>Total number of records in the file.</summary>
    /// <example>15000</example>
    public int TotalRecords { get; set; }

    /// <summary>Timestamp when the review was performed.</summary>
    /// <example>2025-03-15T08:10:00Z</example>
    public DateTime ReviewedAt { get; set; }

    /// <summary>Whether this result was served from cache rather than a fresh review.</summary>
    /// <example>false</example>
    public bool IsCached { get; set; }

    /// <summary>Token usage details from the Claude API call (null for cached results).</summary>
    public AiReviewUsage? Usage { get; set; }
}

/// <summary>
/// A specific issue identified during an AI file review.
/// </summary>
public class AiReviewIssue
{
    /// <summary>Severity level of the issue (Info, Warning, Error, Critical).</summary>
    /// <example>Warning</example>
    public string Severity { get; set; } = string.Empty;

    /// <summary>Category of the issue (e.g., Format, DataQuality, Structure, Anomaly).</summary>
    /// <example>DataQuality</example>
    public string Category { get; set; } = string.Empty;

    /// <summary>Detailed description of the issue found.</summary>
    /// <example>3 records have call durations exceeding 24 hours which may indicate data errors</example>
    public string Description { get; set; } = string.Empty;

    /// <summary>Name of the affected field or column, if applicable.</summary>
    /// <example>ClDuration</example>
    public string? AffectedField { get; set; }

    /// <summary>Example values or records illustrating the issue.</summary>
    public List<string>? Examples { get; set; }

    /// <summary>Suggested action to resolve or investigate the issue.</summary>
    /// <example>Review records with cl_duration > 86400 seconds for potential parsing errors</example>
    public string? Suggestion { get; set; }
}

/// <summary>
/// Token usage information from a Claude API call.
/// </summary>
public class AiReviewUsage
{
    /// <summary>Number of input tokens sent to the Claude API.</summary>
    /// <example>12500</example>
    public int InputTokens { get; set; }

    /// <summary>Number of output tokens received from the Claude API.</summary>
    /// <example>1850</example>
    public int OutputTokens { get; set; }

    /// <summary>Claude model used for the review.</summary>
    /// <example>claude-sonnet-4-20250514</example>
    public string Model { get; set; } = string.Empty;
}

// ============================================
// Domain Config Response Models
// ============================================

/// <summary>
/// Stored example file record from the ntfl_ai_example_file table.
/// References a file on disk that serves as the "known good" format for AI comparisons.
/// </summary>
public class ExampleFileRecord
{
    /// <summary>Auto-generated primary key.</summary>
    /// <example>1</example>
    public int ExampleFileId { get; set; }

    /// <summary>File type code this example applies to.</summary>
    /// <example>TEL_GSM</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Server-side path to the example file.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Example/CDR_example.csv</example>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Original uploaded filename.</summary>
    /// <example>CDR_example.csv</example>
    public string? FileName { get; set; }

    /// <summary>Human-readable description of the example file.</summary>
    /// <example>Standard Telstra GSM CDR format with header, 10 detail records, and trailer</example>
    public string? Description { get; set; }

    /// <summary>Timestamp when this record was created.</summary>
    /// <example>2025-01-15T10:30:00Z</example>
    public DateTime CreatedAt { get; set; }

    /// <summary>User who created this record.</summary>
    /// <example>admin</example>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Timestamp when this record was last updated.</summary>
    /// <example>2025-03-20T14:45:00Z</example>
    public DateTime UpdatedAt { get; set; }

    /// <summary>User who last updated this record.</summary>
    /// <example>admin</example>
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Per-domain AI configuration from the ntfl_ai_domain_config table.
/// Stores the API key, model preference, and rate-limiting settings for AI reviews.
/// </summary>
public class AiDomainConfig
{
    /// <summary>Configuration record ID (auto-generated).</summary>
    /// <example>1</example>
    public int ConfigId { get; set; }

    /// <summary>Anthropic API key (masked in API responses).</summary>
    /// <example>sk-ant-***</example>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Claude model to use for reviews.</summary>
    /// <example>claude-sonnet-4-20250514</example>
    public string Model { get; set; } = "claude-sonnet-4-20250514";

    /// <summary>Whether AI review is enabled for this domain.</summary>
    /// <example>true</example>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum number of reviews allowed per day.</summary>
    /// <example>50</example>
    public int MaxReviewsPerDay { get; set; } = 50;

    /// <summary>Maximum output tokens for Claude API responses.</summary>
    /// <example>4096</example>
    public int MaxOutputTokens { get; set; } = 4096;

    /// <summary>Number of reviews performed today (resets daily).</summary>
    /// <example>5</example>
    public int ReviewsToday { get; set; }

    /// <summary>Timestamp when the daily review counter was last reset.</summary>
    /// <example>2025-03-15T00:00:00Z</example>
    public DateTime? ReviewsResetDt { get; set; }

    /// <summary>Timestamp when this config was created.</summary>
    /// <example>2025-01-15T10:30:00Z</example>
    public DateTime? CreatedAt { get; set; }

    /// <summary>User who created this config.</summary>
    /// <example>admin</example>
    public string? CreatedBy { get; set; }

    /// <summary>Timestamp when this config was last updated.</summary>
    /// <example>2025-03-20T14:45:00Z</example>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>User who last updated this config.</summary>
    /// <example>admin</example>
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Status check response for GET /ai-review/config/status.
/// Provides a quick overview of whether AI review is configured and available.
/// </summary>
public class AiConfigStatusResponse
{
    /// <summary>Whether the AI review feature has been configured (API key set).</summary>
    /// <example>true</example>
    public bool IsConfigured { get; set; }

    /// <summary>Whether the AI review feature is currently enabled.</summary>
    /// <example>true</example>
    public bool IsEnabled { get; set; }

    /// <summary>Claude model configured for reviews.</summary>
    /// <example>claude-sonnet-4-20250514</example>
    public string? Model { get; set; }

    /// <summary>Number of reviews performed today.</summary>
    /// <example>5</example>
    public int ReviewsToday { get; set; }

    /// <summary>Maximum reviews allowed per day.</summary>
    /// <example>50</example>
    public int MaxReviewsPerDay { get; set; }
}

// ============================================
// File Analysis Response Models (discovery/configuration)
// ============================================

/// <summary>
/// Request body for POST /ai-review/analyse/{file-type-code}.
/// Triggers AI analysis of example files to discover structure and generate parser config.
/// </summary>
public class AiFileAnalysisRequest
{
    /// <summary>File class hint to select the right analysis instructions (Charge, Usage, Payment).</summary>
    /// <example>Charge</example>
    public string? FileClass { get; set; }

    /// <summary>Optional focus areas for the analysis.</summary>
    public List<string>? FocusAreas { get; set; }
}

/// <summary>
/// AI file analysis response — discovers file structure, maps billing concepts, and suggests parser config.
/// </summary>
public class AiFileAnalysisResponse
{
    /// <summary>File type code that was analysed.</summary>
    /// <example>CRAYON_SUB</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Ingestion readiness rating (HIGH, MEDIUM, LOW).</summary>
    /// <example>HIGH</example>
    public string IngestionReadiness { get; set; } = string.Empty;

    /// <summary>Prose summary of the analysis findings.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Detected file format details.</summary>
    public DetectedFileFormat DetectedFormat { get; set; } = new();

    /// <summary>Discovered column structure.</summary>
    public List<DiscoveredColumn> Columns { get; set; } = new();

    /// <summary>Billing concept to column mappings with confidence.</summary>
    public List<BillingConceptMapping> BillingConceptMappings { get; set; } = new();

    /// <summary>Data quality issues found during analysis.</summary>
    public List<AiReviewIssue> DataQualityIssues { get; set; } = new();

    /// <summary>Key observations about the file.</summary>
    public List<string> Observations { get; set; } = new();

    /// <summary>Suggested parser configuration ready to save via the parser config endpoint.</summary>
    public SuggestedParserConfig? SuggestedParserConfig { get; set; }

    /// <summary>Token usage details.</summary>
    public AiReviewUsage? Usage { get; set; }

    /// <summary>Timestamp of the analysis.</summary>
    public DateTime AnalysedAt { get; set; }
}

/// <summary>
/// Detected file format from AI analysis.
/// </summary>
public class DetectedFileFormat
{
    /// <summary>File format (CSV, XLSX, Delimited).</summary>
    /// <example>CSV</example>
    public string FileFormat { get; set; } = string.Empty;

    /// <summary>Detected delimiter character.</summary>
    /// <example>,</example>
    public string? Delimiter { get; set; }

    /// <summary>Whether the file has a header row.</summary>
    public bool HasHeaderRow { get; set; }

    /// <summary>Detected file encoding.</summary>
    /// <example>UTF-8</example>
    public string? Encoding { get; set; }

    /// <summary>Number of header rows detected.</summary>
    public int HeaderRowCount { get; set; }

    /// <summary>Number of trailer/footer rows detected.</summary>
    public int TrailerRowCount { get; set; }

    /// <summary>Number of data rows.</summary>
    public int DataRowCount { get; set; }
}

/// <summary>
/// A column discovered during AI file analysis.
/// </summary>
public class DiscoveredColumn
{
    /// <summary>Zero-based column index.</summary>
    /// <example>0</example>
    public int Index { get; set; }

    /// <summary>Column name from header row (if present).</summary>
    /// <example>ResellerName</example>
    public string? Name { get; set; }

    /// <summary>Detected data type (String, Integer, Decimal, Date, DateTime, GUID).</summary>
    /// <example>String</example>
    public string DataType { get; set; } = "String";

    /// <summary>Sample values from the column.</summary>
    public List<string> SampleValues { get; set; } = new();

    /// <summary>Suggested target field from GenericTargetField enum.</summary>
    /// <example>AccountCode</example>
    public string? SuggestedTargetField { get; set; }
}

/// <summary>
/// Mapping from a billing concept to a source column, with confidence level.
/// </summary>
public class BillingConceptMapping
{
    /// <summary>Business billing concept (e.g., "Customer Name", "Buy Price", "Period Start").</summary>
    /// <example>Customer Name</example>
    public string BillingConcept { get; set; } = string.Empty;

    /// <summary>Source column name.</summary>
    /// <example>CustomerName</example>
    public string SourceColumn { get; set; } = string.Empty;

    /// <summary>Zero-based column index.</summary>
    /// <example>15</example>
    public int ColumnIndex { get; set; }

    /// <summary>Confidence level (HIGH, MEDIUM, LOW).</summary>
    /// <example>HIGH</example>
    public string Confidence { get; set; } = "MEDIUM";
}

/// <summary>
/// Suggested parser configuration generated by AI analysis.
/// Can be saved directly via the parser config endpoints.
/// </summary>
public class SuggestedParserConfig
{
    /// <summary>File format (CSV, XLSX, Delimited).</summary>
    public string FileFormat { get; set; } = "CSV";

    /// <summary>Field delimiter.</summary>
    public string? Delimiter { get; set; }

    /// <summary>Whether file has a header row.</summary>
    public bool HasHeaderRow { get; set; }

    /// <summary>Rows to skip at top.</summary>
    public int SkipRowsTop { get; set; }

    /// <summary>Rows to skip at bottom.</summary>
    public int SkipRowsBottom { get; set; }

    /// <summary>Row identification mode (POSITION, INDICATOR, PATTERN).</summary>
    public string RowIdMode { get; set; } = "POSITION";

    /// <summary>Default date format.</summary>
    public string? DateFormat { get; set; }

    /// <summary>Suggested column mappings.</summary>
    public List<SuggestedColumnMapping> ColumnMappings { get; set; } = new();
}

/// <summary>
/// A suggested column mapping from AI analysis.
/// </summary>
public class SuggestedColumnMapping
{
    /// <summary>Zero-based column index.</summary>
    public int ColumnIndex { get; set; }

    /// <summary>Source column name from header.</summary>
    public string? SourceColumnName { get; set; }

    /// <summary>Target field (GenericTargetField enum value or Generic01-20).</summary>
    public string TargetField { get; set; } = string.Empty;

    /// <summary>Data type (String, Int, Decimal, Date, DateTime).</summary>
    public string DataType { get; set; } = "String";

    /// <summary>Date format if applicable.</summary>
    public string? DateFormat { get; set; }

    /// <summary>Whether this column is required.</summary>
    public bool IsRequired { get; set; }
}

// ============================================
// Internal Models (AI Gateway)
// ============================================

/// <summary>
/// Request body for the AI Gateway completions endpoint (internal use only).
/// </summary>
internal class GatewayCompletionRequest
{
    public string? Model { get; set; }
    public int MaxTokens { get; set; } = 4096;
    public string? System { get; set; }
    public List<GatewayMessage> Messages { get; set; } = new();
    public string AppName { get; set; } = "file-loading";
    public string? AgentName { get; set; }
    public string? RequestId { get; set; }
    public double? Temperature { get; set; }
}

/// <summary>
/// A message in a gateway completion request (internal use only).
/// </summary>
internal class GatewayMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Response from the AI Gateway completions endpoint (internal use only).
/// </summary>
internal class GatewayCompletionResponse
{
    public string? Id { get; set; }
    public string Model { get; set; } = string.Empty;
    public string? StopReason { get; set; }
    public List<GatewayContentBlock>? Content { get; set; }
    public GatewayUsage? Usage { get; set; }
    public int? UsageLogId { get; set; }
}

/// <summary>
/// Content block in a gateway response (internal use only).
/// </summary>
internal class GatewayContentBlock
{
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Usage info from gateway response (internal use only).
/// </summary>
internal class GatewayUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal? CostEstimate { get; set; }
}

/// <summary>
/// Structured JSON response expected from Claude for file review (internal use only).
/// </summary>
internal class ClaudeReviewResult
{
    public string OverallAssessment { get; set; } = "Acceptable";
    public string Summary { get; set; } = string.Empty;
    public List<AiReviewIssue> Issues { get; set; } = new();
}

/// <summary>
/// Structured JSON response expected from Claude for file analysis (internal use only).
/// </summary>
internal class ClaudeAnalysisResult
{
    public string IngestionReadiness { get; set; } = "MEDIUM";
    public string Summary { get; set; } = string.Empty;
    public DetectedFileFormat DetectedFormat { get; set; } = new();
    public List<DiscoveredColumn> Columns { get; set; } = new();
    public List<BillingConceptMapping> BillingConceptMappings { get; set; } = new();
    public List<AiReviewIssue> DataQualityIssues { get; set; } = new();
    public List<string> Observations { get; set; } = new();
    public SuggestedParserConfig? SuggestedParserConfig { get; set; }
}
