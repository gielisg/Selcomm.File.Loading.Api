namespace FileLoading.Models;

/// <summary>
/// Metadata for a custom staging table version — mirrors the ntfl_custom_table row.
/// </summary>
public class CustomTableMetadata
{
    /// <summary>Auto-increment primary key.</summary>
    /// <example>1</example>
    public int CustomTableId { get; set; }

    /// <summary>File type code this table was created for.</summary>
    /// <example>OPTUS_CHG</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Physical table name in the database.</summary>
    /// <example>ntfl_optus_chg_v1</example>
    public string TableName { get; set; } = string.Empty;

    /// <summary>Version number (1, 2, 3...).</summary>
    /// <example>1</example>
    public int Version { get; set; } = 1;

    /// <summary>Table status: ACTIVE, RETIRED, or DROPPED.</summary>
    /// <example>ACTIVE</example>
    public string Status { get; set; } = "ACTIVE";

    /// <summary>Number of data columns (excluding PK and status).</summary>
    /// <example>8</example>
    public int ColumnCount { get; set; }

    /// <summary>JSON snapshot of column mappings at the time the table was created.</summary>
    /// <example>[{"ColumnName":"account_code","SqlType":"VARCHAR(64)","SourceField":"AccountCode","DataType":"String"}]</example>
    public string? ColumnDefinition { get; set; }

    /// <summary>When this table version was created.</summary>
    /// <example>2026-03-23T10:30:00</example>
    public DateTime CreatedDt { get; set; }

    /// <summary>User who created this table version.</summary>
    /// <example>admin</example>
    public string? CreatedBy { get; set; }

    /// <summary>When this table was dropped (null if not dropped).</summary>
    /// <example>null</example>
    public DateTime? DroppedDt { get; set; }
}

/// <summary>
/// Response for GET custom-table: all versions for a file type.
/// </summary>
public class CustomTableInfo
{
    /// <summary>File type code.</summary>
    /// <example>OPTUS_CHG</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>The currently active version (null if none).</summary>
    public CustomTableMetadata? ActiveVersion { get; set; }

    /// <summary>All versions including retired and dropped.</summary>
    public List<CustomTableMetadata> AllVersions { get; set; } = new();
}

/// <summary>
/// Response for POST propose: the proposed DDL for a new custom table.
/// </summary>
public class CustomTableProposal
{
    /// <summary>File type code.</summary>
    /// <example>OPTUS_CHG</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Proposed physical table name.</summary>
    /// <example>ntfl_optus_chg_v1</example>
    public string TableName { get; set; } = string.Empty;

    /// <summary>Proposed version number.</summary>
    /// <example>1</example>
    public int ProposedVersion { get; set; }

    /// <summary>Generated CREATE TABLE DDL statement.</summary>
    /// <example>CREATE TABLE ntfl_optus_chg_v1 (...)</example>
    public string Ddl { get; set; } = string.Empty;

    /// <summary>Column definitions within the proposed table.</summary>
    public List<CustomTableColumnDef> Columns { get; set; } = new();
}

/// <summary>
/// Column definition within a custom table proposal.
/// </summary>
public class CustomTableColumnDef
{
    /// <summary>Database column name (snake_case).</summary>
    /// <example>account_code</example>
    public string ColumnName { get; set; } = string.Empty;

    /// <summary>SQL data type.</summary>
    /// <example>VARCHAR(64)</example>
    public string SqlType { get; set; } = string.Empty;

    /// <summary>Whether the column is NOT NULL.</summary>
    /// <example>true</example>
    public bool IsRequired { get; set; }

    /// <summary>Source target field name from the parser config.</summary>
    /// <example>AccountCode</example>
    public string SourceField { get; set; } = string.Empty;

    /// <summary>Data type from the column mapping.</summary>
    /// <example>String</example>
    public string DataType { get; set; } = string.Empty;
}

/// <summary>
/// Response for POST test-load: summary of the test file load.
/// </summary>
public class TestLoadResult
{
    /// <summary>NT file number of the test load record.</summary>
    /// <example>12345</example>
    public int NtFileNum { get; set; }

    /// <summary>Number of records successfully loaded.</summary>
    /// <example>150</example>
    public int RecordsLoaded { get; set; }

    /// <summary>Number of records that failed validation.</summary>
    /// <example>2</example>
    public int RecordsFailed { get; set; }

    /// <summary>Parse/validation errors encountered during loading.</summary>
    public List<string> Errors { get; set; } = new();
}
