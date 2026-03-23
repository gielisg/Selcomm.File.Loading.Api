namespace FileLoading.Models;

// ============================================
// Lookup / Reference Table Models
// ============================================

/// <summary>
/// File class record from the file_class table.
/// Represents a top-level classification of file types (e.g., CDR for call detail records, CHG for charges).
/// </summary>
public class FileClassRecord
{
    /// <summary>File class code (primary key).</summary>
    /// <example>CDR</example>
    public string FileClassCode { get; set; } = string.Empty;

    /// <summary>Human-readable file class description.</summary>
    /// <example>Call Detail Records</example>
    public string FileClass { get; set; } = string.Empty;
}

/// <summary>
/// File type record from the file_type table.
/// Defines a specific file type with a foreign key to file_class and an optional network/vendor association.
/// </summary>
public class FileTypeRecord
{
    /// <summary>File type code (primary key).</summary>
    /// <example>TEL_GSM</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Human-readable file type description.</summary>
    /// <example>Telstra GSM CDR</example>
    public string FileType { get; set; } = string.Empty;

    /// <summary>File class code (foreign key to file_class).</summary>
    /// <example>CDR</example>
    public string FileClassCode { get; set; } = string.Empty;

    /// <summary>Network/vendor ID (foreign key to networks table). Null if not vendor-specific.</summary>
    /// <example>TL</example>
    public string? NetworkId { get; set; }

    /// <summary>File class description (populated by JOIN, read-only).</summary>
    /// <example>Call Detail Records</example>
    public string? FileClass { get; set; }

    /// <summary>Network/vendor name (populated by JOIN, read-only).</summary>
    /// <example>Telstra</example>
    public string? Network { get; set; }

    /// <summary>Updated by user (audit column, not returned in GET responses).</summary>
    /// <example>admin</example>
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// File type NT record from the file_type_nt table.
/// Network-specific file type configuration including sequence tracking and business unit defaults.
/// </summary>
public class FileTypeNtRecord
{
    /// <summary>File type code (foreign key to file_type).</summary>
    /// <example>TEL_GSM</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Network customer number identifying the source account.</summary>
    /// <example>CUST001</example>
    public string NtCustNum { get; set; } = string.Empty;

    /// <summary>Last sequence number processed for this file type and customer.</summary>
    /// <example>42</example>
    public int LastSeq { get; set; }

    /// <summary>Default business unit code for records loaded from this file type.</summary>
    /// <example>BU01</example>
    public string? DefaultBusUnit { get; set; }

    /// <summary>Plan code associated with this file type configuration.</summary>
    /// <example>100</example>
    public int? PlanCode { get; set; }

    /// <summary>Expected file delivery frequency (W = Weekly, M = Monthly, D = Daily).</summary>
    /// <example>W</example>
    public string? ExpectedFreq { get; set; }

    /// <summary>Expected number of files per frequency period.</summary>
    /// <example>7</example>
    public int? FreqFiles { get; set; }

    /// <summary>File type description (populated by JOIN, read-only).</summary>
    /// <example>Telstra GSM CDR</example>
    public string? FileType { get; set; }

    /// <summary>User who created this record.</summary>
    /// <example>admin</example>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>User who last updated this record.</summary>
    /// <example>admin</example>
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Vendor/network record from the networks table.
/// Represents a telecommunications network or vendor that provides data files.
/// </summary>
public class VendorRecord
{
    /// <summary>Network ID (primary key, CHAR(2)).</summary>
    /// <example>TL</example>
    public string NetworkId { get; set; } = string.Empty;

    /// <summary>Network/vendor display name.</summary>
    /// <example>Telstra</example>
    public string Network { get; set; } = string.Empty;
}
