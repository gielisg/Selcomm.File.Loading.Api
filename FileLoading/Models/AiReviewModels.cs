namespace FileLoading.Models;

// ============================================
// Configuration
// ============================================

/// <summary>
/// Operational defaults bound from appsettings "AiReview" section.
/// Per-domain config (API key, model, limits) is in the database.
/// </summary>
public class AiReviewOptions
{
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string AnthropicVersion { get; set; } = "2023-06-01";
    public int MaxSampleRecords { get; set; } = 80;
    public int TimeoutSeconds { get; set; } = 60;
    public int CacheDurationMinutes { get; set; } = 1440;
}

// ============================================
// Request Models
// ============================================

/// <summary>
/// Request body for POST /ai-review/files/{ntFileNum}.
/// </summary>
public class AiReviewRequest
{
    /// <summary>Optional example file content to override stored example.</summary>
    public string? ExampleFileContent { get; set; }

    /// <summary>Optional focus areas for the review.</summary>
    public List<string>? FocusAreas { get; set; }

    /// <summary>Force a new review even if a cached one exists.</summary>
    public bool ForceRefresh { get; set; }
}

/// <summary>
/// Request body for PUT /ai-review/example-files/{fileTypeCode}.
/// </summary>
public class ExampleFileRequest
{
    /// <summary>Path to the example file on the server.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Description of the example file.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Request body for PUT /ai-review/config.
/// </summary>
public class AiDomainConfigRequest
{
    public string ApiKey { get; set; } = string.Empty;
    public string? Model { get; set; }
    public bool Enabled { get; set; } = true;
    public int? MaxReviewsPerDay { get; set; }
    public int? MaxOutputTokens { get; set; }
}

// ============================================
// Response Models
// ============================================

/// <summary>
/// AI review response returned by both POST and GET endpoints.
/// </summary>
public class AiReviewResponse
{
    public int NtFileNum { get; set; }
    public string FileType { get; set; } = string.Empty;
    public string OverallAssessment { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<AiReviewIssue> Issues { get; set; } = new();
    public int RecordsSampled { get; set; }
    public int TotalRecords { get; set; }
    public DateTime ReviewedAt { get; set; }
    public bool IsCached { get; set; }
    public AiReviewUsage? Usage { get; set; }
}

public class AiReviewIssue
{
    public string Severity { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? AffectedField { get; set; }
    public List<string>? Examples { get; set; }
    public string? Suggestion { get; set; }
}

public class AiReviewUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string Model { get; set; } = string.Empty;
}

// ============================================
// Domain Config Response Models
// ============================================

/// <summary>
/// Stored example file record (from ntfl_ai_example_file).
/// </summary>
public class ExampleFileRecord
{
    public string FileTypeCode { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Per-domain AI config (from ntfl_ai_domain_config).
/// </summary>
public class AiDomainConfig
{
    public string Domain { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public bool Enabled { get; set; } = true;
    public int MaxReviewsPerDay { get; set; } = 50;
    public int MaxOutputTokens { get; set; } = 4096;
    public int ReviewsToday { get; set; }
    public DateTime? ReviewsResetDt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Status check response for GET /ai-review/config/status.
/// </summary>
public class AiConfigStatusResponse
{
    public bool IsConfigured { get; set; }
    public bool IsEnabled { get; set; }
    public string? Model { get; set; }
    public int ReviewsToday { get; set; }
    public int MaxReviewsPerDay { get; set; }
}

// ============================================
// Internal Models (Claude API)
// ============================================

/// <summary>
/// Claude Messages API request body.
/// </summary>
internal class ClaudeMessagesRequest
{
    public string Model { get; set; } = string.Empty;
    public int Max_tokens { get; set; }
    public string? System { get; set; }
    public List<ClaudeMessage> Messages { get; set; } = new();
}

internal class ClaudeMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Claude Messages API response body.
/// </summary>
internal class ClaudeMessagesResponse
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Role { get; set; }
    public List<ClaudeContentBlock>? Content { get; set; }
    public string? Stop_reason { get; set; }
    public ClaudeUsage? Usage { get; set; }
    public ClaudeError? Error { get; set; }
}

internal class ClaudeContentBlock
{
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

internal class ClaudeUsage
{
    public int Input_tokens { get; set; }
    public int Output_tokens { get; set; }
}

internal class ClaudeError
{
    public string? Type { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Structured JSON response expected from Claude for file review.
/// </summary>
internal class ClaudeReviewResult
{
    public string OverallAssessment { get; set; } = "Acceptable";
    public string Summary { get; set; } = string.Empty;
    public List<AiReviewIssue> Issues { get; set; } = new();
}
