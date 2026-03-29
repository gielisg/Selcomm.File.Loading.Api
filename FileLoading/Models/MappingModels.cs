namespace FileLoading.Models;

// ============================================
// Account Mapping Models (ntfl_acct_map)
// ============================================

/// <summary>
/// Account mapping record from the ntfl_acct_map table.
/// Maps a file value (mapping_string) to an account for a given file type.
/// </summary>
public class NtflAcctMapRecord
{
    /// <summary>Auto-increment primary key.</summary>
    public int Id { get; set; }

    /// <summary>File type code (foreign key to file_type).</summary>
    /// <example>TEL_GSM</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Account code / debtor code (foreign key to account).</summary>
    /// <example>1000000001</example>
    public string AccountCode { get; set; } = string.Empty;

    /// <summary>The value from the file used to identify this account (exact match).</summary>
    /// <example>ACCT-001</example>
    public string MappingString { get; set; } = string.Empty;

    /// <summary>Match priority order (lower = higher priority).</summary>
    /// <example>0</example>
    public int SeqNo { get; set; }

    /// <summary>Account name (populated by JOIN, read-only).</summary>
    public string? AccountName { get; set; }

    /// <summary>Last updated timestamp (audit column).</summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>Updated by user (audit column).</summary>
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Request DTO for creating or updating an account mapping.
/// </summary>
public class NtflAcctMapRequest
{
    /// <summary>File type code (foreign key to file_type).</summary>
    /// <example>TEL_GSM</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Account code / debtor code (foreign key to account).</summary>
    /// <example>1000000001</example>
    public string AccountCode { get; set; } = string.Empty;

    /// <summary>The value from the file used to identify this account (exact match).</summary>
    /// <example>ACCT-001</example>
    public string MappingString { get; set; } = string.Empty;

    /// <summary>Match priority order (lower = higher priority).</summary>
    /// <example>0</example>
    public int SeqNo { get; set; }
}

// ============================================
// Service Mapping Models (ntfl_svc_map)
// ============================================

/// <summary>
/// Service mapping record from the ntfl_svc_map table.
/// Maps a file value (mapping_string) to a service (sp_connection) for a given file type.
/// </summary>
public class NtflSvcMapRecord
{
    /// <summary>Auto-increment primary key.</summary>
    public int Id { get; set; }

    /// <summary>File type code (foreign key to file_type).</summary>
    /// <example>TEL_GSM</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Service reference (foreign key to sp_connection.sp_cn_ref).</summary>
    /// <example>12345</example>
    public int ServiceReference { get; set; }

    /// <summary>The value from the file used to identify this service (exact match).</summary>
    /// <example>0412345678</example>
    public string MappingString { get; set; } = string.Empty;

    /// <summary>Match priority order (lower = higher priority).</summary>
    /// <example>0</example>
    public int SeqNo { get; set; }

    /// <summary>Phone number of the service (populated by JOIN, read-only).</summary>
    public string? PhoneNum { get; set; }

    /// <summary>Last updated timestamp (audit column).</summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>Updated by user (audit column).</summary>
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Request DTO for creating or updating a service mapping.
/// </summary>
public class NtflSvcMapRequest
{
    /// <summary>File type code (foreign key to file_type).</summary>
    /// <example>TEL_GSM</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Service reference (foreign key to sp_connection.sp_cn_ref).</summary>
    /// <example>12345</example>
    public int ServiceReference { get; set; }

    /// <summary>The value from the file used to identify this service (exact match).</summary>
    /// <example>0412345678</example>
    public string MappingString { get; set; } = string.Empty;

    /// <summary>Match priority order (lower = higher priority).</summary>
    /// <example>0</example>
    public int SeqNo { get; set; }
}
