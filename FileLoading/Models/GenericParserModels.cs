using System.Text.RegularExpressions;

namespace FileLoading.Models;

/// <summary>
/// File format types supported by the generic parser.
/// </summary>
public enum FileFormatType
{
    /// <summary>Comma-separated values file.</summary>
    CSV,
    /// <summary>Microsoft Excel spreadsheet (.xlsx).</summary>
    XLSX,
    /// <summary>Custom delimited text file (delimiter specified in config).</summary>
    Delimited
}

/// <summary>
/// Row identification modes for the generic parser.
/// Determines how the parser classifies each row as header, detail, trailer, or skip.
/// </summary>
public enum RowIdMode
{
    /// <summary>Row type determined by position (skip N top/bottom, header row flag).</summary>
    Position,
    /// <summary>Row type determined by a column value matching indicator strings.</summary>
    Indicator,
    /// <summary>Row type determined by regex pattern matching on the full raw line.</summary>
    Pattern
}

/// <summary>
/// Standard target field names for generic column mapping.
/// Maps source file columns to well-known business fields, plus 20 generic overflow columns.
/// </summary>
public enum GenericTargetField
{
    /// <summary>Customer account code.</summary>
    AccountCode,
    /// <summary>Service identifier (e.g., phone number).</summary>
    ServiceId,
    /// <summary>Charge type classification.</summary>
    ChargeType,
    /// <summary>Cost/charge amount (ex-tax).</summary>
    CostAmount,
    /// <summary>Tax amount.</summary>
    TaxAmount,
    /// <summary>Unit quantity.</summary>
    Quantity,
    /// <summary>Unit of measure.</summary>
    UOM,
    /// <summary>Period start date.</summary>
    FromDate,
    /// <summary>Period end date.</summary>
    ToDate,
    /// <summary>Line item description.</summary>
    Description,
    /// <summary>External reference number.</summary>
    ExternalRef,
    /// <summary>Generic overflow column 01.</summary>
    Generic01,
    /// <summary>Generic overflow column 02.</summary>
    Generic02,
    /// <summary>Generic overflow column 03.</summary>
    Generic03,
    /// <summary>Generic overflow column 04.</summary>
    Generic04,
    /// <summary>Generic overflow column 05.</summary>
    Generic05,
    /// <summary>Generic overflow column 06.</summary>
    Generic06,
    /// <summary>Generic overflow column 07.</summary>
    Generic07,
    /// <summary>Generic overflow column 08.</summary>
    Generic08,
    /// <summary>Generic overflow column 09.</summary>
    Generic09,
    /// <summary>Generic overflow column 10.</summary>
    Generic10,
    /// <summary>Generic overflow column 11.</summary>
    Generic11,
    /// <summary>Generic overflow column 12.</summary>
    Generic12,
    /// <summary>Generic overflow column 13.</summary>
    Generic13,
    /// <summary>Generic overflow column 14.</summary>
    Generic14,
    /// <summary>Generic overflow column 15.</summary>
    Generic15,
    /// <summary>Generic overflow column 16.</summary>
    Generic16,
    /// <summary>Generic overflow column 17.</summary>
    Generic17,
    /// <summary>Generic overflow column 18.</summary>
    Generic18,
    /// <summary>Generic overflow column 19.</summary>
    Generic19,
    /// <summary>Generic overflow column 20.</summary>
    Generic20
}

/// <summary>
/// Configuration for a generic file format — mirrors the ntfl_file_format_config table.
/// Defines how a file should be parsed: format, delimiters, row identification, and column mappings.
/// </summary>
public class GenericFileFormatConfig
{
    /// <summary>File type code this configuration applies to (foreign key to file_type).</summary>
    /// <example>VENDOR_CHG</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Configuration version number. Versions are frozen when a custom table is created from them.</summary>
    /// <example>1</example>
    public int ConfigVersion { get; set; } = 1;

    /// <summary>File format type (CSV, XLSX, or Delimited).</summary>
    /// <example>CSV</example>
    public FileFormatType FileFormat { get; set; } = FileFormatType.CSV;

    /// <summary>Field delimiter character or name (comma, tab, pipe, semicolon). Default: comma.</summary>
    /// <example>,</example>
    public string? Delimiter { get; set; } = ",";

    /// <summary>Whether the file contains a header row with column names.</summary>
    /// <example>true</example>
    public bool HasHeaderRow { get; set; }

    /// <summary>Number of rows to skip at the top of the file before data begins.</summary>
    /// <example>0</example>
    public int SkipRowsTop { get; set; }

    /// <summary>Number of rows to skip at the bottom of the file (e.g., summary rows).</summary>
    /// <example>1</example>
    public int SkipRowsBottom { get; set; }

    /// <summary>Row identification mode (Position, Indicator, or Pattern).</summary>
    /// <example>Position</example>
    public RowIdMode RowIdMode { get; set; } = RowIdMode.Position;

    /// <summary>Column index used for row type identification when RowIdMode is Indicator.</summary>
    /// <example>0</example>
    public int RowIdColumn { get; set; }

    /// <summary>Indicator string or regex pattern that identifies header rows.</summary>
    /// <example>H</example>
    public string? HeaderIndicator { get; set; }

    /// <summary>Indicator string or regex pattern that identifies trailer rows.</summary>
    /// <example>T</example>
    public string? TrailerIndicator { get; set; }

    /// <summary>Indicator string or regex pattern that identifies detail (data) rows.</summary>
    /// <example>D</example>
    public string? DetailIndicator { get; set; }

    /// <summary>Indicator string or regex pattern that identifies rows to skip.</summary>
    /// <example>C</example>
    public string? SkipIndicator { get; set; }

    /// <summary>Column index containing the trailer total value (for validation).</summary>
    /// <example>4</example>
    public int? TotalColumnIndex { get; set; }

    /// <summary>Type of total in the trailer (COUNT, SUM_COST, SUM_QTY, etc.).</summary>
    /// <example>COUNT</example>
    public string? TotalType { get; set; }

    /// <summary>Excel sheet name to read (for XLSX format).</summary>
    /// <example>Sheet1</example>
    public string? SheetName { get; set; }

    /// <summary>Excel sheet index to read, 0-based (for XLSX format).</summary>
    /// <example>0</example>
    public int SheetIndex { get; set; }

    /// <summary>Default date format for parsing date columns (e.g., dd/MM/yyyy).</summary>
    /// <example>dd/MM/yyyy</example>
    public string? DateFormat { get; set; }

    /// <summary>Custom stored procedure name to call for post-processing this file type.</summary>
    /// <example>sp_process_vendor_chg</example>
    public string? CustomSpName { get; set; }

    /// <summary>Whether this configuration is currently active.</summary>
    /// <example>true</example>
    public bool Active { get; set; } = true;

    /// <summary>User who created this configuration.</summary>
    /// <example>admin</example>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>User who last updated this configuration.</summary>
    /// <example>admin</example>
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>Column mappings defining how source columns map to target fields.</summary>
    public List<GenericColumnMapping> ColumnMappings { get; set; } = new();

    /// <summary>Get the delimiter character (defaults to comma if not specified).</summary>
    public char GetDelimiterChar()
    {
        if (string.IsNullOrEmpty(Delimiter)) return ',';
        return Delimiter.ToLowerInvariant() switch
        {
            "tab" or "\t" => '\t',
            "pipe" or "|" => '|',
            "semicolon" or ";" => ';',
            _ => Delimiter[0]
        };
    }

    // Pre-compiled regex patterns for Pattern mode (lazily initialized)
    private Regex? _headerPattern;
    private Regex? _trailerPattern;
    private Regex? _detailPattern;
    private Regex? _skipPattern;

    /// <summary>Gets the compiled regex for header row pattern matching.</summary>
    public Regex? GetHeaderPattern() => GetOrCompile(ref _headerPattern, HeaderIndicator);
    /// <summary>Gets the compiled regex for trailer row pattern matching.</summary>
    public Regex? GetTrailerPattern() => GetOrCompile(ref _trailerPattern, TrailerIndicator);
    /// <summary>Gets the compiled regex for detail row pattern matching.</summary>
    public Regex? GetDetailPattern() => GetOrCompile(ref _detailPattern, DetailIndicator);
    /// <summary>Gets the compiled regex for skip row pattern matching.</summary>
    public Regex? GetSkipPattern() => GetOrCompile(ref _skipPattern, SkipIndicator);

    private static Regex? GetOrCompile(ref Regex? field, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return null;
        return field ??= new Regex(pattern, RegexOptions.Compiled);
    }
}

/// <summary>
/// Column mapping configuration — mirrors the ntfl_column_mapping table.
/// Defines how a single source column maps to a target field with optional validation and transformation.
/// </summary>
public class GenericColumnMapping
{
    /// <summary>File type code this mapping belongs to.</summary>
    /// <example>VENDOR_CHG</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>Configuration version number.</summary>
    /// <example>1</example>
    public int ConfigVersion { get; set; } = 1;

    /// <summary>Zero-based column index in the source file.</summary>
    /// <example>0</example>
    public int ColumnIndex { get; set; }

    /// <summary>Column name from the source file header (for display/documentation).</summary>
    /// <example>Account Number</example>
    public string? SourceColumnName { get; set; }

    /// <summary>Target field name to map this column to (from GenericTargetField enum).</summary>
    /// <example>AccountCode</example>
    public string TargetField { get; set; } = string.Empty;

    /// <summary>Expected data type (String, Int, Decimal, Date, DateTime).</summary>
    /// <example>String</example>
    public string DataType { get; set; } = "String";

    /// <summary>Date format for parsing date columns (overrides config-level DateFormat).</summary>
    /// <example>yyyy-MM-dd</example>
    public string? DateFormat { get; set; }

    /// <summary>Whether this column is required (non-null/non-empty).</summary>
    /// <example>true</example>
    public bool IsRequired { get; set; }

    /// <summary>Default value to use when the source column is empty or null.</summary>
    /// <example>UNKNOWN</example>
    public string? DefaultValue { get; set; }

    /// <summary>Regex pattern for validating column values.</summary>
    /// <example>^[A-Z0-9]{6,10}$</example>
    public string? RegexPattern { get; set; }

    /// <summary>Maximum allowed string length.</summary>
    /// <example>50</example>
    public int? MaxLength { get; set; }

    // Pre-compiled regex (lazily initialized)
    private Regex? _compiledRegex;

    /// <summary>Gets the compiled regex for value validation.</summary>
    public Regex? GetCompiledRegex()
    {
        if (string.IsNullOrEmpty(RegexPattern)) return null;
        return _compiledRegex ??= new Regex(RegexPattern, RegexOptions.Compiled);
    }
}

/// <summary>
/// Generic detail record — maps to the ntfl_generic_detail table.
/// Used for configurable file types where column mapping is defined in the database rather than in code.
/// Inherits NtFileNum and NtFileRecNum from FileDetailRecord.
/// </summary>
public class GenericDetailRecord : FileDetailRecord
{
    /// <summary>Customer account code.</summary>
    /// <example>ACC001234</example>
    public string? AccountCode { get; set; }

    /// <summary>Service identifier (e.g., phone number, circuit ID).</summary>
    /// <example>0412345678</example>
    public string? ServiceId { get; set; }

    /// <summary>Charge type classification code.</summary>
    /// <example>USAGE</example>
    public string? ChargeType { get; set; }

    /// <summary>Cost/charge amount (excluding tax).</summary>
    /// <example>25.50</example>
    public decimal? CostAmount { get; set; }

    /// <summary>Tax amount.</summary>
    /// <example>2.55</example>
    public decimal? TaxAmount { get; set; }

    /// <summary>Unit quantity (e.g., call duration, data volume).</summary>
    /// <example>120</example>
    public decimal? Quantity { get; set; }

    /// <summary>Unit of measure (e.g., SEC, MB, EACH).</summary>
    /// <example>SEC</example>
    public string? UOM { get; set; }

    /// <summary>Period or charge start date.</summary>
    /// <example>2025-03-01</example>
    public DateTime? FromDate { get; set; }

    /// <summary>Period or charge end date.</summary>
    /// <example>2025-03-31</example>
    public DateTime? ToDate { get; set; }

    /// <summary>Line item description.</summary>
    /// <example>Local call to 0298765432</example>
    public string? Description { get; set; }

    /// <summary>External reference number from the vendor.</summary>
    /// <example>INV-2025-001234</example>
    public string? ExternalRef { get; set; }

    // Generic overflow columns (Generic01 through Generic20)
    /// <summary>Generic overflow column 01.</summary>
    public string? Generic01 { get; set; }
    /// <summary>Generic overflow column 02.</summary>
    public string? Generic02 { get; set; }
    /// <summary>Generic overflow column 03.</summary>
    public string? Generic03 { get; set; }
    /// <summary>Generic overflow column 04.</summary>
    public string? Generic04 { get; set; }
    /// <summary>Generic overflow column 05.</summary>
    public string? Generic05 { get; set; }
    /// <summary>Generic overflow column 06.</summary>
    public string? Generic06 { get; set; }
    /// <summary>Generic overflow column 07.</summary>
    public string? Generic07 { get; set; }
    /// <summary>Generic overflow column 08.</summary>
    public string? Generic08 { get; set; }
    /// <summary>Generic overflow column 09.</summary>
    public string? Generic09 { get; set; }
    /// <summary>Generic overflow column 10.</summary>
    public string? Generic10 { get; set; }
    /// <summary>Generic overflow column 11.</summary>
    public string? Generic11 { get; set; }
    /// <summary>Generic overflow column 12.</summary>
    public string? Generic12 { get; set; }
    /// <summary>Generic overflow column 13.</summary>
    public string? Generic13 { get; set; }
    /// <summary>Generic overflow column 14.</summary>
    public string? Generic14 { get; set; }
    /// <summary>Generic overflow column 15.</summary>
    public string? Generic15 { get; set; }
    /// <summary>Generic overflow column 16.</summary>
    public string? Generic16 { get; set; }
    /// <summary>Generic overflow column 17.</summary>
    public string? Generic17 { get; set; }
    /// <summary>Generic overflow column 18.</summary>
    public string? Generic18 { get; set; }
    /// <summary>Generic overflow column 19.</summary>
    public string? Generic19 { get; set; }
    /// <summary>Generic overflow column 20.</summary>
    public string? Generic20 { get; set; }

    /// <summary>All parsed field values keyed by TargetField name. Used by custom table inserts.</summary>
    public Dictionary<string, object?> ParsedFields { get; set; } = new();

    /// <summary>Original raw row data for debugging and error investigation.</summary>
    public string? RawData { get; set; }

    /// <summary>Transaction status (default NEW). Updated by Charges Module.</summary>
    /// <example>NEW</example>
    public string StatusId { get; set; } = TransactionStatus.New;

    /// <summary>Contact code — FK to contact (contact_code). Populated by Charges Module.</summary>
    public string? ContactCode { get; set; }

    /// <summary>Service reference — FK to sp_connection (sp_cn_ref). Populated by Charges Module.</summary>
    public int? ServiceReference { get; set; }

    /// <summary>Charge code — FK to charge_code (chg_code). Populated by Charges Module.</summary>
    public string? ChgCode { get; set; }

    /// <summary>Set a generic overflow field by number (1-20).</summary>
    /// <param name="number">Field number (1-20).</param>
    /// <param name="value">Value to set.</param>
    public void SetGenericField(int number, string? value)
    {
        switch (number)
        {
            case 1: Generic01 = value; break;
            case 2: Generic02 = value; break;
            case 3: Generic03 = value; break;
            case 4: Generic04 = value; break;
            case 5: Generic05 = value; break;
            case 6: Generic06 = value; break;
            case 7: Generic07 = value; break;
            case 8: Generic08 = value; break;
            case 9: Generic09 = value; break;
            case 10: Generic10 = value; break;
            case 11: Generic11 = value; break;
            case 12: Generic12 = value; break;
            case 13: Generic13 = value; break;
            case 14: Generic14 = value; break;
            case 15: Generic15 = value; break;
            case 16: Generic16 = value; break;
            case 17: Generic17 = value; break;
            case 18: Generic18 = value; break;
            case 19: Generic19 = value; break;
            case 20: Generic20 = value; break;
        }
    }

    /// <summary>Get a generic overflow field value by number (1-20).</summary>
    /// <param name="number">Field number (1-20).</param>
    /// <returns>The field value, or null if the number is out of range.</returns>
    public string? GetGenericField(int number) => number switch
    {
        1 => Generic01, 2 => Generic02, 3 => Generic03, 4 => Generic04, 5 => Generic05,
        6 => Generic06, 7 => Generic07, 8 => Generic08, 9 => Generic09, 10 => Generic10,
        11 => Generic11, 12 => Generic12, 13 => Generic13, 14 => Generic14, 15 => Generic15,
        16 => Generic16, 17 => Generic17, 18 => Generic18, 19 => Generic19, 20 => Generic20,
        _ => null
    };
}

/// <summary>
/// Request DTO for creating or updating a generic parser configuration.
/// Includes format settings, row identification rules, and column mappings.
/// </summary>
public class GenericParserConfigRequest
{
    /// <summary>File type code this configuration applies to.</summary>
    /// <example>VENDOR_CHG</example>
    public string FileTypeCode { get; set; } = string.Empty;

    /// <summary>File format (CSV, XLSX, or Delimited).</summary>
    /// <example>CSV</example>
    public string FileFormat { get; set; } = "CSV";

    /// <summary>Field delimiter (comma, tab, pipe, semicolon, or literal character).</summary>
    /// <example>,</example>
    public string? Delimiter { get; set; }

    /// <summary>Whether the file contains a header row.</summary>
    /// <example>true</example>
    public bool HasHeaderRow { get; set; }

    /// <summary>Number of rows to skip at the top of the file.</summary>
    /// <example>0</example>
    public int SkipRowsTop { get; set; }

    /// <summary>Number of rows to skip at the bottom of the file.</summary>
    /// <example>1</example>
    public int SkipRowsBottom { get; set; }

    /// <summary>Row identification mode (POSITION, INDICATOR, or PATTERN).</summary>
    /// <example>POSITION</example>
    public string RowIdMode { get; set; } = "POSITION";

    /// <summary>Column index for row type identification when using INDICATOR mode.</summary>
    /// <example>0</example>
    public int RowIdColumn { get; set; }

    /// <summary>Indicator string or regex that identifies header rows.</summary>
    /// <example>H</example>
    public string? HeaderIndicator { get; set; }

    /// <summary>Indicator string or regex that identifies trailer rows.</summary>
    /// <example>T</example>
    public string? TrailerIndicator { get; set; }

    /// <summary>Indicator string or regex that identifies detail rows.</summary>
    /// <example>D</example>
    public string? DetailIndicator { get; set; }

    /// <summary>Indicator string or regex that identifies rows to skip.</summary>
    /// <example>C</example>
    public string? SkipIndicator { get; set; }

    /// <summary>Column index containing the trailer total value.</summary>
    /// <example>4</example>
    public int? TotalColumnIndex { get; set; }

    /// <summary>Type of total in the trailer (COUNT, SUM_COST, SUM_QTY).</summary>
    /// <example>COUNT</example>
    public string? TotalType { get; set; }

    /// <summary>Excel sheet name (for XLSX format).</summary>
    /// <example>Sheet1</example>
    public string? SheetName { get; set; }

    /// <summary>Excel sheet index, 0-based (for XLSX format).</summary>
    /// <example>0</example>
    public int SheetIndex { get; set; }

    /// <summary>Default date format for parsing date columns.</summary>
    /// <example>dd/MM/yyyy</example>
    public string? DateFormat { get; set; }

    /// <summary>Custom stored procedure name for post-processing.</summary>
    /// <example>sp_process_vendor_chg</example>
    public string? CustomSpName { get; set; }

    /// <summary>Whether this configuration is active.</summary>
    /// <example>true</example>
    public bool Active { get; set; } = true;

    /// <summary>Column mappings defining how source columns map to target fields.</summary>
    public List<GenericColumnMappingRequest> ColumnMappings { get; set; } = new();
}

/// <summary>
/// Request DTO for a single column mapping within a generic parser configuration.
/// </summary>
public class GenericColumnMappingRequest
{
    /// <summary>Zero-based column index in the source file.</summary>
    /// <example>0</example>
    public int ColumnIndex { get; set; }

    /// <summary>Column name from the source file header (for documentation).</summary>
    /// <example>Account Number</example>
    public string? SourceColumnName { get; set; }

    /// <summary>Target field name (from GenericTargetField enum values).</summary>
    /// <example>AccountCode</example>
    public string TargetField { get; set; } = string.Empty;

    /// <summary>Expected data type (String, Int, Decimal, Date, DateTime).</summary>
    /// <example>String</example>
    public string DataType { get; set; } = "String";

    /// <summary>Date format for parsing date columns.</summary>
    /// <example>yyyy-MM-dd</example>
    public string? DateFormat { get; set; }

    /// <summary>Whether this column is required.</summary>
    /// <example>true</example>
    public bool IsRequired { get; set; }

    /// <summary>Default value when the source column is empty.</summary>
    /// <example>UNKNOWN</example>
    public string? DefaultValue { get; set; }

    /// <summary>Regex pattern for value validation.</summary>
    /// <example>^[A-Z0-9]{6,10}$</example>
    public string? RegexPattern { get; set; }

    /// <summary>Maximum allowed string length.</summary>
    /// <example>50</example>
    public int? MaxLength { get; set; }
}
