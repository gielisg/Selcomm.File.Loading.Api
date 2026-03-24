namespace FileLoading.Models;

/// <summary>
/// Record from the ntfl_chg_map table.
/// Maps file charge descriptions to Selcomm charge codes for a given file type.
/// </summary>
public class NtflChgMapRecord
{
    /// <summary>Auto-generated ID (primary key).</summary>
    public int Id { get; set; }

    /// <summary>File type code (FK to file_type).</summary>
    /// <example>SORACOM</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Charge description pattern from the file. Supports SQL LIKE wildcards (%).</summary>
    /// <example>%Monthly Service%</example>
    public string FileChgDesc { get; set; } = string.Empty;

    /// <summary>Match sequence number. Lower numbers are tried first.</summary>
    /// <example>10</example>
    public int SeqNo { get; set; }

    /// <summary>Selcomm charge code (FK to charge_code).</summary>
    /// <example>MRC</example>
    public string ChgCode { get; set; } = string.Empty;

    /// <summary>Auto-exclude this charge from invoicing (Y/N).</summary>
    /// <example>N</example>
    public string AutoExclude { get; set; } = "N";

    /// <summary>Use the network (net) price instead of retail (Y/N).</summary>
    /// <example>N</example>
    public string UseNetPrice { get; set; } = "N";

    /// <summary>Prorate the net price (Y/N).</summary>
    /// <example>Y</example>
    public string NetPrcProrated { get; set; } = "Y";

    /// <summary>Uplift percentage applied to the charge.</summary>
    /// <example>0.000000</example>
    public decimal UpliftPerc { get; set; }

    /// <summary>Uplift fixed amount applied to the charge (null = not used).</summary>
    /// <example>null</example>
    public decimal? UpliftAmt { get; set; }

    /// <summary>Use the network description instead of charge code description (Y/N).</summary>
    /// <example>N</example>
    public string UseNetDesc { get; set; } = "N";

    /// <summary>Source of this mapping: USER (manual), AI_SUGGESTED (pending review), AI_ACCEPTED (reviewed).</summary>
    /// <example>USER</example>
    public string Source { get; set; } = "USER";

    /// <summary>Last updated timestamp (audit).</summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>Updated by user (audit).</summary>
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Request body for creating or updating a charge mapping.
/// </summary>
public class NtflChgMapRequest
{
    /// <summary>File type code (FK to file_type).</summary>
    /// <example>SORACOM</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Charge description pattern from the file. Supports SQL LIKE wildcards (%).</summary>
    /// <example>%Monthly Service%</example>
    public string FileChgDesc { get; set; } = string.Empty;

    /// <summary>Match sequence number. Lower numbers are tried first.</summary>
    /// <example>10</example>
    public int SeqNo { get; set; }

    /// <summary>Selcomm charge code (FK to charge_code).</summary>
    /// <example>MRC</example>
    public string ChgCode { get; set; } = string.Empty;

    /// <summary>Auto-exclude this charge from invoicing (Y/N).</summary>
    /// <example>N</example>
    public string AutoExclude { get; set; } = "N";

    /// <summary>Use the network (net) price instead of retail (Y/N).</summary>
    /// <example>N</example>
    public string UseNetPrice { get; set; } = "N";

    /// <summary>Prorate the net price (Y/N).</summary>
    /// <example>Y</example>
    public string NetPrcProrated { get; set; } = "Y";

    /// <summary>Uplift percentage applied to the charge.</summary>
    /// <example>0.000000</example>
    public decimal UpliftPerc { get; set; }

    /// <summary>Uplift fixed amount applied to the charge (null = not used).</summary>
    /// <example>null</example>
    public decimal? UpliftAmt { get; set; }

    /// <summary>Use the network description instead of charge code description (Y/N).</summary>
    /// <example>N</example>
    public string UseNetDesc { get; set; } = "N";
}

/// <summary>
/// Result of a charge mapping lookup — the resolved charge details for a given description.
/// </summary>
public class ChargeMapMatch
{
    /// <summary>Matched mapping record ID.</summary>
    public int Id { get; set; }

    /// <summary>The pattern that matched.</summary>
    public string FileChgDesc { get; set; } = string.Empty;

    /// <summary>Resolved Selcomm charge code.</summary>
    public string ChgCode { get; set; } = string.Empty;

    /// <summary>Auto-exclude flag.</summary>
    public string AutoExclude { get; set; } = "N";

    /// <summary>Use net price flag.</summary>
    public string UseNetPrice { get; set; } = "N";

    /// <summary>Net price prorated flag.</summary>
    public string NetPrcProrated { get; set; } = "Y";

    /// <summary>Uplift percentage.</summary>
    public decimal UpliftPerc { get; set; }

    /// <summary>Uplift amount.</summary>
    public decimal? UpliftAmt { get; set; }

    /// <summary>Use net description flag.</summary>
    public string UseNetDesc { get; set; } = "N";
}

// ============================================
// AI Charge Map Seeding Models
// ============================================

/// <summary>
/// AI reasoning record for a charge mapping suggestion (ntfl_chg_map_ai_reason).
/// </summary>
public class ChgMapAiReasonRecord
{
    /// <summary>Auto-increment primary key.</summary>
    /// <example>1</example>
    public int ReasonId { get; set; }

    /// <summary>FK to ntfl_chg_map.id.</summary>
    /// <example>42</example>
    public int ChgMapId { get; set; }

    /// <summary>FK to ntfl_ai_analysis_result.analysis_id.</summary>
    /// <example>5</example>
    public int? AnalysisId { get; set; }

    /// <summary>Raw charge description from the file.</summary>
    /// <example>Monthly Service Fee</example>
    public string FileChgDesc { get; set; } = string.Empty;

    /// <summary>The charge code the AI chose.</summary>
    /// <example>MRC</example>
    public string MatchedChgCode { get; set; } = string.Empty;

    /// <summary>Narrative of the matched charge code (denormalized for display).</summary>
    /// <example>Monthly Recurring Charge</example>
    public string? MatchedChgNarr { get; set; }

    /// <summary>Confidence level: HIGH, MEDIUM, LOW.</summary>
    /// <example>HIGH</example>
    public string Confidence { get; set; } = "MEDIUM";

    /// <summary>AI's explanation of why this mapping was chosen.</summary>
    /// <example>File contains charges described as 'Monthly Service Fee'. The charge_code 'MRC' narrative matches.</example>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>How the AI found this match: NARRATIVE_MATCH, CROSS_REFERENCE, PATTERN_MATCH.</summary>
    /// <example>NARRATIVE_MATCH</example>
    public string MatchMethod { get; set; } = string.Empty;

    /// <summary>If matched via cross-reference, which file type provided the pattern.</summary>
    /// <example>TELSTRA_CHG</example>
    public string? CrossRefFileType { get; set; }

    /// <summary>Sample charge descriptions from the file that led to this pattern.</summary>
    /// <example>Monthly Service Fee, Monthly Service - Data</example>
    public string? SampleValues { get; set; }

    /// <summary>When this suggestion was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Who created this suggestion.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Review status: PENDING, ACCEPTED, REJECTED, MODIFIED.</summary>
    /// <example>PENDING</example>
    public string ReviewStatus { get; set; } = "PENDING";

    /// <summary>When the user reviewed this suggestion.</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Who reviewed it.</summary>
    public string? ReviewedBy { get; set; }
}

/// <summary>
/// Request to trigger AI charge map seeding for a file type.
/// </summary>
public class AiChargeMapSeedRequest
{
    /// <summary>Analysis ID to seed from. If omitted, uses the most recent analysis result.</summary>
    /// <example>5</example>
    public int? AnalysisId { get; set; }

    /// <summary>If true, include cross-reference of other file types in same file class.</summary>
    /// <example>true</example>
    public bool UseCrossReference { get; set; } = true;
}

/// <summary>
/// Response from AI charge map seeding.
/// </summary>
public class AiChargeMapSeedResponse
{
    /// <summary>File type code.</summary>
    /// <example>OPTUS_CHG</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Number of suggestions created.</summary>
    /// <example>8</example>
    public int SuggestionsCreated { get; set; }

    /// <summary>Number of patterns skipped because they already exist.</summary>
    /// <example>2</example>
    public int SkippedExisting { get; set; }

    /// <summary>The individual suggestions.</summary>
    public List<AiChargeMapSuggestion> Suggestions { get; set; } = new();

    /// <summary>Token usage details.</summary>
    public AiReviewUsage? Usage { get; set; }
}

/// <summary>
/// A single AI-suggested charge mapping with reasoning.
/// </summary>
public class AiChargeMapSuggestion
{
    /// <summary>Charge map record ID (ntfl_chg_map.id).</summary>
    /// <example>42</example>
    public int ChgMapId { get; set; }

    /// <summary>Charge description LIKE pattern.</summary>
    /// <example>%Monthly Service%</example>
    public string FileChgDesc { get; set; } = string.Empty;

    /// <summary>Matched Selcomm charge code.</summary>
    /// <example>MRC</example>
    public string ChgCode { get; set; } = string.Empty;

    /// <summary>Charge code narrative.</summary>
    /// <example>Monthly Recurring Charge</example>
    public string ChgNarr { get; set; } = string.Empty;

    /// <summary>Confidence level: HIGH, MEDIUM, LOW.</summary>
    /// <example>HIGH</example>
    public string Confidence { get; set; } = "MEDIUM";

    /// <summary>AI's explanation.</summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>How the AI found this match: NARRATIVE_MATCH, CROSS_REFERENCE, PATTERN_MATCH.</summary>
    /// <example>NARRATIVE_MATCH</example>
    public string MatchMethod { get; set; } = string.Empty;
}

/// <summary>
/// Request to accept/reject/modify an AI-suggested charge mapping.
/// </summary>
public class AiChargeMapReviewRequest
{
    /// <summary>Action: ACCEPT, REJECT, or MODIFY.</summary>
    /// <example>ACCEPT</example>
    public string Action { get; set; } = string.Empty;

    /// <summary>If MODIFY, the corrected charge code.</summary>
    /// <example>MRC2</example>
    public string? CorrectedChgCode { get; set; }

    /// <summary>If MODIFY, the corrected description pattern.</summary>
    /// <example>%Monthly Service Fee%</example>
    public string? CorrectedFileChgDesc { get; set; }
}

/// <summary>
/// Charge code with narrative, for providing context to the AI.
/// </summary>
public class ChargeCodeLookup
{
    /// <summary>Charge code.</summary>
    /// <example>MRC</example>
    public string ChgCode { get; set; } = string.Empty;

    /// <summary>Charge narrative/description.</summary>
    /// <example>Monthly Recurring Charge</example>
    public string ChgNarr { get; set; } = string.Empty;
}

// ============================================
// Configuration Readiness Models
// ============================================

/// <summary>
/// Holistic readiness status for a file type's configuration.
/// </summary>
public class FileTypeReadinessResponse
{
    /// <summary>File type code.</summary>
    /// <example>OPTUS_CHG</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>File type description.</summary>
    /// <example>Optus Wholesale Charges</example>
    public string? FileType { get; set; }

    /// <summary>File class code.</summary>
    /// <example>CHG</example>
    public string? FileClassCode { get; set; }

    /// <summary>Overall readiness level: READY, PARTIAL, NOT_CONFIGURED.</summary>
    /// <example>PARTIAL</example>
    public string ReadinessLevel { get; set; } = "NOT_CONFIGURED";

    /// <summary>Score as percentage (0-100).</summary>
    /// <example>65</example>
    public int ReadinessScore { get; set; }

    /// <summary>Tier-by-tier readiness breakdown.</summary>
    public List<ReadinessTier> Tiers { get; set; } = new();

    /// <summary>Human-readable list of missing configuration steps.</summary>
    public List<string> MissingSteps { get; set; } = new();

    /// <summary>Human-readable list of completed configuration steps.</summary>
    public List<string> CompletedSteps { get; set; } = new();
}

/// <summary>
/// A single tier in the readiness breakdown.
/// </summary>
public class ReadinessTier
{
    /// <summary>Tier number (1-5).</summary>
    /// <example>1</example>
    public int Tier { get; set; }

    /// <summary>Tier name.</summary>
    /// <example>Core Identity</example>
    public string Name { get; set; } = string.Empty;

    /// <summary>Tier status: READY, PARTIAL, NOT_CONFIGURED, NOT_APPLICABLE.</summary>
    /// <example>READY</example>
    public string Status { get; set; } = "NOT_CONFIGURED";

    /// <summary>Individual checks within this tier.</summary>
    public List<ReadinessCheck> Checks { get; set; } = new();
}

/// <summary>
/// A single configuration check within a tier.
/// </summary>
public class ReadinessCheck
{
    /// <summary>Item name.</summary>
    /// <example>Parser Config</example>
    public string Item { get; set; } = string.Empty;

    /// <summary>Whether this item is configured.</summary>
    /// <example>true</example>
    public bool IsConfigured { get; set; }

    /// <summary>Whether this item is required for this file type.</summary>
    /// <example>true</example>
    public bool IsRequired { get; set; }

    /// <summary>Status detail or reason it's missing.</summary>
    /// <example>CSV, comma-delimited, 12 column mappings, active</example>
    public string? Detail { get; set; }
}
