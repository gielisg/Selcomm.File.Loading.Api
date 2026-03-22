namespace FileLoading.Models;

// ============================================
// Lookup / Reference Table Models
// ============================================

/// <summary>
/// File class record (file_class table).
/// Classification of file types (e.g., CDR, CHG).
/// </summary>
public class FileClassRecord
{
    /// <summary>File class code (PK).</summary>
    public string FileClassCode { get; set; } = string.Empty;

    /// <summary>File class description.</summary>
    public string FileClassNarr { get; set; } = string.Empty;
}

/// <summary>
/// File type record (file_type table).
/// Defines file types with FK to file_class and networks.
/// </summary>
public class FileTypeRecord
{
    /// <summary>File type code (PK).</summary>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>File type description.</summary>
    public string FileTypeNarr { get; set; } = string.Empty;

    /// <summary>File class code (FK to file_class).</summary>
    public string FileClassCode { get; set; } = string.Empty;

    /// <summary>Network/vendor ID (FK to networks).</summary>
    public string? NetworkId { get; set; }

    /// <summary>File class description (populated by JOIN, read-only).</summary>
    public string? FileClassNarr { get; set; }

    /// <summary>Network/vendor name (populated by JOIN, read-only).</summary>
    public string? NetworkNarr { get; set; }
}

/// <summary>
/// File type NT record (file_type_nt table).
/// Network-specific file type configuration.
/// </summary>
public class FileTypeNtRecord
{
    /// <summary>File type code (FK to file_type).</summary>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Network customer number.</summary>
    public string NtCustNum { get; set; } = string.Empty;

    /// <summary>Last sequence number processed.</summary>
    public int LastSeq { get; set; }

    /// <summary>Default business unit code.</summary>
    public string? DefaultBusUnit { get; set; }

    /// <summary>Plan code.</summary>
    public int? PlanCode { get; set; }

    /// <summary>Expected frequency (W=Weekly, M=Monthly).</summary>
    public string? ExpectedFreq { get; set; }

    /// <summary>Frequency files count.</summary>
    public int? FreqFiles { get; set; }

    /// <summary>File type description (populated by JOIN, read-only).</summary>
    public string? FileTypeNarr { get; set; }

    /// <summary>Created by user.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Updated by user.</summary>
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Vendor/network record (networks table, accessible via vendors synonym/view).
/// </summary>
public class VendorRecord
{
    /// <summary>Network ID (PK, CHAR(2)).</summary>
    public string NetworkId { get; set; } = string.Empty;

    /// <summary>Network/vendor name.</summary>
    public string NetworkNarr { get; set; } = string.Empty;
}
